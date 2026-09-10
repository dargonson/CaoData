using AgentService;
using AgentShared;
using System.Text.Json;

namespace AgentIntegrationTests;

public sealed class RemoteProcessTests
{
    [Fact]
    public void ConfirmationAndStatusForms_CreateSuccessfully()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using var confirmation = new AgentControl.RemoteProcessConfirmationDialog(
                    "TEST-PC",
                    "AGT-TEST",
                    @"C:\Deploy\setup.exe",
                    "Không áp dụng");
                using var status = new AgentControl.RemoteProcessStatusForm(
                    "TEST-PC",
                    "AGT-TEST",
                    @"C:\Deploy\setup.exe");
                status.AddStatus(new RemoteProcessStatus
                {
                    Status = RemoteProcessStatuses.Running,
                    Message = "Đang chạy",
                    ProcessId = 123,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    [Theory]
    [InlineData(@"C:\Deploy\setup.exe", true)]
    [InlineData(@"C:\Deploy\setup.MSI", true)]
    [InlineData(@"C:\Deploy\install.bat", true)]
    [InlineData(@"C:\Deploy\install.CMD", true)]
    [InlineData(@"C:\Deploy\notes.txt", false)]
    [InlineData(@"C:\Deploy\archive.zip", false)]
    [InlineData("", false)]
    public void SupportedFiles_AreRestrictedToTheApprovedExtensions(string path, bool expected)
    {
        Assert.Equal(expected, RemoteProcessRules.IsSupportedFile(path));
    }

    [Fact]
    public void StartInfo_UsesTheSystemExecutableForMsiAndCmd()
    {
        string root = TestEnvironment.CreateDirectory("remote-process-start-info");
        string msiPath = Path.Combine(root, "sample setup.msi");
        string logPath = Path.Combine(root, "sample setup.log");
        string cmdPath = Path.Combine(root, "sample command.cmd");

        var msi = RemoteProcessRunner.BuildStartInfo(msiPath, "INSTALLLEVEL=3", logPath);
        var cmd = RemoteProcessRunner.BuildStartInfo(cmdPath, "first second");

        Assert.Equal("msiexec.exe", Path.GetFileName(msi.FileName), ignoreCase: true);
        Assert.Contains("/i", msi.Arguments);
        Assert.Contains($"\"{msiPath}\"", msi.Arguments);
        Assert.Contains("INSTALLLEVEL=3", msi.Arguments);
        Assert.Contains("/qn /norestart", msi.Arguments);
        Assert.Contains("REBOOT=ReallySuppress", msi.Arguments);
        Assert.Contains("/L*V", msi.Arguments);
        Assert.Contains($"\"{logPath}\"", msi.Arguments);
        Assert.Equal("cmd.exe", Path.GetFileName(cmd.FileName), ignoreCase: true);
        Assert.Contains("/d /s /c", cmd.Arguments);
        Assert.Contains($"\"{cmdPath}\"", cmd.Arguments);
        Assert.False(msi.UseShellExecute);
        Assert.False(cmd.UseShellExecute);
        Assert.True(msi.CreateNoWindow);
        Assert.True(cmd.CreateNoWindow);
        Assert.Equal(root, msi.WorkingDirectory);
        Assert.Equal(root, cmd.WorkingDirectory);
    }

    [Theory]
    [InlineData(".exe", 0, true, false)]
    [InlineData(".exe", 1, false, false)]
    [InlineData(".msi", 0, true, false)]
    [InlineData(".msi", 1641, true, true)]
    [InlineData(".msi", 3010, true, true)]
    [InlineData(".msi", 1603, false, false)]
    public void ExitCodes_AreClassifiedCorrectly(
        string extension,
        int exitCode,
        bool expectedSuccess,
        bool expectedRestart)
    {
        bool success = RemoteProcessRunner.IsSuccessfulExitCode(
            extension,
            exitCode,
            out bool restartRequired);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedRestart, restartRequired);
    }

    [Fact]
    public async Task SuccessfulCmd_ReportsWaitingRunningAndCompleted()
    {
        string root = TestEnvironment.CreateDirectory("remote-process-success");
        string scriptPath = Path.Combine(root, "success script.cmd");
        await File.WriteAllTextAsync(
            scriptPath,
            "@if \"%~1\"==\"expected value\" (exit /b 0) else (exit /b 9)");
        var statuses = new List<RemoteProcessStatus>();
        using var runner = new RemoteProcessRunner();

        RemoteProcessRequest request = Request(scriptPath);
        request.Arguments = "\"expected value\"";
        await runner.ExecuteAsync(request, status =>
        {
            statuses.Add(status);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(
            new[] { RemoteProcessStatuses.Waiting, RemoteProcessStatuses.Running, RemoteProcessStatuses.Completed },
            statuses.Select(status => status.Status));
        Assert.True(statuses[1].ProcessId > 0);
        Assert.Equal(0, statuses[2].ExitCode);
        Assert.Null(statuses[2].ErrorCode);
    }

    [Fact]
    public async Task FailedCmd_ReportsExitCodeAsErrorCode()
    {
        string root = TestEnvironment.CreateDirectory("remote-process-failure");
        string scriptPath = Path.Combine(root, "failure.cmd");
        await File.WriteAllTextAsync(scriptPath, "@exit /b 7");
        var statuses = new List<RemoteProcessStatus>();
        using var runner = new RemoteProcessRunner();

        await runner.ExecuteAsync(Request(scriptPath), status =>
        {
            statuses.Add(status);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(RemoteProcessStatuses.Error, statuses[^1].Status);
        Assert.Equal(7, statuses[^1].ExitCode);
        Assert.Equal(7, statuses[^1].ErrorCode);
    }

    [Fact]
    public async Task InvalidMsi_DoesNotWaitForAHiddenDialog()
    {
        string root = TestEnvironment.CreateDirectory("remote-process-invalid-msi");
        string msiPath = Path.Combine(root, "invalid package.msi");
        await File.WriteAllTextAsync(msiPath, "not an MSI database");
        var statuses = new List<RemoteProcessStatus>();
        using var runner = new RemoteProcessRunner();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        RemoteProcessRequest request = Request(msiPath);
        string temporaryLogPath = RemoteProcessRunner.CreateTemporaryMsiLogPath(request.RequestId);
        await runner.ExecuteAsync(request, status =>
        {
            statuses.Add(status);
            return Task.CompletedTask;
        }, timeout.Token);

        Assert.Equal(RemoteProcessStatuses.Waiting, statuses[0].Status);
        Assert.Equal(RemoteProcessStatuses.Error, statuses[^1].Status);
        Assert.True(statuses[^1].ErrorCode is 1618 or 1619 or 1620);
        if (statuses.Any(status => status.Status == RemoteProcessStatuses.Running))
        {
            Assert.True(statuses[^1].ExitCode is 1619 or 1620);
            Assert.Contains("MSI", statuses[^1].Message);
        }
        Assert.False(File.Exists(temporaryLogPath));
    }

    [Fact]
    public async Task MissingOrUnsupportedFile_IsRejectedBeforeProcessStart()
    {
        string root = TestEnvironment.CreateDirectory("remote-process-invalid");
        string unsupportedPath = Path.Combine(root, "document.txt");
        await File.WriteAllTextAsync(unsupportedPath, "not executable");
        var unsupportedStatuses = new List<RemoteProcessStatus>();
        var missingStatuses = new List<RemoteProcessStatus>();
        using var runner = new RemoteProcessRunner();

        await runner.ExecuteAsync(Request(unsupportedPath), status =>
        {
            unsupportedStatuses.Add(status);
            return Task.CompletedTask;
        }, CancellationToken.None);
        await runner.ExecuteAsync(Request(Path.Combine(root, "missing.exe")), status =>
        {
            missingStatuses.Add(status);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(
            new[] { RemoteProcessStatuses.Waiting, RemoteProcessStatuses.Error },
            unsupportedStatuses.Select(status => status.Status));
        Assert.Contains("Chỉ cho phép", unsupportedStatuses[^1].Message);
        Assert.Equal(RemoteProcessStatuses.Error, missingStatuses[^1].Status);
        Assert.Contains("không tồn tại", missingStatuses[^1].Message);
    }

    [Fact]
    public void InvalidRequestAndOversizedArguments_AreRejected()
    {
        string? missingIdError = RemoteProcessRunner.ValidateRequest(
            new RemoteProcessRequest { FilePath = @"C:\Deploy\setup.exe" },
            out _);
        string? relativePathError = RemoteProcessRunner.ValidateRequest(
            new RemoteProcessRequest { RequestId = "request", FilePath = "setup.exe" },
            out _);
        string? argumentError = RemoteProcessRunner.ValidateRequest(
            new RemoteProcessRequest
            {
                RequestId = "request",
                FilePath = @"C:\Deploy\setup.exe",
                Arguments = new string('a', RemoteProcessRules.MaximumArgumentsLength + 1)
            },
            out _);

        Assert.Contains("thiếu mã", missingIdError);
        Assert.Contains("tuyệt đối", relativePathError);
        Assert.Contains("vượt quá", argumentError);
    }

    [Fact]
    public async Task ConcurrentRequests_AreExecutedOneAtATime()
    {
        string root = TestEnvironment.CreateDirectory("remote-process-queue");
        string firstPath = Path.Combine(root, "first.cmd");
        string secondPath = Path.Combine(root, "second.cmd");
        await File.WriteAllTextAsync(firstPath, "@ping -n 2 127.0.0.1 >nul & exit /b 0");
        await File.WriteAllTextAsync(secondPath, "@exit /b 0");
        var events = new List<string>();
        var eventLock = new object();
        var firstRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runner = new RemoteProcessRunner();

        Task first = runner.ExecuteAsync(Request(firstPath), status =>
        {
            lock (eventLock)
            {
                events.Add("first:" + status.Status);
            }
            if (status.Status == RemoteProcessStatuses.Running)
            {
                firstRunning.TrySetResult();
            }
            return Task.CompletedTask;
        }, CancellationToken.None);
        await firstRunning.Task;
        Task second = runner.ExecuteAsync(Request(secondPath), status =>
        {
            lock (eventLock)
            {
                events.Add("second:" + status.Status);
            }
            return Task.CompletedTask;
        }, CancellationToken.None);

        await Task.WhenAll(first, second);

        int firstCompleted = events.IndexOf("first:" + RemoteProcessStatuses.Completed);
        int secondWaiting = events.IndexOf("second:" + RemoteProcessStatuses.Waiting);
        int secondRunning = events.IndexOf("second:" + RemoteProcessStatuses.Running);
        Assert.True(secondWaiting >= 0 && secondWaiting < firstCompleted);
        Assert.True(firstCompleted >= 0 && firstCompleted < secondRunning);
    }

    [Fact]
    public void RunningMsi_RequiresExplicitTerminationPermission()
    {
        int[] runningProcessIds = { 920, 315, 920 };

        string? blocked = RemoteProcessRunner.GetMsiConflictError(
            @"C:\Deploy\setup.msi",
            terminateExistingProcesses: false,
            runningProcessIds);
        string? approved = RemoteProcessRunner.GetMsiConflictError(
            @"C:\Deploy\setup.msi",
            terminateExistingProcesses: true,
            runningProcessIds);
        string? nonMsi = RemoteProcessRunner.GetMsiConflictError(
            @"C:\Deploy\setup.exe",
            terminateExistingProcesses: false,
            runningProcessIds);

        Assert.Contains("315, 920", blocked);
        Assert.Null(approved);
        Assert.Null(nonMsi);
    }

    [Fact]
    public void MsiPreflightPacket_RoundTripsRunningProcessIds()
    {
        var original = new RemoteMsiPreflightResponse
        {
            RequestId = "preflight-1",
            Success = true,
            ProcessIds = { 123, 456 }
        };

        RemoteMsiPreflightResponse? restored =
            JsonSerializer.Deserialize<RemoteMsiPreflightResponse>(JsonSerializer.Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal(original.RequestId, restored.RequestId);
        Assert.True(restored.Success);
        Assert.Equal(new[] { 123, 456 }, restored.ProcessIds);
    }

    [Fact]
    public void OfficialMsiExitCodes_HaveFriendlyMessages()
    {
        int[] documentedCodes =
        {
            0, 13, 87, 120, 1259,
            1601, 1602, 1603, 1604, 1605, 1606, 1607, 1608, 1609,
            1610, 1611, 1612, 1613, 1614, 1615, 1616, 1618, 1619,
            1620, 1621, 1622, 1623, 1624, 1625, 1626, 1627, 1628, 1629,
            1630, 1631, 1632, 1633, 1634, 1635, 1636, 1637, 1638, 1639,
            1640, 1641, 1642, 1643, 1644, 1645, 1646, 1647, 1648, 1649,
            1650, 1651, 1652, 1653, 1654, 3010
        };

        foreach (int code in documentedCodes)
        {
            string message = MsiExitCodeCatalog.GetVietnameseMessage(code);
            Assert.False(string.IsNullOrWhiteSpace(message));
        }

        Assert.Contains("phiên bản khác", MsiExitCodeCatalog.GetVietnameseMessage(1638));
        Assert.Contains("khởi động lại", MsiExitCodeCatalog.GetVietnameseMessage(3010));
    }

    [Fact]
    public async Task MsiLogSummary_ReturnsRelevantFailureLinesWithoutLoadingWholeLog()
    {
        string root = TestEnvironment.CreateDirectory("remote-process-msi-log");
        string logPath = Path.Combine(root, "install.log");
        await File.WriteAllLinesAsync(logPath, new[]
        {
            "MSI (s) normal setup line",
            "Action ended: InstallFinalize. Return value 3.",
            "Error 1638. Another version is already installed.",
            "MSI (s) final line"
        });

        string summary = RemoteProcessRunner.ReadMsiDiagnosticSummary(logPath);

        Assert.Contains("Return value 3", summary);
        Assert.Contains("Error 1638", summary);
        Assert.DoesNotContain("normal setup line", summary);
    }

    [Fact]
    public async Task MsiLogSummary_IgnoresInstallerErrorTableQueries()
    {
        string root = TestEnvironment.CreateDirectory("remote-process-msi-success-log");
        string logPath = Path.Combine(root, "install.log");
        await File.WriteAllLinesAsync(logPath, new[]
        {
            "MSI (s) Note: 1: 2205 2:  3: Error ",
            "MSI (s) Note: 1: 2228 2:  3: Error 4: SELECT `Message` FROM `Error` WHERE `Error` = 1707",
            "MSI (s) Windows Installer installed the product. Product Name: Demo.",
            "MSI (s) Product: Demo -- Installation completed successfully."
        });

        string summary = RemoteProcessRunner.ReadMsiDiagnosticSummary(logPath);

        Assert.DoesNotContain("SELECT `Message` FROM `Error`", summary);
        Assert.DoesNotContain("2205", summary);
        Assert.Contains("completed successfully", summary);
    }

    [Fact]
    public void StatusPacket_RoundTripsAllDiagnosticFields()
    {
        var original = new RemoteProcessStatus
        {
            RequestId = "request-1",
            FilePath = @"C:\Deploy\setup.msi",
            Status = RemoteProcessStatuses.Completed,
            Message = "Cần khởi động lại.",
            ProcessId = 123,
            ExitCode = 3010,
            RequiresRestart = true,
            DiagnosticDetails = "Return value 3",
            CreatedAtUtc = new DateTime(2026, 8, 24, 1, 2, 3, DateTimeKind.Utc)
        };

        RemoteProcessStatus? restored = JsonSerializer.Deserialize<RemoteProcessStatus>(
            JsonSerializer.Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal(original.RequestId, restored.RequestId);
        Assert.Equal(original.FilePath, restored.FilePath);
        Assert.Equal(original.Status, restored.Status);
        Assert.Equal(original.ProcessId, restored.ProcessId);
        Assert.Equal(original.ExitCode, restored.ExitCode);
        Assert.True(restored.RequiresRestart);
        Assert.Equal(original.DiagnosticDetails, restored.DiagnosticDetails);
        Assert.Equal(original.CreatedAtUtc, restored.CreatedAtUtc);
    }

    private static RemoteProcessRequest Request(string filePath) => new()
    {
        RequestId = Guid.NewGuid().ToString("N"),
        FilePath = filePath,
        RequestedAtUtc = DateTime.UtcNow
    };
}
