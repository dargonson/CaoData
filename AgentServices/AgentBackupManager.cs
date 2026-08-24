using AgentShared;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly string _appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        private readonly string _statePath;

        public AgentBackupManager(
            ILogger<Worker> logger,
            string agentId,
            Func<bool> isConnected,
            Func<SocketPacket, CancellationToken, Task> sendPacket,
            Func<BackupFileChunkHeader, byte[], int, CancellationToken, Task> sendChunk)
        {
            _logger = logger;
            _agentId = agentId;
            _isConnected = isConnected;
            _sendPacket = sendPacket;
            _sendChunk = sendChunk;

            string stateRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Intel",
                "Driver",
                "BackupState");
            _statePath = Path.Combine(stateRoot, SanitizeFileName(agentId) + ".json");
        }

        public async Task<BackupConfigAck> ApplyConfigurationAsync(string json, CancellationToken token)
        {
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
                catch { }
                return;
            }

            BackupSessionResult? result;
            try
            {
                result = JsonSerializer.Deserialize<BackupSessionResult>(json);
            }
            catch
            {
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

            bool isInitialFull = state.Inventory.Count == 0;
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
            if (isInitialFull && state.PendingFirstInventory.Count > 0)
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
            }

            // Khi scan có lỗi quyền truy cập, không kết luận file biến mất để tránh note xóa nhầm.
            if (!isInitialFull && scan.Errors.Count == 0)
            {
                deleted.AddRange(previous.Values.Where(old => !scan.Files.ContainsKey(old.FullPath)));
            }

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
                        PlannedFileCount = isInitialFull ? scan.Files.Count : 0,
                        PlannedTotalBytes = isInitialFull ? scan.Files.Values.Sum(file => file.Size) : 0
                    })
                }, token);

                BackupSessionResult ready = await WaitWithTimeoutAsync(readyCompletion.Task, TimeSpan.FromSeconds(30), token);
                if (!ready.Success)
                {
                    throw new InvalidOperationException(ready.Message);
                }

                HashSet<string> failedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IEnumerable<BackupFileSnapshot> filesToUpload = isInitialFull
                    ? scan.Files.Values
                    : created.Concat(modified);
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
                        continue;
                    }

                    try
                    {
                        if (isInitialFull)
                        {
                            bool uploaded = await SendResumableFirstFileAsync(sessionName, file, token);
                            if (!uploaded)
                            {
                                failedPaths.Add(file.FullPath);
                                state.PendingFirstSkippedFiles[file.FullPath] = "Control đã ghi nhận file này ở trạng thái Skipped.";
                                await SaveStateAsync(state, token);
                            }
                        }
                        else
                        {
                            await SendFileAsync(sessionName, file, token);
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
                            await SaveStateAsync(state, token);
                            await NotifyFirstFileSkippedAsync(sessionName, file, reason, token);
                        }
                        else
                        {
                            manifest.Errors.Add($"Không upload được {file.FullPath}: {ex.Message}");
                        }
                    }
                }

                // Chi dua file da upload thanh cong vao manifest/inventory cua Control.
                manifest.Created.RemoveAll(entry => failedPaths.Contains(entry.SourcePath));
                manifest.Modified.RemoveAll(entry => failedPaths.Contains(entry.SourcePath));

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

                Dictionary<string, BackupFileSnapshot> committedInventory = new Dictionary<string, BackupFileSnapshot>(
                    scan.Files,
                    StringComparer.OrdinalIgnoreCase);
                foreach (string failedPath in failedPaths)
                {
                    if (previous.TryGetValue(failedPath, out BackupFileSnapshot? old))
                    {
                        committedInventory[failedPath] = old;
                    }
                    else
                    {
                        committedInventory.Remove(failedPath);
                    }
                }

                state.Inventory = committedInventory;
                state.LastSuccessfulBackupUtc = DateTime.UtcNow;
                if (isInitialFull || createSyntheticFull)
                {
                    state.LastFullBackupUtc = state.LastSuccessfulBackupUtc;
                }
                if (isInitialFull)
                {
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
                if (!info.Completed)
                {
                    await SendFileAsync(sessionName, transferSnapshot, token, info.Offset);
                }

                FileInfo after = new FileInfo(plannedSnapshot.FullPath);
                if (!after.Exists || after.Length != transferSnapshot.Size ||
                    after.LastWriteTimeUtc != transferSnapshot.LastWriteTimeUtc)
                {
                    throw new IOException("File thay đổi trong lúc upload FIRST; sẽ truyền lại file này ở lần resume.");
                }
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
            CancellationToken token,
            long startOffset = 0)
        {
            using FileStream source = new FileStream(
                snapshot.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileChunkSize,
                useAsync: true);

            long offset = Math.Clamp(startOffset, 0, snapshot.Size);
            source.Seek(offset, SeekOrigin.Begin);
            long remaining = snapshot.Size - offset;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(FileChunkSize);
            try
            {
                if (remaining == 0)
                {
                    await _sendChunk(CreateChunkHeader(sessionName, snapshot, offset, true), buffer, 0, token);
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
                    await _sendChunk(CreateChunkHeader(sessionName, snapshot, offset, isLast), buffer, read, token);
                    offset += read;
                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
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
                LastWriteTimeUtc = snapshot.LastWriteTimeUtc
            };
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
                string tempPath = _appSettingsPath + ".backup.tmp";
                await File.WriteAllTextAsync(
                    tempPath,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                    token);
                File.Move(tempPath, _appSettingsPath, overwrite: true);
            }
            finally
            {
                _configLock.Release();
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

                string json = await File.ReadAllTextAsync(_statePath, token);
                BackupAgentState state = JsonSerializer.Deserialize<BackupAgentState>(json) ?? new BackupAgentState();
                state.Inventory = new Dictionary<string, BackupFileSnapshot>(state.Inventory, StringComparer.OrdinalIgnoreCase);
                state.PendingFirstInventory = new Dictionary<string, BackupFileSnapshot>(
                    state.PendingFirstInventory ?? new Dictionary<string, BackupFileSnapshot>(),
                    StringComparer.OrdinalIgnoreCase);
                state.PendingFirstSkippedFiles = new Dictionary<string, string>(
                    state.PendingFirstSkippedFiles ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase);
                return state;
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
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(tempPath, json, token);
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
            if (!TimeSpan.TryParse(config.BackupTime, out _))
            {
                throw new InvalidDataException("Giờ backup không hợp lệ.");
            }

            config.ExcludedFolders ??= new List<string>();
            config.ExcludedPatterns ??= new List<string>();
        }

        private static BackupManifestEntry ToManifestEntry(BackupFileSnapshot snapshot)
        {
            return new BackupManifestEntry
            {
                SourcePath = snapshot.FullPath,
                RelativeStoragePath = snapshot.RelativeStoragePath,
                Size = snapshot.Size,
                LastWriteTimeUtc = snapshot.LastWriteTimeUtc
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
