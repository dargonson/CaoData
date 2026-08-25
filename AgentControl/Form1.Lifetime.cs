using AgentShared;
using System.Net.Sockets;

namespace AgentControl
{
    public partial class frmToolBackup
    {
        // BO SUNG HA TANG DUNG CHUNG: quản lý vòng đời các tác vụ nền của Control.
        private readonly CancellationTokenSource _controlLifetimeCts = new();
        private Task? _heartbeatMonitorTask;

        private async Task RunControlBackgroundOperationAsync(Func<Task> operation, string operationName)
        {
            try
            {
                await operation();
            }
            catch (OperationCanceledException) when (_controlLifetimeCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{operationName}: {ex}");
                try { await SQLiteHelper.SaveLogAsync(operationName, ex.Message); } catch { }
            }
        }

        private void StopControlRuntime()
        {
            if (!_controlLifetimeCts.IsCancellationRequested)
            {
                _controlLifetimeCts.Cancel();
            }

            _isListening = false;
            try { _serverListener?.Stop(); } catch { }
            _serverListener = null;

            foreach ((TcpClient client, _) in _connectedAgents.Values)
            {
                try { client.Dispose(); } catch { }
            }
            _connectedAgents.Clear();

            foreach (Stream stream in _agentStreams.Values)
            {
                try { stream.Dispose(); } catch { }
            }
            _agentStreams.Clear();

            foreach (RemoteFolderRequestState state in _pendingFolderFileRequests.Values)
            {
                state.Completion.TrySetCanceled(_controlLifetimeCts.Token);
            }
            _pendingFolderFileRequests.Clear();

            foreach (TaskCompletionSource<RemoteFileActionResponse> pending in _pendingRemoteActionRequests.Values)
            {
                pending.TrySetCanceled(_controlLifetimeCts.Token);
            }
            _pendingRemoteActionRequests.Clear();

            foreach (TaskCompletionSource<UploadStatusPacket> pending in _pendingUploadCompletions.Values)
            {
                pending.TrySetCanceled(_controlLifetimeCts.Token);
            }
            _pendingUploadCompletions.Clear();
            _pendingUploadAgents.Clear();
        }

        private void DisposeControlRuntime()
        {
            StopControlRuntime();
            _serverCertificate.Dispose();
            _controlLifetimeCts.Dispose();
        }
    }
}
