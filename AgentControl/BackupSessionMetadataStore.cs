using System.Security.Cryptography;
using System.Text.Json;

namespace AgentControl
{
    /// <summary>
    /// BO SUNG MODULE BACKUP: sidecar nho cho moi phien, giup Recovery sap xep va
    /// kiem tra manifest lon ma khong phai nap manifest vao RAM.
    /// </summary>
    internal static class BackupSessionMetadataStore
    {
        internal const string FileName = "session.json";

        internal static async Task WriteAsync(
            string sessionRoot,
            string manifestPath,
            string agentId,
            string sessionName,
            string backupType,
            DateTime startedAtUtc,
            DateTime completedAtUtc,
            CancellationToken token = default)
        {
            string hash = await ComputeSha256Async(manifestPath, token);
            FileInfo manifest = new FileInfo(manifestPath);
            var metadata = new BackupSessionMetadata
            {
                FormatVersion = 1,
                AgentID = agentId,
                SessionName = sessionName,
                BackupType = backupType,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                ManifestLength = manifest.Length,
                ManifestLastWriteTimeUtcTicks = manifest.LastWriteTimeUtc.Ticks,
                ManifestSha256 = hash
            };

            string finalPath = Path.Combine(sessionRoot, FileName);
            string temporaryPath = finalPath + ".tmp";
            await using (FileStream destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                    destination,
                    metadata,
                    new JsonSerializerOptions { WriteIndented = true },
                    token);
                await destination.FlushAsync(token);
                destination.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, finalPath, overwrite: true);
        }

        internal static BackupSessionMetadata ReadVerified(
            string sessionRoot,
            string manifestPath,
            CancellationToken token = default)
        {
            string sidecarPath = Path.Combine(sessionRoot, FileName);
            BackupSessionMetadata metadata;
            if (File.Exists(sidecarPath))
            {
                metadata = JsonSerializer.Deserialize<BackupSessionMetadata>(File.ReadAllText(sidecarPath))
                    ?? throw new InvalidDataException("session.json không hợp lệ.");
            }
            else
            {
                BackupManifestMetadata legacy = BackupManifestStreamReader.ReadMetadata(manifestPath, token);
                FileInfo manifestInfo = new FileInfo(manifestPath);
                metadata = new BackupSessionMetadata
                {
                    FormatVersion = 0,
                    AgentID = legacy.AgentID,
                    SessionName = legacy.SessionName,
                    BackupType = legacy.BackupType,
                    StartedAtUtc = legacy.StartedAtUtc,
                    CompletedAtUtc = legacy.CompletedAtUtc,
                    ManifestLength = manifestInfo.Length,
                    ManifestLastWriteTimeUtcTicks = manifestInfo.LastWriteTimeUtc.Ticks,
                    ManifestSha256 = ComputeSha256(manifestPath)
                };
            }

            if (string.IsNullOrWhiteSpace(metadata.AgentID) ||
                string.IsNullOrWhiteSpace(metadata.SessionName) ||
                string.IsNullOrWhiteSpace(metadata.BackupType) ||
                metadata.CompletedAtUtc == default ||
                !IsSha256(metadata.ManifestSha256))
            {
                throw new InvalidDataException("Metadata phiên backup thiếu hoặc không hợp lệ.");
            }

            FileInfo current = new FileInfo(manifestPath);
            if (!current.Exists)
            {
                throw new FileNotFoundException("Không tìm thấy manifest.json của phiên backup.", manifestPath);
            }

            // Luon bam lai manifest. Chi so sanh size/mtime co the bo sot truong hop file bi
            // thay noi dung nhung duoc gan lai dung timestamp va do dai cu.
            string actualHash = ComputeSha256(manifestPath);
            if (current.Length != metadata.ManifestLength ||
                !string.Equals(actualHash, metadata.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("manifest.json đã thay đổi sau khi phiên backup được chốt.");
            }

            return metadata;
        }

        internal static BackupSessionMetadata ReadVerifiedSession(
            string sessionRoot,
            string manifestPath,
            string expectedAgentId,
            string expectedSessionName,
            string expectedBackupType,
            bool requireSidecar,
            CancellationToken token = default)
        {
            if (requireSidecar && !File.Exists(Path.Combine(sessionRoot, FileName)))
            {
                throw new InvalidDataException("Phiên backup chưa có session.json hoàn chỉnh.");
            }

            BackupSessionMetadata metadata = ReadVerified(sessionRoot, manifestPath, token);
            if (!metadata.AgentID.Equals(expectedAgentId, StringComparison.OrdinalIgnoreCase) ||
                !metadata.SessionName.Equals(expectedSessionName, StringComparison.OrdinalIgnoreCase) ||
                !metadata.BackupType.Equals(expectedBackupType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Metadata không khớp Agent, tên hoặc loại phiên backup.");
            }

            return metadata;
        }

        private static bool IsSha256(string value) =>
            value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

        private static string ComputeSha256(string path)
        {
            using FileStream source = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(source));
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken token)
        {
            await using FileStream source = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Convert.ToHexString(await SHA256.HashDataAsync(source, token));
        }
    }

    internal sealed class BackupSessionMetadata
    {
        public int FormatVersion { get; set; }
        public string AgentID { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public string BackupType { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public DateTime CompletedAtUtc { get; set; }
        public long ManifestLength { get; set; }
        public long ManifestLastWriteTimeUtcTicks { get; set; }
        public string ManifestSha256 { get; set; } = string.Empty;
    }
}
