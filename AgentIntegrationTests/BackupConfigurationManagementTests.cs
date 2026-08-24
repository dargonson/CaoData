using AgentControl;
using AgentService;
using AgentShared;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentIntegrationTests;

public sealed class BackupConfigurationManagementTests
{
    [Fact]
    public void ConfigurationUiState_FollowsCardCreateEditAndRecoveryRules()
    {
        BackupConfigurationUiState noSelection = BackupConfigurationUiState.Resolve(
            hasSelectedAgent: false,
            hasConfiguration: false,
            isOnline: false,
            hasActiveSession: false,
            isEditing: false,
            isBusy: false);
        Assert.Equal(default, noSelection);

        BackupConfigurationUiState newOfflineAgent = BackupConfigurationUiState.Resolve(
            hasSelectedAgent: true,
            hasConfiguration: false,
            isOnline: false,
            hasActiveSession: false,
            isEditing: false,
            isBusy: false);
        Assert.True(newOfflineAgent.EditorEnabled);
        Assert.True(newOfflineAgent.DeployEnabled);
        Assert.False(newOfflineAgent.EditEnabled);
        Assert.False(newOfflineAgent.DeleteEnabled);
        Assert.False(newOfflineAgent.RecoveryEnabled);

        BackupConfigurationUiState configuredOfflineAgent = BackupConfigurationUiState.Resolve(
            hasSelectedAgent: true,
            hasConfiguration: true,
            isOnline: false,
            hasActiveSession: false,
            isEditing: false,
            isBusy: false);
        Assert.False(configuredOfflineAgent.EditorEnabled);
        Assert.False(configuredOfflineAgent.DeployEnabled);
        Assert.False(configuredOfflineAgent.EditEnabled);
        Assert.False(configuredOfflineAgent.DeleteEnabled);
        Assert.True(configuredOfflineAgent.RecoveryEnabled);

        BackupConfigurationUiState configuredOnlineAgent = BackupConfigurationUiState.Resolve(
            hasSelectedAgent: true,
            hasConfiguration: true,
            isOnline: true,
            hasActiveSession: false,
            isEditing: false,
            isBusy: false);
        Assert.False(configuredOnlineAgent.EditorEnabled);
        Assert.False(configuredOnlineAgent.DeployEnabled);
        Assert.True(configuredOnlineAgent.EditEnabled);
        Assert.True(configuredOnlineAgent.DeleteEnabled);
        Assert.True(configuredOnlineAgent.RecoveryEnabled);

        BackupConfigurationUiState editing = BackupConfigurationUiState.Resolve(
            hasSelectedAgent: true,
            hasConfiguration: true,
            isOnline: true,
            hasActiveSession: false,
            isEditing: true,
            isBusy: false);
        Assert.True(editing.EditorEnabled);
        Assert.True(editing.DeployEnabled);
        Assert.False(editing.EditEnabled);
        Assert.False(editing.DeleteEnabled);
        Assert.True(editing.RecoveryEnabled);
    }

    [Fact]
    public void ConfigurationUiState_BlocksChangesDuringBackupOrConfigOperation()
    {
        BackupConfigurationUiState activeBackup = BackupConfigurationUiState.Resolve(
            hasSelectedAgent: true,
            hasConfiguration: true,
            isOnline: true,
            hasActiveSession: true,
            isEditing: false,
            isBusy: false);
        Assert.False(activeBackup.EditorEnabled);
        Assert.False(activeBackup.DeployEnabled);
        Assert.False(activeBackup.EditEnabled);
        Assert.False(activeBackup.DeleteEnabled);
        Assert.True(activeBackup.RecoveryEnabled);

        BackupConfigurationUiState busy = BackupConfigurationUiState.Resolve(
            hasSelectedAgent: true,
            hasConfiguration: true,
            isOnline: true,
            hasActiveSession: false,
            isEditing: true,
            isBusy: true);
        Assert.False(busy.EditorEnabled);
        Assert.False(busy.DeployEnabled);
        Assert.False(busy.EditEnabled);
        Assert.False(busy.DeleteEnabled);
        Assert.False(busy.RecoveryEnabled);
    }

    [Fact]
    public void DashboardManagement_IsAvailableOnlyForOnlineIdleConfiguredAgent()
    {
        var state = new BackupDashboardAgentState("manage-agent");
        state.UpdateAgent("PC-01", "Nguyen Van A", "Windows 11 Pro");

        Assert.False(state.CanManageConfiguration);
        state.SetConfiguration(CreateConfiguration("manage-agent", "D:\\Backup"));
        Assert.False(state.CanManageConfiguration);
        state.SetOnline(true);
        Assert.True(state.CanManageConfiguration);

        state.StartSession(new BackupSessionBegin
        {
            AgentID = "manage-agent",
            SessionName = "INC-manage-agent-2026-08-23",
            BackupType = "INC",
            StartedAtUtc = DateTime.UtcNow,
            PlannedFileCount = 1,
            PlannedTotalBytes = 1
        });
        Assert.False(state.CanManageConfiguration);
        Assert.Equal("Nguyen Van A", state.OwnerName);

        state.CompleteSession(DateTime.UtcNow);
        state.ResetBackupConfigurationAndHistory();
        Assert.Null(state.Configuration);
        Assert.Null(state.LastSuccessfulSessionStartedAtUtc);
        Assert.Equal("CHƯA BACKUP", state.ProgressDisplayText);
    }

    [Fact]
    public async Task AgentDelete_RemovesOnlyBackupConfigAndRuntimeState()
    {
        string folder = TestEnvironment.CreateDirectory("agent-delete-config");
        string appSettingsPath = Path.Combine(folder, "appsettings.json");
        string statePath = Path.Combine(folder, "backup-state.json");
        await File.WriteAllTextAsync(appSettingsPath, """
        {
          "ServerIP": "127.0.0.1",
          "ServerPort": 9000,
          "BackupConfig": {
            "Enabled": true,
            "AgentID": "agent-delete",
            "ControlStoragePath": "D:\\Backup",
            "BackupIntervalDays": 1,
            "FullBackupPeriodDays": 60,
            "BackupTime": "22:00",
            "SourcePaths": ["D:\\"]
          }
        }
        """);
        await File.WriteAllTextAsync(statePath, "{\"InitialBackupCompleted\":true}");

        var manager = new AgentBackupManager(
            NullLogger<Worker>.Instance,
            "agent-delete",
            () => true,
            (_, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask,
            appSettingsPath,
            statePath);

        BackupConfigAck ack = await manager.DeleteConfigurationAsync(
            JsonSerializer.Serialize(new BackupConfigDeleteRequest { AgentID = "agent-delete" }),
            CancellationToken.None);

        Assert.True(ack.Success, ack.Message);
        JsonObject root = JsonNode.Parse(await File.ReadAllTextAsync(appSettingsPath))!.AsObject();
        Assert.Null(root["BackupConfig"]);
        Assert.Equal("127.0.0.1", root["ServerIP"]!.GetValue<string>());
        Assert.Equal(9000, root["ServerPort"]!.GetValue<int>());
        Assert.False(File.Exists(statePath));
    }

    [Fact]
    public async Task AgentDelete_RejectsMismatchedAgentAndPreservesFiles()
    {
        string folder = TestEnvironment.CreateDirectory("agent-delete-reject");
        string appSettingsPath = Path.Combine(folder, "appsettings.json");
        string statePath = Path.Combine(folder, "backup-state.json");
        const string original = "{\"BackupConfig\":{\"AgentID\":\"agent-a\"}}";
        await File.WriteAllTextAsync(appSettingsPath, original);
        await File.WriteAllTextAsync(statePath, "state");
        var manager = new AgentBackupManager(
            NullLogger<Worker>.Instance,
            "agent-a",
            () => true,
            (_, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask,
            appSettingsPath,
            statePath);

        BackupConfigAck ack = await manager.DeleteConfigurationAsync(
            JsonSerializer.Serialize(new BackupConfigDeleteRequest { AgentID = "agent-b" }),
            CancellationToken.None);

        Assert.False(ack.Success);
        Assert.Equal(original, await File.ReadAllTextAsync(appSettingsPath));
        Assert.True(File.Exists(statePath));
    }

    [Fact]
    public async Task AgentRejectsEditAndDeleteWhileBackupRunLockIsHeld()
    {
        string folder = TestEnvironment.CreateDirectory("agent-config-active-run");
        string appSettingsPath = Path.Combine(folder, "appsettings.json");
        string statePath = Path.Combine(folder, "backup-state.json");
        await File.WriteAllTextAsync(appSettingsPath, "{\"BackupConfig\":{\"AgentID\":\"active-agent\"}}");
        await File.WriteAllTextAsync(statePath, "state");
        var manager = new AgentBackupManager(
            NullLogger<Worker>.Instance,
            "active-agent",
            () => true,
            (_, _) => Task.CompletedTask,
            (_, _, _, _) => Task.CompletedTask,
            appSettingsPath,
            statePath);
        SemaphoreSlim runLock = (SemaphoreSlim)typeof(AgentBackupManager)
            .GetField("_runLock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;
        await runLock.WaitAsync();
        try
        {
            BackupConfigAck editAck = await manager.ApplyConfigurationAsync(
                JsonSerializer.Serialize(CreateConfiguration("active-agent", folder)),
                CancellationToken.None);
            BackupConfigAck deleteAck = await manager.DeleteConfigurationAsync(
                JsonSerializer.Serialize(new BackupConfigDeleteRequest { AgentID = "active-agent" }),
                CancellationToken.None);

            Assert.False(editAck.Success);
            Assert.False(deleteAck.Success);
            Assert.True(File.Exists(statePath));
            Assert.NotNull(JsonNode.Parse(await File.ReadAllTextAsync(appSettingsPath))!["BackupConfig"]);
        }
        finally
        {
            runLock.Release();
        }
    }

    [Fact]
    public async Task ControlArchive_StreamsHistoryThenPurgesDbWithoutDeletingPhysicalBackup()
    {
        string agentId = "archive-" + Guid.NewGuid().ToString("N");
        string otherAgentId = "archive-other-" + Guid.NewGuid().ToString("N");
        string storage = TestEnvironment.CreateDirectory("archive-storage");
        string physicalFolder = Path.Combine(storage, $"FIRST-{agentId}-2026-08-23");
        Directory.CreateDirectory(physicalFolder);
        string physicalFile = Path.Combine(physicalFolder, "payload.bin");
        await File.WriteAllBytesAsync(physicalFile, new byte[] { 1, 2, 3, 4 });

        BackupConfiguration config = CreateConfiguration(agentId, storage);
        await BackupRepository.SaveConfigAsync(config);
        await BackupRepository.SaveConfigAsync(CreateConfiguration(otherAgentId, storage));
        await BackupRepository.SaveSessionAsync(
            new BackupManifest
            {
                AgentID = agentId,
                SessionName = Path.GetFileName(physicalFolder),
                BackupType = "FIRST",
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                Created =
                {
                    new BackupManifestEntry
                    {
                        SourcePath = "D:\\Data\\payload.bin",
                        RelativeStoragePath = "D\\Data\\payload.bin",
                        Size = 4,
                        LastWriteTimeUtc = DateTime.UtcNow,
                        ContentSha256 = "hash"
                    }
                }
            },
            physicalFolder,
            true,
            "OK");
        await BackupRepository.SaveDashboardSnapshotAsync(
            BackupDashboardSnapshot.FromProgress(
                new BackupProgressUpdate
                {
                    AgentID = agentId,
                    SessionName = Path.GetFileName(physicalFolder),
                    BackupType = "FIRST",
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                    PlannedFileCount = 1,
                    ProcessedFileCount = 1,
                    PlannedTotalBytes = 4,
                    ProcessedBytes = 4,
                    TransferredBytes = 4,
                    ProgressPercentage = 100,
                    CurrentFile = physicalFile
                },
                DateTime.UtcNow)!);
        await FirstBackupStore.BeginRunAsync(
            new BackupSessionBegin
            {
                AgentID = agentId,
                SessionName = "FIRST-" + agentId,
                BackupType = "FIRST",
                StartedAtUtc = DateTime.UtcNow,
                IsResumableFirst = true,
                PlannedFileCount = 1,
                PlannedTotalBytes = 4
            },
            physicalFolder + ".inprogress");

        string archivePath = await BackupConfigurationArchiveService.ExportAsync(
            agentId,
            "PC-ARCHIVE",
            "Nguoi dung",
            storage);
        await BackupConfigurationArchiveService.DeleteDatabaseStateAsync(agentId);

        Assert.True(File.Exists(archivePath));
        Assert.True(File.Exists(physicalFile));
        using JsonDocument archive = JsonDocument.Parse(await File.ReadAllTextAsync(archivePath));
        JsonElement tables = archive.RootElement.GetProperty("Tables");
        Assert.Single(tables.GetProperty("BackupConfigs").EnumerateArray());
        Assert.Single(tables.GetProperty("BackupSessions").EnumerateArray());
        Assert.Single(tables.GetProperty("BackupDashboardSnapshots").EnumerateArray());
        Assert.Single(tables.GetProperty("BackupFileInventory").EnumerateArray());
        Assert.Single(tables.GetProperty("FirstBackupRuns").EnumerateArray());
        Assert.Null(await BackupRepository.GetConfigAsync(agentId));
        Assert.NotNull(await BackupRepository.GetConfigAsync(otherAgentId));
        Assert.DoesNotContain(
            agentId,
            (await BackupRepository.GetLatestSuccessfulSessionStartsAsync()).Keys,
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            agentId,
            (await BackupRepository.GetAllDashboardSnapshotsAsync()).Keys,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeletePacket_RoundTripsWithoutSharingDeployAckType()
    {
        var request = new BackupConfigDeleteRequest
        {
            AgentID = "agent-json-delete",
            RequestedAtUtc = DateTime.UtcNow
        };
        BackupConfigDeleteRequest actual = JsonSerializer.Deserialize<BackupConfigDeleteRequest>(
            JsonSerializer.Serialize(request))!;

        Assert.Equal(request.AgentID, actual.AgentID);
        Assert.NotEqual(BackupPacketTypes.ConfigAck, BackupPacketTypes.ConfigDeleteAck);
        Assert.NotEqual(BackupPacketTypes.ConfigDeploy, BackupPacketTypes.ConfigDelete);
    }

    private static BackupConfiguration CreateConfiguration(string agentId, string storage) => new()
    {
        AgentID = agentId,
        ControlStoragePath = storage,
        BackupIntervalDays = 1,
        FullBackupPeriodDays = 60,
        BackupTime = "22:00",
        SourcePaths = { "D:\\" },
        ExcludedFolders = { "D:\\Temp" },
        ExcludedPatterns = { ".tmp", "~*" },
        UpdatedAtUtc = DateTime.UtcNow
    };
}
