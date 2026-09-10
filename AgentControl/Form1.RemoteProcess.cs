using AgentShared;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net.Sockets;
using System.Text.Json;

namespace AgentControl
{
    public partial class frmToolBackup
    {
        private readonly ConcurrentDictionary<string, RemoteProcessTracking> _remoteProcessRequests =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, RemoteMsiPreflightTracking> _remoteMsiPreflightRequests =
            new(StringComparer.OrdinalIgnoreCase);

        private sealed class RemoteProcessTracking
        {
            public string AgentId { get; }
            public RemoteProcessStatusForm Form { get; }

            public RemoteProcessTracking(string agentId, RemoteProcessStatusForm form)
            {
                AgentId = agentId;
                Form = form;
            }
        }

        private sealed class RemoteMsiPreflightTracking
        {
            public string AgentId { get; }
            public TaskCompletionSource<RemoteMsiPreflightResponse> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public RemoteMsiPreflightTracking(string agentId)
            {
                AgentId = agentId;
            }
        }

        private async void btnrunprocess_Click(object? sender, EventArgs e)
        {
            if (lvRemoteFiles.CheckedItems.Count != 1)
            {
                MessageBox.Show(
                    "Hãy tích đúng một file cần chạy. Không thể chạy thư mục hoặc nhiều file cùng lúc.",
                    "Run Process",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ListViewItem checkedItem = lvRemoteFiles.CheckedItems[0];
            if (checkedItem.Tag is not RemoteFileItemTag remoteItem || remoteItem.IsFolder)
            {
                MessageBox.Show(
                    "Mục đã chọn không phải là file.",
                    "Run Process",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!remoteItem.AgentId.Equals(selectedAgentId, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "File đã chọn không thuộc Agent hiện tại. Hãy tải lại danh sách file.",
                    "Run Process",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string filePath = NormalizeRemotePath(remoteItem.FullPath);
            if (!RemoteProcessRules.IsSupportedFile(filePath))
            {
                MessageBox.Show(
                    $"Chỉ cho phép chạy file {RemoteProcessRules.SupportedExtensionsDisplay}.",
                    "Run Process",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!_connectedAgents.TryGetValue(remoteItem.AgentId, out (TcpClient Client, DateTime LastSeen) agentInfo) ||
                agentInfo.Client == null ||
                !agentInfo.Client.Connected)
            {
                MessageBox.Show(
                    "Agent đang offline, không thể gửi lệnh chạy.",
                    "Run Process",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            bool terminateExistingMsiProcesses = false;
            string msiHandling = "Không áp dụng";
            if (IsRemoteMsiFile(filePath))
            {
                RemoteMsiPreflightResponse? preflight;
                try
                {
                    preflight = await RequestRemoteMsiPreflightAsync(
                        remoteItem.AgentId,
                        agentInfo.Client);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Không thể kiểm tra msiexec.exe trên Agent: " + ex.Message,
                        "Run Process",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                if (preflight == null)
                {
                    MessageBox.Show(
                        "Agent không phản hồi yêu cầu kiểm tra msiexec.exe. Đã hủy lệnh cài đặt.",
                        "Run Process",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
                if (!preflight.Success)
                {
                    MessageBox.Show(
                        "Không thể kiểm tra msiexec.exe: " + preflight.ErrorMessage,
                        "Run Process",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                if (preflight.ProcessIds.Count > 0)
                {
                    string processIds = string.Join(", ", preflight.ProcessIds.Distinct().OrderBy(id => id));
                    DialogResult killChoice = MessageBox.Show(
                        $"Agent đang có msiexec.exe chạy với PID: {processIds}.\n\n" +
                        "Bạn có muốn dừng các tiến trình này để tiếp tục cài đặt không?\n" +
                        "Việc dừng ngang có thể làm lỗi bộ cài đang chạy.",
                        "Windows Installer đang bận",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (killChoice != DialogResult.Yes)
                    {
                        return;
                    }

                    terminateExistingMsiProcesses = true;
                    msiHandling = "Sẽ dừng PID " + processIds + ", sau đó chạy /qn /norestart";
                }
                else
                {
                    msiHandling = "Không phát hiện msiexec.exe; chạy /qn /norestart";
                }
            }

            string agentName = GetRemoteProcessAgentName(remoteItem.AgentId);
            using var confirmation = new RemoteProcessConfirmationDialog(
                agentName,
                remoteItem.AgentId,
                filePath,
                msiHandling);
            if (confirmation.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var request = new RemoteProcessRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                FilePath = filePath,
                Arguments = confirmation.ProcessArguments,
                TerminateExistingMsiProcesses = terminateExistingMsiProcesses,
                RequestedAtUtc = DateTime.UtcNow
            };
            var statusForm = new RemoteProcessStatusForm(agentName, remoteItem.AgentId, filePath);
            _remoteProcessRequests[request.RequestId] = new RemoteProcessTracking(remoteItem.AgentId, statusForm);
            statusForm.Show(this);

            try
            {
                await SendPacketToAgentAsync(remoteItem.AgentId, agentInfo.Client, new SocketPacket
                {
                    Type = RemoteProcessPacketTypes.RunRequest,
                    AgentID = remoteItem.AgentId,
                    Data = JsonSerializer.Serialize(request)
                });
                await SQLiteHelper.SaveLogAsync(
                    "Run Process",
                    $"Đã gửi lệnh {request.RequestId} tới Agent {remoteItem.AgentId}: {filePath}");
            }
            catch (Exception ex)
            {
                _remoteProcessRequests.TryRemove(request.RequestId, out _);
                if (!statusForm.IsDisposed)
                {
                    statusForm.AddStatus(new RemoteProcessStatus
                    {
                        RequestId = request.RequestId,
                        FilePath = filePath,
                        Status = RemoteProcessStatuses.Error,
                        Message = "Không thể gửi lệnh tới Agent: " + ex.Message,
                        ErrorCode = ex is Win32Exception win32Exception
                            ? win32Exception.NativeErrorCode
                            : ex.HResult,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }
        }

        private async Task<RemoteMsiPreflightResponse?> RequestRemoteMsiPreflightAsync(
            string agentId,
            TcpClient client)
        {
            string requestId = Guid.NewGuid().ToString("N");
            var tracking = new RemoteMsiPreflightTracking(agentId);
            _remoteMsiPreflightRequests[requestId] = tracking;
            try
            {
                await SendPacketToAgentAsync(agentId, client, new SocketPacket
                {
                    Type = RemoteProcessPacketTypes.MsiPreflightRequest,
                    AgentID = agentId,
                    Data = JsonSerializer.Serialize(new RemoteMsiPreflightRequest
                    {
                        RequestId = requestId
                    })
                });

                Task finished = await Task.WhenAny(
                    tracking.Completion.Task,
                    Task.Delay(TimeSpan.FromSeconds(15)));
                if (finished == tracking.Completion.Task)
                {
                    return await tracking.Completion.Task;
                }

                _remoteMsiPreflightRequests.TryRemove(requestId, out _);
                return null;
            }
            catch
            {
                _remoteMsiPreflightRequests.TryRemove(requestId, out _);
                throw;
            }
        }

        private void CompleteRemoteMsiPreflight(
            string agentId,
            RemoteMsiPreflightResponse response)
        {
            if (string.IsNullOrWhiteSpace(response.RequestId) ||
                !_remoteMsiPreflightRequests.TryGetValue(
                    response.RequestId,
                    out RemoteMsiPreflightTracking? tracking) ||
                !tracking.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_remoteMsiPreflightRequests.TryRemove(response.RequestId, out _))
            {
                tracking.Completion.TrySetResult(response);
            }
        }

        private static bool IsRemoteMsiFile(string filePath) =>
            string.Equals(Path.GetExtension(filePath), ".msi", StringComparison.OrdinalIgnoreCase);

        private void ApplyRemoteProcessStatus(string agentId, RemoteProcessStatus status)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() => ApplyRemoteProcessStatus(agentId, status)));
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(status.RequestId) ||
                !_remoteProcessRequests.TryGetValue(status.RequestId, out RemoteProcessTracking? tracking) ||
                !tracking.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!tracking.Form.IsDisposed)
            {
                tracking.Form.AddStatus(status);
            }

            if (status.Status == RemoteProcessStatuses.Completed ||
                status.Status == RemoteProcessStatuses.Error)
            {
                _remoteProcessRequests.TryRemove(status.RequestId, out _);
            }
        }

        private string GetRemoteProcessAgentName(string agentId)
        {
            if (_backupDashboardStates.TryGetValue(agentId, out BackupDashboardAgentState? state) &&
                !string.IsNullOrWhiteSpace(state.MachineName))
            {
                return state.MachineName;
            }
            return agentId;
        }
    }
}
