using AgentShared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentControl
{
    /// <summary>
    /// Nhan rieng luong file backup tu Agent va ghi truc tiep xuong dia Control.
    /// </summary>
    internal sealed class BackupReceiver
    {
        private readonly SyntheticFullBuilder _syntheticFullBuilder = new SyntheticFullBuilder();
        private readonly ConcurrentDictionary<string, ActiveBackupSession> _sessions =
            new ConcurrentDictionary<string, ActiveBackupSession>(StringComparer.OrdinalIgnoreCase);

        public async Task BeginSessionAsync(BackupSessionBegin request)
        {
            BackupConfiguration? config = await BackupRepository.GetConfigAsync(request.AgentID);
            if (config == null || !config.Enabled)
            {
                throw new InvalidOperationException("Agent chưa có cấu hình backup đang hoạt động trên Control.");
            }

            if (string.IsNullOrWhiteSpace(config.ControlStoragePath))
            {
                throw new InvalidOperationException("Đường dẫn lưu backup trên Control đang trống.");
            }

            string sessionName = SanitizeSessionName(request.SessionName);
            string storageRoot = Path.GetFullPath(config.ControlStoragePath);
            Directory.CreateDirectory(storageRoot);

            bool resumableFirst = request.IsResumableFirst &&
                                  request.BackupType.Equals("FIRST", StringComparison.OrdinalIgnoreCase);
            string folderName = resumableFirst ? sessionName + ".inprogress" : sessionName;
            string sessionRoot = GetSafeChildPath(storageRoot, folderName);
            bool alreadyCompleted = false;

            if (resumableFirst)
            {
                CompletedFirstRun? completedRun = await FirstBackupStore.GetCompletedRunAsync(request.AgentID);
                if (completedRun != null &&
                    Directory.Exists(completedRun.StoragePath) &&
                    File.Exists(Path.Combine(completedRun.StoragePath, "manifest.json")))
                {
                    if (completedRun.Status.Equals("Finalizing", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime recoveredAtUtc = DateTime.UtcNow;
                        await FirstBackupStore.FinalizeRunAsync(
                            request.AgentID,
                            completedRun.SessionName,
                            completedRun.StoragePath,
                            request.StartedAtUtc,
                            recoveredAtUtc,
                            "FIRST được khôi phục sau khi mất điện ở bước chốt DB.");
                    }
                    sessionRoot = completedRun.StoragePath;
                    alreadyCompleted = true;
                }
                else
                {
                    Directory.CreateDirectory(sessionRoot);
                    Directory.CreateDirectory(Path.Combine(sessionRoot, "Files"));
                    await FirstBackupStore.BeginRunAsync(request, sessionRoot);
                }
            }
            else
            {
                Directory.CreateDirectory(sessionRoot);
                Directory.CreateDirectory(Path.Combine(sessionRoot, "Files"));
            }

            ActiveBackupSession session = new ActiveBackupSession(
                request.AgentID,
                sessionName,
                sessionRoot,
                resumableFirst,
                request.StartedAtUtc,
                alreadyCompleted);

            if (_sessions.TryGetValue(request.AgentID, out ActiveBackupSession? previous))
            {
                previous.Dispose();
            }

            _sessions[request.AgentID] = session;
        }

        public async Task<BackupFirstFileResumeInfo> GetFirstFileResumeInfoAsync(BackupFirstFileResumeQuery query)
        {
            if (!_sessions.TryGetValue(query.AgentID, out ActiveBackupSession? session) ||
                !session.IsResumableFirst ||
                !session.SessionName.Equals(query.SessionName, StringComparison.OrdinalIgnoreCase))
            {
                return new BackupFirstFileResumeInfo
                {
                    SessionName = query.SessionName,
                    SourcePath = query.SourcePath,
                    Success = false,
                    Message = "Control không tìm thấy FIRST đang chạy."
                };
            }

            try
            {
                string relativePath = NormalizeRelativePath(query.RelativeStoragePath);
                string finalPath = GetSafeChildPath(Path.Combine(session.SessionRoot, "Files"), relativePath);
                string partialPath = finalPath + ".partial";
                FirstFileRegistration registration = await FirstBackupStore.RegisterFileAsync(query);

                if (registration.ResetRequired)
                {
                    if (File.Exists(partialPath)) File.Delete(partialPath);
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                }

                if (registration.Completed && File.Exists(finalPath) && new FileInfo(finalPath).Length == query.TotalBytes)
                {
                    return new BackupFirstFileResumeInfo
                    {
                        SessionName = query.SessionName,
                        SourcePath = query.SourcePath,
                        Success = true,
                        Completed = true,
                        Offset = query.TotalBytes
                    };
                }

                if (registration.Skipped)
                {
                    return new BackupFirstFileResumeInfo
                    {
                        SessionName = query.SessionName,
                        SourcePath = query.SourcePath,
                        Success = true,
                        Completed = true,
                        Skipped = true,
                        Offset = query.TotalBytes,
                        Message = "File đã được FIRST bỏ qua và sẽ để INC xử lý sau."
                    };
                }

                long offset = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                if (offset < 0 || offset > query.TotalBytes)
                {
                    if (File.Exists(partialPath)) File.Delete(partialPath);
                    offset = 0;
                }
                await FirstBackupStore.UpdateProgressAsync(query.AgentID, query.SourcePath, offset);
                return new BackupFirstFileResumeInfo
                {
                    SessionName = query.SessionName,
                    SourcePath = query.SourcePath,
                    Success = true,
                    Offset = offset
                };
            }
            catch (Exception ex)
            {
                return new BackupFirstFileResumeInfo
                {
                    SessionName = query.SessionName,
                    SourcePath = query.SourcePath,
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        public async Task SkipFirstFileAsync(BackupFirstFileSkip skipped, CancellationToken token = default)
        {
            if (!_sessions.TryGetValue(skipped.AgentID, out ActiveBackupSession? session) ||
                !session.IsResumableFirst ||
                !session.SessionName.Equals(skipped.SessionName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Control không tìm thấy FIRST để ghi nhận file bỏ qua.");
            }

            string relativePath = NormalizeRelativePath(skipped.RelativeStoragePath);
            string finalPath = GetSafeChildPath(Path.Combine(session.SessionRoot, "Files"), relativePath);
            string partialPath = finalPath + ".partial";
            await session.WriteLock.WaitAsync(token);
            try
            {
                if (File.Exists(partialPath)) File.Delete(partialPath);
                if (File.Exists(finalPath)) File.Delete(finalPath);
                await FirstBackupStore.MarkSkippedAsync(skipped);
                string journalLine = JsonSerializer.Serialize(new
                {
                    Operation = "Skipped",
                    skipped.SourcePath,
                    RelativeStoragePath = relativePath,
                    skipped.Reason,
                    AtUtc = DateTime.UtcNow
                }) + Environment.NewLine;
                await File.AppendAllTextAsync(Path.Combine(session.SessionRoot, "manifest.journal"), journalLine, token);
            }
            finally
            {
                session.WriteLock.Release();
            }
        }

        public async Task HandleFileChunkAsync(Stream stream, int frameSize, CancellationToken token = default)
        {
            (BackupFileChunkHeader header, int bodySize) =
                await TransferFrameProtocol.ReadBackupChunkHeaderAsync(stream, frameSize, token);

            if (!_sessions.TryGetValue(header.AgentID, out ActiveBackupSession? session) ||
                !session.SessionName.Equals(header.SessionName, StringComparison.OrdinalIgnoreCase))
            {
                await TransferFrameProtocol.DrainExactAsync(stream, bodySize, token);
                throw new InvalidOperationException("Không tìm thấy phiên backup tương ứng trên Control.");
            }

            if (bodySize != header.ChunkSize || bodySize < 0)
            {
                await TransferFrameProtocol.DrainExactAsync(stream, Math.Max(0, bodySize), token);
                throw new InvalidDataException("Kích thước binary chunk backup không hợp lệ.");
            }

            string relativePath = NormalizeRelativePath(header.RelativeStoragePath);
            string fileRoot = Path.Combine(session.SessionRoot, "Files");
            string finalPath = GetSafeChildPath(fileRoot, relativePath);
            string destinationPath = session.IsResumableFirst ? finalPath + ".partial" : finalPath;
            string? destinationFolder = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            await session.WriteLock.WaitAsync(token);
            try
            {
                {
                    if (session.IsResumableFirst)
                    {
                        long actualOffset = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;
                        if (actualOffset != header.Offset)
                        {
                            await TransferFrameProtocol.DrainExactAsync(stream, bodySize, token);
                            throw new InvalidDataException($"Offset FIRST không khớp. Control={actualOffset}, Agent={header.Offset}.");
                        }
                    }

                    using FileStream destination = new FileStream(
                        destinationPath,
                        FileMode.OpenOrCreate,
                        FileAccess.Write,
                        FileShare.Read,
                        128 * 1024,
                        useAsync: true);
                    destination.Seek(Math.Max(0, header.Offset), SeekOrigin.Begin);
                    await TransferFrameProtocol.CopyExactToAsync(stream, destination, bodySize, token);

                    if (header.IsLastChunk)
                    {
                        destination.SetLength(Math.Max(0, header.TotalBytes));
                    }
                }

                if (header.IsLastChunk)
                {
                    File.SetLastWriteTimeUtc(destinationPath, header.LastWriteTimeUtc);
                    if (session.IsResumableFirst)
                    {
                        if (File.Exists(finalPath)) File.Delete(finalPath);
                        File.Move(destinationPath, finalPath);
                        BackupManifestEntry journalEntry = new BackupManifestEntry
                        {
                            SourcePath = header.SourcePath,
                            RelativeStoragePath = relativePath,
                            Size = header.TotalBytes,
                            LastWriteTimeUtc = header.LastWriteTimeUtc
                        };
                        string journalLine = JsonSerializer.Serialize(journalEntry) + Environment.NewLine;
                        await File.AppendAllTextAsync(Path.Combine(session.SessionRoot, "manifest.journal"), journalLine, token);
                        await FirstBackupStore.MarkCompletedAsync(header.AgentID, header.SourcePath, header.TotalBytes);
                    }
                }
                else if (session.IsResumableFirst && (header.Offset + bodySize) % (8 * 1024 * 1024) == 0)
                {
                    await FirstBackupStore.UpdateProgressAsync(header.AgentID, header.SourcePath, header.Offset + bodySize);
                }
            }
            finally
            {
                session.WriteLock.Release();
            }
        }

        public async Task<BackupSessionResult> CompleteSessionAsync(BackupManifest manifest)
        {
            if (!_sessions.TryRemove(manifest.AgentID, out ActiveBackupSession? session) ||
                !session.SessionName.Equals(manifest.SessionName, StringComparison.OrdinalIgnoreCase))
            {
                return new BackupSessionResult
                {
                    SessionName = manifest.SessionName,
                    Success = false,
                    Message = "Control không tìm thấy phiên backup đang chạy."
                };
            }

            try
            {
                manifest.CompletedAtUtc = manifest.CompletedAtUtc == default
                    ? DateTime.UtcNow
                    : manifest.CompletedAtUtc;

                if (session.IsResumableFirst && manifest.IsResumableFirst)
                {
                    return await CompleteResumableFirstAsync(manifest, session);
                }

                string manifestPath = Path.Combine(session.SessionRoot, "manifest.json");
                JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, options));

                string message = manifest.Errors.Count == 0
                    ? "Backup hoàn tất."
                    : $"Backup hoàn tất, manifest ghi nhận {manifest.Errors.Count} lỗi truy cập file/thư mục.";

                await BackupRepository.SaveSessionAsync(manifest, session.SessionRoot, true, message);

                // BO SUNG MODULE BACKUP - SYNTHETIC FULL:
                // Sau phien INC cuoi chu ky, Control tu dung FIRST moi tu inventory hien tai.
                if (manifest.CreateSyntheticFull)
                {
                    string? storageRoot = Path.GetDirectoryName(session.SessionRoot);
                    if (string.IsNullOrWhiteSpace(storageRoot))
                    {
                        throw new InvalidDataException("Không xác định được thư mục gốc để tạo Synthetic Full.");
                    }

                    SyntheticFullResult syntheticResult = await _syntheticFullBuilder.BuildAsync(
                        manifest,
                        storageRoot);
                    message = syntheticResult.AlreadyCompleted
                        ? $"Backup hoàn tất. Synthetic Full {syntheticResult.SessionName} đã tồn tại."
                        : $"Backup hoàn tất và đã tạo {syntheticResult.SessionName}: " +
                          $"{syntheticResult.FileCount} file, copy {syntheticResult.CopiedFileCount} file.";
                }

                return new BackupSessionResult
                {
                    SessionName = manifest.SessionName,
                    Success = true,
                    Message = message
                };
            }
            catch (Exception ex)
            {
                await BackupRepository.SaveSessionAsync(manifest, session.SessionRoot, false, ex.Message);
                return new BackupSessionResult
                {
                    SessionName = manifest.SessionName,
                    Success = false,
                    Message = ex.Message
                };
            }
            finally
            {
                session.Dispose();
            }
        }

        private static async Task<BackupSessionResult> CompleteResumableFirstAsync(
            BackupManifest manifest,
            ActiveBackupSession session)
        {
            if (session.AlreadyCompleted)
            {
                return new BackupSessionResult
                {
                    SessionName = manifest.SessionName,
                    Success = true,
                    Message = "FIRST đã hoàn tất trước đó; Control xác nhận lại để Agent chốt trạng thái."
                };
            }

            (long planned, long completed) = await FirstBackupStore.GetRunCountsAsync(manifest.AgentID);
            if (planned <= 0 || completed != planned)
            {
                return new BackupSessionResult
                {
                    SessionName = manifest.SessionName,
                    Success = false,
                    Message = $"FIRST chưa nhận đủ file: {completed}/{planned}."
                };
            }

            DateTime completedAtUtc = DateTime.UtcNow;
            string finalSessionName = $"FIRST-{SanitizeSessionName(manifest.AgentID)}-{completedAtUtc.ToLocalTime():yyyy-MM-dd}";
            string storageRoot = Path.GetDirectoryName(session.SessionRoot)
                ?? throw new InvalidDataException("Không xác định được thư mục gốc FIRST.");
            string finalRoot = GetSafeChildPath(storageRoot, finalSessionName);
            if (Directory.Exists(finalRoot))
            {
                throw new IOException($"Thư mục FIRST hoàn tất đã tồn tại: {finalRoot}");
            }

            string tempManifest = Path.Combine(session.SessionRoot, "manifest.json.tmp");
            string finalManifest = Path.Combine(session.SessionRoot, "manifest.json");
            await WriteFirstManifestAsync(manifest, finalSessionName, session.StartedAtUtc, completedAtUtc, tempManifest);
            File.Move(tempManifest, finalManifest, overwrite: true);
            await FirstBackupStore.MarkFinalizingAsync(manifest.AgentID, finalSessionName, finalRoot);
            Directory.Move(session.SessionRoot, finalRoot);

            string message = $"FIRST hoàn tất ngày {completedAtUtc.ToLocalTime():yyyy-MM-dd}: đã xử lý {completed} file.";
            await FirstBackupStore.FinalizeRunAsync(
                manifest.AgentID, finalSessionName, finalRoot,
                session.StartedAtUtc, completedAtUtc, message);

            return new BackupSessionResult
            {
                SessionName = manifest.SessionName,
                Success = true,
                Message = message
            };
        }

        private static async Task WriteFirstManifestAsync(
            BackupManifest source,
            string finalSessionName,
            DateTime startedAtUtc,
            DateTime completedAtUtc,
            string destinationPath)
        {
            await using FileStream stream = new FileStream(
                destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteString("AgentID", source.AgentID);
            writer.WriteString("SessionName", finalSessionName);
            writer.WriteString("BackupType", "FIRST");
            writer.WriteString("StartedAtUtc", startedAtUtc);
            writer.WriteString("CompletedAtUtc", completedAtUtc);
            writer.WriteBoolean("CreateSyntheticFull", false);
            writer.WriteBoolean("IsResumableFirst", true);
            writer.WritePropertyName("Created");
            writer.WriteStartArray();
            string after = string.Empty;
            while (true)
            {
                List<BackupManifestEntry> batch = await FirstBackupStore.GetCompletedBatchAsync(source.AgentID, after, 2000);
                if (batch.Count == 0) break;
                foreach (BackupManifestEntry entry in batch) JsonSerializer.Serialize(writer, entry);
                after = batch[batch.Count - 1].SourcePath;
                writer.Flush();
                await stream.FlushAsync();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("Modified"); writer.WriteStartArray(); writer.WriteEndArray();
            writer.WritePropertyName("Deleted"); writer.WriteStartArray(); writer.WriteEndArray();
            writer.WritePropertyName("Errors");
            writer.WriteStartArray();
            foreach (string error in source.Errors) writer.WriteStringValue(error);
            string skippedAfter = string.Empty;
            while (true)
            {
                List<FirstSkippedFile> skippedBatch = await FirstBackupStore.GetSkippedBatchAsync(source.AgentID, skippedAfter, 2000);
                if (skippedBatch.Count == 0) break;
                foreach (FirstSkippedFile skipped in skippedBatch)
                {
                    writer.WriteStringValue($"Skipped {skipped.SourcePath}: {skipped.Reason}");
                }
                skippedAfter = skippedBatch[skippedBatch.Count - 1].SourcePath;
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
            await stream.FlushAsync();
        }

        private static string SanitizeSessionName(string sessionName)
        {
            string value = Path.GetFileName(sessionName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException("Tên phiên backup không hợp lệ.");
            }

            return value;
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            string value = (relativePath ?? string.Empty)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            {
                throw new InvalidDataException("Đường dẫn tương đối của file backup không hợp lệ.");
            }

            return value;
        }

        private static string GetSafeChildPath(string root, string child)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(fullRoot, child));
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Đường dẫn backup vượt ra ngoài thư mục lưu đã cấu hình.");
            }

            return fullPath;
        }

        private sealed class ActiveBackupSession : IDisposable
        {
            public string AgentID { get; }
            public string SessionName { get; }
            public string SessionRoot { get; }
            public bool IsResumableFirst { get; }
            public DateTime StartedAtUtc { get; }
            public bool AlreadyCompleted { get; }
            public SemaphoreSlim WriteLock { get; } = new SemaphoreSlim(1, 1);

            public ActiveBackupSession(
                string agentId, string sessionName, string sessionRoot,
                bool isResumableFirst, DateTime startedAtUtc, bool alreadyCompleted)
            {
                AgentID = agentId;
                SessionName = sessionName;
                SessionRoot = sessionRoot;
                IsResumableFirst = isResumableFirst;
                StartedAtUtc = startedAtUtc;
                AlreadyCompleted = alreadyCompleted;
            }

            public void Dispose()
            {
                WriteLock.Dispose();
            }
        }
    }
}
