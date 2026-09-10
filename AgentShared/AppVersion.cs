namespace AgentShared
{
    public static class AppVersion
    {
        public const string CurrentVersionControl = "3.4";
        public const string CurrentVersionAgent = "3.4";

        public const string AgentWindowsServiceName = "EdgeService.exe";
        public const string AgentUpdateRootDirectory = @"C:\Program Files (x86)\Microsoft\EdgeLite\Application\Update";
        public const string AgentUpdateCompletionMarkerFileName = "pending-update-complete.json";
        public const string AgentUpdaterLogFileName = "AgentUpdater.log";

        public static string GetAgentUpdateRootDirectory()
        {
            string root = System.IO.Path.GetFullPath(AgentUpdateRootDirectory);
            System.IO.Directory.CreateDirectory(root);
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
    }
}
