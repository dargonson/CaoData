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

        private async void BackupEditConfiguration_Click(object? sender, EventArgs e)
        {
            BackupDashboardAgentState? state = GetSelectedBackupDashboardState();
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
                await LoadBackupConfigIntoEditorAsync(state.AgentId, loadVersion);
                dgvDashboard.ClearSelection();
                btneditconfigBK.Enabled = false;
                btndeleteconfigBK.Enabled = false;
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
            BackupDashboardAgentState? state = GetSelectedBackupDashboardState();
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
            btneditconfigBK.Enabled = false;
            btndeleteconfigBK.Enabled = false;
            btnDeploy.Enabled = false;
            string archivePath = string.Empty;

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
                if (agentId.Equals(GetSelectedBackupAgentId(), StringComparison.OrdinalIgnoreCase))
                {
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
                btnDeploy.Enabled = true;
                UpdateBackupConfigurationButtons();
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
