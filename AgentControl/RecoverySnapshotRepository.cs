using AgentShared;
using System.Data;
using System.Data.SQLite;
using System.Globalization;

namespace AgentControl
{
    /// <summary>
    /// DB index rieng cho giao dien khoi phuc. Khong chen bang/lock vao cac luong cu.
    /// </summary>
    internal sealed class RecoverySnapshotRepository
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);
        private bool _initialized;

        public RecoverySnapshotRepository(string? databasePath = null)
        {
            string path = databasePath ?? Path.Combine(AppContext.BaseDirectory, "RecoverySnapshot.db");
            SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder
            {
                DataSource = path,
                Version = 3,
                DefaultTimeout = 30
            };
            _connectionString = builder.ConnectionString;
        }

        public async Task InitializeAsync()
        {
            await _dbLock.WaitAsync();
            try
            {
                if (_initialized) return;
                using SQLiteConnection connection = OpenConnection();
                using SQLiteCommand command = new SQLiteCommand(@"
CREATE TABLE IF NOT EXISTS RecoverySnapshots (
    AgentID TEXT NOT NULL,
    SnapshotDate TEXT NOT NULL,
    Signature TEXT NOT NULL,
    CompletedAtUtc TEXT NOT NULL,
    PRIMARY KEY (AgentID, SnapshotDate)
);

CREATE TABLE IF NOT EXISTS RecoverySnapshotFiles (
    AgentID TEXT NOT NULL,
    SnapshotDate TEXT NOT NULL,
    SourcePath TEXT NOT NULL COLLATE NOCASE,
    RelativeStoragePath TEXT NOT NULL COLLATE NOCASE,
    VirtualDirectory TEXT NOT NULL COLLATE NOCASE,
    FileName TEXT NOT NULL COLLATE NOCASE,
    Size INTEGER NOT NULL,
    LastWriteTimeUtc TEXT NOT NULL,
    SourceSessionRoot TEXT NOT NULL,
    PRIMARY KEY (AgentID, SnapshotDate, SourcePath)
);

CREATE TABLE IF NOT EXISTS RecoverySnapshotDirectories (
    AgentID TEXT NOT NULL,
    SnapshotDate TEXT NOT NULL,
    VirtualPath TEXT NOT NULL COLLATE NOCASE,
    ParentPath TEXT NOT NULL COLLATE NOCASE,
    DisplayName TEXT NOT NULL COLLATE NOCASE,
    PRIMARY KEY (AgentID, SnapshotDate, VirtualPath)
);

CREATE TABLE IF NOT EXISTS RecoverySelections (
    RunID TEXT NOT NULL,
    Kind TEXT NOT NULL,
    Value TEXT NOT NULL COLLATE NOCASE,
    PRIMARY KEY (RunID, Kind, Value)
);

CREATE INDEX IF NOT EXISTS IX_RecoveryFiles_Directory
ON RecoverySnapshotFiles (AgentID, SnapshotDate, VirtualDirectory, FileName);

CREATE INDEX IF NOT EXISTS IX_RecoveryDirectories_Parent
ON RecoverySnapshotDirectories (AgentID, SnapshotDate, ParentPath, DisplayName);

CREATE INDEX IF NOT EXISTS IX_RecoverySelections_Run
ON RecoverySelections (RunID, Kind, Value);", connection);
                command.ExecuteNonQuery();
                _initialized = true;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<bool> IsCurrentAsync(string agentId, DateTime date, string signature)
        {
            await InitializeAsync();
            await _dbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = OpenConnection();
                using SQLiteCommand command = new SQLiteCommand(@"
SELECT COUNT(1) FROM RecoverySnapshots
WHERE AgentID = @AgentID AND SnapshotDate = @Date AND Signature = @Signature;", connection);
                command.Parameters.AddWithValue("@AgentID", agentId);
                command.Parameters.AddWithValue("@Date", DateKey(date));
                command.Parameters.AddWithValue("@Signature", signature);
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task RebuildAsync(
            string agentId,
            DateTime date,
            string signature,
            Action<RecoverySnapshotWriter> buildAction,
            CancellationToken token)
        {
            await InitializeAsync();
            await _dbLock.WaitAsync(token);
            try
            {
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using SQLiteConnection connection = OpenConnection();
                    using SQLiteTransaction transaction = connection.BeginTransaction();
                    string dateKey = DateKey(date);
                    DeleteSnapshot(connection, transaction, agentId, dateKey);

                    using RecoverySnapshotWriter writer = new RecoverySnapshotWriter(
                        connection, transaction, agentId, dateKey, token);
                    buildAction(writer);
                    writer.RebuildDirectories();

                    using SQLiteCommand meta = new SQLiteCommand(@"
INSERT INTO RecoverySnapshots (AgentID, SnapshotDate, Signature, CompletedAtUtc)
VALUES (@AgentID, @Date, @Signature, @CompletedAtUtc);", connection, transaction);
                    meta.Parameters.AddWithValue("@AgentID", agentId);
                    meta.Parameters.AddWithValue("@Date", dateKey);
                    meta.Parameters.AddWithValue("@Signature", signature);
                    meta.Parameters.AddWithValue("@CompletedAtUtc", DateTime.UtcNow.ToString("O"));
                    meta.ExecuteNonQuery();
                    transaction.Commit();
                }, token);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<RecoveryDirectoryRecord>> GetChildDirectoriesAsync(
            string agentId, DateTime date, string parentPath)
        {
            await InitializeAsync();
            await _dbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = OpenConnection();
                using SQLiteCommand command = new SQLiteCommand(@"
SELECT d.VirtualPath, d.DisplayName,
       EXISTS(SELECT 1 FROM RecoverySnapshotDirectories c
              WHERE c.AgentID = d.AgentID AND c.SnapshotDate = d.SnapshotDate
                AND c.ParentPath = d.VirtualPath LIMIT 1)
FROM RecoverySnapshotDirectories d
WHERE d.AgentID = @AgentID AND d.SnapshotDate = @Date AND d.ParentPath = @Parent
ORDER BY d.DisplayName COLLATE NOCASE;", connection);
                command.Parameters.AddWithValue("@AgentID", agentId);
                command.Parameters.AddWithValue("@Date", DateKey(date));
                command.Parameters.AddWithValue("@Parent", parentPath ?? string.Empty);
                List<RecoveryDirectoryRecord> result = new List<RecoveryDirectoryRecord>();
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new RecoveryDirectoryRecord
                    {
                        VirtualPath = reader.GetString(0),
                        DisplayName = reader.GetString(1),
                        HasChildren = reader.GetInt32(2) != 0
                    });
                }
                return result;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<RecoveryFileRecord>> GetFilesAsync(
            string agentId, DateTime date, string virtualDirectory)
        {
            await InitializeAsync();
            await _dbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = OpenConnection();
                using SQLiteCommand command = CreateFileSelectCommand(connection, @"
WHERE AgentID = @AgentID AND SnapshotDate = @Date AND VirtualDirectory = @Directory
ORDER BY FileName COLLATE NOCASE;");
                command.Parameters.AddWithValue("@AgentID", agentId);
                command.Parameters.AddWithValue("@Date", DateKey(date));
                command.Parameters.AddWithValue("@Directory", virtualDirectory);
                return ReadFiles(command);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task PrepareSelectionAsync(
            string runId,
            IEnumerable<string> folders,
            IEnumerable<string> files)
        {
            await InitializeAsync();
            await _dbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = OpenConnection();
                using SQLiteTransaction transaction = connection.BeginTransaction();
                using (SQLiteCommand clear = new SQLiteCommand(
                    "DELETE FROM RecoverySelections WHERE RunID = @RunID;", connection, transaction))
                {
                    clear.Parameters.AddWithValue("@RunID", runId);
                    clear.ExecuteNonQuery();
                }

                using SQLiteCommand insert = new SQLiteCommand(@"
INSERT OR IGNORE INTO RecoverySelections (RunID, Kind, Value)
VALUES (@RunID, @Kind, @Value);", connection, transaction);
                insert.Parameters.AddWithValue("@RunID", runId);
                SQLiteParameter kind = insert.Parameters.Add("@Kind", DbType.String);
                SQLiteParameter value = insert.Parameters.Add("@Value", DbType.String);
                foreach (string folder in folders)
                {
                    kind.Value = "Folder";
                    value.Value = folder;
                    insert.ExecuteNonQuery();
                }
                foreach (string file in files)
                {
                    kind.Value = "File";
                    value.Value = file;
                    insert.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<(long FileCount, long TotalBytes)> GetSelectionStatsAsync(
            string runId, string agentId, DateTime date)
        {
            await InitializeAsync();
            await _dbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = OpenConnection();
                using SQLiteCommand command = new SQLiteCommand($@"
SELECT COUNT(1), COALESCE(SUM(f.Size), 0)
FROM RecoverySnapshotFiles f
WHERE f.AgentID = @AgentID AND f.SnapshotDate = @Date
  AND {SelectionPredicate("f")};", connection);
                AddSelectionParameters(command, runId, agentId, date);
                using SQLiteDataReader reader = command.ExecuteReader();
                return reader.Read() ? (reader.GetInt64(0), reader.GetInt64(1)) : (0, 0);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<RecoveryFileRecord>> GetSelectedBatchAsync(
            string runId, string agentId, DateTime date, string afterSourcePath, int batchSize)
        {
            await InitializeAsync();
            await _dbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = OpenConnection();
                using SQLiteCommand command = CreateFileSelectCommand(connection, $@"
WHERE f.AgentID = @AgentID AND f.SnapshotDate = @Date
  AND f.SourcePath > @AfterSourcePath COLLATE NOCASE
  AND {SelectionPredicate("f")}
ORDER BY f.SourcePath COLLATE NOCASE
LIMIT @BatchSize;", "f");
                AddSelectionParameters(command, runId, agentId, date);
                command.Parameters.AddWithValue("@AfterSourcePath", afterSourcePath ?? string.Empty);
                command.Parameters.AddWithValue("@BatchSize", Math.Max(1, batchSize));
                return ReadFiles(command);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task ClearSelectionAsync(string runId)
        {
            await InitializeAsync();
            await _dbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = OpenConnection();
                using SQLiteCommand command = new SQLiteCommand(
                    "DELETE FROM RecoverySelections WHERE RunID = @RunID;", connection);
                command.Parameters.AddWithValue("@RunID", runId);
                command.ExecuteNonQuery();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        private SQLiteConnection OpenConnection()
        {
            SQLiteConnection connection = new SQLiteConnection(_connectionString);
            connection.Open();
            using SQLiteCommand pragma = new SQLiteCommand("PRAGMA busy_timeout=5000;", connection);
            pragma.ExecuteNonQuery();
            return connection;
        }

        private static void DeleteSnapshot(
            SQLiteConnection connection, SQLiteTransaction transaction, string agentId, string date)
        {
            foreach (string table in new[] { "RecoverySnapshotFiles", "RecoverySnapshotDirectories", "RecoverySnapshots" })
            {
                using SQLiteCommand command = new SQLiteCommand(
                    $"DELETE FROM {table} WHERE AgentID = @AgentID AND SnapshotDate = @Date;",
                    connection, transaction);
                command.Parameters.AddWithValue("@AgentID", agentId);
                command.Parameters.AddWithValue("@Date", date);
                command.ExecuteNonQuery();
            }
        }

        private static SQLiteCommand CreateFileSelectCommand(
            SQLiteConnection connection, string suffix, string alias = "")
        {
            string prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
            return new SQLiteCommand($@"
SELECT {prefix}SourcePath, {prefix}RelativeStoragePath, {prefix}VirtualDirectory,
       {prefix}FileName, {prefix}Size, {prefix}LastWriteTimeUtc, {prefix}SourceSessionRoot
FROM RecoverySnapshotFiles {(string.IsNullOrEmpty(alias) ? string.Empty : alias)}
{suffix}", connection);
        }

        private static List<RecoveryFileRecord> ReadFiles(SQLiteCommand command)
        {
            List<RecoveryFileRecord> result = new List<RecoveryFileRecord>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new RecoveryFileRecord
                {
                    SourcePath = reader.GetString(0),
                    RelativeStoragePath = reader.GetString(1),
                    VirtualDirectory = reader.GetString(2),
                    FileName = reader.GetString(3),
                    Size = reader.GetInt64(4),
                    LastWriteTimeUtc = DateTime.Parse(
                        reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    SourceSessionRoot = reader.GetString(6)
                });
            }
            return result;
        }

        private static string SelectionPredicate(string alias) => $@"(
EXISTS (SELECT 1 FROM RecoverySelections sf
        WHERE sf.RunID = @RunID AND sf.Kind = 'File' AND sf.Value = {alias}.SourcePath)
OR EXISTS (SELECT 1 FROM RecoverySelections sd
           WHERE sd.RunID = @RunID AND sd.Kind = 'Folder'
             AND ({alias}.VirtualDirectory = sd.Value
                  OR substr({alias}.VirtualDirectory, 1, length(sd.Value) + 1) = (sd.Value || '\') COLLATE NOCASE))
)";

        private static void AddSelectionParameters(
            SQLiteCommand command, string runId, string agentId, DateTime date)
        {
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@AgentID", agentId);
            command.Parameters.AddWithValue("@Date", DateKey(date));
        }

        private static string DateKey(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal sealed class RecoverySnapshotWriter : IDisposable
    {
        private readonly SQLiteConnection _connection;
        private readonly SQLiteTransaction _transaction;
        private readonly string _agentId;
        private readonly string _date;
        private readonly CancellationToken _token;
        private readonly SQLiteCommand _upsert;
        private readonly SQLiteCommand _delete;

        public RecoverySnapshotWriter(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string agentId,
            string date,
            CancellationToken token)
        {
            _connection = connection;
            _transaction = transaction;
            _agentId = agentId;
            _date = date;
            _token = token;
            _upsert = new SQLiteCommand(@"
INSERT INTO RecoverySnapshotFiles
    (AgentID, SnapshotDate, SourcePath, RelativeStoragePath, VirtualDirectory,
     FileName, Size, LastWriteTimeUtc, SourceSessionRoot)
VALUES
    (@AgentID, @Date, @SourcePath, @RelativePath, @Directory,
     @FileName, @Size, @LastWrite, @SessionRoot)
ON CONFLICT(AgentID, SnapshotDate, SourcePath) DO UPDATE SET
    RelativeStoragePath = excluded.RelativeStoragePath,
    VirtualDirectory = excluded.VirtualDirectory,
    FileName = excluded.FileName,
    Size = excluded.Size,
    LastWriteTimeUtc = excluded.LastWriteTimeUtc,
    SourceSessionRoot = excluded.SourceSessionRoot;", connection, transaction);
            _upsert.Parameters.AddWithValue("@AgentID", agentId);
            _upsert.Parameters.AddWithValue("@Date", date);
            _upsert.Parameters.Add("@SourcePath", DbType.String);
            _upsert.Parameters.Add("@RelativePath", DbType.String);
            _upsert.Parameters.Add("@Directory", DbType.String);
            _upsert.Parameters.Add("@FileName", DbType.String);
            _upsert.Parameters.Add("@Size", DbType.Int64);
            _upsert.Parameters.Add("@LastWrite", DbType.String);
            _upsert.Parameters.Add("@SessionRoot", DbType.String);

            _delete = new SQLiteCommand(@"
DELETE FROM RecoverySnapshotFiles
WHERE AgentID = @AgentID AND SnapshotDate = @Date AND SourcePath = @SourcePath;",
                connection, transaction);
            _delete.Parameters.AddWithValue("@AgentID", agentId);
            _delete.Parameters.AddWithValue("@Date", date);
            _delete.Parameters.Add("@SourcePath", DbType.String);
        }

        public void ClearFiles()
        {
            _token.ThrowIfCancellationRequested();
            using SQLiteCommand command = new SQLiteCommand(@"
DELETE FROM RecoverySnapshotFiles WHERE AgentID = @AgentID AND SnapshotDate = @Date;",
                _connection, _transaction);
            command.Parameters.AddWithValue("@AgentID", _agentId);
            command.Parameters.AddWithValue("@Date", _date);
            command.ExecuteNonQuery();
        }

        public void Upsert(BackupManifestEntry entry, string sessionRoot)
        {
            _token.ThrowIfCancellationRequested();
            string relative = NormalizeRelativePath(entry.RelativeStoragePath);
            string directory = NormalizeVirtualPath(Path.GetDirectoryName(relative) ?? string.Empty);
            _upsert.Parameters["@SourcePath"].Value = entry.SourcePath;
            _upsert.Parameters["@RelativePath"].Value = relative;
            _upsert.Parameters["@Directory"].Value = directory;
            _upsert.Parameters["@FileName"].Value = Path.GetFileName(relative);
            _upsert.Parameters["@Size"].Value = Math.Max(0, entry.Size);
            _upsert.Parameters["@LastWrite"].Value = entry.LastWriteTimeUtc.ToString("O");
            _upsert.Parameters["@SessionRoot"].Value = sessionRoot;
            _upsert.ExecuteNonQuery();
        }

        public void Delete(BackupManifestEntry entry)
        {
            _token.ThrowIfCancellationRequested();
            _delete.Parameters["@SourcePath"].Value = entry.SourcePath;
            _delete.ExecuteNonQuery();
        }

        public void RebuildDirectories()
        {
            _token.ThrowIfCancellationRequested();
            using (SQLiteCommand clear = new SQLiteCommand(@"
DELETE FROM RecoverySnapshotDirectories WHERE AgentID = @AgentID AND SnapshotDate = @Date;",
                _connection, _transaction))
            {
                clear.Parameters.AddWithValue("@AgentID", _agentId);
                clear.Parameters.AddWithValue("@Date", _date);
                clear.ExecuteNonQuery();
            }

            using SQLiteCommand insert = new SQLiteCommand(@"
INSERT OR IGNORE INTO RecoverySnapshotDirectories
    (AgentID, SnapshotDate, VirtualPath, ParentPath, DisplayName)
VALUES (@AgentID, @Date, @Path, @Parent, @Name);", _connection, _transaction);
            insert.Parameters.AddWithValue("@AgentID", _agentId);
            insert.Parameters.AddWithValue("@Date", _date);
            insert.Parameters.Add("@Path", DbType.String);
            insert.Parameters.Add("@Parent", DbType.String);
            insert.Parameters.Add("@Name", DbType.String);

            using SQLiteCommand files = new SQLiteCommand(@"
SELECT DISTINCT VirtualDirectory FROM RecoverySnapshotFiles
WHERE AgentID = @AgentID AND SnapshotDate = @Date;", _connection, _transaction);
            files.Parameters.AddWithValue("@AgentID", _agentId);
            files.Parameters.AddWithValue("@Date", _date);
            using SQLiteDataReader reader = files.ExecuteReader();
            while (reader.Read())
            {
                _token.ThrowIfCancellationRequested();
                string directory = reader.GetString(0);
                string[] parts = directory.Split(
                    Path.DirectorySeparatorChar,
                    StringSplitOptions.RemoveEmptyEntries);
                string parent = string.Empty;
                foreach (string part in parts)
                {
                    string current = string.IsNullOrEmpty(parent) ? part : parent + Path.DirectorySeparatorChar + part;
                    insert.Parameters["@Path"].Value = current;
                    insert.Parameters["@Parent"].Value = parent;
                    insert.Parameters["@Name"].Value = string.IsNullOrEmpty(parent) && part.Length == 1
                        ? part + @":\"
                        : part;
                    insert.ExecuteNonQuery();
                    parent = current;
                }
            }
        }

        private static string NormalizeRelativePath(string path)
        {
            string value = (path ?? string.Empty)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Split(Path.DirectorySeparatorChar).Any(p => p == ".."))
            {
                throw new InvalidDataException("Đường dẫn file trong manifest không hợp lệ.");
            }
            return value;
        }

        private static string NormalizeVirtualPath(string path) =>
            (path ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Trim(Path.DirectorySeparatorChar);

        public void Dispose()
        {
            _upsert.Dispose();
            _delete.Dispose();
        }
    }

    internal sealed class RecoveryDirectoryRecord
    {
        public string VirtualPath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool HasChildren { get; set; }
    }

    internal sealed class RecoveryFileRecord
    {
        public string SourcePath { get; set; } = string.Empty;
        public string RelativeStoragePath { get; set; } = string.Empty;
        public string VirtualDirectory { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string SourceSessionRoot { get; set; } = string.Empty;
    }
}
