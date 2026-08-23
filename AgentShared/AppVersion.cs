namespace AgentShared
{
    public static class AppVersion
    {
        public const string CurrentVersionControl = "1.9";
        public const string CurrentVersionAgent = "1.9";

        // BO SUNG DUNG CHUNG - DUONG DAN RUNTIME:
        // Khong tiep tuc ghi du lieu vao ten thu muc Intel; van copy marker/log cu mot lan
        // de qua trinh nang cap dang do khong bi mat trang thai.
        public const string AgentUpdateRootDirectory = @"C:\ProgramData\CaoData\AgentServices\Updates";
        private const string LegacyAgentUpdateRootDirectory = @"C:\ProgramData\Intel\Driver\Updates";
        public const string AgentUpdateCompletionMarkerFileName = "pending-update-complete.json";
        public const string AgentUpdaterLogFileName = "AgentUpdater.log";
        private static readonly object UpdatePathSync = new object();

        public static string GetAgentUpdateRootDirectory()
        {
            string? overriddenDataRoot = System.Environment.GetEnvironmentVariable("CAODATA_AGENT_DATA_ROOT");
            string root = !string.IsNullOrWhiteSpace(overriddenDataRoot)
                ? System.IO.Path.Combine(overriddenDataRoot, "Updates")
                : System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
                    "CaoData",
                    "AgentServices",
                    "Updates");
            root = System.IO.Path.GetFullPath(root);
            System.IO.Directory.CreateDirectory(root);
            if (string.IsNullOrWhiteSpace(overriddenDataRoot))
            {
                MigrateLegacyUpdateArtifacts(root);
            }
            return root;
        }

        public static string GetAgentUpdateSessionDirectory(string sessionId)
        {
            string safeSessionId = PathSafety.NormalizeRelativePath(sessionId);
            if (safeSessionId.Contains(System.IO.Path.DirectorySeparatorChar) ||
                safeSessionId.Contains(System.IO.Path.AltDirectorySeparatorChar))
            {
                throw new System.IO.InvalidDataException("SessionId update không hợp lệ.");
            }

            return PathSafety.GetSafeChildPath(GetAgentUpdateRootDirectory(), safeSessionId);
        }

        public static string GetAgentUpdateCompletionMarkerPath()
        {
            return System.IO.Path.Combine(GetAgentUpdateRootDirectory(), AgentUpdateCompletionMarkerFileName);
        }

        public static string GetAgentUpdaterLogPath()
        {
            return System.IO.Path.Combine(GetAgentUpdateRootDirectory(), AgentUpdaterLogFileName);
        }

        private static void MigrateLegacyUpdateArtifacts(string destinationRoot)
        {
            if (System.IO.Path.GetFullPath(LegacyAgentUpdateRootDirectory)
                .Equals(destinationRoot, System.StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Directory.Exists(LegacyAgentUpdateRootDirectory))
            {
                return;
            }

            lock (UpdatePathSync)
            {
                foreach (string fileName in new[]
                {
                    AgentUpdateCompletionMarkerFileName,
                    AgentUpdaterLogFileName
                })
                {
                    string source = System.IO.Path.Combine(LegacyAgentUpdateRootDirectory, fileName);
                    string destination = System.IO.Path.Combine(destinationRoot, fileName);
                    try
                    {
                        if (System.IO.File.Exists(source) && !System.IO.File.Exists(destination))
                        {
                            System.IO.File.Copy(source, destination, overwrite: false);
                            if (fileName.Equals(
                                AgentUpdateCompletionMarkerFileName,
                                System.StringComparison.OrdinalIgnoreCase))
                            {
                                // Marker la thong diep mot lan; xoa nguon sau khi copy de khong
                                // phat lai moi lan Agent da xu ly va xoa marker dich.
                                System.IO.File.Delete(source);
                            }
                        }
                    }
                    catch (System.IO.IOException)
                    {
                        // Mot process khac co the vua migrate; file dich hien tai duoc uu tien.
                    }
                    catch (System.UnauthorizedAccessException)
                    {
                        // Thu muc legacy khong bat buoc; khong chan Agent khoi dong.
                    }
                }
            }
        }
    }
}
