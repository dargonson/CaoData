using AgentShared;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgentService
{
    internal sealed class AgentUpdateClient
    {
        private const int BufferSize = 512 * 1024;
        private readonly Func<SocketPacket, Task> _sendPacketAsync;
        private readonly Func<(string Host, int Port)> _getControlEndpoint;
        private readonly ILogger _logger;
        private readonly Dictionary<string, UpdateSession> _sessions = new Dictionary<string, UpdateSession>(StringComparer.OrdinalIgnoreCase);

        public AgentUpdateClient(Func<SocketPacket, Task> sendPacketAsync, Func<(string Host, int Port)> getControlEndpoint, ILogger logger)
        {
            _sendPacketAsync = sendPacketAsync;
            _getControlEndpoint = getControlEndpoint;
            _logger = logger;
        }

        public static bool IsUpdatePacket(string? packetType)
        {
            return string.Equals(packetType, AgentUpdatePacketTypes.UpdateAgent, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(packetType, AgentUpdatePacketTypes.UpdateAgentFileBegin, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(packetType, AgentUpdatePacketTypes.UpdateAgentFileChunk, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(packetType, AgentUpdatePacketTypes.UpdateAgentFileEnd, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(packetType, AgentUpdatePacketTypes.UpdateAgentApply, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetCompletionMarkerPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AgentServices",
                "Updates",
                "pending-update-complete.json");
        }

        public async Task SendPendingCompletionStatusAsync(string agentId, CancellationToken cancellationToken)
        {
            string markerPath = GetCompletionMarkerPath();
            if (!File.Exists(markerPath))
            {
                return;
            }

            try
            {
                string json = await File.ReadAllTextAsync(markerPath, cancellationToken);
                var marker = JsonSerializer.Deserialize<AgentUpdateCompletionMarker>(json);
                if (marker != null && !string.IsNullOrWhiteSpace(marker.SessionId))
                {
                    await SendStatusAsync(agentId, marker.SessionId, "Completed", "Lệnh cuối: AgentServices phiên bản mới đã kết nối lại Control.");
                }

                File.Delete(markerPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Khong the gui trang thai hoan tat update.");
            }
        }

        public async Task HandlePacketAsync(SocketPacket packet, CancellationToken cancellationToken)
        {
            try
            {
                if (packet.Type == AgentUpdatePacketTypes.UpdateAgent)
                {
                    await BeginUpdateAsync(packet, cancellationToken);
                }
                else if (packet.Type == AgentUpdatePacketTypes.UpdateAgentFileBegin)
                {
                    await BeginFileAsync(packet, cancellationToken);
                }
                else if (packet.Type == AgentUpdatePacketTypes.UpdateAgentFileChunk)
                {
                    await WriteFileChunkAsync(packet, cancellationToken);
                }
                else if (packet.Type == AgentUpdatePacketTypes.UpdateAgentFileEnd)
                {
                    await EndFileAsync(packet, cancellationToken);
                }
                else if (packet.Type == AgentUpdatePacketTypes.UpdateAgentApply)
                {
                    await ApplyUpdateAsync(packet, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi update Agent.");
                await SendStatusAsync(packet.AgentID, GetSessionId(packet), "Error", ex.Message);
            }
        }

        private async Task BeginUpdateAsync(SocketPacket packet, CancellationToken cancellationToken)
        {
            var request = JsonSerializer.Deserialize<AgentUpdateRequest>(packet.Data);
            if (request == null || string.IsNullOrWhiteSpace(request.SessionId))
            {
                throw new InvalidDataException("Gói update không hợp lệ.");
            }

            string sessionRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AgentServices",
                "Updates",
                request.SessionId);

            Directory.CreateDirectory(sessionRoot);

            var session = new UpdateSession(request.SessionId, sessionRoot, request);
            _sessions[request.SessionId] = session;

            string manifestPath = Path.Combine(sessionRoot, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

            await SendStatusAsync(packet.AgentID, request.SessionId, "Start", "Bắt đầu cập nhật.");
            await SendStatusAsync(packet.AgentID, request.SessionId, "Checking", "Đang kiểm tra file update trên server....");
            await SendStatusAsync(packet.AgentID, request.SessionId, "Ready", "Xác nhận có file update.");
        }

        private async Task BeginFileAsync(SocketPacket packet, CancellationToken cancellationToken)
        {
            var begin = JsonSerializer.Deserialize<AgentUpdateFileBegin>(packet.Data);
            if (begin == null)
            {
                throw new InvalidDataException("Gói bắt đầu file update không hợp lệ.");
            }

            UpdateSession session = GetSession(begin.SessionId);
            string filePath = GetSessionFilePath(session, begin.FileName);

            session.Files[begin.Role] = new UpdateFileState(begin.Role, filePath, begin.TotalBytes, begin.Sha256);

            using (new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
            }

            string message = string.Equals(begin.Role, AgentUpdateFileRoles.Service, StringComparison.OrdinalIgnoreCase)
                ? "Đang tải file về."
                : "Đang tải AgentUpdater về.";
            await SendStatusAsync(packet.AgentID, begin.SessionId, "Downloading", message);
        }

        private async Task WriteFileChunkAsync(SocketPacket packet, CancellationToken cancellationToken)
        {
            var chunk = JsonSerializer.Deserialize<AgentUpdateFileChunk>(packet.Data);
            if (chunk == null)
            {
                throw new InvalidDataException("Gói dữ liệu update không hợp lệ.");
            }

            UpdateSession session = GetSession(chunk.SessionId);
            if (!session.Files.TryGetValue(chunk.Role, out UpdateFileState? fileState))
            {
                throw new InvalidDataException("Chưa nhận thông tin file update.");
            }

            byte[] bytes = Convert.FromBase64String(chunk.Base64Data);
            using var fs = new FileStream(fileState.FilePath, FileMode.Open, FileAccess.Write, FileShare.Read, BufferSize, FileOptions.Asynchronous);
            fs.Seek(chunk.Offset, SeekOrigin.Begin);
            await fs.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            fileState.ReceivedBytes = Math.Max(fileState.ReceivedBytes, chunk.Offset + bytes.Length);
        }

        private async Task EndFileAsync(SocketPacket packet, CancellationToken cancellationToken)
        {
            var end = JsonSerializer.Deserialize<AgentUpdateFileEnd>(packet.Data);
            if (end == null)
            {
                throw new InvalidDataException("Gói kết thúc file update không hợp lệ.");
            }

            UpdateSession session = GetSession(end.SessionId);
            if (!session.Files.TryGetValue(end.Role, out UpdateFileState? fileState))
            {
                throw new InvalidDataException("Không tìm thấy file update để kiểm tra.");
            }

            if (fileState.ReceivedBytes != fileState.TotalBytes)
            {
                throw new InvalidDataException($"File update chưa nhận đủ dữ liệu: {Path.GetFileName(fileState.FilePath)}.");
            }

            string actualSha256 = await ComputeSha256Async(fileState.FilePath, cancellationToken);
            if (!string.Equals(actualSha256, fileState.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SHA-256 không khớp: {Path.GetFileName(fileState.FilePath)}.");
            }

            fileState.IsVerified = true;
            if (string.Equals(end.Role, AgentUpdateFileRoles.Service, StringComparison.OrdinalIgnoreCase))
            {
                await SendStatusAsync(packet.AgentID, end.SessionId, "Downloaded", "Đã tải xong.");
            }
            else
            {
                await SendStatusAsync(packet.AgentID, end.SessionId, "Downloaded", "Đã tải xong AgentUpdater.");
            }
        }

        private async Task ApplyUpdateAsync(SocketPacket packet, CancellationToken cancellationToken)
        {
            var apply = JsonSerializer.Deserialize<AgentUpdateApply>(packet.Data);
            if (apply == null)
            {
                throw new InvalidDataException("Gói apply update không hợp lệ.");
            }

            UpdateSession session = GetSession(apply.SessionId);
            UpdateFileState serviceFile = GetVerifiedFile(session, AgentUpdateFileRoles.Service);
            UpdateFileState updaterFile = GetVerifiedFile(session, AgentUpdateFileRoles.Updater);
            await SendStatusAsync(packet.AgentID, apply.SessionId, "Preparing", "Chuẩn bị update...");

            string currentExe = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            {
                currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            {
                throw new FileNotFoundException("Không xác định được đường dẫn AgentServices.exe hiện tại.");
            }

            string backupDirectory = Path.Combine(session.RootDirectory, "backup");
            Directory.CreateDirectory(backupDirectory);
            (string controlHost, int controlPort) = _getControlEndpoint();

            string arguments =
                $"--service-name \"{apply.ServiceName}\" " +
                $"--current-exe \"{currentExe}\" " +
                $"--new-exe \"{serviceFile.FilePath}\" " +
                $"--backup-dir \"{backupDirectory}\" " +
                $"--expected-sha256 \"{serviceFile.Sha256}\" " +
                $"--target-version \"{apply.TargetVersion}\" " +
                $"--agent-id \"{packet.AgentID}\" " +
                $"--session-id \"{apply.SessionId}\" " +
                $"--control-host \"{controlHost}\" " +
                $"--control-port \"{controlPort}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = updaterFile.FilePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = session.RootDirectory
            };

            await SendStatusAsync(packet.AgentID, apply.SessionId, "OpeningUpdater", "Đang mở AgentUpdater.");
            Process? updaterProcess = Process.Start(startInfo);
            if (updaterProcess == null)
            {
                throw new InvalidOperationException("Mở update thất bại, vui lòng thử lại sau.");
            }

            await SendStatusAsync(packet.AgentID, apply.SessionId, "UpdaterOpened", "Mở thành công. Tới đây là hết nhiệm vụ của AgentServices.");
        }

        private UpdateSession GetSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out UpdateSession? session))
            {
                throw new InvalidDataException("Không tìm thấy phiên update.");
            }

            return session;
        }

        private static UpdateFileState GetVerifiedFile(UpdateSession session, string role)
        {
            if (!session.Files.TryGetValue(role, out UpdateFileState? fileState) || !fileState.IsVerified)
            {
                throw new InvalidDataException($"File update chưa sẵn sàng: {role}.");
            }

            return fileState;
        }

        private static string GetSessionFilePath(UpdateSession session, string fileName)
        {
            string safeName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                throw new InvalidDataException("Tên file update không hợp lệ.");
            }

            return Path.Combine(session.RootDirectory, safeName);
        }

        public async Task SendStatusAsync(string agentId, string sessionId, string status, string message)
        {
            var updateStatus = new AgentUpdateStatus
            {
                SessionId = sessionId,
                Status = status,
                Message = message,
                Version = AppVersion.CurrentVersionAgent,
                Source = "AgentServices",
                CreatedAt = DateTime.Now.ToString("HH:mm:ss")
            };

            await _sendPacketAsync(new SocketPacket
            {
                Type = AgentUpdatePacketTypes.UpdateAgentStatus,
                AgentID = agentId,
                Data = JsonSerializer.Serialize(updateStatus)
            });
        }

        private static string GetSessionId(SocketPacket packet)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(packet.Data);
                if (document.RootElement.TryGetProperty("SessionId", out JsonElement sessionId))
                {
                    return sessionId.GetString() ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await sha.ComputeHashAsync(fs, cancellationToken);
            return Convert.ToHexString(hash);
        }

        private sealed class UpdateSession
        {
            public UpdateSession(string sessionId, string rootDirectory, AgentUpdateRequest request)
            {
                SessionId = sessionId;
                RootDirectory = rootDirectory;
                Request = request;
            }

            public string SessionId { get; }
            public string RootDirectory { get; }
            public AgentUpdateRequest Request { get; }
            public Dictionary<string, UpdateFileState> Files { get; } = new Dictionary<string, UpdateFileState>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class UpdateFileState
        {
            public UpdateFileState(string role, string filePath, long totalBytes, string sha256)
            {
                Role = role;
                FilePath = filePath;
                TotalBytes = totalBytes;
                Sha256 = sha256;
            }

            public string Role { get; }
            public string FilePath { get; }
            public long TotalBytes { get; }
            public string Sha256 { get; }
            public long ReceivedBytes { get; set; }
            public bool IsVerified { get; set; }
        }
    }
}
