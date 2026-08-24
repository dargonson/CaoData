using System.Data.SQLite;
using System.Text.Json;

namespace AgentControl
{
    /// <summary>
    /// BO SUNG MODULE BACKUP - XOA CAU HINH:
    /// Xuat du lieu backup co tham quyen ra JSON theo kieu streaming truoc khi xoa DB.
    /// Khong cham vao cac thu muc FIRST/INC/Synthetic Full vat ly.
    /// </summary>
    internal static class BackupConfigurationArchiveService
    {
        private static readonly string[] BackupTables =
        {
            "BackupConfigs",
            "BackupSessions",
            "BackupDashboardSnapshots",
            "BackupFileInventory",
            "FirstBackupRuns",
            "FirstBackupFiles",
            "FirstBackupSkipped"
        };

        public static async Task<string> ExportAsync(
            string agentId,
            string machineName,
            string ownerName,
            string storageRoot,
            CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                throw new ArgumentException("AgentID không hợp lệ.", nameof(agentId));
            }

            string root = Path.GetFullPath(storageRoot ?? string.Empty);
            Directory.CreateDirectory(root);
            await FirstBackupStore.InitializeAsync();
            await BackupDatabase.Gate.WaitAsync(token).ConfigureAwait(false);
            string safeAgentId = SanitizeFileName(agentId);
            string finalPath = CreateUniqueArchivePath(root, safeAgentId);
            string tempPath = finalPath + ".tmp";

            try
            {
                using SQLiteConnection connection = new SQLiteConnection(BackupDatabase.ConnectionString);
                connection.Open();
                using (SQLiteCommand pragma = new SQLiteCommand("PRAGMA busy_timeout=5000;", connection))
                {
                    pragma.ExecuteNonQuery();
                }

                await using (FileStream stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    using (Utf8JsonWriter writer = new Utf8JsonWriter(
                        stream,
                        new JsonWriterOptions { Indented = true }))
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("SchemaVersion", 1);
                        writer.WriteString("ArchiveType", "AgentBackupConfigurationAndHistory");
                        writer.WriteString("ExportedAtUtc", DateTime.UtcNow);
                        writer.WriteString("AgentID", agentId);
                        writer.WriteString("MachineName", machineName ?? string.Empty);
                        writer.WriteString("OwnerName", ownerName ?? string.Empty);
                        writer.WriteBoolean("PhysicalBackupFoldersPreserved", true);
                        writer.WriteBoolean("DerivedRecoveryCacheIncluded", false);
                        writer.WriteStartObject("Tables");

                        foreach (string table in BackupTables)
                        {
                            token.ThrowIfCancellationRequested();
                            WriteTable(writer, connection, table, agentId, token);
                        }

                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        writer.Flush();
                    }

                    await stream.FlushAsync(token).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(tempPath, finalPath, overwrite: false);
                return finalPath;
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                throw;
            }
            finally
            {
                BackupDatabase.Gate.Release();
            }
        }

        public static async Task DeleteDatabaseStateAsync(string agentId, CancellationToken token = default)
        {
            await FirstBackupStore.InitializeAsync();
            await BackupDatabase.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using SQLiteConnection connection = new SQLiteConnection(BackupDatabase.ConnectionString);
                connection.Open();
                using SQLiteTransaction transaction = connection.BeginTransaction();
                foreach (string table in new[]
                {
                    "FirstBackupFiles",
                    "FirstBackupSkipped",
                    "FirstBackupRuns",
                    "BackupFileInventory",
                    "BackupDashboardSnapshots",
                    "BackupSessions",
                    "BackupConfigs"
                })
                {
                    using SQLiteCommand command = new SQLiteCommand(
                        $"DELETE FROM {table} WHERE AgentID = @AgentID;",
                        connection,
                        transaction);
                    command.Parameters.AddWithValue("@AgentID", agentId);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            finally
            {
                BackupDatabase.Gate.Release();
            }
        }

        private static void WriteTable(
            Utf8JsonWriter writer,
            SQLiteConnection connection,
            string table,
            string agentId,
            CancellationToken token)
        {
            writer.WriteStartArray(table);
            using SQLiteCommand command = new SQLiteCommand(
                $"SELECT * FROM {table} WHERE AgentID = @AgentID;",
                connection);
            command.Parameters.AddWithValue("@AgentID", agentId);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                for (int index = 0; index < reader.FieldCount; index++)
                {
                    WriteValue(writer, reader.GetName(index), reader.GetValue(index));
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static void WriteValue(Utf8JsonWriter writer, string name, object value)
        {
            switch (value)
            {
                case null:
                case DBNull:
                    writer.WriteNull(name);
                    break;
                case byte[] bytes:
                    writer.WriteBase64String(name, bytes);
                    break;
                case bool boolean:
                    writer.WriteBoolean(name, boolean);
                    break;
                case byte or sbyte or short or ushort or int or uint or long:
                    writer.WriteNumber(name, Convert.ToInt64(value));
                    break;
                case ulong unsigned:
                    writer.WriteNumber(name, unsigned);
                    break;
                case float or double or decimal:
                    writer.WriteNumber(name, Convert.ToDecimal(value));
                    break;
                default:
                    writer.WriteString(name, Convert.ToString(value) ?? string.Empty);
                    break;
            }
        }

        private static string SanitizeFileName(string value)
        {
            string result = value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '_');
            }
            return string.IsNullOrWhiteSpace(result) ? "Agent" : result;
        }

        private static string CreateUniqueArchivePath(string root, string safeAgentId)
        {
            string prefix = $"BACKUP-ARCHIVE-{safeAgentId}-{DateTime.Now:yyyyMMdd-HHmmss}";
            for (int suffix = 0; suffix < 1000; suffix++)
            {
                string fileName = suffix == 0
                    ? prefix + ".json"
                    : $"{prefix}-{suffix:000}.json";
                string candidate = Path.Combine(root, fileName);
                if (!File.Exists(candidate) && !File.Exists(candidate + ".tmp"))
                {
                    return candidate;
                }
            }
            throw new IOException("Không thể tạo tên file archive backup duy nhất.");
        }
    }
}
