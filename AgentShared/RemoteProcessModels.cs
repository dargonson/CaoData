namespace AgentShared
{
    public static class RemoteProcessPacketTypes
    {
        public const string MsiPreflightRequest = "REMOTE_MSI_PREFLIGHT";
        public const string MsiPreflightResponse = "REMOTE_MSI_PREFLIGHT_RESPONSE";
        public const string RunRequest = "RUN_REMOTE_PROCESS";
        public const string RunStatus = "RUN_REMOTE_PROCESS_STATUS";
    }

    public static class RemoteProcessStatuses
    {
        public const string Waiting = "ĐANG CHỜ";
        public const string Running = "ĐANG CHẠY";
        public const string Completed = "HOÀN THÀNH";
        public const string Error = "LỖI";
    }

    public static class RemoteProcessRules
    {
        private static readonly HashSet<string> SupportedExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".exe",
                ".msi",
                ".bat",
                ".cmd"
            };

        public const int MaximumArgumentsLength = 4096;

        public static bool IsSupportedFile(string? path) =>
            !string.IsNullOrWhiteSpace(path) &&
            SupportedExtensions.Contains(Path.GetExtension(path));

        public static string SupportedExtensionsDisplay => ".exe, .msi, .bat, .cmd";
    }

    public sealed class RemoteProcessRequest
    {
        public string RequestId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool TerminateExistingMsiProcesses { get; set; }
        public DateTime RequestedAtUtc { get; set; }
    }

    public sealed class RemoteMsiPreflightRequest
    {
        public string RequestId { get; set; } = string.Empty;
    }

    public sealed class RemoteMsiPreflightResponse
    {
        public string RequestId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public List<int> ProcessIds { get; set; } = new();
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public sealed class RemoteProcessStatus
    {
        public string RequestId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int? ProcessId { get; set; }
        public int? ExitCode { get; set; }
        public int? ErrorCode { get; set; }
        public bool RequiresRestart { get; set; }
        public string DiagnosticDetails { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
    }
}
