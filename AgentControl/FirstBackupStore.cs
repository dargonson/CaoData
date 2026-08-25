using AgentShared;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace AgentControl
{
    /// <summary>
    /// DB rieng cho FIRST dang chay lau ngay. Luu offset/trang thai tung file de resume sau restart.
    /// Dung chung BackupManagement.db nhung tach khoi UI va luong download/upload cu.
    /// </summary>
    internal static class FirstBackupStore
    {
        private static string ConnectionString => BackupDatabase.ConnectionString;
        private static SemaphoreSlim DbLock => BackupDatabase.Gate;
        private static bool _initialized;

        public static async Task InitializeAsync()
        {
            await BackupRepository.InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                if (_initialized) return;
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(@"
CREATE TABLE IF NOT EXISTS FirstBackupRuns (
    AgentID TEXT PRIMARY KEY,
    WorkingSessionName TEXT NOT NULL,
    WorkingPath TEXT NOT NULL,
    StartedAtUtc TEXT NOT NULL,
    PlannedFileCount INTEGER NOT NULL,
    PlannedTotalBytes INTEGER NOT NULL,
    Status TEXT NOT NULL,
    FinalSessionName TEXT,
    FinalPath TEXT,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS FirstBackupFiles (
    AgentID TEXT NOT NULL,
    SourcePath TEXT NOT NULL,
    FileName TEXT NOT NULL,
    RelativeStoragePath TEXT NOT NULL,
    Size INTEGER NOT NULL,
    LastWriteTimeUtc TEXT NOT NULL,
    ContentSha256 TEXT NOT NULL DEFAULT '',
    ReceivedBytes INTEGER NOT NULL DEFAULT 0,
    Status TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    PRIMARY KEY (AgentID, SourcePath)
);
CREATE INDEX IF NOT EXISTS IX_FirstBackupFiles_Status
    ON FirstBackupFiles (AgentID, Status, SourcePath);

CREATE TABLE IF NOT EXISTS FirstBackupSkipped (
    AgentID TEXT NOT NULL,
    SourcePath TEXT NOT NULL,
    Reason TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    PRIMARY KEY (AgentID, SourcePath)
);", connection);
                await command.ExecuteNonQueryAsync();
                await BackupDatabase.EnsureColumnAsync(
                    connection,
                    "FirstBackupFiles",
                    "ContentSha256",
                    "TEXT NOT NULL DEFAULT ''");
                _initialized = true;
            }
            finally { DbLock.Release(); }
        }

        public static async Task<bool> BeginRunAsync(BackupSessionBegin request, string workingPath)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteTransaction transaction = connection.BeginTransaction();
                bool sameInProgressRun = false;
                using (SQLiteCommand existing = new SQLiteCommand(@"
SELECT WorkingSessionName, StartedAtUtc, PlannedFileCount, PlannedTotalBytes, Status
FROM FirstBackupRuns
WHERE AgentID = @AgentID
LIMIT 1;", connection, transaction))
                {
                    existing.Parameters.AddWithValue("@AgentID", request.AgentID);
                    using SQLiteDataReader reader = existing.ExecuteReader();
                    if (reader.Read())
                    {
                        sameInProgressRun =
                            reader.GetString(0).Equals(request.SessionName, StringComparison.OrdinalIgnoreCase) &&
                            DateTime.TryParse(reader.GetString(1), CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out DateTime oldStartedAtUtc) &&
                            oldStartedAtUtc == request.StartedAtUtc &&
                            reader.GetInt64(2) == request.PlannedFileCount &&
                            reader.GetInt64(3) == request.PlannedTotalBytes &&
                            reader.GetString(4).Equals("InProgress", StringComparison.OrdinalIgnoreCase);
                    }
                }

                if (!sameInProgressRun)
                {
                    foreach (string table in new[] { "FirstBackupFiles", "FirstBackupSkipped" })
                    {
                        using SQLiteCommand clear = new SQLiteCommand(
                            $"DELETE FROM {table} WHERE AgentID = @AgentID;", connection, transaction);
                        clear.Parameters.AddWithValue("@AgentID", request.AgentID);
                        await clear.ExecuteNonQueryAsync();
                    }
                }

                using SQLiteCommand command = new SQLiteCommand(@"
INSERT INTO FirstBackupRuns
    (AgentID, WorkingSessionName, WorkingPath, StartedAtUtc, PlannedFileCount, PlannedTotalBytes, Status, UpdatedAtUtc)
VALUES
    (@AgentID, @SessionName, @WorkingPath, @StartedAtUtc, @FileCount, @TotalBytes, 'InProgress', @Now)
ON CONFLICT(AgentID) DO UPDATE SET
    WorkingSessionName = excluded.WorkingSessionName,
    WorkingPath = excluded.WorkingPath,
    StartedAtUtc = excluded.StartedAtUtc,
    PlannedFileCount = excluded.PlannedFileCount,
    PlannedTotalBytes = excluded.PlannedTotalBytes,
    Status = 'InProgress',
    FinalSessionName = NULL,
    FinalPath = NULL,
    UpdatedAtUtc = excluded.UpdatedAtUtc;", connection, transaction);
                command.Parameters.AddWithValue("@AgentID", request.AgentID);
                command.Parameters.AddWithValue("@SessionName", request.SessionName);
                command.Parameters.AddWithValue("@WorkingPath", workingPath);
                command.Parameters.AddWithValue("@StartedAtUtc", request.StartedAtUtc.ToString("O"));
                command.Parameters.AddWithValue("@FileCount", request.PlannedFileCount);
                command.Parameters.AddWithValue("@TotalBytes", request.PlannedTotalBytes);
                command.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
                transaction.Commit();
                return !sameInProgressRun;
            }
            finally { DbLock.Release(); }
        }

        public static async Task<CompletedFirstRun?> GetCompletedRunAsync(string agentId)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(@"
SELECT FinalSessionName, FinalPath, Status, WorkingSessionName, WorkingPath,
       StartedAtUtc, PlannedFileCount, PlannedTotalBytes
FROM FirstBackupRuns
WHERE AgentID = @AgentID AND Status IN ('Finalizing', 'Completed')
LIMIT 1;", connection);
                command.Parameters.AddWithValue("@AgentID", agentId);
                using SQLiteDataReader reader = command.ExecuteReader();
                return reader.Read()
                    ? new CompletedFirstRun(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                        reader.GetInt64(6),
                        reader.GetInt64(7))
                    : null;
            }
            finally { DbLock.Release(); }
        }

        public static async Task<FirstFileRegistration> RegisterFileAsync(BackupFirstFileResumeQuery query)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteTransaction transaction = connection.BeginTransaction();
                bool resetRequired = false;
                bool completed = false;
                bool skipped = false;
                long receivedBytes = 0;
                string contentSha256 = string.Empty;

                using (SQLiteCommand select = new SQLiteCommand(@"
SELECT Size, LastWriteTimeUtc, ReceivedBytes, Status, RelativeStoragePath, ContentSha256
FROM FirstBackupFiles
WHERE AgentID = @AgentID AND SourcePath = @SourcePath
LIMIT 1;", connection, transaction))
                {
                    select.Parameters.AddWithValue("@AgentID", query.AgentID);
                    select.Parameters.AddWithValue("@SourcePath", query.SourcePath);
                    using SQLiteDataReader reader = select.ExecuteReader();
                    if (reader.Read())
                    {
                        long oldSize = reader.GetInt64(0);
                        string oldLastWrite = reader.GetString(1);
                        receivedBytes = reader.GetInt64(2);
                        string status = reader.GetString(3);
                        completed = status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
                        skipped = status.Equals("Skipped", StringComparison.OrdinalIgnoreCase);
                        string oldRelativePath = reader.GetString(4);
                        contentSha256 = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                        resetRequired = oldSize != query.TotalBytes ||
                                        !oldLastWrite.Equals(query.LastWriteTimeUtc.ToString("O"), StringComparison.Ordinal) ||
                                        !oldRelativePath.Equals(query.RelativeStoragePath, StringComparison.OrdinalIgnoreCase);
                    }
                }

                if (resetRequired)
                {
                    receivedBytes = 0;
                    completed = false;
                    skipped = false;
                    contentSha256 = string.Empty;
                }

                using (SQLiteCommand upsert = new SQLiteCommand(@"
INSERT INTO FirstBackupFiles
    (AgentID, SourcePath, FileName, RelativeStoragePath, Size, LastWriteTimeUtc, ContentSha256, ReceivedBytes, Status, UpdatedAtUtc)
VALUES
    (@AgentID, @SourcePath, @FileName, @RelativePath, @Size, @LastWrite, @ContentSha256, @Received, @Status, @Now)
ON CONFLICT(AgentID, SourcePath) DO UPDATE SET
    FileName = excluded.FileName,
    RelativeStoragePath = excluded.RelativeStoragePath,
    Size = excluded.Size,
    LastWriteTimeUtc = excluded.LastWriteTimeUtc,
    ContentSha256 = CASE WHEN @Reset = 1 THEN '' ELSE FirstBackupFiles.ContentSha256 END,
    ReceivedBytes = CASE WHEN @Reset = 1 THEN 0 ELSE FirstBackupFiles.ReceivedBytes END,
    Status = CASE WHEN @Reset = 1 THEN 'InProgress' ELSE FirstBackupFiles.Status END,
    UpdatedAtUtc = excluded.UpdatedAtUtc;", connection, transaction))
                {
                    upsert.Parameters.AddWithValue("@AgentID", query.AgentID);
                    upsert.Parameters.AddWithValue("@SourcePath", query.SourcePath);
                    upsert.Parameters.AddWithValue("@FileName", System.IO.Path.GetFileName(query.SourcePath));
                    upsert.Parameters.AddWithValue("@RelativePath", query.RelativeStoragePath);
                    upsert.Parameters.AddWithValue("@Size", query.TotalBytes);
                    upsert.Parameters.AddWithValue("@LastWrite", query.LastWriteTimeUtc.ToString("O"));
                    upsert.Parameters.AddWithValue("@ContentSha256", contentSha256);
                    upsert.Parameters.AddWithValue("@Received", receivedBytes);
                    upsert.Parameters.AddWithValue("@Status", completed ? "Completed" : "InProgress");
                    upsert.Parameters.AddWithValue("@Reset", resetRequired ? 1 : 0);
                    upsert.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                    await upsert.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                return new FirstFileRegistration(
                    receivedBytes,
                    completed,
                    skipped,
                    resetRequired,
                    contentSha256);
            }
            finally { DbLock.Release(); }
        }

        public static async Task UpdateProgressAsync(string agentId, string sourcePath, long receivedBytes)
        {
            await InitializeAsync();
            await ExecuteAsync(@"
UPDATE FirstBackupFiles
SET ReceivedBytes = @ReceivedBytes, Status = 'InProgress', UpdatedAtUtc = @Now
WHERE AgentID = @AgentID AND SourcePath = @SourcePath;",
                ("@AgentID", agentId), ("@SourcePath", sourcePath),
                ("@ReceivedBytes", receivedBytes), ("@Now", DateTime.UtcNow.ToString("O")));
        }

        public static async Task MarkCompletedAsync(
            string agentId,
            string sourcePath,
            long totalBytes,
            string contentSha256)
        {
            await InitializeAsync();
            await ExecuteAsync(@"
UPDATE FirstBackupFiles
SET ReceivedBytes = @TotalBytes, ContentSha256 = @ContentSha256,
    Status = 'Completed', UpdatedAtUtc = @Now
WHERE AgentID = @AgentID AND SourcePath = @SourcePath;",
                ("@AgentID", agentId), ("@SourcePath", sourcePath),
                ("@TotalBytes", totalBytes), ("@ContentSha256", contentSha256),
                ("@Now", DateTime.UtcNow.ToString("O")));
        }

        public static async Task MarkSkippedAsync(BackupFirstFileSkip skipped)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteTransaction transaction = connection.BeginTransaction();
                using (SQLiteCommand file = new SQLiteCommand(@"
INSERT INTO FirstBackupFiles
    (AgentID, SourcePath, FileName, RelativeStoragePath, Size, LastWriteTimeUtc, ContentSha256, ReceivedBytes, Status, UpdatedAtUtc)
VALUES
    (@AgentID, @SourcePath, @FileName, @RelativePath, @Size, @LastWrite, '', 0, 'Skipped', @Now)
ON CONFLICT(AgentID, SourcePath) DO UPDATE SET
    FileName = excluded.FileName,
    RelativeStoragePath = excluded.RelativeStoragePath,
    Size = excluded.Size,
    LastWriteTimeUtc = excluded.LastWriteTimeUtc,
    ContentSha256 = '',
    ReceivedBytes = 0,
    Status = 'Skipped',
    UpdatedAtUtc = excluded.UpdatedAtUtc;", connection, transaction))
                {
                    file.Parameters.AddWithValue("@AgentID", skipped.AgentID);
                    file.Parameters.AddWithValue("@SourcePath", skipped.SourcePath);
                    file.Parameters.AddWithValue("@FileName", System.IO.Path.GetFileName(skipped.SourcePath));
                    file.Parameters.AddWithValue("@RelativePath", skipped.RelativeStoragePath);
                    file.Parameters.AddWithValue("@Size", skipped.Size);
                    file.Parameters.AddWithValue("@LastWrite", skipped.LastWriteTimeUtc.ToString("O"));
                    file.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                    await file.ExecuteNonQueryAsync();
                }

                using (SQLiteCommand reason = new SQLiteCommand(@"
INSERT INTO FirstBackupSkipped (AgentID, SourcePath, Reason, UpdatedAtUtc)
VALUES (@AgentID, @SourcePath, @Reason, @Now)
ON CONFLICT(AgentID, SourcePath) DO UPDATE SET
    Reason = excluded.Reason, UpdatedAtUtc = excluded.UpdatedAtUtc;", connection, transaction))
                {
                    reason.Parameters.AddWithValue("@AgentID", skipped.AgentID);
                    reason.Parameters.AddWithValue("@SourcePath", skipped.SourcePath);
                    reason.Parameters.AddWithValue("@Reason", skipped.Reason ?? string.Empty);
                    reason.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                    await reason.ExecuteNonQueryAsync();
                }
                transaction.Commit();
            }
            finally { DbLock.Release(); }
        }

        public static async Task<(bool Exists, long Planned, long Completed)> GetRunCountsAsync(string agentId)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                bool exists;
                long planned;
                using (SQLiteCommand plannedCommand = new SQLiteCommand(
                    "SELECT PlannedFileCount FROM FirstBackupRuns WHERE AgentID = @AgentID LIMIT 1;", connection))
                {
                    plannedCommand.Parameters.AddWithValue("@AgentID", agentId);
                    object? value = await plannedCommand.ExecuteScalarAsync();
                    exists = value != null && value != DBNull.Value;
                    planned = exists ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;
                }
                using SQLiteCommand completedCommand = new SQLiteCommand(
                    "SELECT COUNT(*) FROM FirstBackupFiles WHERE AgentID = @AgentID AND Status IN ('Completed', 'Skipped');", connection);
                completedCommand.Parameters.AddWithValue("@AgentID", agentId);
                long completed = Convert.ToInt64(await completedCommand.ExecuteScalarAsync());
                return (exists, planned, completed);
            }
            finally { DbLock.Release(); }
        }

        public static async Task<List<BackupManifestEntry>> GetCompletedBatchAsync(string agentId, string afterSourcePath, int count)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(@"
SELECT SourcePath, RelativeStoragePath, Size, LastWriteTimeUtc, ContentSha256
FROM FirstBackupFiles
WHERE AgentID = @AgentID AND Status = 'Completed'
  AND SourcePath > @After COLLATE NOCASE
ORDER BY SourcePath COLLATE NOCASE
LIMIT @Count;", connection);
                command.Parameters.AddWithValue("@AgentID", agentId);
                command.Parameters.AddWithValue("@After", afterSourcePath ?? string.Empty);
                command.Parameters.AddWithValue("@Count", Math.Max(1, count));
                List<BackupManifestEntry> result = new List<BackupManifestEntry>();
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new BackupManifestEntry
                    {
                        SourcePath = reader.GetString(0),
                        RelativeStoragePath = reader.GetString(1),
                        Size = reader.GetInt64(2),
                        LastWriteTimeUtc = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                        ContentSha256 = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
                    });
                }
                return result;
            }
            finally { DbLock.Release(); }
        }

        public static async Task<List<FirstSkippedFile>> GetSkippedBatchAsync(string agentId, string afterSourcePath, int count)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(@"
SELECT SourcePath, Reason
FROM FirstBackupSkipped
WHERE AgentID = @AgentID AND SourcePath > @After COLLATE NOCASE
ORDER BY SourcePath COLLATE NOCASE
LIMIT @Count;", connection);
                command.Parameters.AddWithValue("@AgentID", agentId);
                command.Parameters.AddWithValue("@After", afterSourcePath ?? string.Empty);
                command.Parameters.AddWithValue("@Count", Math.Max(1, count));
                List<FirstSkippedFile> result = new List<FirstSkippedFile>();
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(new FirstSkippedFile(reader.GetString(0), reader.GetString(1)));
                return result;
            }
            finally { DbLock.Release(); }
        }

        public static async Task FinalizeRunAsync(
            string agentId, string finalSessionName, string finalPath,
            DateTime startedAtUtc, DateTime completedAtUtc, string message)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteTransaction transaction = connection.BeginTransaction();
                using (SQLiteCommand session = new SQLiteCommand(@"
INSERT INTO BackupSessions
    (AgentID, SessionName, BackupType, StoragePath, StartedAtUtc, CompletedAtUtc, Success, Message)
VALUES (@AgentID, @SessionName, 'FIRST', @Path, @Started, @Completed, 1, @Message)
ON CONFLICT(AgentID, SessionName) DO UPDATE SET
    StoragePath = excluded.StoragePath, CompletedAtUtc = excluded.CompletedAtUtc,
    Success = 1, Message = excluded.Message;", connection, transaction))
                {
                    session.Parameters.AddWithValue("@AgentID", agentId);
                    session.Parameters.AddWithValue("@SessionName", finalSessionName);
                    session.Parameters.AddWithValue("@Path", finalPath);
                    session.Parameters.AddWithValue("@Started", startedAtUtc.ToString("O"));
                    session.Parameters.AddWithValue("@Completed", completedAtUtc.ToString("O"));
                    session.Parameters.AddWithValue("@Message", message);
                    await session.ExecuteNonQueryAsync();
                }

                using (SQLiteCommand clear = new SQLiteCommand(
                    "DELETE FROM BackupFileInventory WHERE AgentID = @AgentID;", connection, transaction))
                {
                    clear.Parameters.AddWithValue("@AgentID", agentId);
                    await clear.ExecuteNonQueryAsync();
                }

                using (SQLiteCommand inventory = new SQLiteCommand(@"
INSERT INTO BackupFileInventory
    (AgentID, SourcePath, FileName, RelativeStoragePath, Size, LastWriteTimeUtc, ContentSha256, IsDeleted, UpdatedSession)
SELECT AgentID, SourcePath, FileName, RelativeStoragePath, Size, LastWriteTimeUtc, ContentSha256, 0, @SessionName
FROM FirstBackupFiles
WHERE AgentID = @AgentID AND Status = 'Completed';", connection, transaction))
                {
                    inventory.Parameters.AddWithValue("@AgentID", agentId);
                    inventory.Parameters.AddWithValue("@SessionName", finalSessionName);
                    await inventory.ExecuteNonQueryAsync();
                }

                using (SQLiteCommand run = new SQLiteCommand(@"
UPDATE FirstBackupRuns SET Status = 'Completed', FinalSessionName = @SessionName,
    FinalPath = @Path, UpdatedAtUtc = @Now WHERE AgentID = @AgentID;", connection, transaction))
                {
                    run.Parameters.AddWithValue("@AgentID", agentId);
                    run.Parameters.AddWithValue("@SessionName", finalSessionName);
                    run.Parameters.AddWithValue("@Path", finalPath);
                    run.Parameters.AddWithValue("@Now", completedAtUtc.ToString("O"));
                    await run.ExecuteNonQueryAsync();
                }
                transaction.Commit();
            }
            finally { DbLock.Release(); }
        }

        public static async Task MarkFinalizingAsync(string agentId, string finalSessionName, string finalPath)
        {
            await InitializeAsync();
            await ExecuteAsync(@"
UPDATE FirstBackupRuns
SET Status = 'Finalizing', FinalSessionName = @SessionName, FinalPath = @Path, UpdatedAtUtc = @Now
WHERE AgentID = @AgentID;",
                ("@AgentID", agentId), ("@SessionName", finalSessionName),
                ("@Path", finalPath), ("@Now", DateTime.UtcNow.ToString("O")));
        }

        private static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(sql, connection);
                foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
                await command.ExecuteNonQueryAsync();
            }
            finally { DbLock.Release(); }
        }

        private static async Task<SQLiteConnection> OpenConnectionAsync()
        {
            SQLiteConnection connection = new SQLiteConnection(ConnectionString);
            await connection.OpenAsync();
            using SQLiteCommand command = new SQLiteCommand("PRAGMA busy_timeout = 5000;", connection);
            await command.ExecuteNonQueryAsync();
            return connection;
        }
    }

    internal readonly struct FirstFileRegistration
    {
        public long ReceivedBytes { get; }
        public bool Completed { get; }
        public bool Skipped { get; }
        public bool ResetRequired { get; }
        public string ContentSha256 { get; }
        public FirstFileRegistration(
            long receivedBytes,
            bool completed,
            bool skipped,
            bool resetRequired,
            string contentSha256)
        {
            ReceivedBytes = receivedBytes;
            Completed = completed;
            Skipped = skipped;
            ResetRequired = resetRequired;
            ContentSha256 = contentSha256 ?? string.Empty;
        }
    }

    internal sealed class FirstSkippedFile
    {
        public string SourcePath { get; }
        public string Reason { get; }
        public FirstSkippedFile(string sourcePath, string reason)
        {
            SourcePath = sourcePath;
            Reason = reason;
        }
    }

    internal sealed class CompletedFirstRun
    {
        public string SessionName { get; }
        public string StoragePath { get; }
        public string Status { get; }
        public string WorkingSessionName { get; }
        public string WorkingPath { get; }
        public DateTime StartedAtUtc { get; }
        public long PlannedFileCount { get; }
        public long PlannedTotalBytes { get; }

        public CompletedFirstRun(
            string sessionName,
            string storagePath,
            string status,
            string workingSessionName,
            string workingPath,
            DateTime startedAtUtc,
            long plannedFileCount,
            long plannedTotalBytes)
        {
            SessionName = sessionName;
            StoragePath = storagePath;
            Status = status;
            WorkingSessionName = workingSessionName;
            WorkingPath = workingPath;
            StartedAtUtc = startedAtUtc;
            PlannedFileCount = plannedFileCount;
            PlannedTotalBytes = plannedTotalBytes;
        }
    }
}
