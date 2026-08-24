using AgentShared;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AgentControl
{
    public partial class frmToolBackup
    {
        private readonly Dictionary<string, BackupDashboardAgentState> _backupDashboardStates =
            new Dictionary<string, BackupDashboardAgentState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DataGridViewRow> _backupDashboardRows =
            new Dictionary<string, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, BackupDashboardSnapshot> _latestBackupDashboardSnapshots =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _lastBackupDashboardSnapshotWrites =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan BackupDashboardSnapshotWriteInterval = TimeSpan.FromSeconds(1);
        private void InitializeBackupDashboardModule()
        {
            typeof(DataGridView)
                .GetProperty(
                    "DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(dgvDashboard, true, null);
            dgvDashboard.AutoGenerateColumns = false;
            dgvDashboard.MultiSelect = false;
            dgvDashboard.ScrollBars = ScrollBars.Both;
            dgvDashboard.RowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(178, 217, 245);
            dgvDashboard.RowsDefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 25, 25);
            dgvDashboard.CellToolTipTextNeeded += BackupDashboard_CellToolTipTextNeeded;
            dgvDashboard.SelectionChanged += BackupDashboard_SelectionChanged;
            btneditconfigBK.Enabled = false;
            btndeleteconfigBK.Enabled = false;
            foreach (DataGridViewColumn column in dgvDashboard.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
        }

        private async Task LoadBackupDashboardDataAsync()
        {
            try
            {
                Dictionary<string, BackupConfiguration> configs = await BackupRepository.GetAllConfigsAsync();
                Dictionary<string, DateTime> lastSessions = await BackupRepository.GetLatestSuccessfulSessionStartsAsync();
                Dictionary<string, BackupDashboardSnapshot> snapshots =
                    await BackupRepository.GetAllDashboardSnapshotsAsync();

                foreach ((string agentId, BackupConfiguration config) in configs)
                {
                    if (_backupDashboardStates.TryGetValue(agentId, out BackupDashboardAgentState? state))
                    {
                        state.SetConfiguration(config);
                    }
                }
                foreach ((string agentId, BackupDashboardSnapshot snapshot) in snapshots)
                {
                    if (_backupDashboardStates.TryGetValue(agentId, out BackupDashboardAgentState? state) &&
                        state.RestoreSnapshot(snapshot))
                    {
                        _latestBackupDashboardSnapshots[agentId] = snapshot;
                    }
                }
                foreach ((string agentId, DateTime startedAtUtc) in lastSessions)
                {
                    if (_backupDashboardStates.TryGetValue(agentId, out BackupDashboardAgentState? state))
                    {
                        state.SetLastSuccessfulSession(startedAtUtc);
                    }
                }
                foreach (BackupDashboardAgentState state in _backupDashboardStates.Values)
                {
                    RefreshBackupDashboardRow(state);
                }
                dgvDashboard.ClearSelection();
                UpdateBackupConfigurationButtons();
            }
            catch (Exception ex)
            {
                try
                {
                    await SQLiteHelper.SaveLogAsync("Backup Dashboard", "Không thể nạp Dashboard: " + ex.Message);
                }
                catch
                {
                    // Dashboard không được làm Control crash kể cả khi DB/log cùng gặp lỗi.
                }
            }
        }

        private void SyncBackupDashboardAgents(IReadOnlyList<Dictionary<string, string>> agents)
        {
            foreach (Dictionary<string, string> agent in agents)
            {
                string agentId = GetDashboardValue(agent, "AgentID");
                if (string.IsNullOrWhiteSpace(agentId))
                {
                    continue;
                }

                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(agentId);
                state.UpdateAgent(
                    GetDashboardValue(agent, "MachineName"),
                    GetDashboardValue(agent, "OwnerName"),
                    GetDashboardValue(agent, "OSVersion"));
                state.SetOnline(
                    _connectedAgents.TryGetValue(agentId, out var connected) &&
                    connected.Client != null &&
                    connected.Client.Connected);
                RefreshBackupDashboardRow(state);
            }
        }

        private void BackupDashboardConfigurationSaved(BackupConfiguration config)
        {
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(config.AgentID);
                state.SetConfiguration(config);
                RefreshBackupDashboardRow(state);
                dgvDashboard.ClearSelection();
                UpdateBackupConfigurationButtons();
            });
        }

        private async Task BackupDashboardSessionStartedAsync(BackupSessionBegin begin)
        {
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(begin.AgentID);
                if (state.StartSession(begin))
                {
                    RefreshBackupDashboardRow(state);
                }
            });

            DateTime observedAtUtc = DateTime.UtcNow;
            BackupDashboardSnapshot? snapshot;
            if (_latestBackupDashboardSnapshots.TryGetValue(begin.AgentID, out BackupDashboardSnapshot? latest) &&
                latest.SessionState == BackupDashboardSessionState.Active &&
                latest.SessionName.Equals(begin.SessionName, StringComparison.OrdinalIgnoreCase) &&
                latest.StartedAtUtc == begin.StartedAtUtc)
            {
                latest.BackupType = begin.BackupType ?? latest.BackupType;
                latest.PlannedFileCount = begin.PlannedFileCount;
                latest.PlannedTotalBytes = begin.PlannedTotalBytes;
                latest.Touch(observedAtUtc);
                snapshot = latest;
            }
            else
            {
                snapshot = BackupDashboardSnapshot.FromBegin(begin, observedAtUtc);
            }
            if (snapshot != null)
            {
                EnsureBackupDashboardSnapshotRevision(snapshot);
                _latestBackupDashboardSnapshots[snapshot.AgentId] = snapshot;
                await TrySaveBackupDashboardSnapshotAsync(snapshot);
            }
        }

        private async Task BackupDashboardProgressReceivedAsync(BackupProgressUpdate update)
        {
            DateTime observedAtUtc = DateTime.UtcNow;
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(update.AgentID);
                if (state.ApplyProgress(update, observedAtUtc))
                {
                    RefreshBackupDashboardRow(state);
                }
            });

            BackupDashboardSnapshot? snapshot = BackupDashboardSnapshot.FromProgress(update, observedAtUtc);
            if (snapshot == null)
            {
                return;
            }

            if (!_latestBackupDashboardSnapshots.TryGetValue(snapshot.AgentId, out BackupDashboardSnapshot? active) ||
                active.SessionState != BackupDashboardSessionState.Active ||
                !active.SessionName.Equals(snapshot.SessionName, StringComparison.OrdinalIgnoreCase) ||
                active.StartedAtUtc != snapshot.StartedAtUtc)
            {
                return;
            }

            snapshot.Revision = Math.Max(snapshot.Revision, active.Revision + 1);
            _latestBackupDashboardSnapshots[snapshot.AgentId] = snapshot;
            if (ShouldWriteBackupDashboardSnapshot(snapshot.AgentId))
            {
                await TrySaveBackupDashboardSnapshotAsync(snapshot);
            }
        }

        private async Task BackupDashboardSessionCompletedAsync(
            string agentId,
            string sessionName,
            DateTime startedAtUtc)
        {
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(agentId);
                if (!state.HasActiveSession ||
                    state.ActiveSessionName.Equals(sessionName, StringComparison.OrdinalIgnoreCase))
                {
                    state.CompleteSession(startedAtUtc);
                    RefreshBackupDashboardRow(state);
                }
            });

            BackupDashboardSnapshot? snapshot = CreateTerminalBackupDashboardSnapshot(
                agentId,
                sessionName,
                startedAtUtc,
                BackupDashboardSessionState.Completed);
            if (snapshot != null)
            {
                _latestBackupDashboardSnapshots[agentId] = snapshot;
                await TrySaveBackupDashboardSnapshotAsync(snapshot);
            }
        }

        private async Task BackupDashboardSessionFailedAsync(string agentId, string sessionName)
        {
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(agentId);
                if (!state.HasActiveSession ||
                    state.ActiveSessionName.Equals(sessionName, StringComparison.OrdinalIgnoreCase))
                {
                    state.FailSession();
                    RefreshBackupDashboardRow(state);
                }
            });

            BackupDashboardSnapshot? snapshot = CreateTerminalBackupDashboardSnapshot(
                agentId,
                sessionName,
                null,
                BackupDashboardSessionState.Failed);
            if (snapshot != null)
            {
                _latestBackupDashboardSnapshots[agentId] = snapshot;
                await TrySaveBackupDashboardSnapshotAsync(snapshot);
            }
        }

        private async Task SetBackupDashboardAgentOnlineAsync(string agentId, bool online)
        {
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(agentId);
                state.SetOnline(online);
                RefreshBackupDashboardRow(state);
            });

            if (!online &&
                _latestBackupDashboardSnapshots.TryGetValue(agentId, out BackupDashboardSnapshot? snapshot))
            {
                snapshot.Touch(DateTime.UtcNow);
                await TrySaveBackupDashboardSnapshotAsync(snapshot);
            }
        }

        private void RemoveBackupDashboardAgent(string agentId)
        {
            RunOnBackupDashboardUi(() =>
            {
                _backupDashboardStates.Remove(agentId);
                _latestBackupDashboardSnapshots.TryRemove(agentId, out _);
                _lastBackupDashboardSnapshotWrites.TryRemove(agentId, out _);
                if (_backupDashboardRows.Remove(agentId, out DataGridViewRow? row) &&
                    row.DataGridView == dgvDashboard)
                {
                    dgvDashboard.Rows.Remove(row);
                }
                UpdateBackupConfigurationButtons();
            });
        }

        private void BackupDashboardConfigurationDeleted(string agentId)
        {
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(agentId);
                state.ResetBackupConfigurationAndHistory();
                _latestBackupDashboardSnapshots.TryRemove(agentId, out _);
                _lastBackupDashboardSnapshotWrites.TryRemove(agentId, out _);
                RefreshBackupDashboardRow(state);
            });
        }

        private void BackupDashboardOwnerChanged(string agentId, string ownerName)
        {
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(agentId);
                state.UpdateAgent(state.MachineName, ownerName, state.OsDisplay);
                RefreshBackupDashboardRow(state);
            });
        }

        private BackupDashboardAgentState GetOrCreateBackupDashboardState(string agentId)
        {
            string normalizedAgentId = agentId?.Trim() ?? string.Empty;
            if (!_backupDashboardStates.TryGetValue(normalizedAgentId, out BackupDashboardAgentState? state))
            {
                state = new BackupDashboardAgentState(normalizedAgentId);
                _backupDashboardStates[normalizedAgentId] = state;
            }
            return state;
        }

        private void RefreshBackupDashboardRow(BackupDashboardAgentState state)
        {
            if (state.Configuration == null)
            {
                if (_backupDashboardRows.Remove(state.AgentId, out DataGridViewRow? existingRow) &&
                    existingRow.DataGridView == dgvDashboard)
                {
                    dgvDashboard.Rows.Remove(existingRow);
                }
                UpdateBackupConfigurationButtons();
                return;
            }

            if (!_backupDashboardRows.TryGetValue(state.AgentId, out DataGridViewRow? row) ||
                row.DataGridView != dgvDashboard)
            {
                int rowIndex = dgvDashboard.Rows.Add();
                row = dgvDashboard.Rows[rowIndex];
                _backupDashboardRows[state.AgentId] = row;
            }

            row.Tag = state;
            BackupConfiguration? config = state.Configuration;
            SetDashboardCell(row, dashboardAgent, state.MachineName);
            SetDashboardCell(row, dashboardAgentName, state.OwnerName);
            SetDashboardCell(row, dashboardOS, state.OsDisplay);
            SetDashboardCell(row, dashboardOnlineStatus, state.IsOnline ? "Online" : "Offline");
            SetDashboardCell(row, dashboardSourcePaths, JoinDashboardValues(config?.SourcePaths));
            SetDashboardCell(row, dashboardStoragePath, config?.ControlStoragePath ?? string.Empty);
            SetDashboardCell(
                row,
                dashboardExcludedFolders,
                JoinDashboardValues(BackupExclusionDefaults.FolderNames.Concat(
                    config?.ExcludedFolders ?? Enumerable.Empty<string>())));
            SetDashboardCell(
                row,
                dashboardExcludedPatterns,
                JoinDashboardValues(BackupExclusionDefaults.FilePatterns.Concat(
                    config?.ExcludedPatterns ?? Enumerable.Empty<string>())));
            SetDashboardCell(row, dashboardFullBackupDays, config == null ? string.Empty : $"{config.FullBackupPeriodDays} ngày");
            SetDashboardCell(row, dashboardBackupTime, config?.BackupTime ?? string.Empty);
            SetDashboardCell(row, dashboardBackupIntervalDays, config == null ? string.Empty : $"{config.BackupIntervalDays} ngày");
            SetDashboardCell(row, dashboardProgress, state.ProgressPercentage);
            SetDashboardCell(row, dashboardCurrentFile, GetDashboardFileName(state.CurrentFile));
            SetDashboardCell(row, dashboardSpeed, state.BytesPerSecond > 0 ? FormatDashboardSpeed(state.BytesPerSecond) : string.Empty);
            SetDashboardCell(
                row,
                dashboardStartedAt,
                state.StartedAtUtc.HasValue ? state.StartedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : string.Empty);
            SetDashboardCell(row, dashboardStatus, state.StatusText);

            DataGridViewCell statusCell = row.Cells[dashboardStatus.Index];
            statusCell.Style.ForeColor = state.ProgressMode switch
            {
                BackupDashboardProgressMode.Disconnected or BackupDashboardProgressMode.Error => Color.FromArgb(220, 53, 69),
                BackupDashboardProgressMode.Sending => Color.FromArgb(13, 110, 253),
                BackupDashboardProgressMode.Waiting => Color.FromArgb(108, 117, 125),
                _ => dgvDashboard.DefaultCellStyle.ForeColor
            };
            DataGridViewCell onlineCell = row.Cells[dashboardOnlineStatus.Index];
            onlineCell.Style.ForeColor = state.IsOnline
                ? Color.FromArgb(25, 135, 84)
                : Color.FromArgb(220, 53, 69);
            dgvDashboard.InvalidateCell(dashboardProgress.Index, row.Index);
            UpdateBackupConfigurationButtons();
        }

        private static void SetDashboardCell(DataGridViewRow row, DataGridViewColumn column, object value)
        {
            DataGridViewCell cell = row.Cells[column.Index];
            if (!Equals(cell.Value, value))
            {
                cell.Value = value;
            }
        }

        private void BackupDashboard_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvDashboard.Rows.Count)
            {
                return;
            }
            if (e.ColumnIndex == dashboardCurrentFile.Index &&
                dgvDashboard.Rows[e.RowIndex].Tag is BackupDashboardAgentState state)
            {
                e.ToolTipText = state.CurrentFile;
                return;
            }
            if (e.ColumnIndex == dashboardStoragePath.Index ||
                e.ColumnIndex == dashboardSourcePaths.Index ||
                e.ColumnIndex == dashboardExcludedFolders.Index ||
                e.ColumnIndex == dashboardExcludedPatterns.Index)
            {
                e.ToolTipText = dgvDashboard.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? string.Empty;
            }
        }

        internal static string GetDashboardFileName(string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            try
            {
                string fileName = Path.GetFileName(fullPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
                return string.IsNullOrWhiteSpace(fileName) ? fullPath : fileName;
            }
            catch
            {
                return fullPath;
            }
        }

        private void BackupDashboard_SelectionChanged(object? sender, EventArgs e)
        {
            UpdateBackupConfigurationButtons();
        }

        private BackupDashboardAgentState? GetSelectedBackupDashboardState()
        {
            return dgvDashboard.SelectedRows.Count == 1
                ? dgvDashboard.SelectedRows[0].Tag as BackupDashboardAgentState
                : null;
        }

        private void UpdateBackupConfigurationButtons()
        {
            ApplyBackupConfigurationUiState();
        }

        private static string JoinDashboardValues(IEnumerable<string>? values)
        {
            return values == null
                ? string.Empty
                : string.Join(
                    "; ",
                    values
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private void RunOnBackupDashboardUi(Action action)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(action);
                }
                return;
            }
            action();
        }

        private static string GetDashboardValue(IReadOnlyDictionary<string, string> row, string key) =>
            row.TryGetValue(key, out string? value) ? value ?? string.Empty : string.Empty;

        private static string FormatDashboardSpeed(double bytesPerSecond)
        {
            string[] suffixes = { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
            double value = Math.Max(0, bytesPerSecond);
            int suffix = 0;
            while (value >= 1024 && suffix < suffixes.Length - 1)
            {
                value /= 1024;
                suffix++;
            }
            return $"{value:0.##} {suffixes[suffix]}";
        }

        private bool ShouldWriteBackupDashboardSnapshot(string agentId)
        {
            long now = Stopwatch.GetTimestamp();
            long intervalTicks = (long)(BackupDashboardSnapshotWriteInterval.TotalSeconds * Stopwatch.Frequency);
            while (true)
            {
                if (!_lastBackupDashboardSnapshotWrites.TryGetValue(agentId, out long lastWrite))
                {
                    if (_lastBackupDashboardSnapshotWrites.TryAdd(agentId, now))
                    {
                        return true;
                    }
                    continue;
                }

                if (now - lastWrite < intervalTicks)
                {
                    return false;
                }

                if (_lastBackupDashboardSnapshotWrites.TryUpdate(agentId, now, lastWrite))
                {
                    return true;
                }
            }
        }

        private BackupDashboardSnapshot? CreateTerminalBackupDashboardSnapshot(
            string agentId,
            string sessionName,
            DateTime? startedAtUtc,
            BackupDashboardSessionState terminalState)
        {
            DateTime observedAtUtc = DateTime.UtcNow;
            if (_latestBackupDashboardSnapshots.TryGetValue(agentId, out BackupDashboardSnapshot? latest) &&
                latest.SessionName.Equals(sessionName, StringComparison.OrdinalIgnoreCase))
            {
                return latest.Finish(terminalState, observedAtUtc);
            }

            if (!startedAtUtc.HasValue || startedAtUtc.Value == default)
            {
                return null;
            }

            BackupDashboardSnapshot? empty = BackupDashboardSnapshot.FromBegin(
                new BackupSessionBegin
                {
                    AgentID = agentId,
                    SessionName = sessionName,
                    StartedAtUtc = startedAtUtc.Value
                },
                observedAtUtc);
            return empty?.Finish(terminalState, observedAtUtc);
        }

        private async Task TrySaveBackupDashboardSnapshotAsync(BackupDashboardSnapshot snapshot)
        {
            try
            {
                await BackupRepository.SaveDashboardSnapshotAsync(snapshot);
                _lastBackupDashboardSnapshotWrites[snapshot.AgentId] = Stopwatch.GetTimestamp();
            }
            catch (Exception ex)
            {
                _lastBackupDashboardSnapshotWrites.TryRemove(snapshot.AgentId, out _);
                try
                {
                    await SQLiteHelper.SaveLogAsync(
                        "Backup Dashboard",
                        $"Không thể lưu tiến độ Agent {snapshot.AgentId}: {ex.Message}");
                }
                catch
                {
                    // Lỗi lưu Dashboard tuyệt đối không được ngắt phiên backup đang chạy.
                }
            }
        }

        private void EnsureBackupDashboardSnapshotRevision(BackupDashboardSnapshot snapshot)
        {
            if (_latestBackupDashboardSnapshots.TryGetValue(
                    snapshot.AgentId,
                    out BackupDashboardSnapshot? previous) &&
                !ReferenceEquals(previous, snapshot))
            {
                snapshot.Revision = Math.Max(snapshot.Revision, previous.Revision + 1);
            }
        }
    }
}
