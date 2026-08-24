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
        private bool _latestSessionCompleted;

        public string AgentId { get; }
        public string MachineName { get; private set; } = string.Empty;
        public string OwnerName { get; private set; } = string.Empty;
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
        public bool CanManageConfiguration => Configuration != null && IsOnline && !HasActiveSession;
        public string ProgressDisplayText => _latestSessionCompleted && LastSuccessfulSessionStartedAtUtc.HasValue
            ? $"HOÀN THÀNH {LastSuccessfulSessionStartedAtUtc.Value.ToLocalTime():yyyy-MM-dd}"
            : ProgressMode == BackupDashboardProgressMode.Waiting
                ? "CHƯA BACKUP"
                : $"{ProgressPercentage}%";

        public BackupDashboardAgentState(string agentId)
        {
            AgentId = agentId?.Trim() ?? string.Empty;
        }

        public void UpdateAgent(string machineName, string ownerName, string osVersion)
        {
            MachineName = machineName?.Trim() ?? string.Empty;
            OwnerName = ownerName?.Trim() ?? string.Empty;
            OsDisplay = NormalizeOs(osVersion);
        }

        public void SetConfiguration(BackupConfiguration? configuration)
        {
            Configuration = configuration;
            RefreshVisualStatus();
        }

        public void ResetBackupConfigurationAndHistory()
        {
            Configuration = null;
            HasActiveSession = false;
            ActiveSessionName = string.Empty;
            BackupType = string.Empty;
            StartedAtUtc = null;
            LastSuccessfulSessionStartedAtUtc = null;
            _latestSessionCompleted = false;
            PlannedFileCount = 0;
            ProcessedFileCount = 0;
            PlannedTotalBytes = 0;
            ProcessedBytes = 0;
            TransferredBytes = 0;
            ProgressPercentage = 0;
            CurrentFile = string.Empty;
            BytesPerSecond = 0;
            _lastSpeedSampleUtc = null;
            _lastTransferredBytes = 0;
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
            if (startedAtUtc.HasValue &&
                !HasActiveSession &&
                (!StartedAtUtc.HasValue || startedAtUtc.Value >= StartedAtUtc.Value))
            {
                _latestSessionCompleted = true;
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

            bool isResumingSameSession = HasActiveSession &&
                ActiveSessionName.Equals(begin.SessionName, StringComparison.OrdinalIgnoreCase) &&
                StartedAtUtc == begin.StartedAtUtc;
            if (isResumingSameSession)
            {
                BackupType = begin.BackupType ?? BackupType;
                PlannedFileCount = begin.PlannedFileCount;
                PlannedTotalBytes = begin.PlannedTotalBytes;
                IsOnline = true;
                BytesPerSecond = 0;
                _lastSpeedSampleUtc = null;
                _lastTransferredBytes = TransferredBytes;
                ProgressMode = BackupDashboardProgressMode.Sending;
                StatusText = "ĐANG GỬI";
                return true;
            }

            HasActiveSession = true;
            _latestSessionCompleted = false;
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

        public bool RestoreSnapshot(BackupDashboardSnapshot? snapshot)
        {
            if (snapshot == null ||
                !AgentId.Equals(snapshot.AgentId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(snapshot.SessionName) ||
                snapshot.StartedAtUtc == default ||
                snapshot.PlannedFileCount < 0 || snapshot.ProcessedFileCount < 0 ||
                snapshot.PlannedTotalBytes < 0 || snapshot.ProcessedBytes < 0 ||
                snapshot.TransferredBytes < 0)
            {
                return false;
            }

            ActiveSessionName = snapshot.SessionName.Trim();
            BackupType = snapshot.BackupType?.Trim() ?? string.Empty;
            StartedAtUtc = snapshot.StartedAtUtc;
            PlannedFileCount = snapshot.PlannedFileCount;
            ProcessedFileCount = snapshot.PlannedFileCount > 0
                ? Math.Min(snapshot.ProcessedFileCount, snapshot.PlannedFileCount)
                : snapshot.ProcessedFileCount;
            PlannedTotalBytes = snapshot.PlannedTotalBytes;
            ProcessedBytes = snapshot.PlannedTotalBytes > 0
                ? Math.Min(snapshot.ProcessedBytes, snapshot.PlannedTotalBytes)
                : snapshot.ProcessedBytes;
            TransferredBytes = snapshot.TransferredBytes;
            ProgressPercentage = Math.Clamp(snapshot.ProgressPercentage, 0, 100);
            CurrentFile = snapshot.CurrentFile?.Trim() ?? string.Empty;
            HasActiveSession = snapshot.SessionState == BackupDashboardSessionState.Active;
            _latestSessionCompleted = snapshot.SessionState == BackupDashboardSessionState.Completed;
            if (_latestSessionCompleted &&
                (!LastSuccessfulSessionStartedAtUtc.HasValue ||
                 snapshot.StartedAtUtc >= LastSuccessfulSessionStartedAtUtc.Value))
            {
                LastSuccessfulSessionStartedAtUtc = snapshot.StartedAtUtc;
            }
            if (!HasActiveSession)
            {
                ActiveSessionName = string.Empty;
            }
            BytesPerSecond = 0;
            _lastSpeedSampleUtc = null;
            _lastTransferredBytes = TransferredBytes;
            RefreshVisualStatus();
            if (snapshot.SessionState == BackupDashboardSessionState.Failed && IsOnline)
            {
                ProgressMode = BackupDashboardProgressMode.Error;
                StatusText = "LỖI BACKUP";
            }
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
            _latestSessionCompleted = true;
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
            _latestSessionCompleted = false;
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
            if (_latestSessionCompleted)
            {
                ProgressMode = BackupDashboardProgressMode.Waiting;
                StatusText = Configuration == null
                    ? "CHƯA CẤU HÌNH BACKUP"
                    : "ĐANG CHỜ ĐẾN GIỜ BACKUP";
                return;
            }

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
