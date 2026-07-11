namespace AgentShared
{
    public static class AgentUpdatePacketTypes
    {
        public const string UpdateAgent = "UPDATE_AGENT";
        public const string UpdateAgentFileBegin = "UPDATE_AGENT_FILE_BEGIN";
        public const string UpdateAgentFileChunk = "UPDATE_AGENT_FILE_CHUNK";
        public const string UpdateAgentFileEnd = "UPDATE_AGENT_FILE_END";
        public const string UpdateAgentApply = "UPDATE_AGENT_APPLY";
        public const string UpdateAgentStatus = "UPDATE_AGENT_STATUS";
    }

    public static class AgentUpdateFileRoles
    {
        public const string Service = "Service";
        public const string Updater = "Updater";
    }

    public sealed class AgentUpdateRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string TargetVersion { get; set; } = string.Empty;
        public string ServiceFileName { get; set; } = string.Empty;
        public long ServiceFileSize { get; set; }
        public string ServiceSha256 { get; set; } = string.Empty;
        public string UpdaterFileName { get; set; } = string.Empty;
        public long UpdaterFileSize { get; set; }
        public string UpdaterSha256 { get; set; } = string.Empty;
        public string RequestedAt { get; set; } = string.Empty;
    }

    public sealed class AgentUpdateFileBegin
    {
        public string SessionId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    public sealed class AgentUpdateFileChunk
    {
        public string SessionId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public long Offset { get; set; }
        public string Base64Data { get; set; } = string.Empty;
    }

    public sealed class AgentUpdateFileEnd
    {
        public string SessionId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public sealed class AgentUpdateApply
    {
        public string SessionId { get; set; } = string.Empty;
        public string TargetVersion { get; set; } = string.Empty;
        public string ServiceName { get; set; } = "AgentServices";
    }

    public sealed class AgentUpdateStatus
    {
        public string SessionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
    }

    public sealed class AgentUpdateCompletionMarker
    {
        public string SessionId { get; set; } = string.Empty;
        public string TargetVersion { get; set; } = string.Empty;
        public string CompletedAt { get; set; } = string.Empty;
    }
}
