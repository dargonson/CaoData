using AgentShared;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AgentService
{
    internal sealed class RemoteProcessRunner : IDisposable
    {
        private readonly SemaphoreSlim _executionLock = new(1, 1);

        public async Task ExecuteAsync(
            RemoteProcessRequest? request,
            Func<RemoteProcessStatus, Task> reportStatusAsync,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(reportStatusAsync);

            RemoteProcessStatus status = CreateStatus(request, RemoteProcessStatuses.Waiting, "Agent đã nhận lệnh và đang chờ thực thi.");
            await reportStatusAsync(status);

            await _executionLock.WaitAsync(cancellationToken);
            try
            {
                string? validationError = ValidateRequest(request, out string fullPath);
                if (validationError != null)
                {
                    await reportStatusAsync(CreateStatus(
                        request,
                        RemoteProcessStatuses.Error,
                        validationError,
                        errorCode: 87));
                    return;
                }

                string temporaryMsiLogPath = string.Empty;
                try
                {
                    if (IsMsiFile(fullPath))
                    {
                        temporaryMsiLogPath = CreateTemporaryMsiLogPath(request!.RequestId);
                    }

                    int[] runningMsiProcessIds = IsMsiFile(fullPath)
                        ? GetRunningMsiProcessIds()
                        : Array.Empty<int>();
                    string? msiConflict = GetMsiConflictError(
                        fullPath,
                        request!.TerminateExistingMsiProcesses,
                        runningMsiProcessIds);
                    if (msiConflict != null)
                    {
                        await reportStatusAsync(CreateStatus(
                            request,
                            RemoteProcessStatuses.Error,
                            msiConflict,
                            errorCode: 1618));
                        return;
                    }
                    if (runningMsiProcessIds.Length > 0)
                    {
                        await TerminateMsiProcessesAsync(runningMsiProcessIds, cancellationToken);
                    }

                    ProcessStartInfo startInfo = BuildStartInfo(
                        fullPath,
                        request.Arguments,
                        temporaryMsiLogPath);
                    using Process? process = Process.Start(startInfo);
                    if (process == null)
                    {
                        await reportStatusAsync(CreateStatus(
                            request,
                            RemoteProcessStatuses.Error,
                            "Windows không tạo được tiến trình.",
                            errorCode: 31));
                        return;
                    }

                    await reportStatusAsync(CreateStatus(
                        request,
                        RemoteProcessStatuses.Running,
                        $"Tiến trình đang chạy với quyền LocalSystem trong Session 0. PID: {process.Id}.",
                        processId: process.Id));

                    await process.WaitForExitAsync(cancellationToken);
                    int exitCode = process.ExitCode;
                    bool success = IsSuccessfulExitCode(Path.GetExtension(fullPath), exitCode, out bool restartRequired);
                    bool isMsi = IsMsiFile(fullPath);
                    string message = isMsi
                        ? $"MSI {exitCode}: {MsiExitCodeCatalog.GetVietnameseMessage(exitCode)}"
                        : success
                            ? $"Tiến trình hoàn thành với mã thoát {exitCode}."
                            : $"Tiến trình kết thúc với mã lỗi {exitCode}.";
                    string diagnosticDetails = isMsi
                        ? ReadMsiDiagnosticSummary(temporaryMsiLogPath)
                        : string.Empty;

                    await reportStatusAsync(CreateStatus(
                        request,
                        success ? RemoteProcessStatuses.Completed : RemoteProcessStatuses.Error,
                        message,
                        processId: process.Id,
                        exitCode: exitCode,
                        errorCode: success ? null : exitCode,
                        requiresRestart: restartRequired,
                        diagnosticDetails: diagnosticDetails));
                }
                catch (Win32Exception ex)
                {
                    await reportStatusAsync(CreateStatus(
                        request,
                        RemoteProcessStatuses.Error,
                        ex.Message,
                        errorCode: ex.NativeErrorCode,
                        diagnosticDetails: ReadMsiDiagnosticSummary(temporaryMsiLogPath)));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await reportStatusAsync(CreateStatus(
                        request,
                        RemoteProcessStatuses.Error,
                        ex.Message,
                        errorCode: ex.HResult,
                        diagnosticDetails: ReadMsiDiagnosticSummary(temporaryMsiLogPath)));
                }
                finally
                {
                    TryDeleteTemporaryMsiLog(temporaryMsiLogPath);
                }
            }
            finally
            {
                _executionLock.Release();
            }
        }

        internal static string? ValidateRequest(RemoteProcessRequest? request, out string fullPath)
        {
            fullPath = string.Empty;
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
            {
                return "Lệnh chạy không hợp lệ hoặc thiếu mã yêu cầu.";
            }
            if (string.IsNullOrWhiteSpace(request.FilePath))
            {
                return "Đường dẫn file đang trống.";
            }
            if ((request.Arguments?.Length ?? 0) > RemoteProcessRules.MaximumArgumentsLength)
            {
                return $"Tham số vượt quá {RemoteProcessRules.MaximumArgumentsLength} ký tự.";
            }

            try
            {
                if (!Path.IsPathFullyQualified(request.FilePath))
                {
                    return "Đường dẫn file phải là đường dẫn tuyệt đối trên Agent.";
                }
                fullPath = Path.GetFullPath(request.FilePath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "Đường dẫn file không hợp lệ: " + ex.Message;
            }

            if (!RemoteProcessRules.IsSupportedFile(fullPath))
            {
                return $"Chỉ cho phép chạy file {RemoteProcessRules.SupportedExtensionsDisplay}.";
            }
            if (Directory.Exists(fullPath))
            {
                return "Không cho phép chạy thư mục.";
            }
            if (!File.Exists(fullPath))
            {
                return "File không tồn tại trên Agent.";
            }
            return null;
        }

        internal static ProcessStartInfo BuildStartInfo(
            string fullPath,
            string? arguments,
            string? diagnosticLogPath = null)
        {
            string extension = Path.GetExtension(fullPath);
            string workingDirectory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;
            string safeArguments = arguments?.Trim() ?? string.Empty;
            ProcessStartInfo startInfo;

            if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                    Arguments = $"/i {QuoteWindowsArgument(fullPath)}" +
                        $"{AppendArguments(safeArguments)} /qn /norestart REBOOT=ReallySuppress" +
                        (string.IsNullOrWhiteSpace(diagnosticLogPath)
                            ? string.Empty
                            : $" /L*V {QuoteWindowsArgument(diagnosticLogPath)}")
                };
            }
            else if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("ComSpec") ??
                        Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    Arguments = $"/d /s /c \"\"{fullPath}\"{AppendArguments(safeArguments)}\""
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = fullPath,
                    Arguments = safeArguments
                };
            }

            startInfo.WorkingDirectory = workingDirectory;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            return startInfo;
        }

        internal static bool IsSuccessfulExitCode(string? extension, int exitCode, out bool restartRequired)
        {
            bool isMsi = string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase);
            restartRequired = isMsi && (exitCode == 1641 || exitCode == 3010);
            return exitCode == 0 || restartRequired;
        }

        internal static bool IsMsiFile(string? path) =>
            string.Equals(Path.GetExtension(path), ".msi", StringComparison.OrdinalIgnoreCase);

        internal static string? GetMsiConflictError(
            string filePath,
            bool terminateExistingProcesses,
            IReadOnlyCollection<int> runningProcessIds)
        {
            if (!IsMsiFile(filePath) || runningProcessIds.Count == 0 || terminateExistingProcesses)
            {
                return null;
            }

            return "Windows Installer đang bận. msiexec.exe đang chạy với PID: " +
                   string.Join(", ", runningProcessIds.Distinct().OrderBy(id => id)) + ".";
        }

        internal static int[] GetRunningMsiProcessIds()
        {
            var processIds = new List<int>();
            foreach (Process process in Process.GetProcessesByName("msiexec"))
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            processIds.Add(process.Id);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Tiến trình vừa thoát giữa lúc kiểm tra.
                    }
                    catch (Win32Exception)
                    {
                        // Không đọc được trạng thái nhưng PID vẫn chứng minh msiexec đang tồn tại.
                        processIds.Add(process.Id);
                    }
                }
            }
            return processIds.Distinct().OrderBy(id => id).ToArray();
        }

        private static async Task TerminateMsiProcessesAsync(
            IEnumerable<int> processIds,
            CancellationToken cancellationToken)
        {
            foreach (int processId in processIds.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using Process process = Process.GetProcessById(processId);
                    if (process.HasExited)
                    {
                        continue;
                    }

                    process.Kill();
                    await process.WaitForExitAsync(cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                }
                catch (ArgumentException)
                {
                    // Tiến trình đã thoát sau bước preflight.
                }
            }
        }

        internal static string CreateTemporaryMsiLogPath(string requestId)
        {
            string safeRequestId = new string(
                (requestId ?? string.Empty)
                    .Where(character => char.IsLetterOrDigit(character) || character == '-')
                    .Take(80)
                    .ToArray());
            if (string.IsNullOrWhiteSpace(safeRequestId))
            {
                safeRequestId = Guid.NewGuid().ToString("N");
            }

            return Path.Combine(Path.GetTempPath(), $"CaoData-Agent-MSI-{safeRequestId}.log");
        }

        private static void TryDeleteTemporaryMsiLog(string? logPath)
        {
            if (string.IsNullOrWhiteSpace(logPath))
            {
                return;
            }

            try
            {
                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }
            }
            catch
            {
                // Không làm thay đổi kết quả cài đặt nếu Windows vừa giữ file log.
                // Đăng ký xóa khi Agent kết thúc để không lưu log lâu dài trên máy.
                try
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                    {
                        try { File.Delete(logPath); } catch { }
                    };
                }
                catch { }
            }
        }

        internal static string ReadMsiDiagnosticSummary(string? logPath)
        {
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            {
                return string.Empty;
            }

            try
            {
                var importantLines = new Queue<string>();
                var tailLines = new Queue<string>();
                foreach (string rawLine in File.ReadLines(logPath))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!IsInstallerErrorTableNoise(line))
                    {
                        EnqueueLimited(tailLines, line, 8);
                    }
                    if (IsImportantMsiDiagnosticLine(line))
                    {
                        EnqueueLimited(importantLines, line, 12);
                    }
                }

                IEnumerable<string> selectedLines = importantLines.Count > 0
                    ? importantLines
                    : tailLines;
                var summary = new StringBuilder();
                foreach (string line in selectedLines)
                {
                    if (summary.Length > 0)
                    {
                        summary.AppendLine();
                    }
                    int remaining = 4000 - summary.Length;
                    if (remaining <= 0)
                    {
                        break;
                    }
                    summary.Append(line.AsSpan(0, Math.Min(line.Length, remaining)));
                }
                return summary.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsImportantMsiDiagnosticLine(string line)
        {
            if (line.Contains("Return value 3", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("exception", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Windows Installer thường truy vấn bảng Error trong log kể cả khi cài đặt
            // thành công. Chỉ nhận dạng các lỗi MSI có mã thực (ví dụ "Error 1638.").
            int searchStart = 0;
            while ((searchStart = line.IndexOf("Error ", searchStart, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int codeStart = searchStart + "Error ".Length;
                int codeEnd = codeStart;
                while (codeEnd < line.Length && char.IsDigit(line[codeEnd]))
                {
                    codeEnd++;
                }

                if (codeEnd - codeStart >= 3)
                {
                    return true;
                }
                searchStart = codeStart;
            }
            return false;
        }

        private static bool IsInstallerErrorTableNoise(string line)
        {
            return line.Contains("SELECT `Message` FROM `Error`", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("Note: 1: 2205", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnqueueLimited(Queue<string> lines, string value, int maximumCount)
        {
            lines.Enqueue(value);
            while (lines.Count > maximumCount)
            {
                lines.Dequeue();
            }
        }

        private static RemoteProcessStatus CreateStatus(
            RemoteProcessRequest? request,
            string status,
            string message,
            int? processId = null,
            int? exitCode = null,
            int? errorCode = null,
            bool requiresRestart = false,
            string diagnosticDetails = "") => new()
            {
                RequestId = request?.RequestId ?? string.Empty,
                FilePath = request?.FilePath ?? string.Empty,
                Status = status,
                Message = message,
                ProcessId = processId,
                ExitCode = exitCode,
                ErrorCode = errorCode,
                RequiresRestart = requiresRestart,
                DiagnosticDetails = diagnosticDetails,
                CreatedAtUtc = DateTime.UtcNow
            };

        private static string AppendArguments(string arguments) =>
            string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments;

        private static string QuoteWindowsArgument(string value) =>
            "\"" + value.Replace("\"", "\\\"") + "\"";

        public void Dispose()
        {
            _executionLock.Dispose();
        }
    }
}
