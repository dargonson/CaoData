namespace AgentService
{
    /// <summary>
    /// BO SUNG MODULE BACKUP: du lieu runtime cua Agent co thu muc ro rang, on dinh
    /// va co the override khi test/deploy.
    /// </summary>
    internal static class AgentDataPaths
    {
        internal static string DataRoot
        {
            get
            {
                string? overridden = Environment.GetEnvironmentVariable("CAODATA_AGENT_DATA_ROOT");
                string root = !string.IsNullOrWhiteSpace(overridden)
                    ? overridden
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "CaoData",
                        "AgentServices");
                root = Path.GetFullPath(root);
                Directory.CreateDirectory(root);
                return root;
            }
        }

        internal static string GetBackupStatePath(string safeAgentId)
        {
            string stateRoot = Path.Combine(DataRoot, "BackupState");
            Directory.CreateDirectory(stateRoot);
            string destination = Path.Combine(stateRoot, safeAgentId + ".json");
            if (!File.Exists(destination))
            {
                string legacy = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Intel",
                    "Driver",
                    "BackupState",
                    safeAgentId + ".json");
                if (File.Exists(legacy))
                {
                    File.Copy(legacy, destination, overwrite: false);
                }
            }

            return destination;
        }
    }
}
