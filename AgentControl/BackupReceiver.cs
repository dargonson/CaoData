using AgentShared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
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
            if (string.IsNullOrWhiteSpace(request.AgentID) ||
                request.StartedAtUtc == default ||
                request.PlannedFileCount < 0 ||
                request.PlannedTotalBytes < 0)
            {
                throw new InvalidDataException("Metadata mở phiên backup không hợp lệ.");
            }

            string backupType = (request.BackupType ?? string.Empty).Trim().ToUpperInvariant();
            if (backupType != "FIRST" && backupType != "INC")
            {
                throw new InvalidDataException("Loại phiên backup không hợp lệ.");
            }

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

            bool resumableFirst = request.IsResumableFirst && backupType == "FIRST";
            string folderName = resumableFirst ? sessionName + ".inprogress" : sessionName;
            string sessionRoot = GetSafeChildPath(storageRoot, folderName);
            bool alreadyCompleted = false;

            if (resumableFirst)
            {
                CompletedFirstRun? completedRun = await FirstBackupStore.GetCompletedRunAsync(request.AgentID);
                if (completedRun != null && IsSameFirstPlan(completedRun, request))
                {
                    string expectedFinalPath = GetSafeChildPath(
                        storageRoot,
                        Path.GetFileName(completedRun.StoragePath));
                    string expectedWorkingPath = GetSafeChildPath(
                        storageRoot,
                        Path.GetFileName(completedRun.WorkingPath));

                    if (completedRun.Status.Equals("Finalizing", StringComparison.OrdinalIgnoreCase) &&
                        !Directory.Exists(expectedFinalPath) &&
                        Directory.Exists(expectedWorkingPath))
                    {
                        ValidateCompletedFirstArtifacts(
                            expectedWorkingPath,
                            request.AgentID,
                            completedRun.SessionName,
                            requireSidecar: true);
                        Directory.Move(expectedWorkingPath, expectedFinalPath);
                    }

                    if (Directory.Exists(expectedFinalPath))
                    {
                        ValidateCompletedFirstArtifacts(
                            expectedFinalPath,
                            request.AgentID,
                            completedRun.SessionName,
                            requireSidecar: completedRun.Status.Equals(
                                "Finalizing",
                                StringComparison.OrdinalIgnoreCase));
                        if (completedRun.Status.Equals("Finalizing", StringComparison.OrdinalIgnoreCase))
                        {
                            BackupSessionMetadata metadata = BackupSessionMetadataStore.ReadVerifiedSession(
                                expectedFinalPath,
                                Path.Combine(expectedFinalPath, "manifest.json"),
                                request.AgentID,
                                completedRun.SessionName,
                                "FIRST",
                                requireSidecar: true);
                            await FirstBackupStore.FinalizeRunAsync(
                                request.AgentID,
                                completedRun.SessionName,
                                expectedFinalPath,
                                metadata.StartedAtUtc,
                                metadata.CompletedAtUtc,
                                "FIRST được khôi phục sau khi mất điện ở bước chốt DB.");
                        }
                        sessionRoot = expectedFinalPath;
                        alreadyCompleted = true;
                    }
                    else if (completedRun.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                             completedRun.Status.Equals("Finalizing", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "DB ghi nhận FIRST đã chốt nhưng không tìm thấy thư mục backup hoàn chỉnh. " +
                            "Không tự tạo lại để tránh che lấp mất dữ liệu.");
                    }
                }

                if (!alreadyCompleted)
                {
                    bool isNewPlan = await FirstBackupStore.BeginRunAsync(request, sessionRoot);
                    if (isNewPlan && Directory.Exists(sessionRoot))
                    {
                        Directory.Delete(sessionRoot, recursive: true);
                    }
                    Directory.CreateDirectory(sessionRoot);
                    Directory.CreateDirectory(Path.Combine(sessionRoot, "Files"));
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
                backupType,
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
                        Offset = query.TotalBytes,
                        ContentSha256 = registration.ContentSha256
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
                await AppendJournalLineAsync(
                    Path.Combine(session.SessionRoot, "manifest.journal"),
                    journalLine,
                    token);
            }
            finally
            {
                session.WriteLock.Release();
            }
        }

        public async Task HandleFileChunkAsync(
            Stream stream,
            int frameSize,
            string authenticatedAgentId,
            CancellationToken token = default)
        {
            (BackupFileChunkHeader header, int bodySize) =
                await TransferFrameProtocol.ReadBackupChunkHeaderAsync(stream, frameSize, token);

            if (!string.Equals(header.AgentID, authenticatedAgentId, StringComparison.OrdinalIgnoreCase))
            {
                await TransferFrameProtocol.DrainExactAsync(stream, bodySize, token);
                throw new InvalidDataException("AgentID của chunk backup không khớp kết nối đã xác thực.");
            }

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

            if (header.TotalBytes < 0 || header.Offset < 0 ||
                header.Offset > header.TotalBytes ||
                bodySize > header.TotalBytes - header.Offset ||
                (header.IsLastChunk && header.Offset + bodySize != header.TotalBytes) ||
                (!header.IsLastChunk && header.Offset + bodySize >= header.TotalBytes) ||
                (header.IsLastChunk && !IsSha256(header.ContentSha256)))
            {
                await TransferFrameProtocol.DrainExactAsync(stream, bodySize, token);
                throw new InvalidDataException("Metadata chunk backup không hợp lệ.");
            }

            string relativePath = NormalizeRelativePath(header.RelativeStoragePath);
            string fileRoot = Path.Combine(session.SessionRoot, "Files");
            string finalPath = GetSafeChildPath(fileRoot, relativePath);
            // Ghi moi loai backup vao file tam. Khi chunk cuoi hop le moi replace file
            // chinh, de retry INC khong ghi truc tiep vao inode/hard-link cua Synthetic Full.
            string destinationPath = finalPath + (session.IsResumableFirst ? ".partial" : ".incoming");
            string? destinationFolder = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            await session.WriteLock.WaitAsync(token);
            try
            {
                await ResumableTransferFile.WriteChunkAsync(
                    stream,
                    destinationPath,
                    header.Offset,
                    header.TotalBytes,
                    bodySize,
                    header.IsLastChunk,
                    token);

                if (header.IsLastChunk)
                {
                    string actualHash = await ComputeSha256Async(destinationPath, token);
                    if (!string.Equals(actualHash, header.ContentSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(destinationPath);
                        if (session.IsResumableFirst)
                        {
                            await FirstBackupStore.UpdateProgressAsync(header.AgentID, header.SourcePath, 0);
                        }
                        throw new InvalidDataException("SHA-256 file backup không khớp; file tạm đã được reset.");
                    }

                    File.SetLastWriteTimeUtc(destinationPath, header.LastWriteTimeUtc);
                    File.Move(destinationPath, finalPath, overwrite: true);
                    if (session.IsResumableFirst)
                    {
                        BackupManifestEntry journalEntry = new BackupManifestEntry
                        {
                            SourcePath = header.SourcePath,
                            RelativeStoragePath = relativePath,
                            Size = header.TotalBytes,
                            LastWriteTimeUtc = header.LastWriteTimeUtc,
                            ContentSha256 = header.ContentSha256
                        };
                        string journalLine = JsonSerializer.Serialize(journalEntry) + Environment.NewLine;
                        await AppendJournalLineAsync(
                            Path.Combine(session.SessionRoot, "manifest.journal"),
                            journalLine,
                            token);
                        await FirstBackupStore.MarkCompletedAsync(
                            header.AgentID,
                            header.SourcePath,
                            header.TotalBytes,
                            header.ContentSha256);
                    }
                    else
                    {
                        session.ReceivedFiles[relativePath] = new BackupManifestEntry
                        {
                            SourcePath = header.SourcePath,
                            RelativeStoragePath = relativePath,
                            Size = header.TotalBytes,
                            LastWriteTimeUtc = header.LastWriteTimeUtc,
                            ContentSha256 = header.ContentSha256
                        };
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

        private static bool IsSha256(string value)
        {
            return value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken token)
        {
            await using FileStream source = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(source, token);
            return Convert.ToHexString(hash);
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
                if (!string.Equals(manifest.BackupType, session.BackupType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Loại manifest không khớp phiên backup đã mở.");
                }

                manifest.CompletedAtUtc = manifest.CompletedAtUtc == default
                    ? DateTime.UtcNow
                    : manifest.CompletedAtUtc;

                if (session.IsResumableFirst && manifest.IsResumableFirst)
                {
                    return await CompleteResumableFirstAsync(manifest, session);
                }

                ValidateReceivedManifest(session, manifest);

                string manifestPath = Path.Combine(session.SessionRoot, "manifest.json");
                await WriteManifestAtomicallyAsync(manifestPath, manifest);
                await BackupSessionMetadataStore.WriteAsync(
                    session.SessionRoot,
                    manifestPath,
                    manifest.AgentID,
                    manifest.SessionName,
                    session.BackupType,
                    manifest.StartedAtUtc,
                    manifest.CompletedAtUtc);

                string message = manifest.Errors.Count == 0
                    ? "Backup hoàn tất."
                    : $"Backup hoàn tất, manifest ghi nhận {manifest.Errors.Count} lỗi truy cập file/thư mục.";

                await BackupRepository.SaveSessionAsync(manifest, session.SessionRoot, true, message);

                // BO SUNG MODULE BACKUP - SYNTHETIC FULL:
                // Sau phien INC cuoi chu ky, Control tu dung FIRST moi tu inventory hien tai.
                bool syntheticFullCompleted = true;
                if (manifest.CreateSyntheticFull)
                {
                    try
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
                    catch (Exception syntheticError)
                    {
                        // INC va inventory da duoc commit o tren. Khong duoc danh dau lai INC la
                        // that bai, neu khong Agent va Control se giu hai inventory khac nhau.
                        syntheticFullCompleted = false;
                        message = "Backup INC đã hoàn tất nhưng chưa tạo được Synthetic Full; " +
                                  $"hệ thống sẽ thử lại ở kỳ backup sau. Lỗi: {syntheticError.Message}";
                    }
                }

                return new BackupSessionResult
                {
                    SessionName = manifest.SessionName,
                    Success = true,
                    SyntheticFullCompleted = syntheticFullCompleted,
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

            (bool runExists, long planned, long completed) =
                await FirstBackupStore.GetRunCountsAsync(manifest.AgentID);
            if (!runExists || completed != planned)
            {
                return new BackupSessionResult
                {
                    SessionName = manifest.SessionName,
                    Success = false,
                    Message = $"FIRST chưa nhận đủ file: {completed}/{planned}."
                };
            }

            DateTime completedAtUtc = DateTime.UtcNow;
            string storageRoot = Path.GetDirectoryName(session.SessionRoot)
                ?? throw new InvalidDataException("Không xác định được thư mục gốc FIRST.");
            string finalSessionName = CreateAvailableFirstSessionName(
                storageRoot,
                manifest.AgentID,
                completedAtUtc.ToLocalTime().Date);
            string finalRoot = GetSafeChildPath(storageRoot, finalSessionName);

            string tempManifest = Path.Combine(session.SessionRoot, "manifest.json.tmp");
            string finalManifest = Path.Combine(session.SessionRoot, "manifest.json");
            await WriteFirstManifestAsync(manifest, finalSessionName, session.StartedAtUtc, completedAtUtc, tempManifest);
            File.Move(tempManifest, finalManifest, overwrite: true);
            await BackupSessionMetadataStore.WriteAsync(
                session.SessionRoot,
                finalManifest,
                manifest.AgentID,
                finalSessionName,
                "FIRST",
                session.StartedAtUtc,
                completedAtUtc);
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
            stream.Flush(flushToDisk: true);
        }

        private static async Task AppendJournalLineAsync(
            string journalPath,
            string line,
            CancellationToken token)
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(line);
            await using FileStream journal = new FileStream(
                journalPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await journal.WriteAsync(payload, token);
            await journal.FlushAsync(token);
            journal.Flush(flushToDisk: true);
        }

        private static async Task WriteManifestAtomicallyAsync(
            string manifestPath,
            BackupManifest manifest,
            CancellationToken token = default)
        {
            string temporaryPath = manifestPath + ".tmp";
            await using (FileStream destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                    destination,
                    manifest,
                    new JsonSerializerOptions { WriteIndented = true },
                    token);
                await destination.FlushAsync(token);
                destination.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, manifestPath, overwrite: true);
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

        private static bool IsSameFirstPlan(CompletedFirstRun run, BackupSessionBegin request)
        {
            return run.WorkingSessionName.Equals(request.SessionName, StringComparison.OrdinalIgnoreCase) &&
                   run.StartedAtUtc == request.StartedAtUtc &&
                   run.PlannedFileCount == request.PlannedFileCount &&
                   run.PlannedTotalBytes == request.PlannedTotalBytes;
        }

        private static void ValidateReceivedManifest(
            ActiveBackupSession session,
            BackupManifest manifest)
        {
            if (!manifest.AgentID.Equals(session.AgentID, StringComparison.OrdinalIgnoreCase) ||
                !manifest.SessionName.Equals(session.SessionName, StringComparison.OrdinalIgnoreCase) ||
                manifest.StartedAtUtc != session.StartedAtUtc ||
                manifest.CompletedAtUtc < manifest.StartedAtUtc ||
                manifest.Created == null || manifest.Modified == null ||
                manifest.Deleted == null || manifest.Errors == null)
            {
                throw new InvalidDataException("Metadata phiên hoặc danh sách manifest không hợp lệ.");
            }

            var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BackupManifestEntry entry in manifest.Created.Concat(manifest.Modified))
            {
                string relativePath = NormalizeRelativePath(entry.RelativeStoragePath);
                if (string.IsNullOrWhiteSpace(entry.SourcePath) || entry.Size < 0 ||
                    entry.LastWriteTimeUtc == default || !IsSha256(entry.ContentSha256) ||
                    !sourcePaths.Add(entry.SourcePath) || !relativePaths.Add(relativePath) ||
                    !session.ReceivedFiles.TryGetValue(relativePath, out BackupManifestEntry? received) ||
                    !received.SourcePath.Equals(entry.SourcePath, StringComparison.OrdinalIgnoreCase) ||
                    received.Size != entry.Size ||
                    received.LastWriteTimeUtc != entry.LastWriteTimeUtc ||
                    !received.ContentSha256.Equals(entry.ContentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Manifest không khớp file Control đã nhận: {entry.SourcePath}");
                }

                string storedPath = GetSafeChildPath(
                    Path.Combine(session.SessionRoot, "Files"),
                    relativePath);
                if (!File.Exists(storedPath) || new FileInfo(storedPath).Length != entry.Size)
                {
                    throw new InvalidDataException(
                        $"File của manifest không tồn tại hoặc sai kích thước: {entry.SourcePath}");
                }
            }

            foreach (BackupManifestEntry entry in manifest.Deleted)
            {
                if (string.IsNullOrWhiteSpace(entry.SourcePath) || !sourcePaths.Add(entry.SourcePath))
                {
                    throw new InvalidDataException("Danh sách file xóa trong manifest không hợp lệ.");
                }
                if (!string.IsNullOrWhiteSpace(entry.RelativeStoragePath))
                {
                    NormalizeRelativePath(entry.RelativeStoragePath);
                }
            }
        }

        private static void ValidateCompletedFirstArtifacts(
            string sessionRoot,
            string agentId,
            string sessionName,
            bool requireSidecar)
        {
            string manifestPath = Path.Combine(sessionRoot, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException(
                    $"FIRST đang ở trạng thái chốt nhưng thiếu manifest.json: {sessionRoot}");
            }

            BackupSessionMetadataStore.ReadVerifiedSession(
                sessionRoot,
                manifestPath,
                agentId,
                sessionName,
                "FIRST",
                requireSidecar);
        }

        private static string CreateAvailableFirstSessionName(
            string storageRoot,
            string agentId,
            DateTime completedDate)
        {
            string safeAgent = SanitizeSessionName(agentId);
            string dateText = completedDate.ToString("yyyy-MM-dd");
            string standardName = $"FIRST-{safeAgent}-{dateText}";
            if (!Directory.Exists(GetSafeChildPath(storageRoot, standardName)))
            {
                return standardName;
            }

            for (int revision = 2; revision < 10_000; revision++)
            {
                string candidate = $"FIRST-{safeAgent}-R{revision}-{dateText}";
                if (!Directory.Exists(GetSafeChildPath(storageRoot, candidate)))
                {
                    return candidate;
                }
            }

            throw new IOException("Không thể tạo tên FIRST duy nhất cho ngày hoàn tất.");
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            return PathSafety.NormalizeRelativePath(relativePath);
        }

        private static string GetSafeChildPath(string root, string child)
        {
            return PathSafety.GetSafeChildPath(root, child);
        }

        private sealed class ActiveBackupSession : IDisposable
        {
            public string AgentID { get; }
            public string SessionName { get; }
            public string SessionRoot { get; }
            public string BackupType { get; }
            public bool IsResumableFirst { get; }
            public DateTime StartedAtUtc { get; }
            public bool AlreadyCompleted { get; }
            public SemaphoreSlim WriteLock { get; } = new SemaphoreSlim(1, 1);
            public Dictionary<string, BackupManifestEntry> ReceivedFiles { get; } =
                new Dictionary<string, BackupManifestEntry>(StringComparer.OrdinalIgnoreCase);

            public ActiveBackupSession(
                string agentId, string sessionName, string sessionRoot,
                string backupType, bool isResumableFirst, DateTime startedAtUtc, bool alreadyCompleted)
            {
                AgentID = agentId;
                SessionName = sessionName;
                SessionRoot = sessionRoot;
                BackupType = backupType;
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
