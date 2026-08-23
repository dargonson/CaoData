using AgentShared;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;

namespace AgentControl
{
    public partial class frmToolBackup
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<BackupConfigAck>> _pendingBackupDeleteAcks =
            new ConcurrentDictionary<string, TaskCompletionSource<BackupConfigAck>>(StringComparer.OrdinalIgnoreCase);

        private void InitializeBackupConfigurationManagementModule()
        {
            btneditconfigBK.Click += BackupEditConfiguration_Click;
            btndeleteconfigBK.Click += BackupDeleteConfiguration_Click;
        }

        private BackupDashboardAgentState? GetBackupConfigurationManagementTarget()
        {
            BackupDashboardAgentState? dashboardState = GetSelectedBackupDashboardState();
            if (dashboardState != null)
            {
                return dashboardState;
            }

            string selectedCardAgentId = GetSelectedBackupAgentId();
            return !string.IsNullOrWhiteSpace(selectedCardAgentId) &&
                _backupDashboardStates.TryGetValue(selectedCardAgentId, out BackupDashboardAgentState? cardState)
                    ? cardState
                    : null;
        }

        private void ApplyBackupConfigurationUiState()
        {
            string selectedCardAgentId = GetSelectedBackupAgentId();
            bool hasSelectedCard = !string.IsNullOrWhiteSpace(selectedCardAgentId);
            _backupDashboardStates.TryGetValue(selectedCardAgentId, out BackupDashboardAgentState? cardState);

            bool isEditingSelectedCard = hasSelectedCard &&
                selectedCardAgentId.Equals(_backupEditorAgentId, StringComparison.OrdinalIgnoreCase);
            BackupConfigurationUiState cardUiState = BackupConfigurationUiState.Resolve(
                hasSelectedCard,
                cardState?.Configuration != null,
                cardState?.IsOnline == true,
                cardState?.HasActiveSession == true,
                isEditingSelectedCard,
                _backupConfigurationOperationBusy);

            SetBackupEditorControlsEnabled(cardUiState.EditorEnabled);
            btnDeploy.Enabled = cardUiState.DeployEnabled;
            btnrecovery.Enabled = cardUiState.RecoveryEnabled;

            BackupDashboardAgentState? managementTarget = GetBackupConfigurationManagementTarget();
            bool canManageTarget = managementTarget?.CanManageConfiguration == true &&
                !_backupConfigurationOperationBusy &&
                !isEditingSelectedCard;
            btneditconfigBK.Enabled = canManageTarget;
            btndeleteconfigBK.Enabled = canManageTarget;
        }

        private void SetBackupEditorControlsEnabled(bool enabled)
        {
            foreach (Control control in groupBox4.Controls)
            {
                if (ReferenceEquals(control, btnKetNoi) ||
                    ReferenceEquals(control, btnDeploy) ||
                    ReferenceEquals(control, btneditconfigBK) ||
                    ReferenceEquals(control, btndeleteconfigBK) ||
                    ReferenceEquals(control, btnrecovery))
                {
                    continue;
                }

                control.Enabled = enabled;
            }
        }

        private async void BackupEditConfiguration_Click(object? sender, EventArgs e)
        {
            BackupDashboardAgentState? state = GetBackupConfigurationManagementTarget();
            if (state?.CanManageConfiguration != true)
            {
                UpdateBackupConfigurationButtons();
                return;
            }

            if (!TrySelectAgentCard(state.AgentId))
            {
                MessageBox.Show(
                    "Không tìm thấy card của Agent này.",
                    "Backup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            int loadVersion = ++_backupConfigLoadVersion;
            try
            {
                if (!await LoadBackupConfigIntoEditorAsync(state.AgentId, loadVersion))
                {
                    ApplyBackupConfigurationUiState();
                    return;
                }
                _backupEditorAgentId = state.AgentId;
                dgvDashboard.ClearSelection();
                ApplyBackupConfigurationUiState();
                await ExpandConfiguredBackupPathsAsync(state.AgentId, loadVersion);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể nạp cấu hình backup của Agent: " + ex.Message,
                    "Backup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async void BackupDeleteConfiguration_Click(object? sender, EventArgs e)
        {
            BackupDashboardAgentState? state = GetBackupConfigurationManagementTarget();
            BackupConfiguration? config = state?.Configuration;
            if (state?.CanManageConfiguration != true || config == null)
            {
                UpdateBackupConfigurationButtons();
                return;
            }

            if (!_connectedAgents.TryGetValue(state.AgentId, out var agentConnection) ||
                agentConnection.Client == null ||
                !agentConnection.Client.Connected)
            {
                MessageBox.Show("Agent đang Offline.", "Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                $"Xoá cấu hình backup của máy {state.MachineName}?\n\n" +
                "Control sẽ xuất toàn bộ cấu hình/lịch sử DB ra file JSON trước khi xoá. " +
                "Các thư mục FIRST/INC và file backup vật lý vẫn được giữ nguyên.",
                "Xác nhận xoá cấu hình Backup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            string agentId = state.AgentId;
            TaskCompletionSource<BackupConfigAck> completion =
                new TaskCompletionSource<BackupConfigAck>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingBackupDeleteAcks[agentId] = completion;
            _backupConfigurationOperationBusy = true;
            ApplyBackupConfigurationUiState();
            string archivePath = string.Empty;
            bool deleted = false;

            try
            {
                archivePath = await Task.Run(() => BackupConfigurationArchiveService.ExportAsync(
                    agentId,
                    state.MachineName,
                    state.OwnerName,
                    config.ControlStoragePath));

                await SendPacketToAgentAsync(agentId, agentConnection.Client, new SocketPacket
                {
                    Type = BackupPacketTypes.ConfigDelete,
                    AgentID = agentId,
                    Data = JsonSerializer.Serialize(new BackupConfigDeleteRequest
                    {
                        AgentID = agentId,
                        RequestedAtUtc = DateTime.UtcNow
                    })
                });

                Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(15)));
                if (finished != completion.Task)
                {
                    throw new TimeoutException("Agent chưa phản hồi sau 15 giây.");
                }

                BackupConfigAck ack = await completion.Task;
                if (!ack.Success)
                {
                    throw new InvalidOperationException(ack.Message);
                }

                // Chi xoa DB Control sau khi Agent da xoa appsettings/runtime state va ACK thanh cong.
                await Task.Run(() => BackupConfigurationArchiveService.DeleteDatabaseStateAsync(agentId));
                try
                {
                    await Task.Run(() => new RecoverySnapshotRepository().DeleteAgentSnapshotsAsync(agentId));
                }
                catch (Exception cacheError)
                {
                    try
                    {
                        await SQLiteHelper.SaveLogAsync(
                            "Backup",
                            $"Đã xoá config {agentId}, nhưng chưa dọn được cache Recovery: {cacheError.Message}");
                    }
                    catch
                    {
                        // Cache Recovery va log deu la du lieu phu, khong dao nguoc ket qua xoa config da commit.
                    }
                }

                BackupDashboardConfigurationDeleted(agentId);
                deleted = true;
                if (agentId.Equals(GetSelectedBackupAgentId(), StringComparison.OrdinalIgnoreCase))
                {
                    _backupEditorAgentId = string.Empty;
                    ClearBackupEditor();
                }
                try
                {
                    await SQLiteHelper.SaveLogAsync(
                        "Backup",
                        $"Đã xoá cấu hình backup Agent {agentId}. Archive: {archivePath}");
                }
                catch
                {
                    // Khong bao that bai cho user khi chi rieng bang log gap loi.
                }
                MessageBox.Show(
                    $"Đã xoá cấu hình backup.\n\nFile lưu lịch sử:\n{archivePath}",
                    "Backup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                string archiveNotice = string.IsNullOrWhiteSpace(archivePath)
                    ? string.Empty
                    : $"\n\nFile JSON đã xuất vẫn được giữ tại:\n{archivePath}";
                MessageBox.Show(
                    "Không thể xoá cấu hình backup: " + ex.Message + archiveNotice,
                    "Backup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _pendingBackupDeleteAcks.TryRemove(agentId, out _);
                _backupConfigurationOperationBusy = false;
                if (!deleted)
                {
                    _backupEditorAgentId = string.Empty;
                }
                ApplyBackupConfigurationUiState();
            }
        }

        private bool TrySelectAgentCard(string agentId)
        {
            foreach (object item in ListboxAgents.Items)
            {
                if (item is NHFUiControls.AgentInfo agent &&
                    agent.AgentID.Equals(agentId, StringComparison.OrdinalIgnoreCase))
                {
                    ListboxAgents.SelectedItem = item;
                    return true;
                }
            }
            return false;
        }
    }
}
