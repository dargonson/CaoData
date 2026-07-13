using AgentShared;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgentControl
{
    internal sealed class AgentUpdateServer
    {
        private const int ChunkSize = 512 * 1024;
        private readonly string _packageDirectory;

        public AgentUpdateServer()
        {
            _packageDirectory = Path.Combine(AppContext.BaseDirectory, "Updates", "AgentServices");
        }

        public string PackageDirectory => _packageDirectory;

        public async Task SendUpdateAsync(
            string agentId,
            string currentVersion,
            string sessionId,
            Func<SocketPacket, Task> sendPacketAsync,
            Action<AgentUpdateStatus>? localStatusHandler = null,
            CancellationToken cancellationToken = default)
        {
            SendLocalStatus(sessionId, "Checking", "Đang kiểm tra file update trên server....", localStatusHandler);
            AgentUpdatePackage package = await LoadPackageAsync(cancellationToken);
            SendLocalStatus(sessionId, "Ready", "Xác nhận có file update.", localStatusHandler);

            var request = new AgentUpdateRequest
            {
                SessionId = sessionId,
                CurrentVersion = currentVersion,
                TargetVersion = AppVersion.CurrentVersionControl,
                ServiceFileName = Path.GetFileName(package.ServicePath),
                ServiceFileSize = package.ServiceSize,
                ServiceSha256 = package.ServiceSha256,
                UpdaterFileName = Path.GetFileName(package.UpdaterPath),
                UpdaterFileSize = package.UpdaterSize,
                UpdaterSha256 = package.UpdaterSha256,
                RequestedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            await sendPacketAsync(CreatePacket(agentId, AgentUpdatePacketTypes.UpdateAgent, request));
            SendLocalStatus(sessionId, "Sending", "Đang gửi gói update tới Agent.", localStatusHandler);
            await SendFileAsync(agentId, sessionId, AgentUpdateFileRoles.Updater, package.UpdaterPath, package.UpdaterSha256, package.UpdaterSize, sendPacketAsync, cancellationToken);
            await SendFileAsync(agentId, sessionId, AgentUpdateFileRoles.Service, package.ServicePath, package.ServiceSha256, package.ServiceSize, sendPacketAsync, cancellationToken);

            var apply = new AgentUpdateApply
            {
                SessionId = sessionId,
                TargetVersion = AppVersion.CurrentVersionControl,
                ServiceName = "AgentServices"
            };

            await sendPacketAsync(CreatePacket(agentId, AgentUpdatePacketTypes.UpdateAgentApply, apply));
        }

        private static void SendLocalStatus(string sessionId, string status, string message, Action<AgentUpdateStatus>? localStatusHandler)
        {
            var updateStatus = new AgentUpdateStatus
            {
                SessionId = sessionId,
                Status = status,
                Message = message,
                Version = AppVersion.CurrentVersionControl,
                Source = "Control",
                CreatedAt = DateTime.Now.ToString("HH:mm:ss")
            };

            localStatusHandler?.Invoke(updateStatus);
        }

        private async Task<AgentUpdatePackage> LoadPackageAsync(CancellationToken cancellationToken)
        {
            string servicePath = Path.Combine(_packageDirectory, "AgentServices.exe");
            string updaterPath = Path.Combine(_packageDirectory, "AgentUpdater.exe");

            if (!File.Exists(servicePath) || !File.Exists(updaterPath))
            {
                throw new FileNotFoundException(
                    "Chưa tìm thấy gói update. Cần đặt AgentServices.exe và AgentUpdater.exe trong thư mục: " + _packageDirectory);
            }

            var serviceInfo = new FileInfo(servicePath);
            var updaterInfo = new FileInfo(updaterPath);

            return new AgentUpdatePackage(
                servicePath,
                serviceInfo.Length,
                await ComputeSha256Async(servicePath, cancellationToken),
                updaterPath,
                updaterInfo.Length,
                await ComputeSha256Async(updaterPath, cancellationToken));
        }

        private static async Task SendFileAsync(
            string agentId,
            string sessionId,
            string role,
            string filePath,
            string sha256,
            long totalBytes,
            Func<SocketPacket, Task> sendPacketAsync,
            CancellationToken cancellationToken)
        {
            var begin = new AgentUpdateFileBegin
            {
                SessionId = sessionId,
                Role = role,
                FileName = Path.GetFileName(filePath),
                TotalBytes = totalBytes,
                Sha256 = sha256
            };

            await sendPacketAsync(CreatePacket(agentId, AgentUpdatePacketTypes.UpdateAgentFileBegin, begin));

            byte[] buffer = new byte[ChunkSize];
            long offset = 0;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                int read;
                while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    var chunk = new AgentUpdateFileChunk
                    {
                        SessionId = sessionId,
                        Role = role,
                        Offset = offset,
                        Base64Data = Convert.ToBase64String(buffer, 0, read)
                    };

                    await sendPacketAsync(CreatePacket(agentId, AgentUpdatePacketTypes.UpdateAgentFileChunk, chunk));
                    offset += read;
                }
            }

            var end = new AgentUpdateFileEnd
            {
                SessionId = sessionId,
                Role = role
            };

            await sendPacketAsync(CreatePacket(agentId, AgentUpdatePacketTypes.UpdateAgentFileEnd, end));
        }

        private static SocketPacket CreatePacket<T>(string agentId, string type, T data)
        {
            return new SocketPacket
            {
                Type = type,
                AgentID = agentId,
                Data = JsonSerializer.Serialize(data)
            };
        }

        private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await sha.ComputeHashAsync(fs, cancellationToken);
            return Convert.ToHexString(hash);
        }

        private sealed record AgentUpdatePackage(
            string ServicePath,
            long ServiceSize,
            string ServiceSha256,
            string UpdaterPath,
            long UpdaterSize,
            string UpdaterSha256);
    }
}
