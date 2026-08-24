using AgentControl;
using AgentService;
using AgentShared;
using System.Text.Json;

namespace AgentIntegrationTests;

public sealed class BackupDashboardTests
{
    [Theory]
    [InlineData("Microsoft Windows 11 Pro", "Windows 11")]
    [InlineData("Windows 10 Enterprise 22H2", "Windows 10")]
    [InlineData("Windows Server 2022", "Khác")]
    [InlineData("Linux", "Khác")]
    [InlineData("", "Khác")]
    public void Os_IsLimitedToSupportedDashboardGroups(string raw, string expected)
    {
        Assert.Equal(expected, BackupDashboardAgentState.NormalizeOs(raw));
    }

    [Fact]
    public void ActiveBackup_DisconnectsRedAtLastPercentage_ThenResumes()
    {
        var state = ConfiguredState("agent-a");
        state.SetOnline(true);
        Assert.True(state.StartSession(Begin("agent-a", "FIRST-agent-a", 10, 1_000)));
        Assert.True(state.ApplyProgress(
            Progress("agent-a", "FIRST-agent-a", 4, 420, 420, 42),
            DateTime.UtcNow));

        state.SetOnline(false);

        Assert.Equal(42, state.ProgressPercentage);
        Assert.Equal(BackupDashboardProgressMode.Disconnected, state.ProgressMode);
        Assert.Equal("MẤT KẾT NỐI", state.StatusText);

        state.SetOnline(true);
        Assert.Equal(42, state.ProgressPercentage);
        Assert.Equal(BackupDashboardProgressMode.Sending, state.ProgressMode);
        Assert.Equal("ĐANG GỬI", state.StatusText);
    }

    [Fact]
    public void ResumeProgress_CanStartFromStoredOffset()
    {
        var state = ConfiguredState("agent-resume");
        state.SetOnline(true);
        state.StartSession(Begin("agent-resume", "FIRST-agent-resume", 2, 1_000));

        bool applied = state.ApplyProgress(
            Progress("agent-resume", "FIRST-agent-resume", 1, 650, 10, 65),
            DateTime.UtcNow);

        Assert.True(applied);
        Assert.Equal(65, state.ProgressPercentage);
        Assert.Equal(650, state.ProcessedBytes);
        Assert.Equal("C:\\Data\\current.bin", state.CurrentFile);
    }

    [Fact]
    public async Task PersistedActiveSession_RestoresLastProgressWhileAgentIsOffline()
    {
        string agentId = "dashboard-active-" + Guid.NewGuid().ToString("N");
        DateTime startedAtUtc = new(2026, 8, 24, 1, 2, 3, DateTimeKind.Utc);
        BackupProgressUpdate update = Progress(
            agentId,
            "FIRST-" + agentId,
            6,
            680,
            640,
            68);
        update.StartedAtUtc = startedAtUtc;
        update.CurrentFile = @"M:\Data\important.bin";
        BackupDashboardSnapshot snapshot = Assert.IsType<BackupDashboardSnapshot>(
            BackupDashboardSnapshot.FromProgress(update, startedAtUtc.AddMinutes(2)));

        await BackupRepository.SaveDashboardSnapshotAsync(snapshot);
        BackupDashboardSnapshot restored = (await BackupRepository.GetAllDashboardSnapshotsAsync())[agentId];
        var state = ConfiguredState(agentId);

        Assert.True(state.RestoreSnapshot(restored));
        Assert.True(state.HasActiveSession);
        Assert.Equal(68, state.ProgressPercentage);
        Assert.Equal(680, state.ProcessedBytes);
        Assert.Equal(640, state.TransferredBytes);
        Assert.Equal(@"M:\Data\important.bin", state.CurrentFile);
        Assert.Equal(startedAtUtc, state.StartedAtUtc);
        Assert.Equal(BackupDashboardProgressMode.Disconnected, state.ProgressMode);
        Assert.Equal("MẤT KẾT NỐI", state.StatusText);
    }

    [Fact]
    public void RepeatedBeginForRestoredSession_DoesNotResetProgressToZero()
    {
        string agentId = "dashboard-resume";
        DateTime startedAtUtc = new(2026, 8, 24, 2, 0, 0, DateTimeKind.Utc);
        BackupProgressUpdate update = Progress(agentId, "FIRST-dashboard-resume", 5, 520, 500, 52);
        update.StartedAtUtc = startedAtUtc;
        BackupDashboardSnapshot snapshot = BackupDashboardSnapshot.FromProgress(update, DateTime.UtcNow)!;
        var state = ConfiguredState(agentId);

        Assert.True(state.RestoreSnapshot(snapshot));
        Assert.True(state.StartSession(Begin(
            agentId,
            snapshot.SessionName,
            snapshot.PlannedFileCount,
            snapshot.PlannedTotalBytes,
            startedAtUtc)));

        Assert.Equal(52, state.ProgressPercentage);
        Assert.Equal(520, state.ProcessedBytes);
        Assert.Equal(500, state.TransferredBytes);
        Assert.Equal(BackupDashboardProgressMode.Sending, state.ProgressMode);
    }

    [Fact]
    public async Task OlderDashboardSnapshot_CannotOverwriteNewerStoredProgress()
    {
        string agentId = "dashboard-order-" + Guid.NewGuid().ToString("N");
        DateTime startedAtUtc = new(2026, 8, 24, 3, 0, 0, DateTimeKind.Utc);
        BackupProgressUpdate newerUpdate = Progress(agentId, "FIRST-" + agentId, 8, 800, 800, 80);
        newerUpdate.StartedAtUtc = startedAtUtc;
        BackupProgressUpdate olderUpdate = Progress(agentId, "FIRST-" + agentId, 2, 200, 200, 20);
        olderUpdate.StartedAtUtc = startedAtUtc;

        await BackupRepository.SaveDashboardSnapshotAsync(
            BackupDashboardSnapshot.FromProgress(newerUpdate, startedAtUtc.AddMinutes(2))!);
        await BackupRepository.SaveDashboardSnapshotAsync(
            BackupDashboardSnapshot.FromProgress(olderUpdate, startedAtUtc.AddMinutes(1))!);

        BackupDashboardSnapshot stored = (await BackupRepository.GetAllDashboardSnapshotsAsync())[agentId];
        Assert.Equal(80, stored.ProgressPercentage);
        Assert.Equal(800, stored.ProcessedBytes);
    }

    [Fact]
    public void SnapshotRevision_StillMovesForwardWhenWindowsClockMovesBackward()
    {
        DateTime newerClock = new(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc);
        DateTime rolledBackClock = new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);
        BackupDashboardSnapshot snapshot = BackupDashboardSnapshot.FromBegin(
            Begin("agent-clock", "FIRST-agent-clock", 1, 100, newerClock),
            newerClock) !;
        long previousRevision = snapshot.Revision;

        snapshot.Touch(rolledBackClock);

        Assert.True(snapshot.Revision > previousRevision);
        Assert.Equal(rolledBackClock, snapshot.UpdatedAtUtc);
    }

    [Fact]
    public void Completion_UsesSessionStartDate_NotLateCompletionDate()
    {
        DateTime sessionStartUtc = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var state = ConfiguredState("agent-date");
        state.SetOnline(true);
        state.StartSession(Begin("agent-date", "FIRST-agent-date", 1, 10, sessionStartUtc));

        state.CompleteSession(sessionStartUtc);

        Assert.Equal(BackupDashboardProgressMode.Waiting, state.ProgressMode);
        Assert.Equal("ĐANG CHỜ ĐẾN GIỜ BACKUP", state.StatusText);
        Assert.Equal("HOÀN THÀNH 2026-08-23", state.ProgressDisplayText);
    }

    [Fact]
    public async Task PersistedCompletedSession_ShowsCompletionBeforeAgentReconnects()
    {
        string agentId = "agent-completed-offline-" + Guid.NewGuid().ToString("N");
        DateTime sessionStartUtc = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Local)
            .ToUniversalTime();
        BackupDashboardSnapshot active = BackupDashboardSnapshot.FromBegin(
            Begin(agentId, "FIRST-agent-completed-offline", 10, 1_000, sessionStartUtc),
            sessionStartUtc)!;
        BackupDashboardSnapshot completed = active.Finish(
            BackupDashboardSessionState.Completed,
            sessionStartUtc.AddMinutes(5));
        await BackupRepository.SaveDashboardSnapshotAsync(completed);
        BackupDashboardSnapshot restored =
            (await BackupRepository.GetAllDashboardSnapshotsAsync())[agentId];
        var state = ConfiguredState(agentId);

        Assert.True(state.RestoreSnapshot(restored));

        Assert.False(state.IsOnline);
        Assert.False(state.HasActiveSession);
        Assert.Equal(BackupDashboardProgressMode.Waiting, state.ProgressMode);
        Assert.Equal("ĐANG CHỜ ĐẾN GIỜ BACKUP", state.StatusText);
        Assert.Equal("HOÀN THÀNH 2026-08-23", state.ProgressDisplayText);
    }

    [Fact]
    public void OlderSuccessfulSession_DoesNotHideNewerOfflineFailure()
    {
        string agentId = "agent-failed-offline";
        DateTime successfulStartUtc = new(2026, 8, 22, 1, 0, 0, DateTimeKind.Utc);
        DateTime failedStartUtc = successfulStartUtc.AddDays(1);
        BackupDashboardSnapshot failed = BackupDashboardSnapshot.FromBegin(
            Begin(agentId, "INC-agent-failed-offline", 10, 1_000, failedStartUtc),
            failedStartUtc)!.Finish(
                BackupDashboardSessionState.Failed,
                failedStartUtc.AddMinutes(5));
        var state = ConfiguredState(agentId);

        Assert.True(state.RestoreSnapshot(failed));
        state.SetLastSuccessfulSession(successfulStartUtc);

        Assert.Equal(BackupDashboardProgressMode.Disconnected, state.ProgressMode);
        Assert.Equal("MẤT KẾT NỐI", state.StatusText);
        Assert.Equal("0%", state.ProgressDisplayText);
    }

    [Fact]
    public void LateSuccessfulRetry_CannotReplaceNewerCompletionDate()
    {
        DateTime newer = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        DateTime older = newer.AddDays(-2);
        var state = ConfiguredState("agent-late-ack");
        state.SetOnline(true);
        state.SetLastSuccessfulSession(newer);

        state.CompleteSession(older);

        Assert.Equal(newer, state.LastSuccessfulSessionStartedAtUtc);
    }

    [Fact]
    public void StaleOrSpoofedProgress_CannotOverwriteActiveAgent()
    {
        var state = ConfiguredState("agent-secure");
        state.SetOnline(true);
        state.StartSession(Begin("agent-secure", "INC-agent-secure-2026-08-23", 3, 300));

        Assert.False(state.ApplyProgress(
            Progress("other-agent", "INC-agent-secure-2026-08-23", 1, 100, 100, 33),
            DateTime.UtcNow));
        Assert.False(state.ApplyProgress(
            Progress("agent-secure", "old-session", 2, 200, 200, 66),
            DateTime.UtcNow));
        Assert.Equal(0, state.ProgressPercentage);
    }

    [Fact]
    public void InvalidSessionAndNegativeProgress_AreRejected()
    {
        var state = ConfiguredState("agent-invalid");
        state.SetOnline(true);
        BackupSessionBegin invalidBegin = Begin("other-agent", "FIRST-agent-invalid", -1, -1);
        Assert.False(state.StartSession(invalidBegin));
        Assert.False(state.HasActiveSession);

        Assert.True(state.StartSession(Begin("agent-invalid", "FIRST-agent-invalid", 1, 100)));
        BackupProgressUpdate invalidProgress = Progress(
            "agent-invalid",
            "FIRST-agent-invalid",
            0,
            -1,
            -1,
            -10);
        Assert.False(state.ApplyProgress(invalidProgress, DateTime.UtcNow));
        Assert.Equal(0, state.ProgressPercentage);
    }

    [Fact]
    public void TransferCounterResetAfterReconnect_DoesNotProduceNegativeSpeed()
    {
        DateTime now = DateTime.UtcNow;
        var state = ConfiguredState("agent-speed");
        state.SetOnline(true);
        state.StartSession(Begin("agent-speed", "FIRST-agent-speed", 2, 1_000));
        state.ApplyProgress(Progress("agent-speed", "FIRST-agent-speed", 0, 100, 100, 10), now);
        state.ApplyProgress(Progress("agent-speed", "FIRST-agent-speed", 0, 200, 200, 20), now.AddSeconds(1));
        Assert.Equal(100, state.BytesPerSecond, 3);

        state.SetOnline(false);
        state.SetOnline(true);
        state.ApplyProgress(Progress("agent-speed", "FIRST-agent-speed", 0, 200, 10, 20), now.AddSeconds(2));

        Assert.Equal(0, state.BytesPerSecond);
    }

    [Fact]
    public void WaitingWithoutConfiguration_IsExplicit()
    {
        var state = new BackupDashboardAgentState("agent-new");
        state.SetOnline(true);

        Assert.Equal(BackupDashboardProgressMode.Waiting, state.ProgressMode);
        Assert.Equal("CHƯA CẤU HÌNH BACKUP", state.StatusText);
        Assert.Equal("CHƯA BACKUP", state.ProgressDisplayText);
    }

    [Fact]
    public void SessionFailure_IsRedWithoutPretendingConnectionWasLost()
    {
        var state = ConfiguredState("agent-error");
        state.SetOnline(true);
        state.StartSession(Begin("agent-error", "INC-agent-error", 1, 100));

        state.FailSession();

        Assert.Equal(BackupDashboardProgressMode.Error, state.ProgressMode);
        Assert.Equal("LỖI BACKUP", state.StatusText);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 100)]
    [InlineData(4, 2, 0, 0, 50)]
    [InlineData(4, 4, 0, 0, 100)]
    [InlineData(2, 0, 1_000, 420, 42)]
    [InlineData(2, 0, 1_000, 5_000, 100)]
    public void ProgressCalculator_HandlesEmptyAndBoundedPlans(
        long files,
        long processedFiles,
        long bytes,
        long processedBytes,
        int expected)
    {
        Assert.Equal(expected, BackupProgressCalculator.CalculatePercentage(
            files,
            processedFiles,
            bytes,
            processedBytes));
    }

    [Fact]
    public void ProgressCalculator_DoesNotOverflowForHugeBackups()
    {
        Assert.Equal(49, BackupProgressCalculator.CalculatePercentage(
            long.MaxValue,
            long.MaxValue / 2,
            long.MaxValue,
            long.MaxValue / 2));
    }

    [Fact]
    public void ProgressPacket_RoundTripsAllDashboardFields()
    {
        BackupProgressUpdate expected = Progress("agent-json", "FIRST-agent-json", 8, 800, 700, 80);
        expected.BackupType = "FIRST";
        expected.StartedAtUtc = DateTime.UtcNow;
        expected.PlannedFileCount = 10;
        expected.PlannedTotalBytes = 1_000;

        BackupProgressUpdate actual = JsonSerializer.Deserialize<BackupProgressUpdate>(
            JsonSerializer.Serialize(expected))!;

        Assert.Equal(expected.AgentID, actual.AgentID);
        Assert.Equal(expected.SessionName, actual.SessionName);
        Assert.Equal(expected.CurrentFile, actual.CurrentFile);
        Assert.Equal(expected.ProcessedBytes, actual.ProcessedBytes);
        Assert.Equal(expected.TransferredBytes, actual.TransferredBytes);
        Assert.Equal(expected.ProgressPercentage, actual.ProgressPercentage);
    }

    [Theory]
    [InlineData(@"C:\Data\Reports\bao-cao.xlsx", "bao-cao.xlsx")]
    [InlineData(@"D:\Backup\file.bin", "file.bin")]
    [InlineData("", "")]
    public void CurrentFileColumn_ShowsOnlyFileName(string fullPath, string expected)
    {
        Assert.Equal(expected, frmToolBackup.GetDashboardFileName(fullPath));
    }

    [Fact]
    public async Task DashboardPersistence_LoadsConfigAndUsesSuccessfulSessionStartDate()
    {
        string agentId = "dashboard-" + Guid.NewGuid().ToString("N");
        var config = new BackupConfiguration
        {
            AgentID = agentId,
            ControlStoragePath = TestEnvironment.CreateDirectory("dashboard-storage"),
            FullBackupPeriodDays = 45,
            BackupIntervalDays = 2,
            BackupTime = "21:30",
            UpdatedAtUtc = DateTime.UtcNow
        };
        await BackupRepository.SaveConfigAsync(config);
        DateTime startedAtUtc = new DateTime(2026, 8, 23, 4, 0, 0, DateTimeKind.Utc);
        await BackupRepository.SaveSessionAsync(
            new BackupManifest
            {
                AgentID = agentId,
                SessionName = "INC-" + agentId + "-2026-08-23",
                BackupType = "INC",
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = startedAtUtc.AddDays(2)
            },
            config.ControlStoragePath,
            true,
            "OK");

        Dictionary<string, BackupConfiguration> configs = await BackupRepository.GetAllConfigsAsync();
        Dictionary<string, DateTime> sessions = await BackupRepository.GetLatestSuccessfulSessionStartsAsync();

        Assert.Equal(config.ControlStoragePath, configs[agentId].ControlStoragePath);
        Assert.Equal(45, configs[agentId].FullBackupPeriodDays);
        Assert.Equal(startedAtUtc, sessions[agentId]);
    }

    private static BackupDashboardAgentState ConfiguredState(string agentId)
    {
        var state = new BackupDashboardAgentState(agentId);
        state.SetConfiguration(new BackupConfiguration
        {
            AgentID = agentId,
            ControlStoragePath = "D:\\Backup",
            FullBackupPeriodDays = 60,
            BackupIntervalDays = 1,
            BackupTime = "23:00"
        });
        return state;
    }

    private static BackupSessionBegin Begin(
        string agentId,
        string sessionName,
        long files,
        long bytes,
        DateTime? startedAtUtc = null) => new()
        {
            AgentID = agentId,
            SessionName = sessionName,
            BackupType = sessionName.StartsWith("FIRST", StringComparison.OrdinalIgnoreCase) ? "FIRST" : "INC",
            StartedAtUtc = startedAtUtc ?? DateTime.UtcNow,
            PlannedFileCount = files,
            PlannedTotalBytes = bytes
        };

    private static BackupProgressUpdate Progress(
        string agentId,
        string sessionName,
        long processedFiles,
        long processedBytes,
        long transferredBytes,
        int percentage) => new()
        {
            AgentID = agentId,
            SessionName = sessionName,
            BackupType = "FIRST",
            StartedAtUtc = DateTime.UtcNow,
            PlannedFileCount = 10,
            ProcessedFileCount = processedFiles,
            PlannedTotalBytes = 1_000,
            ProcessedBytes = processedBytes,
            TransferredBytes = transferredBytes,
            ProgressPercentage = percentage,
            CurrentFile = "C:\\Data\\current.bin"
        };
}
