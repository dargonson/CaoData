using AgentShared;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgentService
{
    internal sealed class AgentBackupManager
    {
        // BO SUNG MODULE BACKUP - FIRST RESUME: chunk lon hon de giam frame/open-file khi truyen hang tram GB.
        private const int FileChunkSize = 4 * 1024 * 1024;
        private readonly ILogger<Worker> _logger;
        private readonly string _agentId;
        private readonly Func<bool> _isConnected;
        private readonly Func<SocketPacket, CancellationToken, Task> _sendPacket;
        private readonly Func<BackupFileChunkHeader, byte[], int, CancellationToken, Task> _sendChunk;
        private readonly BackupFileScanner _scanner = new BackupFileScanner();
        private readonly SemaphoreSlim _configLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _runLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<BackupSessionResult>> _pendingReady =
            new ConcurrentDictionary<string, TaskCompletionSource<BackupSessionResult>>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<BackupSessionResult>> _pendingResults =
            new ConcurrentDictionary<string, TaskCompletionSource<BackupSessionResult>>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<BackupFirstFileResumeInfo>> _pendingFirstResume =
            new ConcurrentDictionary<string, TaskCompletionSource<BackupFirstFileResumeInfo>>(StringComparer.OrdinalIgnoreCase);
        private readonly string _appSettingsPath;
        private readonly string _statePath;

        public AgentBackupManager(
            ILogger<Worker> logger,
            string agentId,
            Func<bool> isConnected,
            Func<SocketPacket, CancellationToken, Task> sendPacket,
            Func<BackupFileChunkHeader, byte[], int, CancellationToken, Task> sendChunk,
            string? appSettingsPath = null,
            string? statePath = null)
        {
            _logger = logger;
            _agentId = agentId;
            _isConnected = isConnected;
            _sendPacket = sendPacket;
            _sendChunk = sendChunk;

            _appSettingsPath = appSettingsPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            _statePath = statePath ?? AgentDataPaths.GetBackupStatePath(SanitizeFileName(agentId));
        }

        public async Task<BackupConfigAck> ApplyConfigurationAsync(string json, CancellationToken token)
        {
            if (!await _runLock.WaitAsync(0, token))
            {
                return new BackupConfigAck
                {
                    Success = false,
                    Message = "Agent đang backup, chưa thể sửa cấu hình."
                };
            }

            try
            {
                BackupConfiguration? config = JsonSerializer.Deserialize<BackupConfiguration>(json);
                if (config == null)
                {
                    throw new InvalidDataException("Không đọc được cấu hình backup.");
                }

                ValidateConfiguration(config);
                config.AgentID = _agentId;
                config.UpdatedAtUtc = DateTime.UtcNow;

                await SaveConfigurationToAppSettingsAsync(config, token);
                _logger.LogInformation("Đã lưu cấu hình backup vào appsettings.json.");
                return new BackupConfigAck
                {
                    Success = true,
                    Message = "Agent đã lưu cấu hình backup thành công."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("Không thể lưu cấu hình backup: {Message}", ex.Message);
                return new BackupConfigAck { Success = false, Message = ex.Message };
            }
            finally
            {
                _runLock.Release();
            }
        }

        public async Task<BackupConfigAck> DeleteConfigurationAsync(string json, CancellationToken token)
        {
            if (!await _runLock.WaitAsync(0, token))
            {
                return new BackupConfigAck
                {
                    Success = false,
                    Message = "Agent đang backup, chưa thể xoá cấu hình."
                };
            }

            byte[]? originalAppSettings = null;
            bool appSettingsExisted = File.Exists(_appSettingsPath);
            bool configurationRemoved = false;
            try
            {
                BackupConfigDeleteRequest? request = JsonSerializer.Deserialize<BackupConfigDeleteRequest>(json);
                if (request == null ||
                    !request.AgentID.Equals(_agentId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Yêu cầu xoá cấu hình backup không hợp lệ.");
                }

                if (appSettingsExisted)
                {
                    originalAppSettings = await File.ReadAllBytesAsync(_appSettingsPath, token);
                }
                await RemoveConfigurationFromAppSettingsAsync(token);
                configurationRemoved = true;
                DeleteRuntimeState();
                _logger.LogInformation("Đã xoá cấu hình và trạng thái runtime backup.");
                return new BackupConfigAck
                {
                    Success = true,
                    Message = "Agent đã xoá cấu hình backup thành công."
                };
            }
            catch (Exception ex)
            {
                if (configurationRemoved)
                {
                    try
                    {
                        if (appSettingsExisted && originalAppSettings != null)
                        {
                            await WriteAppSettingsBytesAsync(originalAppSettings, CancellationToken.None);
                        }
                        else if (File.Exists(_appSettingsPath))
                        {
                            File.Delete(_appSettingsPath);
                        }
                    }
                    catch (Exception rollbackError)
                    {
                        _logger.LogCritical(
                            "Không thể phục hồi appsettings sau lỗi xoá state backup: {Message}",
                            rollbackError.Message);
                    }
                }
                _logger.LogError("Không thể xoá cấu hình backup: {Message}", ex.Message);
                return new BackupConfigAck { Success = false, Message = ex.Message };
            }
            finally
            {
                _runLock.Release();
            }
        }

        public void HandleSessionSignal(string packetType, string json)
        {
            if (packetType == BackupPacketTypes.FirstFileResumeInfo)
            {
                try
                {
                    BackupFirstFileResumeInfo? info = JsonSerializer.Deserialize<BackupFirstFileResumeInfo>(json);
                    if (info != null &&
                        _pendingFirstResume.TryGetValue(info.SourcePath, out TaskCompletionSource<BackupFirstFileResumeInfo>? pending))
                    {
                        pending.TrySetResult(info);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Không đọc được tín hiệu resume FIRST: {Message}", ex.Message);
                }
                return;
            }

            BackupSessionResult? result;
            try
            {
                result = JsonSerializer.Deserialize<BackupSessionResult>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Không đọc được tín hiệu phiên backup: {Message}", ex.Message);
                return;
            }

            if (result == null || string.IsNullOrWhiteSpace(result.SessionName))
            {
                return;
            }

            if (packetType == BackupPacketTypes.SessionReady &&
                _pendingReady.TryGetValue(result.SessionName, out TaskCompletionSource<BackupSessionResult>? ready))
            {
                ready.TrySetResult(result);
            }
            else if (packetType == BackupPacketTypes.SessionResult &&
                     _pendingResults.TryGetValue(result.SessionName, out TaskCompletionSource<BackupSessionResult>? completion))
            {
                completion.TrySetResult(result);
            }
        }

        public async Task RunSchedulerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    BackupConfiguration? config = await LoadConfigurationAsync(token);
                    if (config != null && config.Enabled && _isConnected())
                    {
                        ValidateConfiguration(config);
                        BackupAgentState state = await LoadStateAsync(token);
                        if (IsBackupDue(config, state, DateTime.Now))
                        {
                            await RunBackupIfIdleAsync(config, state, token);
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Lỗi scheduler backup: {Message}", ex.Message);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RunBackupIfIdleAsync(BackupConfiguration config, BackupAgentState state, CancellationToken token)
        {
            if (!await _runLock.WaitAsync(0, token))
            {
                return;
            }

            try
            {
                await RunBackupAsync(config, state, token);
            }
            finally
            {
                _runLock.Release();
            }
        }

        private async Task RunBackupAsync(BackupConfiguration config, BackupAgentState state, CancellationToken token)
        {
            using IDisposable sleepBlock = SystemSleepBlocker.PreventSystemSleep(
                "AgentServices đang thực hiện backup dữ liệu lên AgentControl.");

            bool isInitialFull = !state.InitialBackupCompleted;
            bool createSyntheticFull = !isInitialFull &&
                                       (state.LastFullBackupUtc == null ||
                                        (DateTime.UtcNow - state.LastFullBackupUtc.Value).TotalDays >= config.FullBackupPeriodDays);
            string backupType = isInitialFull ? "FIRST" : "INC";
            string sessionName = isInitialFull
                ? $"FIRST-{SanitizeFileName(_agentId)}"
                : $"INC-{SanitizeFileName(_agentId)}-{DateTime.Now:yyyy-MM-dd}";
            DateTime startedAtUtc = isInitialFull
                ? state.FirstStartedAtUtc ?? DateTime.UtcNow
                : DateTime.UtcNow;

            _logger.LogInformation("Bắt đầu quét dữ liệu cho phiên {SessionName}.", sessionName);
            BackupScanResult scan;
            if (isInitialFull && state.PendingFirstPlanInitialized)
            {
                scan = new BackupScanResult();
                foreach ((string path, BackupFileSnapshot snapshot) in state.PendingFirstInventory)
                {
                    scan.Files[path] = snapshot;
                }
                _logger.LogInformation("Tiếp tục FIRST đã lưu: {Count} file.", scan.Files.Count);
            }
            else
            {
                scan = await Task.Run(() => _scanner.Scan(config), token);
                if (isInitialFull)
                {
                    state.FirstStartedAtUtc = startedAtUtc;
                    state.PendingFirstPlanInitialized = true;
                    state.PendingFirstInventory = new Dictionary<string, BackupFileSnapshot>(
                        scan.Files, StringComparer.OrdinalIgnoreCase);
                    await SaveStateAsync(state, token);
                }
            }
            Dictionary<string, BackupFileSnapshot> previous = new Dictionary<string, BackupFileSnapshot>(
                state.Inventory,
                StringComparer.OrdinalIgnoreCase);

            List<BackupFileSnapshot> created = new List<BackupFileSnapshot>();
            List<BackupFileSnapshot> modified = new List<BackupFileSnapshot>();
            List<BackupFileSnapshot> deleted = new List<BackupFileSnapshot>();

            foreach (BackupFileSnapshot current in scan.Files.Values)
            {
                if (isInitialFull)
                {
                    continue;
                }
                if (!previous.TryGetValue(current.FullPath, out BackupFileSnapshot? old))
                {
                    created.Add(current);
                }
                else if (old.Size != current.Size || old.LastWriteTimeUtc != current.LastWriteTimeUtc)
                {
                    modified.Add(current);
                }
                else
                {
                    current.ContentSha256 = old.ContentSha256;
                }
            }

            // Khi scan có lỗi quyền truy cập, không kết luận file biến mất để tránh note xóa nhầm.
            if (!isInitialFull && scan.Errors.Count == 0)
            {
                deleted.AddRange(previous.Values.Where(old => !scan.Files.ContainsKey(old.FullPath)));
            }

            List<BackupFileSnapshot> filesToUpload = (isInitialFull
                    ? scan.Files.Values
                    : created.Concat(modified))
                .ToList();
            long plannedTotalBytes = filesToUpload.Sum(file => file.Size);
            var progress = new BackupProgressTracker(
                sessionName,
                backupType,
                startedAtUtc,
                filesToUpload.Count,
                plannedTotalBytes);

            BackupManifest manifest = new BackupManifest
            {
                AgentID = _agentId,
                SessionName = sessionName,
                BackupType = backupType,
                StartedAtUtc = startedAtUtc,
                CreateSyntheticFull = createSyntheticFull,
                IsResumableFirst = isInitialFull,
                Created = isInitialFull ? new List<BackupManifestEntry>() : created.Select(ToManifestEntry).ToList(),
                Modified = modified.Select(ToManifestEntry).ToList(),
                Deleted = deleted.Select(ToManifestEntry).ToList(),
                Errors = new List<string>(scan.Errors)
            };

            TaskCompletionSource<BackupSessionResult> readyCompletion =
                new TaskCompletionSource<BackupSessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<BackupSessionResult> resultCompletion =
                new TaskCompletionSource<BackupSessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingReady[sessionName] = readyCompletion;
            _pendingResults[sessionName] = resultCompletion;

            try
            {
                await _sendPacket(new SocketPacket
                {
                    Type = BackupPacketTypes.SessionBegin,
                    AgentID = _agentId,
                    Data = JsonSerializer.Serialize(new BackupSessionBegin
                    {
                        AgentID = _agentId,
                        SessionName = sessionName,
                        BackupType = backupType,
                        StartedAtUtc = startedAtUtc,
                        IsResumableFirst = isInitialFull,
                        PlannedFileCount = progress.PlannedFileCount,
                        PlannedTotalBytes = progress.PlannedTotalBytes
                    })
                }, token);

                BackupSessionResult ready = await WaitWithTimeoutAsync(readyCompletion.Task, TimeSpan.FromSeconds(30), token);
                if (!ready.Success)
                {
                    throw new InvalidOperationException(ready.Message);
                }

                HashSet<string> failedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int skippedSinceStateCheckpoint = 0;
                foreach (BackupFileSnapshot file in filesToUpload)
                {
                    if (!_isConnected())
                    {
                        throw new IOException("Mất kết nối Control trong khi đang backup.");
                    }

                    if (isInitialFull && state.PendingFirstSkippedFiles.TryGetValue(file.FullPath, out string? savedReason))
                    {
                        failedPaths.Add(file.FullPath);
                        await NotifyFirstFileSkippedAsync(sessionName, file, savedReason, token);
                        await MarkBackupFileProcessedAsync(progress, file, token);
                        continue;
                    }

                    try
                    {
                        if (isInitialFull)
                        {
                            bool uploaded = await SendResumableFirstFileAsync(sessionName, file, progress, token);
                            if (!uploaded)
                            {
                                failedPaths.Add(file.FullPath);
                                state.PendingFirstSkippedFiles[file.FullPath] = "Control đã ghi nhận file này ở trạng thái Skipped.";
                                skippedSinceStateCheckpoint++;
                                if (skippedSinceStateCheckpoint >= 100)
                                {
                                    await SaveStateAsync(state, token);
                                    skippedSinceStateCheckpoint = 0;
                                }
                            }
                        }
                        else
                        {
                            await SendFileAsync(sessionName, file, progress, token);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!_isConnected())
                        {
                            throw new IOException("Mất kết nối Control trong khi đang backup; FIRST sẽ resume.", ex);
                        }
                        failedPaths.Add(file.FullPath);
                        if (isInitialFull)
                        {
                            string reason = ex.Message;
                            state.PendingFirstSkippedFiles[file.FullPath] = reason;
                            await NotifyFirstFileSkippedAsync(sessionName, file, reason, token);
                            skippedSinceStateCheckpoint++;
                            if (skippedSinceStateCheckpoint >= 100)
                            {
                                await SaveStateAsync(state, token);
                                skippedSinceStateCheckpoint = 0;
                            }
                        }
                        else
                        {
                            manifest.Errors.Add($"Không upload được {file.FullPath}: {ex.Message}");
                        }
                    }

                    await MarkBackupFileProcessedAsync(progress, file, token);
                }

                await SendBackupProgressAsync(progress, string.Empty, progress.ProcessedBytes, token, force: true);

                // Tao entry sau upload de manifest mang SHA-256 da tinh tren dung noi dung da gui.
                manifest.Created = isInitialFull
                    ? new List<BackupManifestEntry>()
                    : created.Where(file => !failedPaths.Contains(file.FullPath)).Select(ToManifestEntry).ToList();
                manifest.Modified = modified
                    .Where(file => !failedPaths.Contains(file.FullPath))
                    .Select(ToManifestEntry)
                    .ToList();

                manifest.CompletedAtUtc = DateTime.UtcNow;
                await _sendPacket(new SocketPacket
                {
                    Type = BackupPacketTypes.SessionComplete,
                    AgentID = _agentId,
                    Data = JsonSerializer.Serialize(manifest)
                }, token);

                TimeSpan completionTimeout = createSyntheticFull || isInitialFull
                    ? TimeSpan.FromHours(12)
                    : TimeSpan.FromSeconds(30);
                BackupSessionResult result = await WaitWithTimeoutAsync(resultCompletion.Task, completionTimeout, token);
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.Message);
                }

                state.Inventory = BackupInventoryCommitter.Build(
                    scan.Files,
                    previous,
                    failedPaths,
                    scan.Errors.Count > 0);
                state.LastSuccessfulBackupUtc = DateTime.UtcNow;
                if (isInitialFull || (createSyntheticFull && result.SyntheticFullCompleted))
                {
                    state.LastFullBackupUtc = state.LastSuccessfulBackupUtc;
                }
                if (createSyntheticFull && !result.SyntheticFullCompleted)
                {
                    _logger.LogWarning(
                        "Phiên {SessionName} đã chốt INC nhưng Synthetic Full chưa hoàn tất: {Message}",
                        sessionName,
                        result.Message);
                }
                if (isInitialFull)
                {
                    state.InitialBackupCompleted = true;
                    state.PendingFirstPlanInitialized = false;
                    state.PendingFirstInventory.Clear();
                    state.PendingFirstSkippedFiles.Clear();
                    state.FirstStartedAtUtc = null;
                }
                await SaveStateAsync(state, token);

                _logger.LogInformation(
                    "Hoàn tất {SessionName}: mới {Created}, sửa {Modified}, xóa {Deleted}, lỗi {Errors}.",
                    sessionName,
                    manifest.Created.Count,
                    manifest.Modified.Count,
                    manifest.Deleted.Count,
                    manifest.Errors.Count);
            }
            finally
            {
                _pendingReady.TryRemove(sessionName, out _);
                _pendingResults.TryRemove(sessionName, out _);
            }
        }

        private async Task<bool> SendResumableFirstFileAsync(
            string sessionName,
            BackupFileSnapshot plannedSnapshot,
            BackupProgressTracker progress,
            CancellationToken token)
        {
            FileInfo before = new FileInfo(plannedSnapshot.FullPath);
            if (!before.Exists)
            {
                throw new FileNotFoundException("File trong kế hoạch FIRST không còn tồn tại.", plannedSnapshot.FullPath);
            }

            BackupFileSnapshot transferSnapshot = new BackupFileSnapshot
            {
                FullPath = plannedSnapshot.FullPath,
                RelativeStoragePath = plannedSnapshot.RelativeStoragePath,
                Size = before.Length,
                LastWriteTimeUtc = before.LastWriteTimeUtc
            };

            TaskCompletionSource<BackupFirstFileResumeInfo> completion =
                new TaskCompletionSource<BackupFirstFileResumeInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingFirstResume[transferSnapshot.FullPath] = completion;
            try
            {
                await _sendPacket(new SocketPacket
                {
                    Type = BackupPacketTypes.FirstFileResumeQuery,
                    AgentID = _agentId,
                    Data = JsonSerializer.Serialize(new BackupFirstFileResumeQuery
                    {
                        AgentID = _agentId,
                        SessionName = sessionName,
                        SourcePath = transferSnapshot.FullPath,
                        RelativeStoragePath = transferSnapshot.RelativeStoragePath,
                        TotalBytes = transferSnapshot.Size,
                        LastWriteTimeUtc = transferSnapshot.LastWriteTimeUtc
                    })
                }, token);

                BackupFirstFileResumeInfo info = await WaitWithTimeoutAsync(
                    completion.Task, TimeSpan.FromSeconds(30), token);
                if (!info.Success)
                {
                    throw new IOException(info.Message);
                }
                if (info.Skipped)
                {
                    return false;
                }
                await ReportBackupFilePositionAsync(
                    progress,
                    transferSnapshot,
                    info.Completed ? transferSnapshot.Size : info.Offset,
                    0,
                    token);
                if (!info.Completed)
                {
                    await SendFileAsync(sessionName, transferSnapshot, progress, token, info.Offset);
                }
                else
                {
                    transferSnapshot.ContentSha256 = info.ContentSha256;
                }

                FileInfo after = new FileInfo(plannedSnapshot.FullPath);
                if (!after.Exists || after.Length != transferSnapshot.Size ||
                    after.LastWriteTimeUtc != transferSnapshot.LastWriteTimeUtc)
                {
                    throw new IOException("File thay đổi trong lúc upload FIRST; sẽ truyền lại file này ở lần resume.");
                }
                plannedSnapshot.ContentSha256 = transferSnapshot.ContentSha256;
                return true;
            }
            finally
            {
                _pendingFirstResume.TryRemove(transferSnapshot.FullPath, out _);
            }
        }

        private async Task NotifyFirstFileSkippedAsync(
            string sessionName,
            BackupFileSnapshot snapshot,
            string reason,
            CancellationToken token)
        {
            await _sendPacket(new SocketPacket
            {
                Type = BackupPacketTypes.FirstFileSkip,
                AgentID = _agentId,
                Data = JsonSerializer.Serialize(new BackupFirstFileSkip
                {
                    AgentID = _agentId,
                    SessionName = sessionName,
                    SourcePath = snapshot.FullPath,
                    RelativeStoragePath = snapshot.RelativeStoragePath,
                    Size = snapshot.Size,
                    LastWriteTimeUtc = snapshot.LastWriteTimeUtc,
                    Reason = reason ?? string.Empty
                })
            }, token);
        }

        private async Task SendFileAsync(
            string sessionName,
            BackupFileSnapshot snapshot,
            BackupProgressTracker progress,
            CancellationToken token,
            long startOffset = 0)
        {
            using FileStream source = new FileStream(
                snapshot.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (source.Length != snapshot.Size ||
                File.GetLastWriteTimeUtc(snapshot.FullPath) != snapshot.LastWriteTimeUtc)
            {
                throw new IOException("File đã thay đổi sau lúc quét; sẽ xử lý ở lần backup kế tiếp.");
            }

            long offset = Math.Clamp(startOffset, 0, snapshot.Size);
            long remaining = snapshot.Size - offset;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(FileChunkSize);
            using IncrementalHash contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try
            {
                // Resume van bam lai prefix tren Agent (khong gui) de hash cuoi dai dien toan bo file.
                source.Seek(0, SeekOrigin.Begin);
                long hashedPrefix = 0;
                while (hashedPrefix < offset)
                {
                    int requested = (int)Math.Min(buffer.Length, offset - hashedPrefix);
                    int read = await source.ReadAsync(buffer.AsMemory(0, requested), token);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("File kết thúc trước mốc resume backup.");
                    }
                    contentHash.AppendData(buffer, 0, read);
                    hashedPrefix += read;
                }

                if (remaining == 0)
                {
                    snapshot.ContentSha256 = Convert.ToHexString(contentHash.GetHashAndReset());
                    await _sendChunk(CreateChunkHeader(sessionName, snapshot, offset, true), buffer, 0, token);
                    await ReportBackupFilePositionAsync(progress, snapshot, offset, 0, token);
                    return;
                }

                while (remaining > 0)
                {
                    int requested = (int)Math.Min(buffer.Length, remaining);
                    int read = await source.ReadAsync(buffer.AsMemory(0, requested), token);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("File thay đổi kích thước trong lúc backup.");
                    }

                    bool isLast = offset + read >= snapshot.Size;
                    contentHash.AppendData(buffer, 0, read);
                    if (isLast)
                    {
                        snapshot.ContentSha256 = Convert.ToHexString(contentHash.GetHashAndReset());
                    }
                    await _sendChunk(CreateChunkHeader(sessionName, snapshot, offset, isLast), buffer, read, token);
                    offset += read;
                    remaining -= read;
                    await ReportBackupFilePositionAsync(progress, snapshot, offset, read, token);
                }

                if (source.Length != snapshot.Size ||
                    File.GetLastWriteTimeUtc(snapshot.FullPath) != snapshot.LastWriteTimeUtc)
                {
                    throw new IOException("File thay đổi trong lúc backup; không chốt phiên này.");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task ReportBackupFilePositionAsync(
            BackupProgressTracker progress,
            BackupFileSnapshot snapshot,
            long filePosition,
            int transferredChunkBytes,
            CancellationToken token)
        {
            progress.TransferredBytes += Math.Max(0, transferredChunkBytes);
            long position = Math.Clamp(filePosition, 0, snapshot.Size);
            long processedBytes = Math.Min(
                progress.PlannedTotalBytes,
                progress.ProcessedBytes + position);
            await SendBackupProgressAsync(progress, snapshot.FullPath, processedBytes, token);
        }

        private async Task MarkBackupFileProcessedAsync(
            BackupProgressTracker progress,
            BackupFileSnapshot snapshot,
            CancellationToken token)
        {
            progress.ProcessedBytes = Math.Min(
                progress.PlannedTotalBytes,
                progress.ProcessedBytes + Math.Max(0, snapshot.Size));
            progress.ProcessedFileCount = Math.Min(
                progress.PlannedFileCount,
                progress.ProcessedFileCount + 1);
            await SendBackupProgressAsync(progress, snapshot.FullPath, progress.ProcessedBytes, token);
        }

        private async Task SendBackupProgressAsync(
            BackupProgressTracker progress,
            string currentFile,
            long processedBytes,
            CancellationToken token,
            bool force = false)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (!force &&
                progress.LastReportUtc.HasValue &&
                nowUtc - progress.LastReportUtc.Value < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            int percentage = BackupProgressCalculator.CalculatePercentage(
                progress.PlannedFileCount,
                progress.ProcessedFileCount,
                progress.PlannedTotalBytes,
                processedBytes);

            await _sendPacket(new SocketPacket
            {
                Type = BackupPacketTypes.Progress,
                AgentID = _agentId,
                Data = JsonSerializer.Serialize(new BackupProgressUpdate
                {
                    AgentID = _agentId,
                    SessionName = progress.SessionName,
                    BackupType = progress.BackupType,
                    StartedAtUtc = progress.StartedAtUtc,
                    PlannedFileCount = progress.PlannedFileCount,
                    ProcessedFileCount = progress.ProcessedFileCount,
                    PlannedTotalBytes = progress.PlannedTotalBytes,
                    ProcessedBytes = processedBytes,
                    TransferredBytes = progress.TransferredBytes,
                    ProgressPercentage = percentage,
                    CurrentFile = currentFile ?? string.Empty
                })
            }, token);
            progress.LastReportUtc = nowUtc;
        }

        private BackupFileChunkHeader CreateChunkHeader(
            string sessionName,
            BackupFileSnapshot snapshot,
            long offset,
            bool isLast)
        {
            return new BackupFileChunkHeader
            {
                AgentID = _agentId,
                SessionName = sessionName,
                SourcePath = snapshot.FullPath,
                RelativeStoragePath = snapshot.RelativeStoragePath,
                TotalBytes = snapshot.Size,
                Offset = offset,
                IsLastChunk = isLast,
                LastWriteTimeUtc = snapshot.LastWriteTimeUtc,
                ContentSha256 = isLast ? snapshot.ContentSha256 : string.Empty
            };
        }

        private sealed class BackupProgressTracker
        {
            public string SessionName { get; }
            public string BackupType { get; }
            public DateTime StartedAtUtc { get; }
            public long PlannedFileCount { get; }
            public long PlannedTotalBytes { get; }
            public long ProcessedFileCount { get; set; }
            public long ProcessedBytes { get; set; }
            public long TransferredBytes { get; set; }
            public DateTime? LastReportUtc { get; set; }

            public BackupProgressTracker(
                string sessionName,
                string backupType,
                DateTime startedAtUtc,
                long plannedFileCount,
                long plannedTotalBytes)
            {
                SessionName = sessionName;
                BackupType = backupType;
                StartedAtUtc = startedAtUtc;
                PlannedFileCount = Math.Max(0, plannedFileCount);
                PlannedTotalBytes = Math.Max(0, plannedTotalBytes);
            }
        }

        private async Task SaveConfigurationToAppSettingsAsync(BackupConfiguration config, CancellationToken token)
        {
            await _configLock.WaitAsync(token);
            try
            {
                JsonObject root;
                if (File.Exists(_appSettingsPath))
                {
                    string json = await File.ReadAllTextAsync(_appSettingsPath, token);
                    root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                root["BackupConfig"] = JsonSerializer.SerializeToNode(config);
                await WriteAppSettingsRootAsync(root, token);
            }
            finally
            {
                _configLock.Release();
            }
        }

        private async Task RemoveConfigurationFromAppSettingsAsync(CancellationToken token)
        {
            await _configLock.WaitAsync(token);
            try
            {
                JsonObject root;
                if (File.Exists(_appSettingsPath))
                {
                    string json = await File.ReadAllTextAsync(_appSettingsPath, token);
                    root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                root.Remove("BackupConfig");
                await WriteAppSettingsRootAsync(root, token);
            }
            finally
            {
                _configLock.Release();
            }
        }

        private async Task WriteAppSettingsRootAsync(JsonObject root, CancellationToken token)
        {
            string? folder = Path.GetDirectoryName(_appSettingsPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            byte[] payload = System.Text.Encoding.UTF8.GetBytes(
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            await WriteAppSettingsBytesAsync(payload, token);
        }

        private async Task WriteAppSettingsBytesAsync(byte[] payload, CancellationToken token)
        {
            string tempPath = _appSettingsPath + ".backup.tmp";
            await using (FileStream destination = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await destination.WriteAsync(payload, token);
                await destination.FlushAsync(token);
                destination.Flush(flushToDisk: true);
            }
            File.Move(tempPath, _appSettingsPath, overwrite: true);
        }

        private void DeleteRuntimeState()
        {
            if (File.Exists(_statePath))
            {
                File.Delete(_statePath);
            }

            string tempPath = _statePath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        private async Task<BackupConfiguration?> LoadConfigurationAsync(CancellationToken token)
        {
            await _configLock.WaitAsync(token);
            try
            {
                if (!File.Exists(_appSettingsPath))
                {
                    return null;
                }

                string json = await File.ReadAllTextAsync(_appSettingsPath, token);
                JsonNode? node = JsonNode.Parse(json)?["BackupConfig"];
                BackupConfiguration? config = node?.Deserialize<BackupConfiguration>();
                if (config != null)
                {
                    config.AgentID = _agentId;
                }
                return config;
            }
            finally
            {
                _configLock.Release();
            }
        }

        private async Task<BackupAgentState> LoadStateAsync(CancellationToken token)
        {
            try
            {
                if (!File.Exists(_statePath))
                {
                    return new BackupAgentState();
                }

                await using FileStream source = new FileStream(
                    _statePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                BackupAgentState state = await JsonSerializer.DeserializeAsync<BackupAgentState>(
                    source,
                    cancellationToken: token) ?? new BackupAgentState();
                state.Inventory = new Dictionary<string, BackupFileSnapshot>(state.Inventory, StringComparer.OrdinalIgnoreCase);
                state.PendingFirstInventory = new Dictionary<string, BackupFileSnapshot>(
                    state.PendingFirstInventory ?? new Dictionary<string, BackupFileSnapshot>(),
                    StringComparer.OrdinalIgnoreCase);
                state.PendingFirstSkippedFiles = new Dictionary<string, string>(
                    state.PendingFirstSkippedFiles ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase);
                // Tuong thich state cua cac ban truoc khi co hai co nay.
                state.InitialBackupCompleted = state.InitialBackupCompleted ||
                                               state.LastSuccessfulBackupUtc.HasValue ||
                                               state.Inventory.Count > 0;
                state.PendingFirstPlanInitialized = state.PendingFirstPlanInitialized ||
                                                    (!state.InitialBackupCompleted &&
                                                     (state.FirstStartedAtUtc.HasValue ||
                                                      state.PendingFirstInventory.Count > 0));
                return state;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Không đọc được trạng thái backup cũ, sẽ tạo FULL mới: {Message}", ex.Message);
                return new BackupAgentState();
            }
        }

        private async Task SaveStateAsync(BackupAgentState state, CancellationToken token)
        {
            string? folder = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string tempPath = _statePath + ".tmp";
            await using (FileStream destination = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(destination, state, cancellationToken: token);
                await destination.FlushAsync(token);
                destination.Flush(flushToDisk: true);
            }
            File.Move(tempPath, _statePath, overwrite: true);
        }

        private static bool IsBackupDue(BackupConfiguration config, BackupAgentState state, DateTime nowLocal)
        {
            if (!TimeSpan.TryParse(config.BackupTime, out TimeSpan scheduledTime) || nowLocal.TimeOfDay < scheduledTime)
            {
                return false;
            }

            if (state.LastSuccessfulBackupUtc == null)
            {
                return true;
            }

            DateTime lastLocalDate = state.LastSuccessfulBackupUtc.Value.ToLocalTime().Date;
            int elapsedDays = (nowLocal.Date - lastLocalDate).Days;
            return elapsedDays >= Math.Max(1, config.BackupIntervalDays);
        }

        private static void ValidateConfiguration(BackupConfiguration config)
        {
            if (config.SourcePaths == null || config.SourcePaths.Count == 0)
            {
                throw new InvalidDataException("Cấu hình chưa có nguồn backup.");
            }
            if (config.BackupIntervalDays < 1 || config.FullBackupPeriodDays < 1)
            {
                throw new InvalidDataException("Chu kỳ backup không hợp lệ.");
            }
            if (!TimeSpan.TryParse(config.BackupTime, out TimeSpan backupTime) ||
                backupTime < TimeSpan.Zero || backupTime >= TimeSpan.FromDays(1))
            {
                throw new InvalidDataException("Giờ backup không hợp lệ.");
            }

            BackupExclusionDefaults.EnsureIncluded(config);
        }

        private static BackupManifestEntry ToManifestEntry(BackupFileSnapshot snapshot)
        {
            return new BackupManifestEntry
            {
                SourcePath = snapshot.FullPath,
                RelativeStoragePath = snapshot.RelativeStoragePath,
                Size = snapshot.Size,
                LastWriteTimeUtc = snapshot.LastWriteTimeUtc,
                ContentSha256 = snapshot.ContentSha256
            };
        }

        private static async Task<T> WaitWithTimeoutAsync<T>(Task<T> task, TimeSpan timeout, CancellationToken token)
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task delay = Task.Delay(timeout, timeoutCts.Token);
            Task completed = await Task.WhenAny(task, delay);
            if (completed != task)
            {
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("Control không phản hồi phiên backup đúng thời gian.");
            }

            timeoutCts.Cancel();
            return await task;
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
    }

    internal sealed class BackupAgentState
    {
        public bool InitialBackupCompleted { get; set; }
        public bool PendingFirstPlanInitialized { get; set; }
        public DateTime? LastSuccessfulBackupUtc { get; set; }
        public DateTime? LastFullBackupUtc { get; set; }
        public DateTime? FirstStartedAtUtc { get; set; }
        public Dictionary<string, BackupFileSnapshot> PendingFirstInventory { get; set; } =
            new Dictionary<string, BackupFileSnapshot>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PendingFirstSkippedFiles { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BackupFileSnapshot> Inventory { get; set; } =
            new Dictionary<string, BackupFileSnapshot>(StringComparer.OrdinalIgnoreCase);
    }
}
