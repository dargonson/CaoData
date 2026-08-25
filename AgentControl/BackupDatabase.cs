using System.Data.SQLite;

namespace AgentControl
{
    /// <summary>
    /// BO SUNG MODULE BACKUP: dung chung mot duong dan va mot khoa ghi cho tat ca
    /// thanh phan truy cap BackupManagement.db.
    /// </summary>
    internal static class BackupDatabase
    {
        internal static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        internal static string ConnectionString
        {
            get
            {
                var builder = new SQLiteConnectionStringBuilder
                {
                    DataSource = ControlDataPaths.GetDatabasePath("BackupManagement.db"),
                    Version = 3,
                    DefaultTimeout = 30
                };
                return builder.ConnectionString + ";BusyTimeout=5000;";
            }
        }

        internal static async Task EnsureColumnAsync(
            SQLiteConnection connection,
            string tableName,
            string columnName,
            string definition)
        {
            using (SQLiteCommand info = new SQLiteCommand($"PRAGMA table_info({tableName});", connection))
            using (SQLiteDataReader reader = info.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            using SQLiteCommand alter = new SQLiteCommand(
                $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};",
                connection);
            await alter.ExecuteNonQueryAsync();
        }
    }
}
