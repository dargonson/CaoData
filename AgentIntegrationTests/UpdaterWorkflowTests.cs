using AgentUpdater;

namespace AgentIntegrationTests;

public sealed class UpdaterWorkflowTests
{
    [Fact]
    public async Task Workflow_ReplacesFile_AndLeavesCompletionMarkerForNewAgent()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync();

        await AgentUpdateWorkflow.ExecuteAsync(
            fixture.CurrentPath,
            fixture.NewPath,
            fixture.BackupPath,
            fixture.CreateOperations());

        Assert.Equal("new-version", await File.ReadAllTextAsync(fixture.CurrentPath));
        Assert.Equal("old-version", await File.ReadAllTextAsync(fixture.BackupPath));
        Assert.True(fixture.ServiceRunning);
        Assert.True(fixture.MarkerExists);
        Assert.DoesNotContain(fixture.Statuses, status => status == "RolledBack");
    }

    [Fact]
    public async Task Workflow_RestoresOldExe_WhenNewServiceCannotStart()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        fixture.FailStart = true;

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentUpdateWorkflow.ExecuteAsync(
                fixture.CurrentPath,
                fixture.NewPath,
                fixture.BackupPath,
                fixture.CreateOperations()));

        Assert.Contains("đã được khôi phục", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old-version", await File.ReadAllTextAsync(fixture.CurrentPath));
        Assert.True(fixture.ServiceRunning);
        Assert.False(fixture.MarkerExists);
        Assert.Contains("RolledBack", fixture.Statuses);
    }

    [Fact]
    public async Task Workflow_RollsBack_WhenReplacementCopyFailsAfterTruncatingCurrentExe()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        fixture.FailReplacementCopyAfterWritingPartialData = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentUpdateWorkflow.ExecuteAsync(
                fixture.CurrentPath,
                fixture.NewPath,
                fixture.BackupPath,
                fixture.CreateOperations()));

        Assert.Equal("old-version", await File.ReadAllTextAsync(fixture.CurrentPath));
        Assert.True(fixture.ServiceRunning);
        Assert.False(fixture.MarkerExists);
    }

    [Fact]
    public async Task Workflow_RestartsOldService_WhenCreatingBackupFails()
    {
        WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        fixture.FailBackupCopy = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentUpdateWorkflow.ExecuteAsync(
                fixture.CurrentPath,
                fixture.NewPath,
                fixture.BackupPath,
                fixture.CreateOperations()));

        Assert.Equal("old-version", await File.ReadAllTextAsync(fixture.CurrentPath));
        Assert.False(File.Exists(fixture.BackupPath));
        Assert.True(fixture.ServiceRunning);
        Assert.False(fixture.MarkerExists);
    }

    private sealed class WorkflowFixture
    {
        internal string CurrentPath { get; private init; } = string.Empty;
        internal string NewPath { get; private init; } = string.Empty;
        internal string BackupPath { get; private init; } = string.Empty;
        internal bool ServiceRunning { get; set; } = true;
        internal bool MarkerExists { get; set; }
        internal bool FailStart { get; set; }
        internal bool FailReplacementCopyAfterWritingPartialData { get; set; }
        internal bool FailBackupCopy { get; set; }
        internal List<string> Statuses { get; } = new();

        internal static async Task<WorkflowFixture> CreateAsync()
        {
            string root = TestEnvironment.CreateDirectory("updater-workflow");
            var fixture = new WorkflowFixture
            {
                CurrentPath = Path.Combine(root, "AgentServices.exe"),
                NewPath = Path.Combine(root, "staged-AgentServices.exe"),
                BackupPath = Path.Combine(root, "backup", "AgentServices.exe.bak")
            };
            await File.WriteAllTextAsync(fixture.CurrentPath, "old-version");
            await File.WriteAllTextAsync(fixture.NewPath, "new-version");
            return fixture;
        }

        internal AgentUpdateWorkflowOperations CreateOperations()
        {
            return new AgentUpdateWorkflowOperations
            {
                StopServiceAsync = () =>
                {
                    ServiceRunning = false;
                    return Task.CompletedTask;
                },
                TryStopServiceAsync = () =>
                {
                    ServiceRunning = false;
                    return Task.CompletedTask;
                },
                WaitUntilUnlockedAsync = () => Task.CompletedTask,
                StartAndVerifyServiceAsync = () =>
                {
                    if (FailStart)
                    {
                        throw new InvalidOperationException("Bản mới không vào RUNNING.");
                    }
                    ServiceRunning = true;
                    return Task.CompletedTask;
                },
                EnsureServiceRunningAsync = () =>
                {
                    ServiceRunning = true;
                    return Task.CompletedTask;
                },
                WriteCompletionMarkerAsync = () =>
                {
                    MarkerExists = true;
                    return Task.CompletedTask;
                },
                DeleteCompletionMarkerAsync = () =>
                {
                    MarkerExists = false;
                    return Task.CompletedTask;
                },
                CopyFileAsync = CopyFileAsync,
                ReportStatusAsync = (status, _) =>
                {
                    Statuses.Add(status);
                    return Task.CompletedTask;
                },
                LogAsync = _ => Task.CompletedTask
            };
        }

        private Task CopyFileAsync(string source, string destination, bool overwrite)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (FailBackupCopy &&
                source.Equals(CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                destination.Equals(BackupPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Không tạo được backup.");
            }
            if (FailReplacementCopyAfterWritingPartialData &&
                source.Equals(NewPath, StringComparison.OrdinalIgnoreCase) &&
                destination.Equals(CurrentPath, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(destination, "partial");
                throw new IOException("Copy bản mới bị ngắt giữa chừng.");
            }

            File.Copy(source, destination, overwrite);
            return Task.CompletedTask;
        }
    }
}
