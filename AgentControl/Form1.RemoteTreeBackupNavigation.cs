using System.Collections.Concurrent;

namespace AgentControl
{
    public partial class frmToolBackup
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _remoteDriveLoadWaiters =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _remoteDirectoryLoadWaiters =
            new(StringComparer.OrdinalIgnoreCase);

        private async Task ExpandConfiguredBackupPathsAsync(string agentId, int loadVersion)
        {
            List<string> configuredPaths = _configuredBackupSourcePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeRemotePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path.Count(character =>
                    character == Path.DirectorySeparatorChar ||
                    character == Path.AltDirectorySeparatorChar))
                .ToList();
            if (configuredPaths.Count == 0 ||
                !await WaitForRemoteDriveNodesAsync(agentId, loadVersion))
            {
                return;
            }

            TreeNode? firstConfiguredNode = null;
            foreach (string configuredPath in configuredPaths)
            {
                if (!IsCurrentBackupTreeLoad(agentId, loadVersion))
                {
                    return;
                }

                TreeNode? configuredNode = await ExpandRemotePathAsync(agentId, configuredPath, loadVersion);
                firstConfiguredNode ??= configuredNode;
            }

            if (firstConfiguredNode != null && IsCurrentBackupTreeLoad(agentId, loadVersion))
            {
                firstConfiguredNode.EnsureVisible();
            }
        }

        private async Task<TreeNode?> ExpandRemotePathAsync(string agentId, string remotePath, int loadVersion)
        {
            string normalizedPath = NormalizeRemotePath(remotePath);
            string rootPath = NormalizeRemotePath(Path.GetPathRoot(normalizedPath) ?? string.Empty);
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return null;
            }

            TreeNode? currentNode = FindRemoteNode(agentId, rootPath);
            if (currentNode == null)
            {
                return null;
            }

            string relativePath = normalizedPath[rootPath.Length..]
                .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                currentNode.EnsureVisible();
                return currentNode;
            }

            foreach (string segment in relativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (!IsCurrentBackupTreeLoad(agentId, loadVersion) ||
                    !await EnsureRemoteNodeChildrenLoadedAsync(currentNode, agentId, loadVersion))
                {
                    return null;
                }

                currentNode.Expand();
                string expectedPath = NormalizeRemotePath(Path.Combine(
                    ((RemoteNodeTag)currentNode.Tag).RemotePath,
                    segment));
                currentNode = currentNode.Nodes
                    .Cast<TreeNode>()
                    .FirstOrDefault(node =>
                        TryGetRemoteNodeTag(node, out RemoteNodeTag? childTag) &&
                        childTag != null &&
                        NormalizeRemotePath(childTag.RemotePath).Equals(
                            expectedPath,
                            StringComparison.OrdinalIgnoreCase));
                if (currentNode == null)
                {
                    return null;
                }
            }

            currentNode.EnsureVisible();
            return currentNode;
        }

        private async Task<bool> EnsureRemoteNodeChildrenLoadedAsync(
            TreeNode node,
            string agentId,
            int loadVersion)
        {
            if (!HasRemoteLoadingPlaceholder(node))
            {
                return true;
            }
            if (!TryGetRemoteNodeTag(node, out RemoteNodeTag? tag) || tag == null)
            {
                return false;
            }

            string key = GetRemoteDirectoryLoadKey(agentId, tag.RemotePath);
            TaskCompletionSource<bool> completion = _remoteDirectoryLoadWaiters.GetOrAdd(
                key,
                _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

            if (!HasRemoteLoadingPlaceholder(node))
            {
                completion.TrySetResult(true);
            }
            else
            {
                await RequestRemoteDirectoryAsync(tag);
            }

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            RemoveRemoteDirectoryWaiter(key, completion);
            return finished == completion.Task &&
                await completion.Task &&
                IsCurrentBackupTreeLoad(agentId, loadVersion);
        }

        private async Task<bool> WaitForRemoteDriveNodesAsync(string agentId, int loadVersion)
        {
            if (HasRemoteDriveNodes(agentId))
            {
                return true;
            }

            TaskCompletionSource<bool> completion = _remoteDriveLoadWaiters.GetOrAdd(
                agentId,
                _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            if (HasRemoteDriveNodes(agentId))
            {
                completion.TrySetResult(true);
            }

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            if (_remoteDriveLoadWaiters.TryGetValue(agentId, out TaskCompletionSource<bool>? current) &&
                ReferenceEquals(current, completion))
            {
                _remoteDriveLoadWaiters.TryRemove(agentId, out _);
            }

            return finished == completion.Task &&
                await completion.Task &&
                IsCurrentBackupTreeLoad(agentId, loadVersion);
        }

        private bool HasRemoteDriveNodes(string agentId) =>
            tvRemoteFolders.Nodes
                .Cast<TreeNode>()
                .Any(node =>
                    TryGetRemoteNodeTag(node, out RemoteNodeTag? tag) &&
                    tag != null &&
                    tag.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase));

        private bool IsCurrentBackupTreeLoad(string agentId, int loadVersion) =>
            loadVersion == _backupConfigLoadVersion &&
            agentId.Equals(GetSelectedBackupAgentId(), StringComparison.OrdinalIgnoreCase);

        private static bool HasRemoteLoadingPlaceholder(TreeNode node) =>
            node.Nodes.Count == 1 &&
            node.Nodes[0].Tag == null &&
            node.Nodes[0].Text.Equals("Loading...", StringComparison.Ordinal);

        private static string GetRemoteDirectoryLoadKey(string agentId, string remotePath) =>
            agentId.Trim() + "\0" + NormalizeRemotePath(remotePath);

        private void NotifyRemoteDrivesLoaded(string agentId, bool success)
        {
            if (_remoteDriveLoadWaiters.TryGetValue(agentId, out TaskCompletionSource<bool>? completion))
            {
                completion.TrySetResult(success);
            }
        }

        private void NotifyRemoteDirectoryLoaded(string agentId, string remotePath, bool success)
        {
            string key = GetRemoteDirectoryLoadKey(agentId, remotePath);
            if (_remoteDirectoryLoadWaiters.TryGetValue(key, out TaskCompletionSource<bool>? completion))
            {
                completion.TrySetResult(success);
            }
        }

        private void RemoveRemoteDirectoryWaiter(string key, TaskCompletionSource<bool> completion)
        {
            if (_remoteDirectoryLoadWaiters.TryGetValue(key, out TaskCompletionSource<bool>? current) &&
                ReferenceEquals(current, completion))
            {
                _remoteDirectoryLoadWaiters.TryRemove(key, out _);
            }
        }
    }
}
