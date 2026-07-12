namespace AgentShared
{
    public static class AppVersion
    {
        public const string CurrentVersionControl = "1.4";
        public const string CurrentVersionAgent = "1.4";

        public const string AgentUpdateRootDirectory = @"C:\ProgramData\Intel\Driver\Updates";
        public const string AgentUpdateCompletionMarkerFileName = "pending-update-complete.json";
        public const string AgentUpdaterLogFileName = "AgentUpdater.log";

        public static string GetAgentUpdateRootDirectory()
        {
            return AgentUpdateRootDirectory;
        }

        public static string GetAgentUpdateSessionDirectory(string sessionId)
        {
            return System.IO.Path.Combine(GetAgentUpdateRootDirectory(), sessionId);
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
