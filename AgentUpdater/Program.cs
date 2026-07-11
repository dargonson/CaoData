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
                reporter = new UpdateStatusReporter(options.AgentId, options.SessionId, options.ControlHost, options.ControlPort);
                await LogAsync(logPath, "Bắt đầu update AgentServices.");
                await SendStatusAsync(reporter, logPath, "UpdaterStarted", "AgentUpdater đã khởi động. Thông báo này được gửi từ AgentUpdater.");

                string actualSha256 = await ComputeSha256Async(options.NewExe);
                if (!string.Equals(actualSha256, options.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("SHA-256 của file AgentServices mới không khớp.");
                }

                Directory.CreateDirectory(options.BackupDirectory);
                string backupPath = Path.Combine(options.BackupDirectory, Path.GetFileName(options.CurrentExe) + ".bak");

                await SendStatusAsync(reporter, logPath, "StoppingService", "Chuẩn bị tắt AgentServices.");
                await RunScAsync("stop", options.ServiceName, logPath);
                await WaitUntilFileUnlockedAsync(options.CurrentExe, logPath);
                await SendStatusAsync(reporter, logPath, "ServiceStopped", "Đã tắt thành công.");

                File.Copy(options.CurrentExe, backupPath, true);
                await LogAsync(logPath, "Đã backup: " + backupPath);

                try
                {
                    await SendStatusAsync(reporter, logPath, "UpdatingFile", "Chuẩn bị update file mới.");
                    File.Copy(options.NewExe, options.CurrentExe, true);
                    await LogAsync(logPath, "Đã copy file mới vào: " + options.CurrentExe);
                    await SendStatusAsync(reporter, logPath, "FileUpdated", "Đã update file mới.");
                }
                catch
                {
                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, options.CurrentExe, true);
                        await LogAsync(logPath, "Đã rollback file cũ.");
                    }

                    throw;
                }

                await WriteCompletionMarkerAsync(options);
                await SendStatusAsync(reporter, logPath, "StartingService", "Khởi động lại services......");
                await RunScAsync("start", options.ServiceName, logPath);
                await SendStatusAsync(reporter, logPath, "ServiceStarted", "Khởi động thành công...");
                await SendStatusAsync(reporter, logPath, "WaitingAgent", "Đang chờ AgentServices kết nối đến Control.");
                await SendStatusAsync(reporter, logPath, "ControlConnected", "Đã kết nối thành công đến Control.");

                for (int i = 5; i >= 1; i--)
                {
                    await SendStatusAsync(reporter, logPath, "ExitCountdown", $"AgentUpdater sẽ thoát trong {i}s.");
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
            await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
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
                    ControlPort = int.TryParse(Get(values, "control-port", "9000"), out int port) ? port : 9000
                };

                if (!File.Exists(options.CurrentExe))
                {
                    throw new FileNotFoundException("Không tìm thấy AgentServices.exe hiện tại.", options.CurrentExe);
                }
                if (!File.Exists(options.NewExe))
                {
                    throw new FileNotFoundException("Không tìm thấy AgentServices.exe mới.", options.NewExe);
                }

                return options;
            }

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

            public UpdateStatusReporter(string agentId, string sessionId, string host, int port)
            {
                _agentId = agentId;
                _sessionId = sessionId;
                _host = host;
                _port = port;
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

                    await TransferFrameProtocol.WriteJsonPacketAsync(client.GetStream(), packet, timeoutCts.Token);
                }
                catch
                {
                    // Update must continue even if Control cannot receive a progress line.
                }
            }
        }
    }
}
