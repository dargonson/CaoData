using AgentShared;

namespace AgentControl
{
    public partial class frmToolBackup
    {
        private readonly Dictionary<string, BackupDashboardAgentState> _backupDashboardStates =
            new Dictionary<string, BackupDashboardAgentState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DataGridViewRow> _backupDashboardRows =
            new Dictionary<string, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);
        private DataGridViewTextBoxColumn _dashboardOnlineStatus = null!;
        private DataGridViewTextBoxColumn _dashboardSourcePaths = null!;
        private DataGridViewTextBoxColumn _dashboardExcludedFolders = null!;
        private DataGridViewTextBoxColumn _dashboardExcludedPatterns = null!;

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
            dashboardAgent.HeaderText = "Tên máy tính";
            dashboardAgentName.HeaderText = "Người sử dụng";
            ConfigureResizableDashboardPathColumns();
            AddBackupDashboardConfigurationColumns();
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

                foreach ((string agentId, BackupConfiguration config) in configs)
                {
                    if (_backupDashboardStates.TryGetValue(agentId, out BackupDashboardAgentState? state))
                    {
                        state.SetConfiguration(config);
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

        private void BackupDashboardSessionStarted(BackupSessionBegin begin)
        {
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(begin.AgentID);
                if (state.StartSession(begin))
                {
                    RefreshBackupDashboardRow(state);
                }
            });
        }

        private void BackupDashboardProgressReceived(BackupProgressUpdate update)
        {
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(update.AgentID);
                if (state.ApplyProgress(update, DateTime.UtcNow))
                {
                    RefreshBackupDashboardRow(state);
                }
            });
        }

        private void BackupDashboardSessionCompleted(string agentId, string sessionName, DateTime startedAtUtc)
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
        }

        private void BackupDashboardSessionFailed(string agentId, string sessionName)
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
        }

        private void SetBackupDashboardAgentOnline(string agentId, bool online)
        {
            RunOnBackupDashboardUi(() =>
            {
                BackupDashboardAgentState state = GetOrCreateBackupDashboardState(agentId);
                state.SetOnline(online);
                RefreshBackupDashboardRow(state);
            });
        }

        private void RemoveBackupDashboardAgent(string agentId)
        {
            RunOnBackupDashboardUi(() =>
            {
                _backupDashboardStates.Remove(agentId);
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
            SetDashboardCell(row, _dashboardOnlineStatus, state.IsOnline ? "Online" : "Offline");
            SetDashboardCell(row, _dashboardSourcePaths, JoinDashboardValues(config?.SourcePaths));
            SetDashboardCell(row, dashboardStoragePath, config?.ControlStoragePath ?? string.Empty);
            SetDashboardCell(
                row,
                _dashboardExcludedFolders,
                JoinDashboardValues(BackupExclusionDefaults.FolderNames.Concat(
                    config?.ExcludedFolders ?? Enumerable.Empty<string>())));
            SetDashboardCell(
                row,
                _dashboardExcludedPatterns,
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
            DataGridViewCell onlineCell = row.Cells[_dashboardOnlineStatus.Index];
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
                e.ColumnIndex == _dashboardSourcePaths.Index ||
                e.ColumnIndex == _dashboardExcludedFolders.Index ||
                e.ColumnIndex == _dashboardExcludedPatterns.Index)
            {
                e.ToolTipText = dgvDashboard.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? string.Empty;
            }
        }

        private void ConfigureResizableDashboardPathColumns()
        {
            foreach (DataGridViewColumn column in new[] { dashboardStoragePath, dashboardCurrentFile })
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.MinimumWidth = 2;
                column.Resizable = DataGridViewTriState.True;
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

        private void AddBackupDashboardConfigurationColumns()
        {
            _dashboardOnlineStatus = new DataGridViewTextBoxColumn
            {
                Name = "dashboardOnlineStatus",
                HeaderText = "Online Status",
                ReadOnly = true,
                Width = 95
            };
            _dashboardExcludedFolders = new DataGridViewTextBoxColumn
            {
                Name = "dashboardExcludedFolders",
                HeaderText = "Thư mục loại trừ",
                ReadOnly = true,
                Width = 180
            };
            _dashboardSourcePaths = new DataGridViewTextBoxColumn
            {
                Name = "dashboardSourcePaths",
                HeaderText = "Thư mục backup trên Agent",
                ReadOnly = true,
                Width = 210
            };
            _dashboardExcludedPatterns = new DataGridViewTextBoxColumn
            {
                Name = "dashboardExcludedPatterns",
                HeaderText = "Extension / pattern loại trừ",
                ReadOnly = true,
                Width = 190
            };

            dgvDashboard.Columns.Insert(dashboardOS.Index + 1, _dashboardOnlineStatus);
            dgvDashboard.Columns.Insert(_dashboardOnlineStatus.Index + 1, _dashboardSourcePaths);
            dgvDashboard.Columns.Insert(dashboardStoragePath.Index + 1, _dashboardExcludedFolders);
            dgvDashboard.Columns.Insert(_dashboardExcludedFolders.Index + 1, _dashboardExcludedPatterns);
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
            BackupDashboardAgentState? state = GetSelectedBackupDashboardState();
            bool enabled = state?.CanManageConfiguration == true;
            btneditconfigBK.Enabled = enabled;
            btndeleteconfigBK.Enabled = enabled;
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
    }
}
