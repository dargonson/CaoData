using AgentShared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentControl
{
    public partial class frmToolBackup
    {
        private readonly BackupReceiver _backupReceiver = new BackupReceiver();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<BackupConfigAck>> _pendingBackupConfigAcks =
            new ConcurrentDictionary<string, TaskCompletionSource<BackupConfigAck>>(StringComparer.OrdinalIgnoreCase);
        private bool _applyingBackupTreeChecks;
        private int _backupConfigLoadVersion;
        private string _backupEditorAgentId = string.Empty;
        private bool _backupConfigurationOperationBusy;
        private readonly HashSet<string> _configuredBackupSourcePaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void InitializeBackupModule()
        {
            button1.Click += BackupBrowseStorage_Click;
            btnAddExt.Click += BackupAddPattern_Click;
            btnDeleteExt.Click += BackupDeletePattern_Click;
            btnAddExcFolder.Click += BackupAddExcludedFolder_Click;
            btndeleteExcFolder.Click += BackupDeleteExcludedFolder_Click;
            btnDeploy.Click += BackupDeploy_Click;
            tvRemoteFolders.AfterCheck += BackupTree_AfterCheck;
            ListboxAgents.SelectedIndexChanged += BackupAgentSelectionChanged;
            InitializeBackupConfigurationManagementModule();

            AddDefaultBackupExclusions();
            ApplyBackupConfigurationUiState();
            _ = RunControlBackgroundOperationAsync(
                BackupRepository.InitializeAsync,
                "Khởi tạo database Backup");
        }

        private void BackupAgentSelectionChanged(object? sender, EventArgs e)
        {
            ++_backupConfigLoadVersion;
            _backupEditorAgentId = string.Empty;
            // Card chi phuc vu chon Agent/cay thu muc. Config chi nap khi bam nut Sua tren Dashboard.
            ClearBackupEditor();
            dgvDashboard.ClearSelection();
            ApplyBackupConfigurationUiState();
        }

        private async Task<bool> LoadBackupConfigIntoEditorAsync(string agentId, int loadVersion)
        {
            BackupConfiguration? config = await BackupRepository.GetConfigAsync(agentId);
            if (loadVersion != _backupConfigLoadVersion ||
                !string.Equals(agentId, GetSelectedBackupAgentId(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (config == null)
            {
                return false;
            }

            textBox1.Text = config.ControlStoragePath;
            numericUpDown1.Value = ClampNumericValue(numericUpDown1, config.FullBackupPeriodDays);
            numericUpDown2.Value = ClampNumericValue(numericUpDown2, config.BackupIntervalDays);
            if (TimeSpan.TryParse(config.BackupTime, out TimeSpan backupTime))
            {
                dateTimePicker1.Value = DateTime.Today.Add(backupTime);
            }

            ReplaceListItems(listBox1, config.ExcludedFolders ?? Enumerable.Empty<string>());
            ReplaceListItems(listBox2, config.ExcludedPatterns ?? Enumerable.Empty<string>());
            AddDefaultBackupExclusions();
            _configuredBackupSourcePaths.Clear();
            foreach (string path in (config.SourcePaths ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                _configuredBackupSourcePaths.Add(NormalizeBackupPath(path));
            }
            ApplyConfiguredBackupChecks(tvRemoteFolders.Nodes);
            return true;
        }

        private string GetSelectedBackupAgentId()
        {
            if (ListboxAgents.SelectedItem is NHFUiControls.AgentInfo agent)
            {
                return agent.AgentID?.Trim() ?? string.Empty;
            }

            object? selectedItem = ListboxAgents.SelectedItem;
            if (selectedItem == null)
            {
                return string.Empty;
            }

            try
            {
                return selectedItem.GetType().GetProperty("AgentID")?.GetValue(selectedItem)?.ToString()?.Trim()
                    ?? selectedItem.ToString()?.Trim()
                    ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void BackupBrowseStorage_Click(object? sender, EventArgs e)
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "Chọn thư mục trên máy AgentControl để lưu backup",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (Directory.Exists(textBox1.Text))
            {
                dialog.SelectedPath = textBox1.Text;
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                textBox1.Text = dialog.SelectedPath;
            }
        }

        private void BackupAddPattern_Click(object? sender, EventArgs e)
        {
            string? value = BackupTextPrompt.Show(
                this,
                "Extension / pattern loại trừ",
                "Nhập extension hoặc pattern, ví dụ .tmp, *.temp, ~*:");
            value = value?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            AddUniqueListItem(listBox2, value);
        }

        private void BackupDeletePattern_Click(object? sender, EventArgs e)
        {
            RemoveSelectedListItems(listBox2);
            AddDefaultBackupExclusions();
        }

        private void BackupAddExcludedFolder_Click(object? sender, EventArgs e)
        {
            if (!TryGetRemoteNodeTag(tvRemoteFolders.SelectedNode, out RemoteNodeTag? tag) || tag == null)
            {
                MessageBox.Show(
                    "Hãy chọn một thư mục trên cây Agent trước khi bấm Thêm.",
                    "Backup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            AddUniqueListItem(listBox1, NormalizeBackupPath(tag.RemotePath));
        }

        private void BackupDeleteExcludedFolder_Click(object? sender, EventArgs e)
        {
            RemoveSelectedListItems(listBox1);
            AddDefaultBackupExclusions();
        }

        private async void BackupDeploy_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedAgentId))
            {
                MessageBox.Show("Hãy chọn Agent trước khi gửi cấu hình backup.", "Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_connectedAgents.TryGetValue(selectedAgentId, out var agentInfo) ||
                agentInfo.Client == null || !agentInfo.Client.Connected)
            {
                MessageBox.Show("Agent đang Offline.", "Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_backupDashboardStates.TryGetValue(selectedAgentId, out BackupDashboardAgentState? dashboardState) &&
                dashboardState.HasActiveSession)
            {
                MessageBox.Show(
                    "Agent đang backup, hãy chờ phiên hiện tại hoàn tất rồi mới sửa cấu hình.",
                    "Backup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string storagePath;
            try
            {
                storagePath = Path.GetFullPath(textBox1.Text.Trim());
                Directory.CreateDirectory(storagePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đường dẫn lưu trên Control không hợp lệ: " + ex.Message, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            List<string> sourcePaths = CollectCheckedBackupPaths();
            if (sourcePaths.Count == 0)
            {
                MessageBox.Show("Hãy tick ít nhất một ổ đĩa hoặc thư mục trên cây Agent.", "Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (sourcePaths.Any(IsSystemDriveRoot))
            {
                MessageBox.Show(
                    "Không hỗ trợ backup toàn bộ ổ C:. Fen có thể tick Desktop hoặc thư mục con cụ thể trên ổ C:.",
                    "Backup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            BackupConfiguration config = new BackupConfiguration
            {
                Enabled = true,
                AgentID = selectedAgentId,
                ControlStoragePath = storagePath,
                BackupIntervalDays = (int)numericUpDown2.Value,
                FullBackupPeriodDays = (int)numericUpDown1.Value,
                BackupTime = dateTimePicker1.Value.ToString("HH:mm"),
                SourcePaths = sourcePaths,
                ExcludedFolders = GetListItems(listBox1),
                ExcludedPatterns = GetListItems(listBox2),
                UpdatedAtUtc = DateTime.UtcNow
            };
            BackupExclusionDefaults.EnsureIncluded(config);

            string agentId = selectedAgentId;
            TaskCompletionSource<BackupConfigAck> completion =
                new TaskCompletionSource<BackupConfigAck>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingBackupConfigAcks[agentId] = completion;
            _backupConfigurationOperationBusy = true;
            ApplyBackupConfigurationUiState();
            bool deployed = false;

            try
            {
                await SendPacketToAgentAsync(agentId, agentInfo.Client, new SocketPacket
                {
                    Type = BackupPacketTypes.ConfigDeploy,
                    AgentID = agentId,
                    Data = JsonSerializer.Serialize(config)
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

                // Chi chot DB Control sau khi Agent da ghi appsettings va ACK thanh cong.
                await BackupRepository.SaveConfigAsync(config);
                BackupDashboardConfigurationSaved(config);
                _backupEditorAgentId = string.Empty;
                deployed = true;
                MessageBox.Show(ack.Message, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể gửi cấu hình backup: " + ex.Message, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _pendingBackupConfigAcks.TryRemove(agentId, out _);
                _backupConfigurationOperationBusy = false;
                if (deployed)
                {
                    ClearBackupEditor();
                }
                ApplyBackupConfigurationUiState();
            }
        }

        private async Task<bool> TryHandleBackupPacketAsync(SocketPacket packet, TcpClient client)
        {
            if (packet.Type == BackupPacketTypes.ConfigAck)
            {
                BackupConfigAck? ack = JsonSerializer.Deserialize<BackupConfigAck>(packet.Data);
                if (ack != null && _pendingBackupConfigAcks.TryGetValue(packet.AgentID, out TaskCompletionSource<BackupConfigAck>? completion))
                {
                    completion.TrySetResult(ack);
                }
                return true;
            }

            if (packet.Type == BackupPacketTypes.ConfigDeleteAck)
            {
                BackupConfigAck? ack = JsonSerializer.Deserialize<BackupConfigAck>(packet.Data);
                if (ack != null &&
                    _pendingBackupDeleteAcks.TryGetValue(
                        packet.AgentID,
                        out TaskCompletionSource<BackupConfigAck>? completion))
                {
                    completion.TrySetResult(ack);
                }
                return true;
            }

            if (packet.Type == BackupPacketTypes.SessionBegin)
            {
                BackupSessionBegin? request = JsonSerializer.Deserialize<BackupSessionBegin>(packet.Data);
                BackupSessionResult result = new BackupSessionResult
                {
                    SessionName = request?.SessionName ?? string.Empty,
                    Success = false,
                    Message = "Dữ liệu mở phiên backup không hợp lệ."
                };

                if (request != null)
                {
                    try
                    {
                        EnsureBackupPayloadAgent(packet.AgentID, request.AgentID);
                        await _backupReceiver.BeginSessionAsync(request);
                        result.Success = true;
                        result.Message = "Control đã sẵn sàng nhận backup.";
                        BackupDashboardSessionStarted(request);
                    }
                    catch (Exception ex)
                    {
                        result.Message = ex.Message;
                    }
                }

                await SendPacketToAgentAsync(packet.AgentID, client, new SocketPacket
                {
                    Type = BackupPacketTypes.SessionReady,
                    AgentID = packet.AgentID,
                    Data = JsonSerializer.Serialize(result)
                });
                return true;
            }

            if (packet.Type == BackupPacketTypes.Progress)
            {
                BackupProgressUpdate? progress = JsonSerializer.Deserialize<BackupProgressUpdate>(packet.Data);
                if (progress == null)
                {
                    throw new InvalidDataException("Dữ liệu tiến độ backup không hợp lệ.");
                }
                EnsureBackupPayloadAgent(packet.AgentID, progress.AgentID);
                BackupDashboardProgressReceived(progress);
                return true;
            }

            if (packet.Type == BackupPacketTypes.SessionComplete)
            {
                BackupManifest? manifest = JsonSerializer.Deserialize<BackupManifest>(packet.Data);
                BackupSessionResult result = manifest == null
                    ? new BackupSessionResult { Success = false, Message = "Manifest backup không hợp lệ." }
                    : !IsBackupPayloadAgent(packet.AgentID, manifest.AgentID)
                        ? new BackupSessionResult
                        {
                            SessionName = manifest.SessionName,
                            Success = false,
                            Message = "AgentID trong manifest không khớp kết nối đã xác thực."
                        }
                        : await _backupReceiver.CompleteSessionAsync(manifest);

                if (manifest != null && IsBackupPayloadAgent(packet.AgentID, manifest.AgentID))
                {
                    if (result.Success)
                    {
                        BackupDashboardSessionCompleted(packet.AgentID, manifest.SessionName, manifest.StartedAtUtc);
                    }
                    else
                    {
                        BackupDashboardSessionFailed(packet.AgentID, manifest.SessionName);
                    }
                }

                await SendPacketToAgentAsync(packet.AgentID, client, new SocketPacket
                {
                    Type = BackupPacketTypes.SessionResult,
                    AgentID = packet.AgentID,
                    Data = JsonSerializer.Serialize(result)
                });
                return true;
            }

            if (packet.Type == BackupPacketTypes.FirstFileResumeQuery)
            {
                BackupFirstFileResumeQuery? query = JsonSerializer.Deserialize<BackupFirstFileResumeQuery>(packet.Data);
                BackupFirstFileResumeInfo info = query == null
                    ? new BackupFirstFileResumeInfo { Success = false, Message = "Yêu cầu resume FIRST không hợp lệ." }
                    : !IsBackupPayloadAgent(packet.AgentID, query.AgentID)
                        ? new BackupFirstFileResumeInfo
                        {
                            SessionName = query.SessionName,
                            SourcePath = query.SourcePath,
                            Success = false,
                            Message = "AgentID trong yêu cầu resume không khớp kết nối đã xác thực."
                        }
                        : await _backupReceiver.GetFirstFileResumeInfoAsync(query);

                await SendPacketToAgentAsync(packet.AgentID, client, new SocketPacket
                {
                    Type = BackupPacketTypes.FirstFileResumeInfo,
                    AgentID = packet.AgentID,
                    Data = JsonSerializer.Serialize(info)
                });
                return true;
            }

            if (packet.Type == BackupPacketTypes.FirstFileSkip)
            {
                BackupFirstFileSkip? skipped = JsonSerializer.Deserialize<BackupFirstFileSkip>(packet.Data);
                if (skipped == null)
                {
                    throw new InvalidDataException("Thông tin file FIRST bỏ qua không hợp lệ.");
                }
                EnsureBackupPayloadAgent(packet.AgentID, skipped.AgentID);
                await _backupReceiver.SkipFirstFileAsync(skipped);
                return true;
            }

            return false;
        }

        private static bool IsBackupPayloadAgent(string authenticatedAgentId, string payloadAgentId) =>
            !string.IsNullOrWhiteSpace(payloadAgentId) &&
            payloadAgentId.Equals(authenticatedAgentId, StringComparison.OrdinalIgnoreCase);

        private static void EnsureBackupPayloadAgent(string authenticatedAgentId, string payloadAgentId)
        {
            if (!IsBackupPayloadAgent(authenticatedAgentId, payloadAgentId))
            {
                throw new InvalidDataException(
                    "AgentID trong dữ liệu backup không khớp kết nối đã xác thực.");
            }
        }

        private void BackupTree_AfterCheck(object? sender, TreeViewEventArgs e)
        {
            if (_applyingBackupTreeChecks || e.Node == null)
            {
                return;
            }

            _applyingBackupTreeChecks = true;
            try
            {
                SetChildCheckState(e.Node.Nodes, e.Node.Checked);
                TrackConfiguredBackupSourceSelection(e.Node);
                SyncVisibleRemoteFileChecksFromTreeNode(e.Node);
            }
            finally
            {
                _applyingBackupTreeChecks = false;
            }
        }

        private List<string> CollectCheckedBackupPaths()
        {
            List<string> result = new List<string>();
            CollectCheckedBackupPaths(tvRemoteFolders.Nodes, false, result);
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void CollectCheckedBackupPaths(TreeNodeCollection nodes, bool ancestorChecked, List<string> result)
        {
            foreach (TreeNode node in nodes)
            {
                bool currentChecked = node.Checked;
                if (currentChecked && !ancestorChecked && TryGetRemoteNodeTag(node, out RemoteNodeTag? tag) && tag != null)
                {
                    result.Add(NormalizeBackupPath(tag.RemotePath));
                }

                CollectCheckedBackupPaths(node.Nodes, ancestorChecked || currentChecked, result);
            }
        }

        private void ClearBackupEditor()
        {
            textBox1.Clear();
            numericUpDown1.Value = ClampNumericValue(numericUpDown1, 60);
            numericUpDown2.Value = ClampNumericValue(numericUpDown2, 1);
            dateTimePicker1.Value = DateTime.Today;
            listBox1.Items.Clear();
            listBox2.Items.Clear();
            _configuredBackupSourcePaths.Clear();
            ClearTreeChecks(tvRemoteFolders.Nodes);
            SyncVisibleRemoteFileChecksFromTreeSelection();
            AddDefaultBackupExclusions();
        }

        private void ClearTreeChecks(TreeNodeCollection nodes)
        {
            _applyingBackupTreeChecks = true;
            try
            {
                SetChildCheckState(nodes, false);
            }
            finally
            {
                _applyingBackupTreeChecks = false;
            }
        }

        private static void SetChildCheckState(TreeNodeCollection nodes, bool value)
        {
            foreach (TreeNode node in nodes)
            {
                node.Checked = value;
                SetChildCheckState(node.Nodes, value);
            }
        }

        private void ApplyConfiguredBackupChecks(TreeNodeCollection nodes)
        {
            _applyingBackupTreeChecks = true;
            try
            {
                ApplyConfiguredBackupChecksCore(nodes);
            }
            finally
            {
                _applyingBackupTreeChecks = false;
            }
        }

        private void TrackConfiguredBackupSourceSelection(TreeNode node)
        {
            if (!TryGetRemoteNodeTag(node, out RemoteNodeTag? tag) || tag == null)
            {
                return;
            }

            string changedPath = NormalizeBackupPath(tag.RemotePath);
            _configuredBackupSourcePaths.RemoveWhere(source =>
                IsSameOrDescendantRemotePath(source, changedPath));
            if (node.Checked)
            {
                _configuredBackupSourcePaths.Add(changedPath);
            }
        }

        private void ApplyConfiguredBackupChecksCore(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (TryGetRemoteNodeTag(node, out RemoteNodeTag? tag) && tag != null)
                {
                    node.Checked = ShouldCheckConfiguredBackupPath(tag.RemotePath);
                }
                ApplyConfiguredBackupChecksCore(node.Nodes);
            }
        }

        private bool ShouldCheckConfiguredBackupPath(string remotePath)
        {
            string candidate = NormalizeBackupPath(remotePath);
            return _configuredBackupSourcePaths.Any(source =>
                IsSameOrDescendantRemotePath(candidate, source));
        }

        private void AddDefaultBackupExclusions()
        {
            foreach (string folderName in BackupExclusionDefaults.FolderNames)
            {
                AddUniqueListItem(listBox1, folderName);
            }

            foreach (string pattern in BackupExclusionDefaults.FilePatterns)
            {
                AddUniqueListItem(listBox2, pattern);
            }
        }

        private static void ReplaceListItems(ListBox listBox, IEnumerable<string> values)
        {
            listBox.Items.Clear();
            foreach (string value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                AddUniqueListItem(listBox, value);
            }
        }

        private static void AddUniqueListItem(ListBox listBox, string value)
        {
            if (!listBox.Items.Cast<object>().Any(item => string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase)))
            {
                listBox.Items.Add(value);
            }
        }

        private static void RemoveSelectedListItems(ListBox listBox)
        {
            while (listBox.SelectedIndices.Count > 0)
            {
                listBox.Items.RemoveAt(listBox.SelectedIndices[0]);
            }
        }

        private static List<string> GetListItems(ListBox listBox)
        {
            return listBox.Items.Cast<object>()
                .Select(item => item?.ToString()?.Trim() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static decimal ClampNumericValue(NumericUpDown control, int value)
        {
            return Math.Max(control.Minimum, Math.Min(control.Maximum, value));
        }

        private static string NormalizeBackupPath(string path)
        {
            string value = (path ?? string.Empty).Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (value.Length > 3)
            {
                value = value.TrimEnd(Path.DirectorySeparatorChar);
            }
            return value;
        }

        private static bool IsSystemDriveRoot(string path)
        {
            string normalized = NormalizeBackupPath(path);
            return normalized.Equals(@"C:\", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("C:", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class BackupTextPrompt
    {
        public static string? Show(IWin32Window owner, string title, string prompt)
        {
            using Form dialog = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(420, 122),
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false
            };

            Label label = new Label { Left = 12, Top = 12, Width = 396, Text = prompt };
            TextBox input = new TextBox { Left = 12, Top = 38, Width = 396 };
            Button ok = new Button { Text = "OK", Left = 252, Top = 78, Width = 75, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "Cancel", Left = 333, Top = 78, Width = 75, DialogResult = DialogResult.Cancel };
            dialog.Controls.AddRange(new Control[] { label, input, ok, cancel });
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;

            return dialog.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
        }
    }
}
