using AgentShared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentControl
{
    /// <summary>
    /// Module rieng dung Full hien tai ngay tren Control tu inventory da chot.
    /// File cu duoc hard link neu co the; neu khong thi copy noi bo, Agent khong upload lai.
    /// </summary>
    internal sealed class SyntheticFullBuilder
    {
        private const int InventoryBatchSize = 2000;

        public async Task<SyntheticFullResult> BuildAsync(
            BackupManifest sourceManifest,
            string controlStorageRoot,
            CancellationToken token = default)
        {
            DateTime snapshotDate = GetSessionLocalDate(sourceManifest);
            string sessionName = $"FIRST-{SanitizeFileName(sourceManifest.AgentID)}-{snapshotDate:yyyy-MM-dd}";
            string storageRoot = Path.GetFullPath(controlStorageRoot);
            string finalRoot = GetSafeChildPath(storageRoot, sessionName);

            string? completedStoragePath = await BackupRepository.GetSuccessfulSessionStoragePathAsync(
                sourceManifest.AgentID,
                sessionName);
            if (!string.IsNullOrWhiteSpace(completedStoragePath) &&
                Directory.Exists(completedStoragePath) &&
                File.Exists(Path.Combine(completedStoragePath, "manifest.json")))
            {
                BackupSessionMetadataStore.ReadVerifiedSession(
                    completedStoragePath,
                    Path.Combine(completedStoragePath, "manifest.json"),
                    sourceManifest.AgentID,
                    sessionName,
                    "FIRST",
                    requireSidecar: false,
                    token);
                return new SyntheticFullResult(sessionName, completedStoragePath, 0, 0, true);
            }

            if (Directory.Exists(finalRoot))
            {
                // Neu mat dien sau luc doi ten thu muc nhung truoc khi commit DB,
                // manifest + sidecar da xac minh la dau hieu an toan de khoi phuc moc Full.
                if (File.Exists(Path.Combine(finalRoot, "manifest.json")))
                {
                    BackupSessionMetadata metadata = BackupSessionMetadataStore.ReadVerifiedSession(
                        finalRoot,
                        Path.Combine(finalRoot, "manifest.json"),
                        sourceManifest.AgentID,
                        sessionName,
                        "FIRST",
                        requireSidecar: true,
                        token);
                    string recoveredMessage = "Synthetic Full đã hoàn tất trên đĩa và được khôi phục trạng thái vào DB.";
                    await BackupRepository.SaveSyntheticFullAsync(
                        sourceManifest.AgentID,
                        sessionName,
                        finalRoot,
                        metadata.StartedAtUtc,
                        metadata.CompletedAtUtc,
                        recoveredMessage);
                    return new SyntheticFullResult(sessionName, finalRoot, 0, 0, true);
                }

                throw new IOException($"Thư mục Synthetic Full đã tồn tại nhưng không có manifest hoàn chỉnh: {finalRoot}");
            }

            string buildRoot = GetSafeChildPath(storageRoot, sessionName + ".building");
            if (Directory.Exists(buildRoot))
            {
                // Chi xoa thu muc tam co ten da duoc kiem tra nam trong storage root.
                Directory.Delete(buildRoot, recursive: true);
            }

            string buildFilesRoot = Path.Combine(buildRoot, "Files");
            Directory.CreateDirectory(buildFilesRoot);

            DateTime startedAtUtc = DateTime.UtcNow;
            long fileCount = 0;
            long copiedFileCount = 0;
            DateTime completedAtUtc = default;
            string manifestTempPath = Path.Combine(buildRoot, "manifest.json.tmp");
            string manifestPath = Path.Combine(buildRoot, "manifest.json");

            await using (FileStream manifestStream = new FileStream(
                manifestTempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (Utf8JsonWriter writer = new Utf8JsonWriter(
                manifestStream,
                new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("AgentID", sourceManifest.AgentID);
                writer.WriteString("SessionName", sessionName);
                writer.WriteString("BackupType", "FIRST");
                writer.WriteString("StartedAtUtc", startedAtUtc);
                writer.WriteBoolean("CreateSyntheticFull", false);
                writer.WritePropertyName("Created");
                writer.WriteStartArray();

                string afterSourcePath = string.Empty;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    List<BackupInventoryRecord> records = await BackupRepository.GetLiveInventoryBatchAsync(
                        sourceManifest.AgentID,
                        afterSourcePath,
                        InventoryBatchSize);
                    if (records.Count == 0)
                    {
                        break;
                    }

                    foreach (BackupInventoryRecord record in records)
                    {
                        token.ThrowIfCancellationRequested();
                        bool copied = await MaterializeFileAsync(record, buildFilesRoot, token);
                        copiedFileCount += copied ? 1 : 0;
                        fileCount++;

                        JsonSerializer.Serialize(writer, new BackupManifestEntry
                        {
                            SourcePath = record.SourcePath,
                            RelativeStoragePath = record.RelativeStoragePath,
                            Size = record.Size,
                            LastWriteTimeUtc = record.LastWriteTimeUtc,
                            ContentSha256 = record.ContentSha256
                        });
                    }

                    afterSourcePath = records[records.Count - 1].SourcePath;
                    writer.Flush();
                    await manifestStream.FlushAsync(token);
                }

                writer.WriteEndArray();
                writer.WritePropertyName("Modified");
                writer.WriteStartArray();
                writer.WriteEndArray();
                writer.WritePropertyName("Deleted");
                writer.WriteStartArray();
                writer.WriteEndArray();
                writer.WritePropertyName("Errors");
                writer.WriteStartArray();
                writer.WriteEndArray();
                completedAtUtc = DateTime.UtcNow;
                writer.WriteString("CompletedAtUtc", completedAtUtc);
                writer.WriteEndObject();
                writer.Flush();
                await manifestStream.FlushAsync(token);
                manifestStream.Flush(flushToDisk: true);
            }

            File.Move(manifestTempPath, manifestPath);
            await BackupSessionMetadataStore.WriteAsync(
                buildRoot,
                manifestPath,
                sourceManifest.AgentID,
                sessionName,
                "FIRST",
                startedAtUtc,
                completedAtUtc,
                token);
            Directory.Move(buildRoot, finalRoot);

            string message = $"Synthetic Full hoàn tất: {fileCount} file, hard link {fileCount - copiedFileCount}, copy {copiedFileCount}.";
            await BackupRepository.SaveSyntheticFullAsync(
                sourceManifest.AgentID,
                sessionName,
                finalRoot,
                startedAtUtc,
                completedAtUtc,
                message);

            return new SyntheticFullResult(sessionName, finalRoot, fileCount, copiedFileCount, false);
        }

        private static async Task<bool> MaterializeFileAsync(
            BackupInventoryRecord record,
            string destinationFilesRoot,
            CancellationToken token)
        {
            string relativePath = NormalizeRelativePath(record.RelativeStoragePath);
            string sourceFilesRoot = Path.Combine(record.SourceSessionRoot, "Files");
            string sourcePath = GetSafeChildPath(sourceFilesRoot, relativePath);
            string destinationPath = GetSafeChildPath(destinationFilesRoot, relativePath);

            FileInfo sourceInfo = new FileInfo(sourcePath);
            if (!sourceInfo.Exists)
            {
                throw new FileNotFoundException("Không tìm thấy file nguồn để tạo Synthetic Full.", sourcePath);
            }
            if (sourceInfo.Length != record.Size)
            {
                throw new InvalidDataException($"Kích thước file nguồn không khớp inventory: {sourcePath}");
            }

            string? destinationFolder = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            if (TryCreateHardLink(destinationPath, sourcePath))
            {
                return false;
            }

            await using (FileStream source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, 1024 * 1024, token);
                await destination.FlushAsync(token);
                destination.Flush(flushToDisk: true);
            }
            File.SetLastWriteTimeUtc(destinationPath, record.LastWriteTimeUtc);
            return true;
        }

        private static bool TryCreateHardLink(string destinationPath, string sourcePath)
        {
            if (CreateHardLink(destinationPath, sourcePath, IntPtr.Zero))
            {
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            if (File.Exists(destinationPath))
            {
                throw new IOException(
                    $"Không thể tạo hard link vì file đích đã tồn tại: {destinationPath}",
                    new Win32Exception(error));
            }

            return false;
        }

        private static DateTime GetSessionLocalDate(BackupManifest manifest)
        {
            string sessionName = manifest.SessionName ?? string.Empty;
            if (sessionName.Length >= 10 &&
                DateTime.TryParseExact(
                    sessionName.Substring(sessionName.Length - 10),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsed))
            {
                return parsed;
            }

            DateTime completed = manifest.CompletedAtUtc == default ? DateTime.UtcNow : manifest.CompletedAtUtc;
            return completed.ToLocalTime().Date;
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            return PathSafety.NormalizeRelativePath(relativePath);
        }

        private static string GetSafeChildPath(string root, string child)
        {
            return PathSafety.GetSafeChildPath(root, child);
        }

        private static string SanitizeFileName(string value)
        {
            string result = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(result) ? "Agent" : result;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes);
    }

    internal sealed class SyntheticFullResult
    {
        public string SessionName { get; }
        public string StoragePath { get; }
        public long FileCount { get; }
        public long CopiedFileCount { get; }
        public bool AlreadyCompleted { get; }

        public SyntheticFullResult(
            string sessionName,
            string storagePath,
            long fileCount,
            long copiedFileCount,
            bool alreadyCompleted)
        {
            SessionName = sessionName;
            StoragePath = storagePath;
            FileCount = fileCount;
            CopiedFileCount = copiedFileCount;
            AlreadyCompleted = alreadyCompleted;
        }
    }
}
