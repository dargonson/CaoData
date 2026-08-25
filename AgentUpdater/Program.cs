using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using AgentShared;

namespace AgentUpdater
{
    internal static class Program
    {
        private const int RetryDelayMs = 1000;
        private const int MaxWaitSeconds = 90;

        private static async Task<int> Main(string[] args)
        {
            UpdateOptions? options = null;
            UpdateStatusReporter? reporter = null;
            string logPath = AppVersion.GetAgentUpdaterLogPath();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                options = UpdateOptions.Parse(args);
                reporter = new UpdateStatusReporter(
                    options.AgentId,
                    options.SessionId,
                    options.ControlHost,
                    options.ControlPort,
                    options.SecurityConfig);
                await LogAsync(logPath, "Bắt đầu update AgentServices.");
                await SendStatusAsync(reporter, logPath, "UpdaterStarted", "AgentUpdater đã khởi động. Thông báo này được gửi từ AgentUpdater.");

                string actualSha256 = await ComputeSha256Async(options.NewExe);
                if (!string.Equals(actualSha256, options.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("SHA-256 của file AgentServices mới không khớp.");
                }

                Directory.CreateDirectory(options.BackupDirectory);
                string backupPath = Path.Combine(options.BackupDirectory, Path.GetFileName(options.CurrentExe) + ".bak");
                await AgentUpdateWorkflow.ExecuteAsync(
                    options.CurrentExe,
                    options.NewExe,
                    backupPath,
                    new AgentUpdateWorkflowOperations
                    {
                        StopServiceAsync = () => RunScAsync("stop", options.ServiceName, logPath),
                        TryStopServiceAsync = () => TryRunScAsync("stop", options.ServiceName, logPath),
                        WaitUntilUnlockedAsync = () => WaitUntilFileUnlockedAsync(options.CurrentExe, logPath),
                        StartAndVerifyServiceAsync = async () =>
                        {
                            await RunScAsync("start", options.ServiceName, logPath);
                            await WaitForServiceRunningAsync(options.ServiceName, logPath);
                        },
                        EnsureServiceRunningAsync = () => EnsureServiceRunningAsync(options.ServiceName, logPath),
                        WriteCompletionMarkerAsync = () => WriteCompletionMarkerAsync(options),
                        DeleteCompletionMarkerAsync = () => TryDeleteCompletionMarkerAsync(logPath),
                        CopyFileAsync = (source, destination, overwrite) =>
                        {
                            File.Copy(source, destination, overwrite);
                            return Task.CompletedTask;
                        },
                        ReportStatusAsync = (status, message) =>
                            SendStatusAsync(reporter, logPath, status, message),
                        LogAsync = message => LogAsync(logPath, message)
                    });

                /*
                 * Khong gui them status qua updater sau khi start: AgentServices moi se doc
                 * marker va tu gui Completed sau khi no thuc su dang ky lai voi Control.
                 */
                for (int i = 5; i >= 1; i--)
                {
                    await LogAsync(logPath, $"AgentUpdater sẽ thoát trong {i}s.");
                    await Task.Delay(1000);
                }

                await LogAsync(logPath, "Update hoàn tất. TargetVersion=" + options.TargetVersion);
                return 0;
            }
            catch (Exception ex)
            {
                await LogAsync(logPath, "Update lỗi: " + ex);
                if (reporter != null)
                {
                    await SendStatusAsync(reporter, logPath, "Error", ex.Message);
                }
                return 1;
            }
        }

        private static async Task SendStatusAsync(UpdateStatusReporter reporter, string logPath, string status, string message)
        {
            await LogAsync(logPath, status + ": " + message);
            await reporter.SendAsync(status, message);
        }

        private static async Task WriteCompletionMarkerAsync(UpdateOptions options)
        {
            string markerPath = AppVersion.GetAgentUpdateCompletionMarkerPath();

            var marker = new AgentUpdateCompletionMarker
            {
                SessionId = options.SessionId,
                TargetVersion = options.TargetVersion,
                CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            string temporaryPath = markerPath + ".tmp";
            await using (FileStream destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    destination,
                    marker,
                    new JsonSerializerOptions { WriteIndented = true });
                await destination.FlushAsync();
                destination.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, markerPath, overwrite: true);
        }

        private static async Task TryDeleteCompletionMarkerAsync(string logPath)
        {
            try
            {
                string markerPath = AppVersion.GetAgentUpdateCompletionMarkerPath();
                if (File.Exists(markerPath)) File.Delete(markerPath);
                string temporaryPath = markerPath + ".tmp";
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (Exception ex)
            {
                await LogAsync(logPath, "Không xóa được completion marker khi rollback: " + ex.Message);
            }
        }

        private static async Task RunScAsync(string command, string serviceName, string logPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"{command} \"{serviceName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Không chạy được sc.exe.");
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            await LogAsync(logPath, $"sc {command} exit={process.ExitCode} output={output.Trim()} error={error.Trim()}");
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"sc {command} {serviceName} thất bại. ExitCode={process.ExitCode}. {output} {error}");
            }
        }

        private static async Task TryRunScAsync(string command, string serviceName, string logPath)
        {
            try
            {
                await RunScAsync(command, serviceName, logPath);
            }
            catch (Exception ex)
            {
                await LogAsync(logPath, $"Bỏ qua lỗi sc {command} khi rollback: {ex.Message}");
            }
        }

        private static async Task EnsureServiceRunningAsync(string serviceName, string logPath)
        {
            if (!await IsServiceRunningAsync(serviceName, logPath))
            {
                try
                {
                    await RunScAsync("start", serviceName, logPath);
                }
                catch (Exception startError)
                {
                    // Service co the vua duoc Windows/SCM tu khoi dong trong luc query.
                    if (!await IsServiceRunningAsync(serviceName, logPath))
                    {
                        throw new InvalidOperationException(
                            "Không thể khởi động lại AgentServices sau rollback.",
                            startError);
                    }
                }
            }

            await WaitForServiceRunningAsync(serviceName, logPath);
        }

        private static async Task<bool> IsServiceRunningAsync(string serviceName, string logPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query \"{serviceName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Không chạy được sc.exe query.");
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await LogAsync(logPath, $"sc query exit={process.ExitCode} output={output.Trim()} error={error.Trim()}");
            return process.ExitCode == 0 &&
                   output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task WaitForServiceRunningAsync(string serviceName, string logPath)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(MaxWaitSeconds);
            while (DateTime.UtcNow < deadline)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"query \"{serviceName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Không chạy được sc.exe query.");
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode == 0 &&
                    output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                {
                    await LogAsync(logPath, "Service đã xác nhận trạng thái RUNNING.");
                    return;
                }

                await Task.Delay(RetryDelayMs);
            }

            throw new TimeoutException($"Service {serviceName} không đạt trạng thái RUNNING sau {MaxWaitSeconds} giây.");
        }

        private static async Task WaitUntilFileUnlockedAsync(string filePath, string logPath)
        {
            DateTime deadline = DateTime.Now.AddSeconds(MaxWaitSeconds);
            Exception? lastError = null;

            while (DateTime.Now < deadline)
            {
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    await Task.Delay(RetryDelayMs);
                }
            }

            await LogAsync(logPath, "File vẫn bị khóa sau khi chờ: " + filePath);
            throw lastError ?? new IOException("File AgentServices.exe vẫn bị khóa.");
        }

        private static async Task<string> ComputeSha256Async(string filePath)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await sha.ComputeHashAsync(fs);
            return Convert.ToHexString(hash);
        }

        private static Task LogAsync(string logPath, string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            return File.AppendAllTextAsync(logPath, line);
        }

        private sealed class UpdateOptions
        {
            public string ServiceName { get; private set; } = "AgentServices";
            public string CurrentExe { get; private set; } = string.Empty;
            public string NewExe { get; private set; } = string.Empty;
            public string BackupDirectory { get; private set; } = string.Empty;
            public string ExpectedSha256 { get; private set; } = string.Empty;
            public string TargetVersion { get; private set; } = string.Empty;
            public string AgentId { get; private set; } = string.Empty;
            public string SessionId { get; private set; } = string.Empty;
            public string ControlHost { get; private set; } = string.Empty;
            public int ControlPort { get; private set; }
            public string SecurityConfig { get; private set; } = string.Empty;

            public static UpdateOptions Parse(string[] args)
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < args.Length; i++)
                {
                    if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string key = args[i].Substring(2);
                    string value = i + 1 < args.Length ? args[++i] : string.Empty;
                    values[key] = value;
                }

                var options = new UpdateOptions
                {
                    ServiceName = Get(values, "service-name", "AgentServices"),
                    CurrentExe = GetRequired(values, "current-exe"),
                    NewExe = GetRequired(values, "new-exe"),
                    BackupDirectory = GetRequired(values, "backup-dir"),
                    ExpectedSha256 = GetRequired(values, "expected-sha256"),
                    TargetVersion = Get(values, "target-version", string.Empty),
                    AgentId = GetRequired(values, "agent-id"),
                    SessionId = GetRequired(values, "session-id"),
                    ControlHost = GetRequired(values, "control-host"),
                    ControlPort = int.TryParse(Get(values, "control-port", "9000"), out int port) ? port : 9000,
                    SecurityConfig = GetRequired(values, "security-config")
                };

                options.CurrentExe = Path.GetFullPath(options.CurrentExe);
                options.NewExe = Path.GetFullPath(options.NewExe);
                options.BackupDirectory = Path.GetFullPath(options.BackupDirectory);
                options.SecurityConfig = Path.GetFullPath(options.SecurityConfig);

                if (!File.Exists(options.CurrentExe))
                {
                    throw new FileNotFoundException("Không tìm thấy AgentServices.exe hiện tại.", options.CurrentExe);
                }
                if (!File.Exists(options.NewExe))
                {
                    throw new FileNotFoundException("Không tìm thấy AgentServices.exe mới.", options.NewExe);
                }
                if (options.CurrentExe.Equals(options.NewExe, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("File AgentServices mới phải nằm ngoài đường dẫn EXE đang chạy.");
                }
                if (!IsSha256(options.ExpectedSha256))
                {
                    throw new ArgumentException("--expected-sha256 không hợp lệ.");
                }
                if (options.ControlPort is < 1 or > 65535)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(options.ControlPort),
                        options.ControlPort,
                        "Port Control không hợp lệ.");
                }
                if (string.IsNullOrWhiteSpace(options.ServiceName) ||
                    options.ServiceName.Any(char.IsControl) ||
                    options.ServiceName.Contains('"'))
                {
                    throw new ArgumentException("--service-name không hợp lệ.");
                }

                return options;
            }

            private static bool IsSha256(string value) =>
                value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

            private static string Get(Dictionary<string, string> values, string key, string defaultValue)
            {
                return values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : defaultValue;
            }

            private static string GetRequired(Dictionary<string, string> values, string key)
            {
                string value = Get(values, key, string.Empty);
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Thiếu tham số: --" + key);
                }

                return value;
            }
        }

        private sealed class UpdateStatusReporter
        {
            private readonly string _agentId;
            private readonly string _sessionId;
            private readonly string _host;
            private readonly int _port;
            private readonly string _sharedKey;

            public UpdateStatusReporter(string agentId, string sessionId, string host, int port, string securityConfig)
            {
                _agentId = agentId;
                _sessionId = sessionId;
                _host = host;
                _port = port;
                _sharedKey = LoadSharedKey(securityConfig);
            }

            public async Task SendAsync(string status, string message)
            {
                if (string.IsNullOrWhiteSpace(_host) || _port <= 0)
                {
                    return;
                }

                try
                {
                    using var client = new TcpClient();
                    using var timeoutCts = new CancellationTokenSource(5000);
                    await client.ConnectAsync(_host, _port, timeoutCts.Token);

                    var updateStatus = new AgentUpdateStatus
                    {
                        SessionId = _sessionId,
                        Status = status,
                        Message = message,
                        Version = string.Empty,
                        Source = "AgentUpdater",
                        CreatedAt = DateTime.Now.ToString("HH:mm:ss")
                    };

                    var packet = new SocketPacket
                    {
                        Type = AgentUpdatePacketTypes.UpdateAgentStatus,
                        AgentID = _agentId,
                        Data = JsonSerializer.Serialize(updateStatus)
                    };

                    using Stream secureStream = await SecureTransport.AuthenticateClientAsync(
                        client.GetStream(),
                        _host,
                        _agentId,
                        _sharedKey,
                        timeoutCts.Token);
                    await TransferFrameProtocol.WriteJsonPacketAsync(secureStream, packet, timeoutCts.Token);
                }
                catch
                {
                    // Update must continue even if Control cannot receive a progress line.
                }
            }

            private static string LoadSharedKey(string securityConfig)
            {
                string key = Environment.GetEnvironmentVariable("CAODATA_SHARED_KEY") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key) && File.Exists(securityConfig))
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(securityConfig));
                    if (document.RootElement.TryGetProperty("ConnectionConfig", out JsonElement section) &&
                        section.TryGetProperty("SharedKey", out JsonElement keyElement))
                    {
                        key = keyElement.GetString() ?? string.Empty;
                    }
                }

                key = key.Trim();
                if (key.Length < 32)
                {
                    throw new InvalidOperationException("Không đọc được SharedKey cho AgentUpdater.");
                }

                return key;
            }
        }
    }
}
