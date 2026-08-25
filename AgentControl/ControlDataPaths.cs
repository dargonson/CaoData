using System.Data.SQLite;

namespace AgentControl
{
    /// <summary>
    /// Duong dan du lieu on dinh cua Control, khong phu thuoc working directory.
    /// Tu dong copy DB cu neu day la lan chay dau sau nang cap.
    /// </summary>
    internal static class ControlDataPaths
    {
        private static readonly object SyncRoot = new object();

        public static string DataRoot
        {
            get
            {
                string? overridden = Environment.GetEnvironmentVariable("CAODATA_CONTROL_DATA_ROOT");
                string root = !string.IsNullOrWhiteSpace(overridden)
                    ? overridden
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CaoData",
                        "AgentControl");
                root = Path.GetFullPath(root);
                Directory.CreateDirectory(root);
                return root;
            }
        }

        public static string GetDatabasePath(string fileName)
        {
            string safeName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                throw new InvalidDataException("Tên database không hợp lệ.");
            }

            string destination = Path.Combine(DataRoot, safeName);
            lock (SyncRoot)
            {
                if (!File.Exists(destination))
                {
                    foreach (string legacy in GetLegacyCandidates(safeName))
                    {
                        if (!File.Exists(legacy) ||
                            Path.GetFullPath(legacy).Equals(destination, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        MigrateSqliteDatabase(legacy, destination);
                        break;
                    }
                }
            }
            return destination;
        }

        public static string ServerCertificatePath =>
            Path.Combine(DataRoot, "AgentControl.transport.pfx");

        private static IEnumerable<string> GetLegacyCandidates(string fileName)
        {
            yield return Path.Combine(Environment.CurrentDirectory, fileName);
            yield return Path.Combine(AppContext.BaseDirectory, fileName);
        }

        private static void MigrateSqliteDatabase(string sourcePath, string destinationPath)
        {
            string temporaryPath = destinationPath + ".migrating-" + Guid.NewGuid().ToString("N");
            try
            {
                var sourceBuilder = new SQLiteConnectionStringBuilder
                {
                    DataSource = sourcePath,
                    Version = 3,
                    ReadOnly = true,
                    DefaultTimeout = 30
                };
                var destinationBuilder = new SQLiteConnectionStringBuilder
                {
                    DataSource = temporaryPath,
                    Version = 3,
                    DefaultTimeout = 30
                };

                using (SQLiteConnection source = new SQLiteConnection(sourceBuilder.ConnectionString))
                using (SQLiteConnection target = new SQLiteConnection(destinationBuilder.ConnectionString))
                {
                    source.Open();
                    target.Open();
                    // SQLite Backup API doc ca file WAL dang con transaction da commit,
                    // tranh mat du lieu khi chi File.Copy file .db cu.
                    source.BackupDatabase(target, "main", "main", -1, null, 0);
                }

                File.Move(temporaryPath, destinationPath, overwrite: false);
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
                throw;
            }
        }
    }
}
