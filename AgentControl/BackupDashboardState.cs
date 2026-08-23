using AgentShared;

namespace AgentControl
{
    internal enum BackupDashboardProgressMode
    {
        None,
        Sending,
        Disconnected,
        Waiting,
        Error
    }

    internal sealed class BackupDashboardAgentState
    {
        private DateTime? _lastSpeedSampleUtc;
        private long _lastTransferredBytes;

        public string AgentId { get; }
        public string MachineName { get; private set; } = string.Empty;
        public string UserName { get; private set; } = string.Empty;
        public string OsDisplay { get; private set; } = "Khác";
        public BackupConfiguration? Configuration { get; private set; }
        public bool IsOnline { get; private set; }
        public bool HasActiveSession { get; private set; }
        public string ActiveSessionName { get; private set; } = string.Empty;
        public string BackupType { get; private set; } = string.Empty;
        public DateTime? StartedAtUtc { get; private set; }
        public DateTime? LastSuccessfulSessionStartedAtUtc { get; private set; }
        public long PlannedFileCount { get; private set; }
        public long ProcessedFileCount { get; private set; }
        public long PlannedTotalBytes { get; private set; }
        public long ProcessedBytes { get; private set; }
        public long TransferredBytes { get; private set; }
        public int ProgressPercentage { get; private set; }
        public string CurrentFile { get; private set; } = string.Empty;
        public double BytesPerSecond { get; private set; }
        public BackupDashboardProgressMode ProgressMode { get; private set; }
        public string StatusText { get; private set; } = string.Empty;
        public string ProgressDisplayText => ProgressMode == BackupDashboardProgressMode.Waiting
            ? LastSuccessfulSessionStartedAtUtc.HasValue
                ? $"HOÀN THÀNH {LastSuccessfulSessionStartedAtUtc.Value.ToLocalTime():yyyy-MM-dd}"
                : "CHƯA BACKUP"
            : $"{ProgressPercentage}%";

        public BackupDashboardAgentState(string agentId)
        {
            AgentId = agentId?.Trim() ?? string.Empty;
        }

        public void UpdateAgent(string machineName, string userName, string osVersion)
        {
            MachineName = machineName?.Trim() ?? string.Empty;
            UserName = userName?.Trim() ?? string.Empty;
            OsDisplay = NormalizeOs(osVersion);
        }

        public void SetConfiguration(BackupConfiguration? configuration)
        {
            Configuration = configuration;
            RefreshVisualStatus();
        }

        public void SetLastSuccessfulSession(DateTime? startedAtUtc)
        {
            if (!startedAtUtc.HasValue ||
                !LastSuccessfulSessionStartedAtUtc.HasValue ||
                startedAtUtc.Value >= LastSuccessfulSessionStartedAtUtc.Value)
            {
                LastSuccessfulSessionStartedAtUtc = startedAtUtc;
            }
            RefreshVisualStatus();
        }

        public void SetOnline(bool online)
        {
            IsOnline = online;
            BytesPerSecond = 0;
            _lastSpeedSampleUtc = null;
            _lastTransferredBytes = TransferredBytes;
            RefreshVisualStatus();
        }

        public bool StartSession(BackupSessionBegin begin)
        {
            if (begin == null ||
                !AgentId.Equals(begin.AgentID, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(begin.SessionName) ||
                begin.PlannedFileCount < 0 ||
                begin.PlannedTotalBytes < 0)
            {
                return false;
            }

            HasActiveSession = true;
            ActiveSessionName = begin.SessionName;
            BackupType = begin.BackupType ?? string.Empty;
            StartedAtUtc = begin.StartedAtUtc;
            PlannedFileCount = begin.PlannedFileCount;
            PlannedTotalBytes = begin.PlannedTotalBytes;
            ProcessedFileCount = 0;
            ProcessedBytes = 0;
            TransferredBytes = 0;
            ProgressPercentage = 0;
            CurrentFile = string.Empty;
            BytesPerSecond = 0;
            _lastSpeedSampleUtc = null;
            _lastTransferredBytes = 0;
            IsOnline = true;
            ProgressMode = BackupDashboardProgressMode.Sending;
            StatusText = "ĐANG GỬI";
            return true;
        }

        public bool ApplyProgress(BackupProgressUpdate update, DateTime observedAtUtc)
        {
            if (update == null || !HasActiveSession ||
                !AgentId.Equals(update.AgentID, StringComparison.OrdinalIgnoreCase) ||
                !ActiveSessionName.Equals(update.SessionName, StringComparison.OrdinalIgnoreCase) ||
                update.PlannedFileCount < 0 || update.ProcessedFileCount < 0 ||
                update.PlannedTotalBytes < 0 || update.ProcessedBytes < 0 ||
                update.TransferredBytes < 0)
            {
                return false;
            }

            PlannedFileCount = update.PlannedFileCount;
            PlannedTotalBytes = update.PlannedTotalBytes;
            ProcessedFileCount = update.PlannedFileCount > 0
                ? Math.Min(update.ProcessedFileCount, update.PlannedFileCount)
                : update.ProcessedFileCount;
            ProcessedBytes = update.PlannedTotalBytes > 0
                ? Math.Min(update.ProcessedBytes, update.PlannedTotalBytes)
                : update.ProcessedBytes;
            ProgressPercentage = Math.Clamp(update.ProgressPercentage, 0, 100);
            CurrentFile = update.CurrentFile?.Trim() ?? string.Empty;
            BackupType = update.BackupType ?? BackupType;
            if (StartedAtUtc == null || StartedAtUtc == default)
            {
                StartedAtUtc = update.StartedAtUtc;
            }

            UpdateSpeed(update.TransferredBytes, observedAtUtc);
            TransferredBytes = update.TransferredBytes;
            IsOnline = true;
            ProgressMode = BackupDashboardProgressMode.Sending;
            StatusText = "ĐANG GỬI";
            return true;
        }

        public void CompleteSession(DateTime startedAtUtc)
        {
            if (!LastSuccessfulSessionStartedAtUtc.HasValue ||
                startedAtUtc >= LastSuccessfulSessionStartedAtUtc.Value)
            {
                LastSuccessfulSessionStartedAtUtc = startedAtUtc;
            }
            HasActiveSession = false;
            ActiveSessionName = string.Empty;
            ProgressPercentage = 100;
            CurrentFile = string.Empty;
            BytesPerSecond = 0;
            _lastSpeedSampleUtc = null;
            RefreshVisualStatus();
        }

        public void FailSession()
        {
            HasActiveSession = false;
            ActiveSessionName = string.Empty;
            BytesPerSecond = 0;
            _lastSpeedSampleUtc = null;
            if (IsOnline)
            {
                ProgressMode = BackupDashboardProgressMode.Error;
                StatusText = "LỖI BACKUP";
            }
            else
            {
                ProgressMode = BackupDashboardProgressMode.Disconnected;
                StatusText = "MẤT KẾT NỐI";
            }
        }

        public static string NormalizeOs(string? osVersion)
        {
            if (osVersion?.Contains("Windows 11", StringComparison.OrdinalIgnoreCase) == true)
            {
                return "Windows 11";
            }
            if (osVersion?.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) == true)
            {
                return "Windows 10";
            }
            return "Khác";
        }

        private void UpdateSpeed(long transferredBytes, DateTime observedAtUtc)
        {
            if (_lastSpeedSampleUtc.HasValue &&
                transferredBytes >= _lastTransferredBytes &&
                observedAtUtc > _lastSpeedSampleUtc.Value)
            {
                double seconds = (observedAtUtc - _lastSpeedSampleUtc.Value).TotalSeconds;
                BytesPerSecond = seconds > 0
                    ? (transferredBytes - _lastTransferredBytes) / seconds
                    : 0;
            }
            else
            {
                BytesPerSecond = 0;
            }

            _lastSpeedSampleUtc = observedAtUtc;
            _lastTransferredBytes = transferredBytes;
        }

        private void RefreshVisualStatus()
        {
            if (!IsOnline)
            {
                ProgressMode = BackupDashboardProgressMode.Disconnected;
                StatusText = "MẤT KẾT NỐI";
                return;
            }

            if (HasActiveSession)
            {
                ProgressMode = BackupDashboardProgressMode.Sending;
                StatusText = "ĐANG GỬI";
                return;
            }

            ProgressMode = BackupDashboardProgressMode.Waiting;
            StatusText = Configuration == null
                ? "CHƯA CẤU HÌNH BACKUP"
                : "ĐANG CHỜ ĐẾN GIỜ BACKUP";
        }
    }
}
