using AgentShared;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentControl
{
    /// <summary>
    /// Kho du lieu va file DB rieng cua module Backup, khong chen logic/lock vao
    /// SQLiteHelper de giu nguyen cac chuc nang cu.
    /// </summary>
    internal static class BackupRepository
    {
        private static string ConnectionString => BackupDatabase.ConnectionString;
        private static SemaphoreSlim DbLock => BackupDatabase.Gate;
        private static bool _initialized;

        public static async Task InitializeAsync()
        {
            await DbLock.WaitAsync();
            try
            {
                if (_initialized)
                {
                    return;
                }

                using SQLiteConnection connection = await OpenConnectionAsync();
                using (SQLiteCommand pragma = new SQLiteCommand("PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;", connection))
                {
                    await pragma.ExecuteNonQueryAsync();
                }
                using SQLiteCommand command = new SQLiteCommand(connection);
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS BackupConfigs (
    AgentID TEXT PRIMARY KEY,
    ConfigJson TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS BackupSessions (
    AgentID TEXT NOT NULL,
    SessionName TEXT NOT NULL,
    BackupType TEXT NOT NULL,
    StoragePath TEXT NOT NULL,
    StartedAtUtc TEXT NOT NULL,
    CompletedAtUtc TEXT NOT NULL,
    Success INTEGER NOT NULL,
    Message TEXT,
    PRIMARY KEY (AgentID, SessionName)
);

CREATE TABLE IF NOT EXISTS BackupDashboardSnapshots (
    AgentID TEXT PRIMARY KEY,
    SnapshotJson TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    Revision INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS BackupFileInventory (
    AgentID TEXT NOT NULL,
    SourcePath TEXT NOT NULL,
    FileName TEXT NOT NULL,
    RelativeStoragePath TEXT NOT NULL,
    Size INTEGER NOT NULL,
    LastWriteTimeUtc TEXT NOT NULL,
    ContentSha256 TEXT NOT NULL DEFAULT '',
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    UpdatedSession TEXT NOT NULL,
    PRIMARY KEY (AgentID, SourcePath)
);";
                await command.ExecuteNonQueryAsync();
                await BackupDatabase.EnsureColumnAsync(
                    connection,
                    "BackupFileInventory",
                    "ContentSha256",
                    "TEXT NOT NULL DEFAULT ''");
                await BackupDatabase.EnsureColumnAsync(
                    connection,
                    "BackupDashboardSnapshots",
                    "Revision",
                    "INTEGER NOT NULL DEFAULT 0");
                _initialized = true;
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task SaveConfigAsync(BackupConfiguration config)
        {
            await InitializeAsync();
            string json = JsonSerializer.Serialize(config);

            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(@"
INSERT INTO BackupConfigs (AgentID, ConfigJson, UpdatedAtUtc)
VALUES (@AgentID, @ConfigJson, @UpdatedAtUtc)
ON CONFLICT(AgentID) DO UPDATE SET
    ConfigJson = excluded.ConfigJson,
    UpdatedAtUtc = excluded.UpdatedAtUtc;", connection);
                command.Parameters.AddWithValue("@AgentID", config.AgentID);
                command.Parameters.AddWithValue("@ConfigJson", json);
                command.Parameters.AddWithValue("@UpdatedAtUtc", config.UpdatedAtUtc.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task<BackupConfiguration?> GetConfigAsync(string agentId)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(
                    "SELECT ConfigJson FROM BackupConfigs WHERE AgentID = @AgentID LIMIT 1;",
                    connection);
                command.Parameters.AddWithValue("@AgentID", agentId);
                object? value = await command.ExecuteScalarAsync();
                return value == null || value == DBNull.Value
                    ? null
                    : JsonSerializer.Deserialize<BackupConfiguration>(value.ToString() ?? string.Empty);
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task<Dictionary<string, BackupConfiguration>> GetAllConfigsAsync()
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(
                    "SELECT AgentID, ConfigJson FROM BackupConfigs;",
                    connection);
                var result = new Dictionary<string, BackupConfiguration>(StringComparer.OrdinalIgnoreCase);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string agentId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    string json = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(json))
                    {
                        continue;
                    }

                    try
                    {
                        BackupConfiguration? config = JsonSerializer.Deserialize<BackupConfiguration>(json);
                        if (config != null)
                        {
                            result[agentId] = config;
                        }
                    }
                    catch (JsonException)
                    {
                        // Một config hỏng không được làm mất Dashboard của các Agent còn lại.
                    }
                }
                return result;
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task<Dictionary<string, DateTime>> GetLatestSuccessfulSessionStartsAsync()
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(@"
SELECT AgentID, StartedAtUtc
FROM BackupSessions
WHERE Success = 1
ORDER BY CompletedAtUtc DESC;", connection);
                var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string agentId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    if (string.IsNullOrWhiteSpace(agentId) || result.ContainsKey(agentId) || reader.IsDBNull(1))
                    {
                        continue;
                    }

                    if (DateTime.TryParse(
                        reader.GetString(1),
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out DateTime startedAtUtc))
                    {
                        result[agentId] = startedAtUtc;
                    }
                }
                return result;
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task SaveDashboardSnapshotAsync(BackupDashboardSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.AgentId))
            {
                throw new ArgumentException("Snapshot Dashboard không hợp lệ.", nameof(snapshot));
            }

            await InitializeAsync();
            string json = JsonSerializer.Serialize(snapshot);
            DateTime updatedAtUtc = snapshot.UpdatedAtUtc.Kind == DateTimeKind.Utc
                ? snapshot.UpdatedAtUtc
                : snapshot.UpdatedAtUtc.ToUniversalTime();

            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(@"
INSERT INTO BackupDashboardSnapshots (AgentID, SnapshotJson, UpdatedAtUtc, Revision)
VALUES (@AgentID, @SnapshotJson, @UpdatedAtUtc, @Revision)
ON CONFLICT(AgentID) DO UPDATE SET
    SnapshotJson = excluded.SnapshotJson,
    UpdatedAtUtc = excluded.UpdatedAtUtc,
    Revision = excluded.Revision
WHERE excluded.Revision >= BackupDashboardSnapshots.Revision;", connection);
                command.Parameters.AddWithValue("@AgentID", snapshot.AgentId.Trim());
                command.Parameters.AddWithValue("@SnapshotJson", json);
                command.Parameters.AddWithValue("@UpdatedAtUtc", updatedAtUtc.ToString("O"));
                command.Parameters.AddWithValue("@Revision", Math.Max(0, snapshot.Revision));
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task<Dictionary<string, BackupDashboardSnapshot>> GetAllDashboardSnapshotsAsync()
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(
                    "SELECT AgentID, SnapshotJson FROM BackupDashboardSnapshots;",
                    connection);
                var result = new Dictionary<string, BackupDashboardSnapshot>(StringComparer.OrdinalIgnoreCase);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string agentId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    string json = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(json))
                    {
                        continue;
                    }

                    try
                    {
                        BackupDashboardSnapshot? snapshot = JsonSerializer.Deserialize<BackupDashboardSnapshot>(json);
                        if (snapshot != null &&
                            agentId.Equals(snapshot.AgentId, StringComparison.OrdinalIgnoreCase))
                        {
                            result[agentId] = snapshot;
                        }
                    }
                    catch (JsonException)
                    {
                        // Một snapshot hỏng không được chặn các Agent khác xuất hiện trên Dashboard.
                    }
                }
                return result;
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task SaveSessionAsync(
            BackupManifest manifest,
            string storagePath,
            bool success,
            string message)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteTransaction transaction = connection.BeginTransaction();
                using SQLiteCommand command = new SQLiteCommand(@"
INSERT INTO BackupSessions
    (AgentID, SessionName, BackupType, StoragePath, StartedAtUtc, CompletedAtUtc, Success, Message)
VALUES
    (@AgentID, @SessionName, @BackupType, @StoragePath, @StartedAtUtc, @CompletedAtUtc, @Success, @Message)
ON CONFLICT(AgentID, SessionName) DO UPDATE SET
    StoragePath = CASE
        WHEN BackupSessions.Success = 1 AND excluded.Success = 0 THEN BackupSessions.StoragePath
        ELSE excluded.StoragePath END,
    CompletedAtUtc = CASE
        WHEN BackupSessions.Success = 1 AND excluded.Success = 0 THEN BackupSessions.CompletedAtUtc
        ELSE excluded.CompletedAtUtc END,
    Success = CASE
        WHEN BackupSessions.Success = 1 THEN 1
        ELSE excluded.Success END,
    Message = CASE
        WHEN BackupSessions.Success = 1 AND excluded.Success = 0 THEN BackupSessions.Message
        ELSE excluded.Message END;", connection, transaction);
                command.Parameters.AddWithValue("@AgentID", manifest.AgentID);
                command.Parameters.AddWithValue("@SessionName", manifest.SessionName);
                command.Parameters.AddWithValue("@BackupType", manifest.BackupType);
                command.Parameters.AddWithValue("@StoragePath", storagePath);
                command.Parameters.AddWithValue("@StartedAtUtc", manifest.StartedAtUtc.ToString("O"));
                command.Parameters.AddWithValue("@CompletedAtUtc", manifest.CompletedAtUtc.ToString("O"));
                command.Parameters.AddWithValue("@Success", success ? 1 : 0);
                command.Parameters.AddWithValue("@Message", message ?? string.Empty);
                await command.ExecuteNonQueryAsync();

                if (success)
                {
                    if (manifest.BackupType.Equals("FIRST", StringComparison.OrdinalIgnoreCase))
                    {
                        using SQLiteCommand clearCommand = new SQLiteCommand(
                            "DELETE FROM BackupFileInventory WHERE AgentID = @AgentID;",
                            connection,
                            transaction);
                        clearCommand.Parameters.AddWithValue("@AgentID", manifest.AgentID);
                        await clearCommand.ExecuteNonQueryAsync();
                    }

                    using SQLiteCommand upsertCommand = new SQLiteCommand(@"
INSERT INTO BackupFileInventory
    (AgentID, SourcePath, FileName, RelativeStoragePath, Size, LastWriteTimeUtc, ContentSha256, IsDeleted, UpdatedSession)
VALUES
    (@AgentID, @SourcePath, @FileName, @RelativeStoragePath, @Size, @LastWriteTimeUtc, @ContentSha256, 0, @UpdatedSession)
ON CONFLICT(AgentID, SourcePath) DO UPDATE SET
    FileName = excluded.FileName,
    RelativeStoragePath = excluded.RelativeStoragePath,
    Size = excluded.Size,
    LastWriteTimeUtc = excluded.LastWriteTimeUtc,
    ContentSha256 = excluded.ContentSha256,
    IsDeleted = 0,
    UpdatedSession = excluded.UpdatedSession;", connection, transaction);

                    SQLiteParameter sourcePathParameter = upsertCommand.Parameters.Add("@SourcePath", System.Data.DbType.String);
                    SQLiteParameter fileNameParameter = upsertCommand.Parameters.Add("@FileName", System.Data.DbType.String);
                    SQLiteParameter relativePathParameter = upsertCommand.Parameters.Add("@RelativeStoragePath", System.Data.DbType.String);
                    SQLiteParameter sizeParameter = upsertCommand.Parameters.Add("@Size", System.Data.DbType.Int64);
                    SQLiteParameter lastWriteParameter = upsertCommand.Parameters.Add("@LastWriteTimeUtc", System.Data.DbType.String);
                    SQLiteParameter hashParameter = upsertCommand.Parameters.Add("@ContentSha256", System.Data.DbType.String);
                    upsertCommand.Parameters.AddWithValue("@AgentID", manifest.AgentID);
                    upsertCommand.Parameters.AddWithValue("@UpdatedSession", manifest.SessionName);

                    foreach (BackupManifestEntry entry in manifest.Created.Concat(manifest.Modified))
                    {
                        sourcePathParameter.Value = entry.SourcePath;
                        fileNameParameter.Value = System.IO.Path.GetFileName(entry.SourcePath);
                        relativePathParameter.Value = entry.RelativeStoragePath;
                        sizeParameter.Value = entry.Size;
                        lastWriteParameter.Value = entry.LastWriteTimeUtc.ToString("O");
                        hashParameter.Value = entry.ContentSha256 ?? string.Empty;
                        await upsertCommand.ExecuteNonQueryAsync();
                    }

                    using SQLiteCommand deleteCommand = new SQLiteCommand(@"
UPDATE BackupFileInventory
SET IsDeleted = 1, UpdatedSession = @UpdatedSession
WHERE AgentID = @AgentID AND SourcePath = @SourcePath;", connection, transaction);
                    SQLiteParameter deletedPathParameter = deleteCommand.Parameters.Add("@SourcePath", System.Data.DbType.String);
                    deleteCommand.Parameters.AddWithValue("@AgentID", manifest.AgentID);
                    deleteCommand.Parameters.AddWithValue("@UpdatedSession", manifest.SessionName);
                    foreach (BackupManifestEntry entry in manifest.Deleted)
                    {
                        deletedPathParameter.Value = entry.SourcePath;
                        await deleteCommand.ExecuteNonQueryAsync();
                    }
                }

                transaction.Commit();
            }
            finally
            {
                DbLock.Release();
            }
        }

        /// <summary>
        /// BO SUNG MODULE BACKUP - SYNTHETIC FULL:
        /// Doc inventory theo lo nho de Control co the tao Full ma khong nap toan bo danh sach vao RAM.
        /// </summary>
        public static async Task<List<BackupInventoryRecord>> GetLiveInventoryBatchAsync(
            string agentId,
            string afterSourcePath,
            int batchSize)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(@"
SELECT i.SourcePath,
       i.RelativeStoragePath,
       i.Size,
       i.LastWriteTimeUtc,
       i.ContentSha256,
       s.StoragePath
FROM BackupFileInventory i
INNER JOIN BackupSessions s
    ON s.AgentID = i.AgentID AND s.SessionName = i.UpdatedSession
WHERE i.AgentID = @AgentID
  AND i.IsDeleted = 0
  AND i.SourcePath > @AfterSourcePath COLLATE NOCASE
ORDER BY i.SourcePath COLLATE NOCASE
LIMIT @BatchSize;", connection);
                command.Parameters.AddWithValue("@AgentID", agentId);
                command.Parameters.AddWithValue("@AfterSourcePath", afterSourcePath ?? string.Empty);
                command.Parameters.AddWithValue("@BatchSize", Math.Max(1, batchSize));

                List<BackupInventoryRecord> records = new List<BackupInventoryRecord>();
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    records.Add(new BackupInventoryRecord
                    {
                        SourcePath = reader.GetString(0),
                        RelativeStoragePath = reader.GetString(1),
                        Size = reader.GetInt64(2),
                        LastWriteTimeUtc = DateTime.Parse(
                            reader.GetString(3),
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.RoundtripKind),
                        ContentSha256 = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        SourceSessionRoot = reader.GetString(5)
                    });
                }

                return records;
            }
            finally
            {
                DbLock.Release();
            }
        }

        /// <summary>
        /// BO SUNG MODULE BACKUP - SYNTHETIC FULL:
        /// Tra ve noi luu neu phien Synthetic Full da duoc chot thanh cong.
        /// </summary>
        public static async Task<string?> GetSuccessfulSessionStoragePathAsync(string agentId, string sessionName)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteCommand command = new SQLiteCommand(@"
SELECT StoragePath
FROM BackupSessions
WHERE AgentID = @AgentID AND SessionName = @SessionName AND Success = 1
LIMIT 1;", connection);
                command.Parameters.AddWithValue("@AgentID", agentId);
                command.Parameters.AddWithValue("@SessionName", sessionName);
                object? value = await command.ExecuteScalarAsync();
                return value == null || value == DBNull.Value ? null : value.ToString();
            }
            finally
            {
                DbLock.Release();
            }
        }

        /// <summary>
        /// BO SUNG MODULE BACKUP - SYNTHETIC FULL:
        /// Ghi nhan Full da hoan tat va doi moc cua inventory sang Full moi trong cung transaction.
        /// </summary>
        public static async Task SaveSyntheticFullAsync(
            string agentId,
            string sessionName,
            string storagePath,
            DateTime startedAtUtc,
            DateTime completedAtUtc,
            string message)
        {
            await InitializeAsync();
            await DbLock.WaitAsync();
            try
            {
                using SQLiteConnection connection = await OpenConnectionAsync();
                using SQLiteTransaction transaction = connection.BeginTransaction();
                using (SQLiteCommand command = new SQLiteCommand(@"
INSERT INTO BackupSessions
    (AgentID, SessionName, BackupType, StoragePath, StartedAtUtc, CompletedAtUtc, Success, Message)
VALUES
    (@AgentID, @SessionName, 'FIRST', @StoragePath, @StartedAtUtc, @CompletedAtUtc, 1, @Message)
ON CONFLICT(AgentID, SessionName) DO UPDATE SET
    BackupType = 'FIRST',
    StoragePath = excluded.StoragePath,
    StartedAtUtc = excluded.StartedAtUtc,
    CompletedAtUtc = excluded.CompletedAtUtc,
    Success = 1,
    Message = excluded.Message;", connection, transaction))
                {
                    command.Parameters.AddWithValue("@AgentID", agentId);
                    command.Parameters.AddWithValue("@SessionName", sessionName);
                    command.Parameters.AddWithValue("@StoragePath", storagePath);
                    command.Parameters.AddWithValue("@StartedAtUtc", startedAtUtc.ToString("O"));
                    command.Parameters.AddWithValue("@CompletedAtUtc", completedAtUtc.ToString("O"));
                    command.Parameters.AddWithValue("@Message", message ?? string.Empty);
                    await command.ExecuteNonQueryAsync();
                }

                using (SQLiteCommand rebaseCommand = new SQLiteCommand(@"
UPDATE BackupFileInventory
SET UpdatedSession = @SessionName
WHERE AgentID = @AgentID AND IsDeleted = 0;", connection, transaction))
                {
                    rebaseCommand.Parameters.AddWithValue("@AgentID", agentId);
                    rebaseCommand.Parameters.AddWithValue("@SessionName", sessionName);
                    await rebaseCommand.ExecuteNonQueryAsync();
                }

                transaction.Commit();
            }
            finally
            {
                DbLock.Release();
            }
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

    /// <summary>
    /// BO SUNG MODULE BACKUP - SYNTHETIC FULL: mot dong inventory da kem noi chua file hien tai.
    /// </summary>
    internal sealed class BackupInventoryRecord
    {
        public string SourcePath { get; set; } = string.Empty;
        public string RelativeStoragePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string ContentSha256 { get; set; } = string.Empty;
        public string SourceSessionRoot { get; set; } = string.Empty;
    }
}
