using AgentShared;
using System.Text.Json;
using System.Security.Cryptography;

namespace AgentControl
{
    /// <summary>
    /// Trich xuat file backup ve may Control. Ghi .restoring va resume theo kich thuoc vat ly.
    /// </summary>
    internal sealed class RecoveryFileExtractor
    {
        private const int BatchSize = 500;
        private const int BufferSize = 1024 * 1024;
        private readonly RecoverySnapshotRepository _repository;

        public RecoveryFileExtractor(RecoverySnapshotRepository repository)
        {
            _repository = repository;
        }

        public async Task<RecoveryExtractionResult> ExtractAsync(
            string runId,
            string agentId,
            DateTime date,
            string destinationRoot,
            IProgress<RecoveryExtractionProgress>? progress,
            CancellationToken token)
        {
            string safeDestinationRoot = Path.GetFullPath(destinationRoot)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(safeDestinationRoot);
            (long fileCount, long totalBytes) = await _repository.GetSelectionStatsAsync(runId, agentId, date);
            RecoveryExtractionResult result = new RecoveryExtractionResult
            {
                PlannedFiles = fileCount,
                PlannedBytes = totalBytes
            };
            long completedBytes = 0;
            string afterSourcePath = string.Empty;

            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    List<RecoveryFileRecord> files = await _repository.GetSelectedBatchAsync(
                        runId, agentId, date, afterSourcePath, BatchSize);
                    if (files.Count == 0) break;

                    foreach (RecoveryFileRecord file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        long reportedForFile = 0;
                        try
                        {
                            long copiedForFile = await CopyOneAsync(
                                file,
                                safeDestinationRoot,
                                bytes =>
                                {
                                    reportedForFile += bytes;
                                    completedBytes += bytes;
                                    progress?.Report(new RecoveryExtractionProgress
                                    {
                                        CurrentFile = file.SourcePath,
                                        CompletedFiles = result.CompletedFiles,
                                        TotalFiles = fileCount,
                                        CompletedBytes = completedBytes,
                                        TotalBytes = totalBytes
                                    });
                                },
                                token);
                            if (copiedForFile < file.Size)
                            {
                                completedBytes += file.Size - copiedForFile;
                            }
                            result.CompletedFiles++;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            completedBytes += Math.Max(0, file.Size - reportedForFile);
                            result.FailedFiles++;
                            if (result.Errors.Count < 100)
                            {
                                result.Errors.Add($"{file.SourcePath}: {ex.Message}");
                            }
                        }

                        progress?.Report(new RecoveryExtractionProgress
                        {
                            CurrentFile = file.SourcePath,
                            CompletedFiles = result.CompletedFiles + result.FailedFiles,
                            TotalFiles = fileCount,
                            CompletedBytes = Math.Min(completedBytes, totalBytes),
                            TotalBytes = totalBytes
                        });
                    }

                    afterSourcePath = files[files.Count - 1].SourcePath;
                }
                return result;
            }
            finally
            {
                await _repository.ClearSelectionAsync(runId);
            }
        }

        private static async Task<long> CopyOneAsync(
            RecoveryFileRecord file,
            string destinationRoot,
            Action<long> reportBytes,
            CancellationToken token)
        {
            string sourceFilesRoot = Path.GetFullPath(Path.Combine(file.SourceSessionRoot, "Files"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string sourcePath = GetSafeChildPath(sourceFilesRoot, file.RelativeStoragePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("File vật lý của phiên backup không còn tồn tại.", sourcePath);
            }

            FileInfo sourceInfo = new FileInfo(sourcePath);
            if (sourceInfo.Length != file.Size)
            {
                throw new InvalidDataException(
                    $"Kích thước file backup không khớp manifest ({sourceInfo.Length}/{file.Size}).");
            }

            string destinationPath = GetSafeChildPath(destinationRoot, file.RelativeStoragePath);
            string? destinationFolder = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }
            string temporaryPath = destinationPath + ".restoring";

            string metadataPath = temporaryPath + ".meta";
            RecoveryPartialMetadata expectedMetadata = new RecoveryPartialMetadata
            {
                SourceSessionRoot = file.SourceSessionRoot,
                RelativeStoragePath = file.RelativeStoragePath,
                Size = file.Size,
                LastWriteTimeUtc = file.LastWriteTimeUtc,
                ContentSha256 = file.ContentSha256
            };
            bool canResume = File.Exists(temporaryPath) &&
                             TryReadMatchingMetadata(metadataPath, expectedMetadata);
            if (!canResume)
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                if (File.Exists(metadataPath)) File.Delete(metadataPath);
                await WriteMetadataAsync(metadataPath, expectedMetadata, token);
            }

            long offset = File.Exists(temporaryPath) ? new FileInfo(temporaryPath).Length : 0;
            if (offset < 0 || offset > file.Size)
            {
                File.Delete(temporaryPath);
                offset = 0;
            }

            await using FileStream source = new FileStream(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using FileStream destination = new FileStream(
                temporaryPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            source.Seek(offset, SeekOrigin.Begin);
            destination.Seek(offset, SeekOrigin.Begin);
            if (offset > 0) reportBytes(offset);

            byte[] buffer = new byte[BufferSize];
            int read;
            long copied = offset;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), token);
                copied += read;
                reportBytes(read);
            }
            destination.SetLength(file.Size);
            await destination.FlushAsync(token);
            destination.Close();

            if (IsSha256(file.ContentSha256))
            {
                string restoredHash = await ComputeSha256Async(temporaryPath, token);
                if (!string.Equals(restoredHash, file.ContentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(temporaryPath);
                    if (File.Exists(metadataPath)) File.Delete(metadataPath);
                    throw new InvalidDataException("SHA-256 file khôi phục không khớp manifest.");
                }
            }

            File.SetLastWriteTimeUtc(temporaryPath, file.LastWriteTimeUtc);
            File.Move(temporaryPath, destinationPath, overwrite: true);
            if (File.Exists(metadataPath)) File.Delete(metadataPath);
            return copied;
        }

        private static bool TryReadMatchingMetadata(string path, RecoveryPartialMetadata expected)
        {
            try
            {
                if (!File.Exists(path)) return false;
                RecoveryPartialMetadata? actual = JsonSerializer.Deserialize<RecoveryPartialMetadata>(File.ReadAllText(path));
                return actual != null &&
                       actual.SourceSessionRoot.Equals(expected.SourceSessionRoot, StringComparison.OrdinalIgnoreCase) &&
                       actual.RelativeStoragePath.Equals(expected.RelativeStoragePath, StringComparison.OrdinalIgnoreCase) &&
                       actual.Size == expected.Size &&
                       actual.LastWriteTimeUtc == expected.LastWriteTimeUtc &&
                       actual.ContentSha256.Equals(expected.ContentSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSha256(string value) =>
            value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

        private static async Task<string> ComputeSha256Async(string path, CancellationToken token)
        {
            await using FileStream source = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Convert.ToHexString(await SHA256.HashDataAsync(source, token));
        }

        private static async Task WriteMetadataAsync(
            string path, RecoveryPartialMetadata metadata, CancellationToken token)
        {
            string temporaryMetadata = path + ".tmp";
            await File.WriteAllTextAsync(temporaryMetadata, JsonSerializer.Serialize(metadata), token);
            File.Move(temporaryMetadata, path, overwrite: true);
        }

        private static string GetSafeChildPath(string root, string relativePath)
        {
            return PathSafety.GetSafeChildPath(root, relativePath);
        }
    }

    internal sealed class RecoveryExtractionProgress
    {
        public string CurrentFile { get; set; } = string.Empty;
        public long CompletedFiles { get; set; }
        public long TotalFiles { get; set; }
        public long CompletedBytes { get; set; }
        public long TotalBytes { get; set; }
    }

    internal sealed class RecoveryExtractionResult
    {
        public long PlannedFiles { get; set; }
        public long PlannedBytes { get; set; }
        public long CompletedFiles { get; set; }
        public long FailedFiles { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }

    internal sealed class RecoveryPartialMetadata
    {
        public string SourceSessionRoot { get; set; } = string.Empty;
        public string RelativeStoragePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string ContentSha256 { get; set; } = string.Empty;
    }
}
