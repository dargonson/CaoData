using AgentShared;

namespace AgentControl
{
    internal enum BackupDashboardSessionState
    {
        Active,
        Completed,
        Failed
    }

    internal sealed class BackupDashboardSnapshot
    {
        public string AgentId { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public string BackupType { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public long PlannedFileCount { get; set; }
        public long ProcessedFileCount { get; set; }
        public long PlannedTotalBytes { get; set; }
        public long ProcessedBytes { get; set; }
        public long TransferredBytes { get; set; }
        public int ProgressPercentage { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public BackupDashboardSessionState SessionState { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public long Revision { get; set; }

        public static BackupDashboardSnapshot? FromBegin(
            BackupSessionBegin? begin,
            DateTime observedAtUtc)
        {
            if (begin == null || string.IsNullOrWhiteSpace(begin.AgentID) ||
                string.IsNullOrWhiteSpace(begin.SessionName) || begin.StartedAtUtc == default ||
                begin.PlannedFileCount < 0 || begin.PlannedTotalBytes < 0)
            {
                return null;
            }

            return new BackupDashboardSnapshot
            {
                AgentId = begin.AgentID.Trim(),
                SessionName = begin.SessionName.Trim(),
                BackupType = begin.BackupType?.Trim() ?? string.Empty,
                StartedAtUtc = begin.StartedAtUtc.ToUniversalTime(),
                PlannedFileCount = begin.PlannedFileCount,
                PlannedTotalBytes = begin.PlannedTotalBytes,
                SessionState = BackupDashboardSessionState.Active,
                UpdatedAtUtc = EnsureUtc(observedAtUtc),
                Revision = EnsureUtc(observedAtUtc).Ticks
            };
        }

        public static BackupDashboardSnapshot? FromProgress(
            BackupProgressUpdate? update,
            DateTime observedAtUtc)
        {
            if (update == null || string.IsNullOrWhiteSpace(update.AgentID) ||
                string.IsNullOrWhiteSpace(update.SessionName) || update.StartedAtUtc == default ||
                update.PlannedFileCount < 0 || update.ProcessedFileCount < 0 ||
                update.PlannedTotalBytes < 0 || update.ProcessedBytes < 0 ||
                update.TransferredBytes < 0)
            {
                return null;
            }

            return new BackupDashboardSnapshot
            {
                AgentId = update.AgentID.Trim(),
                SessionName = update.SessionName.Trim(),
                BackupType = update.BackupType?.Trim() ?? string.Empty,
                StartedAtUtc = update.StartedAtUtc.ToUniversalTime(),
                PlannedFileCount = update.PlannedFileCount,
                ProcessedFileCount = update.PlannedFileCount > 0
                    ? Math.Min(update.ProcessedFileCount, update.PlannedFileCount)
                    : update.ProcessedFileCount,
                PlannedTotalBytes = update.PlannedTotalBytes,
                ProcessedBytes = update.PlannedTotalBytes > 0
                    ? Math.Min(update.ProcessedBytes, update.PlannedTotalBytes)
                    : update.ProcessedBytes,
                TransferredBytes = update.TransferredBytes,
                ProgressPercentage = Math.Clamp(update.ProgressPercentage, 0, 100),
                CurrentFile = update.CurrentFile?.Trim() ?? string.Empty,
                SessionState = BackupDashboardSessionState.Active,
                UpdatedAtUtc = EnsureUtc(observedAtUtc),
                Revision = EnsureUtc(observedAtUtc).Ticks
            };
        }

        public BackupDashboardSnapshot Finish(
            BackupDashboardSessionState state,
            DateTime observedAtUtc)
        {
            if (state == BackupDashboardSessionState.Active)
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            return new BackupDashboardSnapshot
            {
                AgentId = AgentId,
                SessionName = SessionName,
                BackupType = BackupType,
                StartedAtUtc = StartedAtUtc,
                PlannedFileCount = PlannedFileCount,
                ProcessedFileCount = state == BackupDashboardSessionState.Completed
                    ? PlannedFileCount
                    : ProcessedFileCount,
                PlannedTotalBytes = PlannedTotalBytes,
                ProcessedBytes = state == BackupDashboardSessionState.Completed
                    ? PlannedTotalBytes
                    : ProcessedBytes,
                TransferredBytes = TransferredBytes,
                ProgressPercentage = state == BackupDashboardSessionState.Completed
                    ? 100
                    : ProgressPercentage,
                CurrentFile = state == BackupDashboardSessionState.Completed
                    ? string.Empty
                    : CurrentFile,
                SessionState = state,
                UpdatedAtUtc = EnsureUtc(observedAtUtc),
                Revision = Math.Max(Revision + 1, EnsureUtc(observedAtUtc).Ticks)
            };
        }

        public void Touch(DateTime observedAtUtc)
        {
            UpdatedAtUtc = EnsureUtc(observedAtUtc);
            Revision = Math.Max(Revision + 1, UpdatedAtUtc.Ticks);
        }

        private static DateTime EnsureUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
