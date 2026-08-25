using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AgentShared;
using System.Collections.Concurrent;
using System.Buffers;
// Dùng chung định dạng gói tin với Server

namespace AgentService
{
    public class Worker : BackgroundService
    {
        private const string AgentServiceVersion = AppVersion.CurrentVersionAgent;

        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly AgentUpdateClient _updateClient;
        // BO SUNG MODULE BACKUP: scheduler/engine tach rieng, Worker chi noi socket.
        private readonly AgentBackupManager _backupManager;
        private string _activeControlHost = "127.0.0.1";
        private int _activeControlPort = 9000;
        private readonly string _sharedKey;

        private TcpClient? _client;
        private Stream? _stream;
        private bool _isConnected = false;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _downloadLock = new SemaphoreSlim(3, 3);
        private readonly ConcurrentDictionary<string, IDisposable> _uploadSleepBlocks =
            new ConcurrentDictionary<string, IDisposable>(StringComparer.OrdinalIgnoreCase);

        // Chu kỳ Reconnect lũy tiến theo yêu cầu: 5s, 10s, 20s, 30s
        private readonly int[] _reconnectDelays = { 5, 10, 20, 30 };
        private int _reconnectIndex = 0;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _sharedKey = (Environment.GetEnvironmentVariable("CAODATA_SHARED_KEY")
                ?? configuration["ConnectionConfig:SharedKey"]
                ?? string.Empty).Trim();
            if (_sharedKey.Length < 32)
            {
                throw new InvalidOperationException(
                    "ConnectionConfig:SharedKey chưa được cấu hình hoặc ngắn hơn 32 ký tự.");
            }
            _updateClient = new AgentUpdateClient(SendPacketAsync, () => (_activeControlHost, _activeControlPort), logger);
            string agentId = HardwareInfo.GetUniqueAgentID();
            _backupManager = new AgentBackupManager(
                logger,
                agentId,
                () => _isConnected,
                SendBackupPacketAsync,
                SendBackupChunkAsync);
        }




        // Hãy dán hàm phụ trợ này vào bất kỳ vị trí trống nào trong lòng class Worker : BackgroundService
        private AgentInfo CollectSystemInfo()
        {
            string machineName = Environment.MachineName;
            string username = GetLoggedOnUsername();
            string osVersion = "Windows Unknown";
            var os = Environment.OSVersion;

            if (os.Platform == PlatformID.Win32NT)
            {
                // Dựa vào cơ chế quản lý phiên bản của Microsoft để lọc tên thương mại
                switch (os.Version.Major)
                {
                    case 10:
                        // Từ Build 22000 trở lên Microsoft chính thức gọi là Windows 11
                        if (os.Version.Build >= 22000)
                            osVersion = "Windows 11";
                        else
                            osVersion = "Windows 10";
                        break;
                    case 6:
                        switch (os.Version.Minor)
                        {
                            case 3: osVersion = "Windows 8.1"; break;
                            case 2: osVersion = "Windows 8"; break;
                            case 1: osVersion = "Windows 7"; break;
                            case 0: osVersion = "Windows Vista"; break;
                        }
                        break;
                    case 5:
                        osVersion = "Windows XP";
                        break;
                }
            }

            string agentId = HardwareInfo.GetUniqueAgentID();

            return new AgentInfo
            {
                AgentID = agentId, // Mã 10 ký tự bất tử đã được gán vào đây
                MachineName = machineName,
                Username = username,
                IPAddress = GetLocalIPAddress(), // Gọi hàm lấy IP LAN
                OSVersion = osVersion,
                AgentVersion = AgentServiceVersion
            };
        }

        // Hàm phụ trợ lấy IP LAN gọn gàng cho fen
        private string GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private string GetLoggedOnUsername()
        {
            string? activeUsername = TryGetActiveSessionUsername();
            if (!string.IsNullOrWhiteSpace(activeUsername))
            {
                return activeUsername;
            }

            string serviceUsername = Environment.UserName;
            if (serviceUsername.EndsWith("$", StringComparison.Ordinal))
            {
                return "No logged-in user";
            }

            return serviceUsername;
        }

        private static string? TryGetActiveSessionUsername()
        {
            string? activeSessionUsername = TryGetUsernameFromActiveSessions();
            if (!string.IsNullOrWhiteSpace(activeSessionUsername))
            {
                return activeSessionUsername;
            }

            uint sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                return null;
            }

            string? username = QuerySessionString(sessionId, WTS_INFO_CLASS.WTSUserName);
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            return username;
        }

        private static string? TryGetUsernameFromActiveSessions()
        {
            IntPtr sessionInfoPointer = IntPtr.Zero;

            try
            {
                if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out sessionInfoPointer, out int sessionCount) ||
                    sessionInfoPointer == IntPtr.Zero ||
                    sessionCount <= 0)
                {
                    return null;
                }

                int dataSize = Marshal.SizeOf<WTS_SESSION_INFO>();
                for (int i = 0; i < sessionCount; i++)
                {
                    IntPtr current = IntPtr.Add(sessionInfoPointer, i * dataSize);
                    WTS_SESSION_INFO sessionInfo = Marshal.PtrToStructure<WTS_SESSION_INFO>(current);
                    if (sessionInfo.State != WTS_CONNECTSTATE_CLASS.WTSActive)
                    {
                        continue;
                    }

                    string? username = QuerySessionString(sessionInfo.SessionId, WTS_INFO_CLASS.WTSUserName);
                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        return username;
                    }
                }
            }
            finally
            {
                if (sessionInfoPointer != IntPtr.Zero)
                {
                    WTSFreeMemory(sessionInfoPointer);
                }
            }

            return null;
        }

        private static string? QuerySessionString(uint sessionId, WTS_INFO_CLASS infoClass)
        {
            IntPtr buffer = IntPtr.Zero;

            try
            {
                if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out buffer, out int bytesReturned) ||
                    buffer == IntPtr.Zero ||
                    bytesReturned <= 1)
                {
                    return null;
                }

                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    WTSFreeMemory(buffer);
                }
            }
        }

        private enum WTS_INFO_CLASS
        {
            WTSUserName = 5
        }

        private enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,
            WTSConnected,
            WTSConnectQuery,
            WTSShadow,
            WTSDisconnected,
            WTSIdle,
            WTSListen,
            WTSReset,
            WTSDown,
            WTSInit
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WTS_SESSION_INFO
        {
            public uint SessionId;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string WinStationName;

            public WTS_CONNECTSTATE_CLASS State;
        }

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WTSQuerySessionInformation(
            IntPtr hServer,
            uint sessionId,
            WTS_INFO_CLASS wtsInfoClass,
            out IntPtr ppBuffer,
            out int pBytesReturned);

        [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WTSEnumerateSessions(
            IntPtr hServer,
            int reserved,
            int version,
            out IntPtr ppSessionInfo,
            out int pCount);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr pointer);


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            // Bọc lót chống Null tuyệt đối: Nếu không tìm thấy thẻ trong JSON thì tự lấy mặc định
            string lanIP = "127.0.0.1";
            string wanIP = "127.0.0.1";
            int port = 9000;
            int connectionTimeoutMs = 8000;

            if (_configuration != null)
            {
                lanIP = (_configuration["ConnectionConfig:ServerLAN"] ?? "127.0.0.1").Trim();
                wanIP = (_configuration["ConnectionConfig:ServerWAN"] ?? "127.0.0.1").Trim();
                if (!int.TryParse(_configuration["ConnectionConfig:Port"], out port) ||
                    port is < 1 or > 65535)
                {
                    port = 9000;
                }
                if (!int.TryParse(
                        _configuration["ConnectionConfig:ConnectionTimeoutMs"],
                        out connectionTimeoutMs) ||
                    connectionTimeoutMs is < 1000 or > 120000)
                {
                    connectionTimeoutMs = 8000;
                }
            }

            string agentID = HardwareInfo.GetUniqueAgentID();

            // Kiểm tra logger trước khi ghi để tránh NullReferenceException
            _logger?.LogInformation("Agent Service bắt đầu khởi chạy với ID: {AgentID}", agentID);

            // BO SUNG MODULE BACKUP: scheduler doc appsettings va chi chay khi den lich.
            Task backupSchedulerTask = RunBackgroundOperationAsync(
                () => _backupManager.RunSchedulerAsync(stoppingToken),
                "lịch backup",
                stoppingToken);

            // Mỗi lần kết nối chỉ có đúng một listener và một heartbeat.
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger?.LogInformation("Đang thử kết nối tới Server...");

                _isConnected = await TryConnectAsync(lanIP, port, connectionTimeoutMs, "LAN", stoppingToken);
                if (!_isConnected && !stoppingToken.IsCancellationRequested)
                {
                    _logger?.LogWarning("Kết nối LAN thất bại. Đang chuyển sang thử WAN...");
                    _isConnected = await TryConnectAsync(wanIP, port, connectionTimeoutMs, "WAN", stoppingToken);
                }

                if (!_isConnected)
                {
                    int delaySeconds = _reconnectDelays[_reconnectIndex];
                    _logger?.LogError("Không thể kết nối tới Server. Thử lại sau {delay} giây...", delaySeconds);
                    if (_reconnectIndex < _reconnectDelays.Length - 1)
                    {
                        _reconnectIndex++;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                    continue;
                }

                _logger?.LogInformation("KẾT NỐI SERVER THÀNH CÔNG!");
                _reconnectIndex = 0;
                using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                Task? heartbeatTask = null;
                try
                {
                    await SendRegisterInfoAsync(connectionCts.Token);
                    await _updateClient.SendPendingCompletionStatusAsync(agentID, connectionCts.Token);
                    heartbeatTask = StartHeartbeatLoopAsync(agentID, connectionCts.Token);
                    await ListenToServerAsync(connectionCts.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("Kết nối tới Server kết thúc: {Message}", ex.Message);
                }
                finally
                {
                    _isConnected = false;
                    connectionCts.Cancel();
                    CloseCurrentConnection();
                    if (heartbeatTask != null)
                    {
                        try { await heartbeatTask; }
                        catch (OperationCanceledException) { }
                    }
                }
            }

            await backupSchedulerTask;
        }

        // Hàm thử kết nối Socket
        private async Task<bool> TryConnectAsync(string ip, int port, int timeoutMs, string profileName, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                _logger?.LogWarning("Bỏ qua kết nối {Profile}: chưa cấu hình địa chỉ Server.", profileName);
                return false;
            }

            TcpClient? client = null;
            try
            {
                _logger?.LogInformation("Thử kết nối {Profile}: {Ip}:{Port}", profileName, ip, port);
                client = new TcpClient();
                client.NoDelay = true;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(timeoutMs);

                await client.ConnectAsync(ip, port, timeoutCts.Token);
                if (client.Connected)
                {
                    Stream secureStream = await SecureTransport.AuthenticateClientAsync(
                        client.GetStream(),
                        ip,
                        HardwareInfo.GetUniqueAgentID(),
                        _sharedKey,
                        timeoutCts.Token);
                    CloseCurrentConnection();
                    _client = client;
                    _stream = secureStream;
                    _activeControlHost = ip;
                    _activeControlPort = port;
                    _logger?.LogInformation("Kết nối {Profile} thành công: {Ip}:{Port}", profileName, ip, port);
                    return true;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                _logger?.LogWarning("Kết nối {Profile} timeout sau {TimeoutMs}ms: {Ip}:{Port}", profileName, timeoutMs, ip, port);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Kết nối {Profile} thất bại {Ip}:{Port} - {Message}", profileName, ip, port, ex.Message);
            }
            finally
            {
                if (!ReferenceEquals(client, _client))
                {
                    client?.Close();
                }
            }

            return false;
        }

        // Hàm gửi gói tin chuẩn mã hóa kích thước 4 bytes đầu chống dính gói
        private async Task SendPacketAsync(SocketPacket packet)
        {
            if (!_isConnected || _stream == null)
            {
                throw new IOException("Chưa kết nối tới AgentControl.");
            }

            await _sendLock.WaitAsync();
            try
            {
                if (!_isConnected || _stream == null)
                {
                    throw new IOException("Kết nối tới AgentControl đã đóng.");
                }

                await TransferFrameProtocol.WriteJsonPacketAsync(_stream, packet);

                // Gửi 4 bytes chiều dài trước, gửi dữ liệu chuỗi JSON theo sau
            }
            catch
            {
                CloseCurrentConnection();
                throw;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task SendRequiredPacketAsync(SocketPacket packet, CancellationToken token)
        {
            if (!_isConnected || _stream == null)
            {
                throw new IOException("Chưa kết nối tới AgentControl.");
            }

            await _sendLock.WaitAsync(token);
            try
            {
                if (!_isConnected || _stream == null)
                {
                    throw new IOException("Kết nối tới AgentControl đã đóng.");
                }

                await TransferFrameProtocol.WriteJsonPacketAsync(_stream, packet, token);
            }
            catch
            {
                CloseCurrentConnection();
                throw;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void CloseCurrentConnection()
        {
            _isConnected = false;
            Stream? stream = Interlocked.Exchange(ref _stream, null);
            TcpClient? client = Interlocked.Exchange(ref _client, null);
            try { stream?.Dispose(); } catch { }
            try { client?.Dispose(); } catch { }
        }

        private async Task RunBackgroundOperationAsync(
            Func<Task> operation,
            string operationName,
            CancellationToken token)
        {
            try
            {
                await operation();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Tác vụ nền {OperationName} kết thúc lỗi.", operationName);
            }
        }

        // BO SUNG MODULE BACKUP: bien the co CancellationToken va bao loi cho engine.
        private async Task SendBackupPacketAsync(SocketPacket packet, CancellationToken token)
        {
            if (!_isConnected || _stream == null)
            {
                throw new IOException("Chưa kết nối tới AgentControl.");
            }

            await _sendLock.WaitAsync(token);
            try
            {
                if (!_isConnected || _stream == null)
                {
                    throw new IOException("Mất kết nối tới AgentControl.");
                }

                await TransferFrameProtocol.WriteJsonPacketAsync(_stream, packet, token);
            }
            catch
            {
                CloseCurrentConnection();
                throw;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        // BO SUNG MODULE BACKUP: stream file qua marker 0x04, dung chung send lock.
        private async Task SendBackupChunkAsync(
            BackupFileChunkHeader header,
            byte[] buffer,
            int count,
            CancellationToken token)
        {
            if (!_isConnected || _stream == null)
            {
                throw new IOException("Chưa kết nối tới AgentControl.");
            }

            await _sendLock.WaitAsync(token);
            try
            {
                if (!_isConnected || _stream == null)
                {
                    throw new IOException("Mất kết nối tới AgentControl.");
                }

                await TransferFrameProtocol.WriteBinaryBackupChunkAsync(_stream, header, buffer, count, token);
            }
            catch
            {
                CloseCurrentConnection();
                throw;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task SendDownloadChunkAsync(FileChunkPacket chunk, byte[] buffer, int count, CancellationToken token)
        {
            if (!_isConnected || _stream == null)
            {
                throw new IOException("Chưa kết nối tới AgentControl.");
            }

            await _sendLock.WaitAsync(token);
            try
            {
                if (!_isConnected || _stream == null)
                {
                    throw new IOException("Kết nối tới AgentControl đã đóng.");
                }

                chunk.AgentID = HardwareInfo.GetUniqueAgentID();
                await TransferFrameProtocol.WriteBinaryDownloadChunkAsync(_stream, chunk, buffer, count, token);
            }
            catch
            {
                CloseCurrentConnection();
                throw;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task SendDownloadErrorAsync(
            string agentId,
            string downloadId,
            string remotePath,
            string message,
            long downloadedBytes = 0,
            long totalBytes = 0,
            bool resetRequired = false)
        {
            var errorPacket = new DownloadErrorPacket
            {
                DownloadID = downloadId,
                RemotePath = remotePath,
                ErrorMessage = message,
                DownloadedBytes = downloadedBytes,
                TotalBytes = totalBytes,
                ResetRequired = resetRequired
            };

            await SendPacketAsync(new SocketPacket
            {
                Type = "DOWNLOAD_ERROR",
                AgentID = agentId,
                Data = JsonSerializer.Serialize(errorPacket)
            });
        }

        // Tính năng số 1: Gửi thông tin máy cho Server
        private async Task SendUploadStatusAsync(
            string agentId,
            string uploadId,
            string remotePath,
            long uploadedBytes,
            long totalBytes,
            string status,
            string checksumAlgorithm,
            string errorMessage = "")
        {
            var statusPacket = new UploadStatusPacket
            {
                UploadID = uploadId,
                RemotePath = remotePath,
                UploadedBytes = Math.Max(0, uploadedBytes),
                TotalBytes = Math.Max(0, totalBytes),
                Status = status,
                ChecksumAlgorithm = NormalizeChecksumAlgorithm(checksumAlgorithm),
                ErrorMessage = errorMessage
            };

            await SendPacketAsync(new SocketPacket
            {
                Type = "UPLOAD_STATUS",
                AgentID = agentId,
                Data = JsonSerializer.Serialize(statusPacket)
            });
        }

        private async Task HandleBinaryUploadChunkAsync(Stream stream, int frameSize, CancellationToken token)
        {
            FileChunkPacket? chunk = null;
            int bodySize = 0;
            bool bodyCopyStarted = false;
            bool bodyCopyCompleted = false;
            bool transferFinished = false;

            try
            {
                var binaryFrame = await TransferFrameProtocol.ReadBinaryChunkHeaderAsync(stream, frameSize, token);
                chunk = binaryFrame.Header;
                bodySize = binaryFrame.BodySize;

                string agentId = HardwareInfo.GetUniqueAgentID();
                string uploadId = chunk.DownloadID;
                string targetPath = chunk.RemotePath;
                string checksumAlgorithm = NormalizeChecksumAlgorithm(chunk.ChecksumAlgorithm);

                if (string.IsNullOrWhiteSpace(uploadId) || string.IsNullOrWhiteSpace(targetPath))
                {
                    await TransferFrameProtocol.DrainExactAsync(stream, bodySize, token);
                    return;
                }

                if (!string.Equals(chunk.AgentID, agentId, StringComparison.OrdinalIgnoreCase) ||
                    chunk.TotalBytes < 0 || chunk.Offset < 0 ||
                    chunk.Offset > chunk.TotalBytes || bodySize != chunk.ChunkSize ||
                    bodySize > chunk.TotalBytes - chunk.Offset ||
                    (chunk.IsLastChunk && chunk.Offset + bodySize != chunk.TotalBytes) ||
                    (!chunk.IsLastChunk && chunk.Offset + bodySize >= chunk.TotalBytes))
                {
                    throw new InvalidDataException("Metadata upload chunk không hợp lệ.");
                }

                _uploadSleepBlocks.GetOrAdd(
                    uploadId,
                    _ => SystemSleepBlocker.PreventSystemSleep(
                        "AgentServices đang nhận file từ AgentControl."));

                transferFinished = chunk.IsLastChunk;

                string tempPath = targetPath + ".uploading";
                bodyCopyStarted = true;
                long currentUploaded = await ResumableTransferFile.WriteChunkAsync(
                    stream,
                    tempPath,
                    chunk.Offset,
                    chunk.TotalBytes,
                    bodySize,
                    chunk.IsLastChunk,
                    token);
                bodyCopyCompleted = true;
                if (!chunk.IsLastChunk)
                {
                    await SendUploadStatusAsync(agentId, uploadId, targetPath, currentUploaded, chunk.TotalBytes, "Uploading", checksumAlgorithm);
                    return;
                }

                if (IsChecksumEnabled(checksumAlgorithm))
                {
                    await SendUploadStatusAsync(agentId, uploadId, targetPath, currentUploaded, chunk.TotalBytes, "Verifying", checksumAlgorithm);

                    if (string.IsNullOrWhiteSpace(chunk.SourceChecksum) || !File.Exists(tempPath))
                    {
                        await SendUploadStatusAsync(agentId, uploadId, targetPath, currentUploaded, chunk.TotalBytes, "ChecksumFailed", checksumAlgorithm, "Khong co checksum de doi chieu.");
                        return;
                    }

                    string actualChecksum = await ComputeFileChecksumAsync(tempPath, checksumAlgorithm, token);
                    if (!string.Equals(actualChecksum, chunk.SourceChecksum, StringComparison.OrdinalIgnoreCase))
                    {
                        await SendUploadStatusAsync(agentId, uploadId, targetPath, currentUploaded, chunk.TotalBytes, "ChecksumFailed", checksumAlgorithm, "Checksum khong khop.");
                        return;
                    }
                }

                File.Move(tempPath, targetPath, true);
                string finalStatus = IsChecksumEnabled(checksumAlgorithm) ? "ChecksumMatched" : "Completed";
                await SendUploadStatusAsync(agentId, uploadId, targetPath, chunk.TotalBytes, chunk.TotalBytes, finalStatus, checksumAlgorithm);
            }
            catch (Exception ex)
            {
                if (chunk != null)
                {
                    transferFinished = true;
                    if (!bodyCopyStarted && bodySize > 0)
                    {
                        try
                        {
                            await TransferFrameProtocol.DrainExactAsync(stream, bodySize, token);
                        }
                        catch
                        {
                            throw;
                        }
                    }

                    string agentId = HardwareInfo.GetUniqueAgentID();
                    long uploadedBytes = Math.Max(0, chunk.Offset);
                    try
                    {
                        await SendUploadStatusAsync(agentId, chunk.DownloadID, chunk.RemotePath, uploadedBytes, chunk.TotalBytes, "Error", chunk.ChecksumAlgorithm, ex.Message);
                    }
                    catch
                    {
                        if (bodyCopyStarted && !bodyCopyCompleted)
                        {
                            throw;
                        }
                    }
                }

                if (bodyCopyStarted && !bodyCopyCompleted)
                {
                    throw;
                }
            }
            finally
            {
                if (transferFinished && chunk != null &&
                    _uploadSleepBlocks.TryRemove(chunk.DownloadID, out IDisposable? sleepBlock))
                {
                    sleepBlock.Dispose();
                }
            }

        }

        private void ReleaseUploadSleepBlocks()
        {
            foreach (string uploadId in _uploadSleepBlocks.Keys)
            {
                if (_uploadSleepBlocks.TryRemove(uploadId, out IDisposable? sleepBlock))
                {
                    sleepBlock.Dispose();
                }
            }
        }

        private static bool IsChecksumEnabled(string? algorithm)
        {
            return string.Equals(algorithm, "MD5", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(algorithm, "SHA256", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeChecksumAlgorithm(string? algorithm)
        {
            if (string.Equals(algorithm, "MD5", StringComparison.OrdinalIgnoreCase))
            {
                return "MD5";
            }

            if (string.Equals(algorithm, "SHA256", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase))
            {
                return "SHA256";
            }

            return "None";
        }

        private static HashAlgorithm CreateHashAlgorithm(string algorithm)
        {
            return string.Equals(algorithm, "MD5", StringComparison.OrdinalIgnoreCase)
                ? MD5.Create()
                : SHA256.Create();
        }

        private static async Task<string> ComputeFileChecksumAsync(string filePath, string algorithm, CancellationToken token)
        {
            algorithm = NormalizeChecksumAlgorithm(algorithm);
            if (!IsChecksumEnabled(algorithm))
            {
                return string.Empty;
            }

            const int checksumBufferSize = 1024 * 1024;
            await using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, checksumBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await ComputeStreamChecksumAsync(fs, algorithm, token);
        }

        private static async Task<string> ComputeStreamChecksumAsync(Stream stream, string algorithm, CancellationToken token)
        {
            using HashAlgorithm hash = CreateHashAlgorithm(algorithm);
            byte[] result = await hash.ComputeHashAsync(stream, token);
            return Convert.ToHexString(result);
        }

        private async Task SendRemoteFolderFilesAsync(
            string agentId,
            string requestData,
            CancellationToken token)
        {
            RemoteFolderFilesRequest? request = null;
            var errors = new List<string>();
            var response = new RemoteFolderFilesResponse { IsFinalPage = false };
            int pageNumber = 0;
            int sentErrorCount = 0;

            try
            {
                request = JsonSerializer.Deserialize<RemoteFolderFilesRequest>(requestData);
                if (request == null || string.IsNullOrWhiteSpace(request.RemoteRootPath))
                {
                    return;
                }

                response.RequestID = request.RequestID;
                response.RemoteRootPath = request.RemoteRootPath;
                if (!Directory.Exists(request.RemoteRootPath))
                {
                    errors.Add("Folder khong ton tai hoac khong truy cap duoc.");
                }
                else
                {
                    foreach (string filePath in EnumerateFilesSafe(request.RemoteRootPath, errors))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var fileInfo = new FileInfo(filePath);
                            response.Files.Add(new RemoteFolderFileEntry
                            {
                                RemotePath = fileInfo.FullName,
                                RelativePath = Path.GetRelativePath(request.RemoteRootPath, fileInfo.FullName),
                                Size = fileInfo.Length
                            });
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{filePath}: {ex.Message}");
                        }

                        if (response.Files.Count >= 1000)
                        {
                            response.PageNumber = pageNumber++;
                            response.Errors.AddRange(errors.Skip(sentErrorCount));
                            sentErrorCount = errors.Count;
                            await SendRemoteFolderFilesPageAsync(agentId, response, token);
                            response = new RemoteFolderFilesResponse
                            {
                                RequestID = request.RequestID,
                                RemoteRootPath = request.RemoteRootPath,
                                IsFinalPage = false
                            };
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                response.RequestID = request?.RequestID ?? string.Empty;
                response.RemoteRootPath = request?.RemoteRootPath ?? string.Empty;
                errors.Add(ex.Message);
            }

            response.PageNumber = pageNumber;
            response.IsFinalPage = true;
            response.Errors.AddRange(errors.Skip(sentErrorCount));
            await SendRemoteFolderFilesPageAsync(agentId, response, token);
        }

        private Task SendRemoteFolderFilesPageAsync(
            string agentId,
            RemoteFolderFilesResponse response,
            CancellationToken token)
        {
            return SendRequiredPacketAsync(new SocketPacket
            {
                Type = "GET_FOLDER_FILES_RESPONSE",
                AgentID = agentId,
                Data = JsonSerializer.Serialize(response)
            }, token);
        }

        private async Task SendRemoteFileActionResponseAsync(string agentId, RemoteFileActionResponse response)
        {
            await SendPacketAsync(new SocketPacket
            {
                Type = "REMOTE_FILE_ACTION_RESPONSE",
                AgentID = agentId,
                Data = JsonSerializer.Serialize(response)
            });
        }

        private async Task HandleRemoteDeleteAsync(string agentId, string requestData)
        {
            RemoteDeleteRequest? request = null;
            var response = new RemoteFileActionResponse();

            try
            {
                request = JsonSerializer.Deserialize<RemoteDeleteRequest>(requestData);
                response.RequestID = request?.RequestID ?? string.Empty;

                if (request == null || request.Items.Count == 0)
                {
                    response.Errors.Add("Khong co muc nao de xoa.");
                }
                else
                {
                    foreach (RemoteFileActionItem item in request.Items)
                    {
                        string path = item.FullPath;
                        try
                        {
                            if (item.IsFolder || Directory.Exists(path))
                            {
                                if (!Directory.Exists(path))
                                {
                                    response.Errors.Add($"{path}: Thu muc khong ton tai.");
                                    continue;
                                }

                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                                    path,
                                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            }
                            else
                            {
                                if (!File.Exists(path))
                                {
                                    response.Errors.Add($"{path}: File khong ton tai.");
                                    continue;
                                }

                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                                    path,
                                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            }

                            response.Paths.Add(path);
                        }
                        catch (Exception ex)
                        {
                            response.Errors.Add($"{path}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                response.RequestID = request?.RequestID ?? string.Empty;
                response.Errors.Add(ex.Message);
            }

            response.Success = response.Errors.Count == 0;
            response.Message = response.Success
                ? $"Da xoa {response.Paths.Count} muc tren Agent."
                : $"Da xoa {response.Paths.Count} muc, loi {response.Errors.Count} muc.";

            await SendRemoteFileActionResponseAsync(agentId, response);
        }

        private async Task HandleRemoteOpenAsync(string agentId, string requestData)
        {
            RemoteOpenRequest? request = null;
            var response = new RemoteFileActionResponse();

            try
            {
                request = JsonSerializer.Deserialize<RemoteOpenRequest>(requestData);
                response.RequestID = request?.RequestID ?? string.Empty;

                string path = request?.FullPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path))
                {
                    response.Errors.Add("Duong dan file rong.");
                }
                else if (!File.Exists(path) && !Directory.Exists(path))
                {
                    response.Errors.Add($"{path}: File/folder khong ton tai.");
                }
                else
                {
                    InteractiveSessionLauncher.OpenPath(path);

                    response.Paths.Add(path);
                }
            }
            catch (Exception ex)
            {
                response.RequestID = request?.RequestID ?? string.Empty;
                response.Errors.Add(ex.Message);
            }

            response.Success = response.Errors.Count == 0;
            response.Message = response.Success ? "Da gui lenh mo file/folder tren Agent." : "Khong the mo file/folder tren Agent.";
            await SendRemoteFileActionResponseAsync(agentId, response);
        }

        private async Task HandleRemoteCreateDirectoryAsync(string agentId, string requestData)
        {
            RemoteCreateDirectoryRequest? request = null;
            var response = new RemoteFileActionResponse();

            try
            {
                request = JsonSerializer.Deserialize<RemoteCreateDirectoryRequest>(requestData);
                response.RequestID = request?.RequestID ?? string.Empty;

                string path = request?.FullPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path))
                {
                    response.Errors.Add("Duong dan thu muc rong.");
                }
                else
                {
                    Directory.CreateDirectory(path);
                    response.Paths.Add(path);
                }
            }
            catch (Exception ex)
            {
                response.RequestID = request?.RequestID ?? string.Empty;
                response.Errors.Add(ex.Message);
            }

            response.Success = response.Errors.Count == 0;
            response.Message = response.Success ? "Da tao thu muc tren Agent." : "Khong the tao thu muc tren Agent.";
            await SendRemoteFileActionResponseAsync(agentId, response);
        }

        private IEnumerable<string> EnumerateFilesSafe(string rootPath, List<string> errors)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(rootPath);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                string[] files;
                string[] directories;
                try
                {
                    files = Directory.GetFiles(current);
                    directories = Directory.GetDirectories(current);
                }
                catch (Exception ex)
                {
                    errors.Add($"{current}: {ex.Message}");
                    continue;
                }

                foreach (string file in files)
                {
                    bool include = false;
                    try
                    {
                        include = (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{file}: {ex.Message}");
                    }
                    if (include)
                    {
                        yield return file;
                    }
                }

                foreach (string directory in directories)
                {
                    try
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(directory);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{directory}: {ex.Message}");
                    }
                }
            }
        }

        private async Task SendRegisterInfoAsync(CancellationToken token)
        {
            try
            {
                // 1. Tự lấy thông tin máy
                AgentInfo myInfo = CollectSystemInfo();

                // 2. Đóng gói payload chi tiết của Agent thành chuỗi JSON
                string jsonPayload = System.Text.Json.JsonSerializer.Serialize(myInfo);

                // 3. Khởi tạo gói tin KHỚP 100% cấu trúc class SocketPacket của fen
                SocketPacket registerPacket = new SocketPacket
                {
                    Type = "REGISTER",
                    AgentID = myInfo.AgentID, // Điền luôn mã định danh máy con vào vỏ gói tin
                    Data = jsonPayload        // Điền chuỗi JSON thông tin chi tiết vào thuộc tính Data kiểu string
                };

                await SendRequiredPacketAsync(registerPacket, token);
                _logger?.LogInformation("Đã gửi gói REGISTER đăng ký thông tin máy lên Server thành công.");
            }
            catch (Exception ex)
            {
                _logger?.LogError("Lỗi khi gửi gói REGISTER: {Message}", ex.Message);
                throw;
            }
        }

        // Tính năng số 7: Vòng lặp bắn Tim Mạch (Heartbeat) 30 giây một lần
        private async Task StartHeartbeatLoopAsync(string agentID, CancellationToken token)
        {
            while (_isConnected && !token.IsCancellationRequested)
            {
                await Task.Delay(30000, token); // Đợi đúng 30 giây
                if (!_isConnected) break;

                var heartbeatPacket = new SocketPacket
                {
                    Type = "HEARTBEAT",
                    AgentID = agentID,
                    Data = ""
                };

                await SendPacketAsync(heartbeatPacket);
                _logger?.LogInformation("-> Đã gửi Heartbeat (30s/lần)");
            }
        }

        private async Task ListenToServerAsync(CancellationToken token)
        {
            byte[] sizeBuffer = new byte[4];
            byte[] firstByteBuffer = new byte[1];
            string expectedAgentId = HardwareInfo.GetUniqueAgentID();
            try
            {
                while (_isConnected && !token.IsCancellationRequested && _stream != null)
                {
                    int bytesRead = await TransferFrameProtocol.ReadExactOrEndAsync(_stream, sizeBuffer, 0, 4, token);
                    if (bytesRead == 0) break; // Server chủ động ngắt kết nối

                    if (bytesRead != 4) throw new EndOfStreamException("Socket closed before packet size was fully received.");

                    int packetSize = BitConverter.ToInt32(sizeBuffer, 0);
                    TransferFrameProtocol.ValidateFrameSize(packetSize);

                    await TransferFrameProtocol.ReadExactAsync(_stream, firstByteBuffer, 0, 1, token);

                    if (firstByteBuffer[0] == TransferFrameProtocol.BinaryUploadChunkMarker)
                    {
                        await HandleBinaryUploadChunkAsync(_stream, packetSize, token);
                        continue;
                    }

                    SocketPacket? packet;
                    byte[] dataBuffer = ArrayPool<byte>.Shared.Rent(packetSize);
                    try
                    {
                        dataBuffer[0] = firstByteBuffer[0];
                        await TransferFrameProtocol.ReadExactAsync(
                            _stream,
                            dataBuffer,
                            1,
                            packetSize - 1,
                            token);
                        packet = JsonSerializer.Deserialize<SocketPacket>(
                            new ReadOnlySpan<byte>(dataBuffer, 0, packetSize));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(dataBuffer);
                    }

                    if (packet != null)
                    {
                        if (!string.Equals(packet.AgentID, expectedAgentId, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new AuthenticationException(
                                "AgentID trong lệnh Control không khớp Agent đang kết nối.");
                        }

                        // BO SUNG MODULE BACKUP: config va ACK cua phien backup.
                        if (packet.Type == BackupPacketTypes.ConfigDeploy)
                        {
                            BackupConfigAck ack = await _backupManager.ApplyConfigurationAsync(packet.Data, token);
                            await SendPacketAsync(new SocketPacket
                            {
                                Type = BackupPacketTypes.ConfigAck,
                                AgentID = packet.AgentID,
                                Data = JsonSerializer.Serialize(ack)
                            });
                            continue;
                        }

                        if (packet.Type == BackupPacketTypes.SessionReady ||
                            packet.Type == BackupPacketTypes.SessionResult ||
                            packet.Type == BackupPacketTypes.FirstFileResumeInfo)
                        {
                            _backupManager.HandleSessionSignal(packet.Type, packet.Data);
                            continue;
                        }

                        if (AgentUpdateClient.IsUpdatePacket(packet.Type))
                        {
                            await _updateClient.HandlePacketAsync(packet, token);
                            continue;
                        }

                        _logger?.LogInformation("Nhận lệnh từ Server: {Type}", packet.Type);

                        // ====================================================================
                        // 1. LUỒNG CÀO Ổ ĐĨA: GỘP CHUNG VỀ CHUẨN "BROWSE_DRIVES" CHO ĐỒNG BỘ UI
                        // ====================================================================


                        // --- TÍCH HỢP VÀO VÒNG LẶP ĐỌC SOCKET CỦA AGENT ---

                        if (packet.Type == "REQUEST_DOWNLOAD")
                        {
                            if (!string.IsNullOrEmpty(packet.Data))
                            {
                                // Lay slot truoc khi tao task de thu muc hang tram nghin file khong
                                // tao hang tram nghin Task dang cho trong RAM. TCP se tu back-pressure.
                                await _downloadLock.WaitAsync(token);
                                _ = RunBackgroundOperationAsync(async () =>
                                {
                                    using IDisposable sleepBlock = SystemSleepBlocker.PreventSystemSleep(
                                        "AgentServices đang gửi file về AgentControl.");
                                    DownloadRequestModel? request = null;
                                    long currentOffset = 0;
                                    long totalBytes = 0;
                                    try
                                    {
                                        // 1. Giải mã gói yêu cầu từ Server
                                        request = JsonSerializer.Deserialize<DownloadRequestModel>(packet.Data);
                                        if (request == null ||
                                            string.IsNullOrWhiteSpace(request.DownloadID) ||
                                            string.IsNullOrWhiteSpace(request.RemotePath))
                                        {
                                            throw new InvalidDataException("Yêu cầu download không hợp lệ.");
                                        }

                                        string filePath = request.RemotePath;
                                        currentOffset = request.Offset;

                                        // 2. Kiểm tra xem file ngoài đời thực có tồn tại không [cite: 12]
                                        if (!File.Exists(filePath))
                                        {
                                            await SendDownloadErrorAsync(packet.AgentID, request.DownloadID, filePath, "File không còn tồn tại hoặc đã bị antivirus xóa.", currentOffset, totalBytes);
                                            return;
                                        }

                                        const int bufferSize = 1024 * 1024;
                                        using FileStream fs = new FileStream(
                                            filePath,
                                            FileMode.Open,
                                            FileAccess.Read,
                                            FileShare.Read,
                                            bufferSize,
                                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                                        totalBytes = fs.Length;
                                        if (currentOffset < 0 || currentOffset > totalBytes)
                                        {
                                            await SendDownloadErrorAsync(
                                                packet.AgentID,
                                                request.DownloadID,
                                                filePath,
                                                "Mốc resume không còn phù hợp với file nguồn; cần tải lại từ đầu.",
                                                0,
                                                totalBytes,
                                                resetRequired: true);
                                            return;
                                        }

                                        string checksumAlgorithm = NormalizeChecksumAlgorithm(request.ChecksumAlgorithm);
                                        string sourceChecksum = IsChecksumEnabled(checksumAlgorithm)
                                            ? await ComputeStreamChecksumAsync(fs, checksumAlgorithm, token)
                                            : string.Empty;
                                        fs.Seek(0, SeekOrigin.Begin);

                                        if (totalBytes == 0)
                                        {
                                            var emptyChunk = new FileChunkPacket
                                            {
                                                DownloadID = request.DownloadID,
                                                RemotePath = filePath,
                                                TotalBytes = 0,
                                                Offset = 0,
                                                ChunkSize = 0,
                                                IsLastChunk = true,
                                                Base64Data = string.Empty,
                                                ChecksumAlgorithm = checksumAlgorithm,
                                                SourceChecksum = sourceChecksum
                                            };

                                            await SendDownloadChunkAsync(emptyChunk, Array.Empty<byte>(), 0, token);
                                            return;
                                        }

                                        // Định nghĩa kích thước mỗi khối băm nhỏ (Buffer Size = 256KB tăng tốc độ truyền tải)
                                        if (currentOffset == totalBytes)
                                        {
                                            var completedChunk = new FileChunkPacket
                                            {
                                                DownloadID = request.DownloadID,
                                                RemotePath = filePath,
                                                TotalBytes = totalBytes,
                                                Offset = totalBytes,
                                                ChunkSize = 0,
                                                IsLastChunk = true,
                                                Base64Data = string.Empty,
                                                ChecksumAlgorithm = checksumAlgorithm,
                                                SourceChecksum = sourceChecksum
                                            };

                                            await SendDownloadChunkAsync(completedChunk, Array.Empty<byte>(), 0, token);
                                            return;
                                        }

                                        byte[] buffer = new byte[bufferSize];

                                        // File duoc giu khoa ghi trong suot luc bam va truyen de checksum cung mot phien ban.
                                        fs.Seek(currentOffset, SeekOrigin.Begin);

                                        int bytesRead;
                                        while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                                        {
                                                // Trích xuất mảng byte thực tế đọc được từ file
                                                // Đóng gói dữ liệu sang mô hình FileChunkPacket
                                                var chunk = new FileChunkPacket
                                                {
                                                    DownloadID = request.DownloadID,
                                                    RemotePath = filePath,
                                                    TotalBytes = totalBytes,
                                                    Offset = currentOffset,
                                                    ChunkSize = bytesRead,
                                                    IsLastChunk = (currentOffset + bytesRead >= totalBytes), // Đánh dấu nếu là mảnh cuối [cite: 15]
                                                    Base64Data = string.Empty,
                                                    ChecksumAlgorithm = checksumAlgorithm
                                                };

                                                // Gói vào SocketPacket chung để bắn lên mạng
                                                if (chunk.IsLastChunk)
                                                {
                                                    chunk.SourceChecksum = sourceChecksum;
                                                }

                                                await SendDownloadChunkAsync(chunk, buffer, bytesRead, token);

                                                // Cập nhật lại vị trí dịch chuyển tiếp theo [cite: 6]
                                                currentOffset += bytesRead;

                                                // Mẹo nhỏ: Thêm một khoảng Delay cực ngắn (1-2ms) nếu muốn giảm tải CPU cho máy Agent khi tải file quá lớn
                                                // await Task.Delay(1);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Lỗi băm file phía Agent: {ex.Message}");
                                        if (request != null)
                                        {
                                            await SendDownloadErrorAsync(packet.AgentID, request.DownloadID, request.RemotePath, ex.Message, currentOffset, totalBytes);
                                        }
                                    }
                                    finally
                                    {
                                        _downloadLock.Release();
                                    }
                                }, "gửi file download về Control", token);
                            }
                        }

                        if (packet.Type == "GET_FOLDER_FILES")
                        {
                            string requestData = packet.Data;
                            _ = RunBackgroundOperationAsync(
                                () => SendRemoteFolderFilesAsync(packet.AgentID, requestData, token),
                                "liệt kê file thư mục từ xa",
                                token);
                        }

                        if (packet.Type == "DELETE_REMOTE_ITEMS")
                        {
                            string requestData = packet.Data;
                            _ = RunBackgroundOperationAsync(
                                () => HandleRemoteDeleteAsync(packet.AgentID, requestData),
                                "xóa file/thư mục từ xa",
                                token);
                        }

                        if (packet.Type == "OPEN_REMOTE_FILE")
                        {
                            string requestData = packet.Data;
                            _ = RunBackgroundOperationAsync(
                                () => HandleRemoteOpenAsync(packet.AgentID, requestData),
                                "mở file từ xa",
                                token);
                        }

                        if (packet.Type == "CREATE_REMOTE_DIRECTORY")
                        {
                            string requestData = packet.Data;
                            _ = RunBackgroundOperationAsync(
                                () => HandleRemoteCreateDirectoryAsync(packet.AgentID, requestData),
                                "tạo thư mục từ xa",
                                token);
                        }

                        if (packet.Type == "BROWSE_DRIVES" || packet.Type == "GET_DRIVES")
                        {
                            _logger?.LogInformation("Thực hiện cào danh sách ổ đĩa hệ thống...");

                            var driveList = new System.Collections.Generic.List<string>();
                            foreach (var drive in System.IO.DriveInfo.GetDrives())
                            {
                                if (drive.IsReady)
                                {
                                    driveList.Add(drive.Name); // Trả về dạng "C:\", "D:\"
                                }
                            }

                            SocketPacket response = new SocketPacket
                            {
                                Type = "BROWSE_DRIVES_RESPONSE", // Thống nhất dùng chung một loại Response này
                                AgentID = packet.AgentID,
                                Data = System.Text.Json.JsonSerializer.Serialize(driveList)
                            };

                            await SendPacketAsync(response); // Dùng luôn hàm SendPacketAsync cho sạch code
                        }

                        // ====================================================================
                        // 2. LUỒNG CÀO THƯ MỤC LAZY LOADING: TỐI ƯU TỐC ĐỘ, ĐỒNG BỘ KIỂU LỆNH
                        // ====================================================================
                        else if (packet.Type == "GET_DIRECTORY" || packet.Type == "BROWSE_FOLDER")
                        {
                            string targetPath = packet.Data; // Đường dẫn cần cào (Ví dụ: C:\ hoặc C:\Users)
                            _logger?.LogInformation("Tiến hành cào nhanh thư mục: {Path}", targetPath);

                            var dirContent = new RemoteDirectoryContent
                            {
                                CurrentPath = targetPath
                            };

                            try
                            {
                                if (System.IO.Directory.Exists(targetPath))
                                {
                                    // TỐI ƯU HIỆU SUẤT X5: Dùng Enumerate Thay vì Get để lấy đến đâu trả đến đó, không ngâm RAM

                                    // Cào thư mục con nhanh
                                    var dirs = System.IO.Directory.EnumerateDirectories(targetPath);
                                    foreach (var dir in dirs)
                                    {
                                        try
                                        {
                                            var info = new System.IO.DirectoryInfo(dir);
                                            // Bỏ qua thư mục ẩn/hệ thống để tránh bị từ chối quyền (Access Denied) làm chậm luồng
                                            if ((info.Attributes & System.IO.FileAttributes.Hidden) == 0 &&
                                                (info.Attributes & System.IO.FileAttributes.System) == 0)
                                            {
                                                dirContent.SubFolders.Add(info.FullName);
                                                dirContent.Folders.Add(new RemoteFileSystemEntry
                                                {
                                                    FullPath = info.FullName,
                                                    Name = info.Name,
                                                    IsFolder = true,
                                                    Size = 0,
                                                    LastWriteTime = info.LastWriteTime,
                                                    Extension = string.Empty
                                                });
                                            }
                                        }
                                        catch { /* Bỏ qua các folder lỗi quyền riêng lẻ để tiếp tục vòng lặp */ }
                                    }

                                    // Cào tập tin con nhanh
                                    var files = System.IO.Directory.EnumerateFiles(targetPath);
                                    foreach (var file in files)
                                    {
                                        try
                                        {
                                            var info = new System.IO.FileInfo(file);
                                            if ((info.Attributes & System.IO.FileAttributes.Hidden) == 0)
                                            {
                                                dirContent.Files.Add(info.FullName);
                                                dirContent.FileEntries.Add(new RemoteFileSystemEntry
                                                {
                                                    FullPath = info.FullName,
                                                    Name = info.Name,
                                                    IsFolder = false,
                                                    Size = info.Length,
                                                    LastWriteTime = info.LastWriteTime,
                                                    Extension = info.Extension
                                                });
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                dirContent.ErrorMessage = ex.Message; // Trả lỗi phân quyền tổng quát nếu có
                            }

                            SocketPacket response = new SocketPacket
                            {
                                Type = "GET_DIRECTORY_RESPONSE",
                                AgentID = packet.AgentID,
                                Data = System.Text.Json.JsonSerializer.Serialize(dirContent)
                            };

                            await SendPacketAsync(response);
                            _logger?.LogInformation("Đã phản hồi dữ liệu cấu trúc thư mục về Server.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("Lỗi trong luồng lắng nghe lệnh: {Message}", ex.Message);
            }
            finally
            {
                _isConnected = false;
                ReleaseUploadSleepBlocks();
                _logger?.LogWarning("Mất kết nối tới Server. Đã kích hoạt trạng thái chờ Reconnect...");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _isConnected = false;
            ReleaseUploadSleepBlocks();

            CloseCurrentConnection();

            await base.StopAsync(cancellationToken);
        }
    }
}
