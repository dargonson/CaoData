
using AgentShared; // ⬅️ Thêm mới
using System.Buffers;
using System.Collections.Concurrent;
using System.Net; // ⬅️ Thêm mới
using System.Net.Sockets; // ⬅️ Thêm mới
using System.Text; // ⬅️ Thêm mới
using System.Text.Json; // ⬅️ Thêm mới


namespace AgentControl
{
    public partial class Form1 : Form
    {

        // --- BIẾN KHỞI TẠO SOCKET SERVER ---
        private TcpListener _serverListener;
        private bool _isListening = false;
        // Quản lý danh sách kết nối: key là AgentID, Value gồm TcpClient và thời gian LastSeen
        private ConcurrentDictionary<string, (TcpClient Client, DateTime LastSeen)> _connectedAgents = new ConcurrentDictionary<string, (TcpClient, DateTime)>();
        private ConcurrentDictionary<string, (long Bytes, DateTime Time)> _downloadSpeedTracker = new ConcurrentDictionary<string, (long, DateTime)>();
        private ConcurrentDictionary<string, string> _downloadLocalPathCache = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<string, DateTime> _downloadDbUpdateTracker = new ConcurrentDictionary<string, DateTime>();
        private ConcurrentDictionary<string, SemaphoreSlim> _agentSendLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        private ConcurrentDictionary<string, string> _pendingDirectoryRequests = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<string, TaskCompletionSource<RemoteFolderFilesResponse>> _pendingFolderFileRequests = new ConcurrentDictionary<string, TaskCompletionSource<RemoteFolderFilesResponse>>();
        private HashSet<string> _activeDownloadBatchIds = new HashSet<string>();
        private bool _activeDownloadBatchNotified = true;
        private bool _isDownloadGridRefreshing = false;
        private DateTime _lastUiUpdate = DateTime.MinValue;
        private Font? _downloadStatusBoldFont;
        public Form1()
        {
            InitializeComponent();
            lvRemoteFiles.View = View.Details;// Đảm bảo ListView hiển thị dạng bảng và có cột lúc chạy
        }

        private ImageList shellImages = new ImageList();
        private Dictionary<string, int> iconCache = new Dictionary<string, int>();

        private string selectedAgentId = string.Empty;

        private sealed class RemoteNodeTag
        {
            public string AgentId { get; }
            public string RemotePath { get; }

            public RemoteNodeTag(string agentId, string remotePath)
            {
                AgentId = agentId;
                RemotePath = remotePath;
            }

            public override string ToString()
            {
                return RemotePath;
            }
        }

        private sealed class RemoteFileItemTag
        {
            public string AgentId { get; }
            public string FullPath { get; }
            public bool IsFolder { get; }

            public RemoteFileItemTag(string agentId, string fullPath, bool isFolder)
            {
                AgentId = agentId;
                FullPath = fullPath;
                IsFolder = isFolder;
            }
        }

        private static bool TryGetRemoteNodeTag(TreeNode? node, out RemoteNodeTag? tag)
        {
            tag = null;
            if (node?.Tag is RemoteNodeTag remoteTag)
            {
                tag = remoteTag;
                return true;
            }

            return false;
        }

        private static string GetRemoteDisplayName(string remotePath)
        {
            remotePath = NormalizeRemotePath(remotePath);
            string trimmedPath = remotePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmedPath);
            return string.IsNullOrWhiteSpace(name) ? remotePath : name;
        }

        private static string NormalizeRemotePath(string remotePath)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                return string.Empty;
            }

            string normalized = remotePath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (normalized.Length == 2 && normalized[1] == ':')
            {
                return normalized + Path.DirectorySeparatorChar;
            }

            if (normalized.Length > 3)
            {
                normalized = normalized.TrimEnd(Path.DirectorySeparatorChar);
            }

            return normalized;
        }

        private TreeNode CreateRemoteFolderNode(string agentId, string remotePath)
        {
            remotePath = NormalizeRemotePath(remotePath);
            int icon = GetRemoteFolderIconIndex(remotePath);
            TreeNode node = new TreeNode(GetRemoteDisplayName(remotePath))
            {
                Tag = new RemoteNodeTag(agentId, remotePath),
                ImageIndex = icon,
                SelectedImageIndex = icon
            };

            node.Nodes.Add(new TreeNode("Loading..."));
            return node;
        }

        private TreeNode? FindRemoteNode(string agentId, string remotePath)
        {
            remotePath = NormalizeRemotePath(remotePath);
            foreach (TreeNode node in tvRemoteFolders.Nodes)
            {
                TreeNode? found = FindRemoteNode(node, agentId, remotePath);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private TreeNode? FindRemoteNode(TreeNode node, string agentId, string remotePath)
        {
            if (node.Tag is RemoteNodeTag tag &&
                tag.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase) &&
                NormalizeRemotePath(tag.RemotePath).Equals(remotePath, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            foreach (TreeNode child in node.Nodes)
            {
                TreeNode? found = FindRemoteNode(child, agentId, remotePath);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private async Task RequestRemoteDirectoryAsync(RemoteNodeTag tag)
        {
            string requestedPath = NormalizeRemotePath(tag.RemotePath);
            _pendingDirectoryRequests[tag.AgentId] = requestedPath;

            if (!_connectedAgents.TryGetValue(tag.AgentId, out var agentInfo))
            {
                return;
            }

            TcpClient client = agentInfo.Client;
            if (client == null || !client.Connected)
            {
                return;
            }

            SocketPacket requestPacket = new SocketPacket
            {
                Type = "GET_DIRECTORY",
                AgentID = tag.AgentId,
                Data = requestedPath
            };

            await SendPacketToAgentAsync(tag.AgentId, client, requestPacket);
        }

        private async Task<RemoteFolderFilesResponse?> RequestRemoteFolderFilesAsync(string agentId, string remoteRootPath)
        {
            if (!_connectedAgents.TryGetValue(agentId, out var agentInfo))
            {
                return null;
            }

            TcpClient client = agentInfo.Client;
            if (client == null || !client.Connected)
            {
                return null;
            }

            string requestId = Guid.NewGuid().ToString();
            var completion = new TaskCompletionSource<RemoteFolderFilesResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingFolderFileRequests[requestId] = completion;

            var request = new RemoteFolderFilesRequest
            {
                RequestID = requestId,
                RemoteRootPath = remoteRootPath
            };

            try
            {
                await SendPacketToAgentAsync(agentId, client, new SocketPacket
                {
                    Type = "GET_FOLDER_FILES",
                    AgentID = agentId,
                    Data = JsonSerializer.Serialize(request)
                });

                Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMinutes(2)));
                if (finished == completion.Task)
                {
                    return await completion.Task;
                }

                _pendingFolderFileRequests.TryRemove(requestId, out _);
                return new RemoteFolderFilesResponse
                {
                    RequestID = requestId,
                    RemoteRootPath = remoteRootPath,
                    Errors = { "Timeout khi Agent liet ke file trong thu muc." }
                };
            }
            catch
            {
                _pendingFolderFileRequests.TryRemove(requestId, out _);
                throw;
            }
        }

        private void CompleteRemoteFolderFilesRequest(RemoteFolderFilesResponse response)
        {
            if (string.IsNullOrWhiteSpace(response.RequestID))
            {
                return;
            }

            if (_pendingFolderFileRequests.TryRemove(response.RequestID, out var completion))
            {
                completion.TrySetResult(response);
            }
        }

        private void RenderRemoteDirectory(string agentId, RemoteDirectoryContent dirContent)
        {
            if (!agentId.Equals(selectedAgentId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            TryGetRemoteNodeTag(tvRemoteFolders.SelectedNode, out RemoteNodeTag? selectedTag);

            string currentPath = NormalizeRemotePath(dirContent.CurrentPath);
            if (string.IsNullOrWhiteSpace(currentPath) &&
                _pendingDirectoryRequests.TryGetValue(agentId, out string pendingPath))
            {
                currentPath = NormalizeRemotePath(pendingPath);
            }

            if (string.IsNullOrWhiteSpace(currentPath) &&
                selectedTag != null &&
                selectedTag.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            {
                currentPath = NormalizeRemotePath(selectedTag.RemotePath);
            }

            if (string.IsNullOrWhiteSpace(currentPath))
            {
                return;
            }

            TreeNode? targetNode = FindRemoteNode(agentId, currentPath);
            if (targetNode == null)
            {
                return;
            }

            List<RemoteFileSystemEntry> folderEntries = dirContent.Folders.Count > 0
                ? dirContent.Folders
                : dirContent.SubFolders.Select(path => new RemoteFileSystemEntry
                {
                    FullPath = path,
                    Name = GetRemoteDisplayName(path),
                    IsFolder = true
                }).ToList();

            List<RemoteFileSystemEntry> fileEntries = dirContent.FileEntries.Count > 0
                ? dirContent.FileEntries
                : dirContent.Files.Select(path => new RemoteFileSystemEntry
                {
                    FullPath = path,
                    Name = Path.GetFileName(path),
                    IsFolder = false,
                    Extension = Path.GetExtension(path)
                }).ToList();

            tvRemoteFolders.BeginUpdate();
            targetNode.Nodes.Clear();
            foreach (RemoteFileSystemEntry folder in folderEntries)
            {
                targetNode.Nodes.Add(CreateRemoteFolderNode(agentId, folder.FullPath));
            }
            tvRemoteFolders.EndUpdate();

            _pendingDirectoryRequests.TryRemove(agentId, out _);

            if (selectedTag == null ||
                !selectedTag.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase) ||
                !NormalizeRemotePath(selectedTag.RemotePath).Equals(currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lvRemoteFiles.BeginUpdate();
            try
            {
                lvRemoteFiles.Items.Clear();

                if (!string.IsNullOrWhiteSpace(dirContent.ErrorMessage))
                {
                    lvRemoteFiles.Items.Add(new ListViewItem("[Error: " + dirContent.ErrorMessage + "]"));
                    return;
                }

                foreach (RemoteFileSystemEntry folder in folderEntries)
                {
                    int icon = GetRemoteFolderIconIndex(folder.FullPath);
                    ListViewItem item = new ListViewItem(string.IsNullOrWhiteSpace(folder.Name) ? GetRemoteDisplayName(folder.FullPath) : folder.Name)
                    {
                        ImageIndex = icon,
                        Tag = new RemoteFileItemTag(agentId, folder.FullPath, true)
                    };
                    item.SubItems.Add("");
                    item.SubItems.Add("File Folder");
                    item.SubItems.Add(folder.LastWriteTime == default ? "" : folder.LastWriteTime.ToString("dd/MM/yyyy HH:mm"));
                    lvRemoteFiles.Items.Add(item);
                }

                foreach (RemoteFileSystemEntry file in fileEntries)
                {
                    int icon = GetRemoteFileIconIndex(file.FullPath);
                    ListViewItem item = new ListViewItem(string.IsNullOrWhiteSpace(file.Name) ? Path.GetFileName(file.FullPath) : file.Name)
                    {
                        ImageIndex = icon,
                        Tag = new RemoteFileItemTag(agentId, file.FullPath, false)
                    };
                    item.SubItems.Add(file.Size > 0 ? FormatSize(file.Size) : "");
                    item.SubItems.Add(string.IsNullOrWhiteSpace(file.Extension) ? Path.GetExtension(file.FullPath) : file.Extension);
                    item.SubItems.Add(file.LastWriteTime == default ? "" : file.LastWriteTime.ToString("dd/MM/yyyy HH:mm"));
                    lvRemoteFiles.Items.Add(item);
                }
            }
            finally
            {
                lvRemoteFiles.EndUpdate();
            }
        }

        private bool HasSubDirectories(string path)
        {
            try
            {
                string[] dirs = Directory.GetDirectories(path);
                foreach (string dir in dirs)
                {
                    DirectoryInfo di = new DirectoryInfo(dir);
                    if ((di.Attributes & FileAttributes.Hidden) == 0 && (di.Attributes & FileAttributes.System) == 0)
                    {
                        return true; // Có thư mục con hợp lệ
                    }
                }
            }
            catch { }
            return false; // Không có thư mục con nào
        }
        private int GetIconIndex(string path)
        {
            string key = path;

            if (iconCache.TryGetValue(key, out int index))
                return index;

            Icon icon = ShellIcon.GetSmallIcon(path);

            shellImages.Images.Add(icon);

            index = shellImages.Images.Count - 1;

            iconCache[key] = index;

            return index;
        }

        private int GetCachedIconIndex(string key, Func<Icon> iconFactory)
        {
            if (iconCache.TryGetValue(key, out int index))
                return index;

            using Icon icon = iconFactory();
            shellImages.Images.Add((Icon)icon.Clone());
            index = shellImages.Images.Count - 1;
            iconCache[key] = index;
            return index;
        }

        private int GetRemoteFolderIconIndex(string remotePath)
        {
            string normalized = NormalizeRemotePath(remotePath);
            bool isDriveRoot = normalized.Length == 3 && normalized[1] == ':' && normalized[2] == Path.DirectorySeparatorChar;
            string key = isDriveRoot ? "__remote_drive" : "__remote_folder";
            string iconPath = isDriveRoot ? normalized : "Folder";
            return GetCachedIconIndex(key, () => ShellIcon.GetSmallIcon(iconPath, true));
        }

        private int GetRemoteFileIconIndex(string remotePath)
        {
            string extension = Path.GetExtension(remotePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".file";
            }

            string key = "__remote_file_" + extension.ToLowerInvariant();
            return GetCachedIconIndex(key, () => ShellIcon.GetSmallIcon("file" + extension, false));
        }
        private void InitDownloadGrid()
        {
            dgvDownloads.Columns.Clear();
            dgvDownloads.AutoGenerateColumns = false;
            dgvDownloads.AllowUserToAddRows = false;
            dgvDownloads.ReadOnly = true;
            dgvDownloads.RowHeadersVisible = false; // Ẩn cột đầu thừa thãi cho gọn
            dgvDownloads.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(dgvDownloads, true, null);

            // 1. Cột Mã Tải (Ẩn ngầm làm mồi tương tác)
            dgvDownloads.Columns.Add(new DataGridViewTextBoxColumn { Name = "DownloadID", HeaderText = "Mã Tải", Visible = false }); // [cite: 7]

            // 2. Cột Tên Tập Tin
            dgvDownloads.Columns.Add(new DataGridViewTextBoxColumn { Name = "FileName", HeaderText = "Tên Tập Tin", Width = 200, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }); // [cite: 8]

            // 3. Cột Dung Lượng
            dgvDownloads.Columns.Add(new DataGridViewTextBoxColumn { Name = "TotalSize", HeaderText = "Dung Lượng", Width = 100 }); // [cite: 9]

            // 4. Cột Tiến Độ (Sử dụng con hàng Custom ProgressBar vừa tạo ở Bước 1)
            var progressCol = new DataGridViewProgressBarColumn();
            progressCol.Name = "Progress";
            progressCol.HeaderText = "Tiến Độ";
            progressCol.Width = 150;
            dgvDownloads.Columns.Add(progressCol); // [cite: 10]

            // 5. Cột Tốc Độ
            dgvDownloads.Columns.Add(new DataGridViewTextBoxColumn { Name = "Speed", HeaderText = "Tốc Độ", Width = 100 }); // [cite: 11]

            // 6. Cột Trạng Thái
            dgvDownloads.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Trạng Thái", Width = 140 }); // [cite: 13, 14]
        }
        private async void Form1_Load(object sender, EventArgs e)
        {
            InitDownloadGrid();
 // Kích hoạt Timer bắt đầu đập chu kỳ 500ms một lần để quét DB lên UI
            tmrUpdateUI.Start();
            //ListboxAgents.AddAgent("PC-NHF-01", "Administrator", "192.168.1.15", "Windows 11", "adsadsads", true);
            ListboxAgents.ItemHeight = 123;
            shellImages.ImageSize = new Size(16, 16);
            shellImages.ColorDepth = ColorDepth.Depth32Bit;
            tvRemoteFolders.ImageList = shellImages;
            tvRemoteFolders.Font = new Font("Segoe UI", 9F);
            tvRemoteFolders.ItemHeight = 24;
            lvRemoteFiles.SmallImageList = shellImages;


            // Khởi tạo Database SQLite ngầm
            await SQLiteHelper.InitializeDatabaseAsync();
            _connectedAgents.Clear();
            await SQLiteHelper.SetAllAgentsOfflineAsync();
            var startupAgents = await SQLiteHelper.GetAllAgentsAsync();
            foreach (var agent in startupAgents)
            {
                await SQLiteHelper.FailPendingDownloadsByAgentAsync(agent["AgentID"]);
            }

            // Load lại danh sách các Agent cũ đã từng kết nối lên giao diện
            await LoadAllAgentsFromDbAsync();
            // Kích hoạt luồng chạy ngầm quét Tim mạch (Heartbeat) chu kỳ 5 giây/lần
            _ = StartServerHeartbeatMonitorAsync();
        }
        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return string.Format("{0:n2} {1}", number, suffixes[counter]);
        }

        private async Task SendPacketToAgentAsync(string agentId, TcpClient client, SocketPacket packet)
        {
            if (client == null || !client.Connected)
            {
                throw new IOException("Agent socket is not connected.");
            }

            SemaphoreSlim sendLock = _agentSendLocks.GetOrAdd(agentId, _ => new SemaphoreSlim(1, 1));
            await sendLock.WaitAsync();
            try
            {
                NetworkStream stream = client.GetStream();
                await TransferFrameProtocol.WriteJsonPacketAsync(stream, packet);
            }
            finally
            {
                sendLock.Release();
            }
        }

        private async void btnKetNoi_Click(object sender, EventArgs e)
        {
            if (!_isListening)
            {
                try
                {
                    // Mở port 9000 lắng nghe tất cả IP đổ tới [cite: 850]
                    _serverListener = new TcpListener(IPAddress.Any, 9000);
                    _serverListener.Start();
                    _isListening = true;

                    btnKetNoi.Text = "Ngắt kết nối"; // Chuyển chữ nút bấm [cite: 851]
                    btnKetNoi.BackColor = System.Drawing.Color.LightCoral;

                    // Kích hoạt luồng bất đồng bộ đứng đón Client kết nối vĩnh viễn
                    _ = ListenForAgentsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Không thể mở Port 9000: " + ex.Message);
                }
            }
            else
            {
                // Thực hiện đóng cửa Server
                _isListening = false;
                _serverListener?.Stop();

                // Đóng toàn bộ socket của các máy con đang thông tuyến
                foreach (var item in _connectedAgents.Values)
                {
                    item.Client?.Close();
                }
                _connectedAgents.Clear();

                btnKetNoi.Text = "Kết nối";
                btnKetNoi.BackColor = System.Drawing.Color.LightGreen;

                // Cập nhật lại toàn bộ DB sang Offline và nạp lại giao diện
                var dbAgents = await SQLiteHelper.GetAllAgentsAsync();
                foreach (var agent in dbAgents)
                {
                    await SQLiteHelper.FailPendingDownloadsByAgentAsync(agent["AgentID"]);
                    await SQLiteHelper.SetAgentOfflineAsync(agent["AgentID"]);
                }
                await LoadAllAgentsFromDbAsync();
            }

        }
        private async Task ListenForAgentsAsync()
        {
            while (_isListening)
            {
                try
                {
                    TcpClient client = await _serverListener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    // Ném Client mới vào một luồng riêng để xử lý nhận gói tin
                    _ = HandleAgentCommunicationAsync(client);
                }
                catch
                {
                    // Tránh crash khi dừng Listener đột ngột
                }
            }
        }

        // Xử lý đọc gói tin từ Agent đổ về (Chống dính gói bằng 4 bytes độ dài) [cite: 876, 991]
        private async Task HandleBinaryDownloadChunkAsync(NetworkStream stream, int frameSize)
        {
            FileChunkPacket? chunk = null;
            int bodySize = 0;

            try
            {
                var binaryFrame = await TransferFrameProtocol.ReadBinaryChunkHeaderAsync(stream, frameSize);
                chunk = binaryFrame.Header;
                bodySize = binaryFrame.BodySize;

                if (chunk == null || string.IsNullOrEmpty(chunk.DownloadID))
                {
                    await TransferFrameProtocol.DrainExactAsync(stream, bodySize);
                    return;
                }

                if (!_downloadLocalPathCache.TryGetValue(chunk.DownloadID, out string localPath))
                {
                    localPath = await SQLiteHelper.GetLocalPathByDownloadIdAsync(chunk.DownloadID);
                    if (!string.IsNullOrEmpty(localPath))
                    {
                        _downloadLocalPathCache[chunk.DownloadID] = localPath;
                    }
                }

                if (string.IsNullOrEmpty(localPath))
                {
                    await TransferFrameProtocol.DrainExactAsync(stream, bodySize);
                    return;
                }

                string? localFolder = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(localFolder))
                {
                    Directory.CreateDirectory(localFolder);
                }

                using (FileStream fs = new FileStream(localPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 128 * 1024, true))
                {
                    fs.Seek(chunk.Offset, SeekOrigin.Begin);
                    await TransferFrameProtocol.CopyExactToAsync(stream, fs, bodySize);
                    await fs.FlushAsync();
                }

                long currentDownloaded = chunk.Offset + bodySize;
                string status = chunk.IsLastChunk ? "Completed" : "Downloading";

                DateTime now = DateTime.Now;
                bool shouldUpdateDb =
                    chunk.IsLastChunk ||
                    !_downloadDbUpdateTracker.TryGetValue(chunk.DownloadID, out DateTime lastUpdate) ||
                    (now - lastUpdate).TotalMilliseconds >= 250;

                if (shouldUpdateDb)
                {
                    await SQLiteHelper.UpdateDownloadProgressAsync(chunk.DownloadID, currentDownloaded, chunk.TotalBytes, status);
                    _downloadDbUpdateTracker[chunk.DownloadID] = now;
                }

                if (chunk.IsLastChunk || (now - _lastUiUpdate).TotalMilliseconds > 150)
                {
                    _lastUiUpdate = now;
                }

                if (chunk.IsLastChunk)
                {
                    _downloadDbUpdateTracker.TryRemove(chunk.DownloadID, out _);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Loi xu ly binary download chunk: {ex.Message}");
                if (chunk != null && !string.IsNullOrEmpty(chunk.DownloadID))
                {
                    long currentDownloaded = Math.Max(0, chunk.Offset);
                    await SQLiteHelper.UpdateDownloadProgressAsync(chunk.DownloadID, currentDownloaded, chunk.TotalBytes, "Error");
                    _downloadSpeedTracker.TryRemove(chunk.DownloadID, out _);
                    _downloadDbUpdateTracker.TryRemove(chunk.DownloadID, out _);
                }
            }
        }

        private async Task HandleAgentCommunicationAsync(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                byte[] sizeBuffer = new byte[4];
                string currentAgentID = string.Empty;

                try
                {
                    while (_isListening)
                    {
                        // 1. Đọc 4 bytes đầu lấy kích thước gói
                        int prefixBytesRead = await TransferFrameProtocol.ReadExactOrEndAsync(stream, sizeBuffer, 0, sizeBuffer.Length);
                        if (prefixBytesRead == 0) break;
                        if (prefixBytesRead != sizeBuffer.Length) throw new EndOfStreamException();

                        int packetSize = BitConverter.ToInt32(sizeBuffer, 0);
                        if (packetSize <= 0) throw new InvalidDataException("Invalid packet size.");

                        byte[] firstByteBuffer = new byte[1];
                        await TransferFrameProtocol.ReadExactAsync(stream, firstByteBuffer, 0, 1);

                        if (firstByteBuffer[0] == TransferFrameProtocol.BinaryDownloadChunkMarker)
                        {
                            await HandleBinaryDownloadChunkAsync(stream, packetSize);
                            continue;
                        }

                        byte[] dataBuffer = ArrayPool<byte>.Shared.Rent(packetSize);

                        // 2. Đọc đủ số lượng byte của gói dữ liệu JSON
                        SocketPacket? packet = null;
                        try
                        {
                            dataBuffer[0] = firstByteBuffer[0];
                            await TransferFrameProtocol.ReadExactAsync(stream, dataBuffer, 1, packetSize - 1);
                            string jsonStr = Encoding.UTF8.GetString(dataBuffer, 0, packetSize);
                            packet = JsonSerializer.Deserialize<SocketPacket>(jsonStr);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(dataBuffer);
                        }

                        if (packet != null)
                        {
                            currentAgentID = packet.AgentID;

                            // --- PHÂN LOẠI CÁC LOẠI GÓI TIN ĐỔ VỀ ---

                            if (packet.Type == "BROWSE_DRIVES_RESPONSE")
                            {
                                var drives = JsonSerializer.Deserialize<List<string>>(packet.Data);

                                if (drives != null)
                                {
                                    this.BeginInvoke(new Action(() =>
                                    {
                                        if (!packet.AgentID.Equals(selectedAgentId, StringComparison.OrdinalIgnoreCase))
                                        {
                                            return;
                                        }

                                        if (tvRemoteFolders.ImageList == null)
                                        {
                                            tvRemoteFolders.ImageList = shellImages;
                                        }

                                        tvRemoteFolders.Nodes.Clear();
                                        lvRemoteFiles.Items.Clear();

                                        foreach (var drive in drives)
                                        {
                                            tvRemoteFolders.Nodes.Add(CreateRemoteFolderNode(packet.AgentID, drive));
                                        }
                                    }));
                                }
                            }
                            else if (packet.Type == "GET_DIRECTORY_RESPONSE")
                            {
                                if (!string.IsNullOrEmpty(packet.Data))
                                {
                                    var dirContent = JsonSerializer.Deserialize<RemoteDirectoryContent>(packet.Data);
                                    if (dirContent != null)
                                    {
                                        this.BeginInvoke(new Action(() =>
                                        {
                                            RenderRemoteDirectory(packet.AgentID, dirContent);
                                        }));
                                    }
                                }
                            }
                            else if (packet.Type == "GET_FOLDER_FILES_RESPONSE")
                            {
                                if (!string.IsNullOrEmpty(packet.Data))
                                {
                                    var response = JsonSerializer.Deserialize<RemoteFolderFilesResponse>(packet.Data);
                                    if (response != null)
                                    {
                                        CompleteRemoteFolderFilesRequest(response);
                                    }
                                }
                            }
                            else if (packet.Type == "REGISTER")
                            {
                                var info = System.Text.Json.JsonSerializer.Deserialize<AgentInfo>(packet.Data);
                                if (info != null)
                                {
                                    await SQLiteHelper.SaveOrUpdateAgentAsync(packet.AgentID, info, true);
                                    // [ĐÃ CÓ SẴN] Lưu hoặc cập nhật thông tin Agent vào danh sách quản lý
                                    // [ĐÃ CÓ SẴN CỦA FEN] Lưu thông tin kết nối
                                    if (_connectedAgents.TryGetValue(packet.AgentID, out var oldAgent) && !ReferenceEquals(oldAgent.Client, client))
                                    {
                                        oldAgent.Client?.Close();
                                    }
                                    _connectedAgents[packet.AgentID] = (client, DateTime.Now);

                                    // 🚀 Tự động quét hàng đợi để Resume khi reconnect (Tách luồng an toàn)
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            // 1. Quét DB lấy danh sách dở dang
                                            var pendingDownloads = await SQLiteHelper.GetPendingDownloadsByAgentAsync(packet.AgentID);

                                            foreach (var job in pendingDownloads)
                                            {
                                                // 2. Sử dụng chuẩn DownloadedBytes cho DownloadJobDto của fen
                                                var resumeModel = new DownloadRequestModel
                                                {
                                                    DownloadID = job.DownloadID,
                                                    RemotePath = job.RemotePath,
                                                    Offset = job.DownloadedBytes // Sửa thành DownloadedBytes là hết gạch đỏ!
                                                };

                                                var responsePacket = new SocketPacket
                                                {
                                                    Type = "REQUEST_DOWNLOAD",
                                                    AgentID = packet.AgentID,
                                                    Data = JsonSerializer.Serialize(resumeModel)
                                                };

                                                await SendPacketToAgentAsync(packet.AgentID, client, responsePacket);

                                                // 4. Cập nhật trạng thái
                                                await SQLiteHelper.UpdateDownloadProgressAsync(job.DownloadID, job.DownloadedBytes, "Downloading");
                                            }
                                        }
                                        catch { /* Cô lập luồng chạy ngầm để không sập Tool */ }
                                    });
                                    this.BeginInvoke(new Action(async () =>
                                    {
                                        await LoadAllAgentsFromDbAsync();
                                    }));
                                }
                            }
                            else if (packet.Type == "HEARTBEAT")
                            {
                                if (_connectedAgents.ContainsKey(packet.AgentID))
                                {
                                    _connectedAgents[packet.AgentID] = (client, DateTime.Now);

                                    var dbAgents = await SQLiteHelper.GetAllAgentsAsync();
                                    var matched = dbAgents.Find(a => a["AgentID"] == packet.AgentID);
                                    if (matched != null)
                                    {
                                        var info = new AgentInfo
                                        {
                                            MachineName = matched["MachineName"],
                                            Username = matched["Username"],
                                            IPAddress = matched["IPAddress"],
                                            OSVersion = matched["OSVersion"],
                                            AgentVersion = matched["AgentVersion"].Replace("Version: ", "")
                                        };
                                        await SQLiteHelper.SaveOrUpdateAgentAsync(packet.AgentID, info, true);
                                    }
                                }
                            }
                            // 🌟 TÍNH NĂNG MỚI: Xử lý nhận khối dữ liệu băm nhỏ (File Chunk) đổ về từ Agent
                            else if (packet.Type == "DOWNLOAD_CHUNK")
                            {
                                if (!string.IsNullOrEmpty(packet.Data))
                                {
                                    // 🌟 GIẢI PHÁP VÀNG: Đẩy toàn bộ tác vụ IO nặng (Ghi file, Base64, SQLite) ra luồng xử lý ngầm biệt lập
                                    _ = Task.Run(async () =>
                                    {
                                        FileChunkPacket? chunk = null;
                                        try
                                        {
                                            chunk = JsonSerializer.Deserialize<FileChunkPacket>(packet.Data);
                                            if (chunk != null)
                                            {
                                                if (!_downloadLocalPathCache.TryGetValue(chunk.DownloadID, out string localPath))
                                                {
                                                    localPath = await SQLiteHelper.GetLocalPathByDownloadIdAsync(chunk.DownloadID);
                                                    if (!string.IsNullOrEmpty(localPath))
                                                    {
                                                        _downloadLocalPathCache[chunk.DownloadID] = localPath;
                                                    }
                                                }

                                                if (string.IsNullOrEmpty(localPath)) return;

                                                // Giải mã nhị phân nặng từ chuỗi Base64
                                                byte[] realBytes = Convert.FromBase64String(chunk.Base64Data);

                                                string? localFolder = Path.GetDirectoryName(localPath);
                                                if (!string.IsNullOrEmpty(localFolder))
                                                {
                                                    Directory.CreateDirectory(localFolder);
                                                }

                                                // Mở luồng ghi ổ cứng độc lập
                                                using (FileStream fs = new FileStream(localPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                                                {
                                                    fs.Seek(chunk.Offset, SeekOrigin.Begin);
                                                    fs.Write(realBytes, 0, realBytes.Length);
                                                }

                                                long currentDownloaded = chunk.Offset + realBytes.Length;
                                                string status = chunk.IsLastChunk ? "Completed" : "Downloading";

                                                DateTime now = DateTime.Now;
                                                bool shouldUpdateDb =
                                                    chunk.IsLastChunk ||
                                                    !_downloadDbUpdateTracker.TryGetValue(chunk.DownloadID, out DateTime lastUpdate) ||
                                                    (now - lastUpdate).TotalMilliseconds >= 250;

                                                if (shouldUpdateDb)
                                                {
                                                    // Cập nhật DB ngầm không làm nghẽn Socket, gồm cả tổng dung lượng để UI vẽ đúng tiến độ
                                                    await SQLiteHelper.UpdateDownloadProgressAsync(chunk.DownloadID, currentDownloaded, chunk.TotalBytes, status);
                                                    _downloadDbUpdateTracker[chunk.DownloadID] = now;
                                                }

                                                // Kiểm soát tần suất vẽ giao diện (Chặn UI nghẽn mạch)
                                                if (chunk.IsLastChunk || (now - _lastUiUpdate).TotalMilliseconds > 150)
                                                {
                                                    _lastUiUpdate = now;
                                                }

                                                if (chunk.IsLastChunk)
                                                {
                                                    _downloadDbUpdateTracker.TryRemove(chunk.DownloadID, out _);
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Lỗi xử lý luồng ghi file độc lập: {ex.Message}");
                                            if (chunk != null)
                                            {
                                                long currentDownloaded = Math.Max(0, chunk.Offset);
                                                await SQLiteHelper.UpdateDownloadProgressAsync(chunk.DownloadID, currentDownloaded, chunk.TotalBytes, "Error");
                                                _downloadSpeedTracker.TryRemove(chunk.DownloadID, out _);
                                                _downloadDbUpdateTracker.TryRemove(chunk.DownloadID, out _);
                                            }
                                        }
                                    });
                                }
                            }
                            else if (packet.Type == "DOWNLOAD_ERROR")
                            {
                                if (!string.IsNullOrEmpty(packet.Data))
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            var error = JsonSerializer.Deserialize<DownloadErrorPacket>(packet.Data);
                                            if (error == null || string.IsNullOrEmpty(error.DownloadID)) return;

                                            long downloadedBytes = Math.Max(0, error.DownloadedBytes);
                                            await SQLiteHelper.UpdateDownloadProgressAsync(error.DownloadID, downloadedBytes, error.TotalBytes, "Error");
                                            await SQLiteHelper.SaveLogAsync("Download Error", $"{error.RemotePath}: {error.ErrorMessage}");

                                            _downloadSpeedTracker.TryRemove(error.DownloadID, out _);
                                            _downloadLocalPathCache.TryRemove(error.DownloadID, out _);
                                            _downloadDbUpdateTracker.TryRemove(error.DownloadID, out _);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Lỗi xử lý DOWNLOAD_ERROR: {ex.Message}");
                                        }
                                    });
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Lỗi ngắt kết nối đột ngột từ Agent
                }
                finally
                {
                    // Tiến hành dọn dẹp khi Agent đứt kết nối
                    if (!string.IsNullOrEmpty(currentAgentID))
                    {
                        bool isCurrentSocket = _connectedAgents.TryGetValue(currentAgentID, out var activeAgent) && ReferenceEquals(activeAgent.Client, client);
                        if (isCurrentSocket)
                        {
                            _connectedAgents.TryRemove(currentAgentID, out _);
                            _agentSendLocks.TryRemove(currentAgentID, out _);
                            await SQLiteHelper.FailPendingDownloadsByAgentAsync(currentAgentID);
                            await SQLiteHelper.SetAgentOfflineAsync(currentAgentID);
                            this.BeginInvoke(new Action(async () =>
                            {
                                await LoadAllAgentsFromDbAsync();
                            }));
                        }
                    }
                }
            }
        }

        // TÍNH NĂNG 10: QUÉT TIM MẠCH - QUÁ 90 GIÂY KHÔNG PHẢI HỒI THÌ ĐÁ SANG OFFLINE [cite: 856, 857, 992]
        private async Task StartServerHeartbeatMonitorAsync()
        {
            while (true)
            {
                await Task.Delay(5000); // Cứ mỗi 5 giây rà soát một lần [cite: 885]

                foreach (var agentId in _connectedAgents.Keys)
                {
                    if (_connectedAgents.TryGetValue(agentId, out var item))
                    {
                        // Kiểm tra nếu thời gian hiện tại trừ đi LastSeen vượt quá 90 giây [cite: 857, 885]
                        if ((DateTime.Now - item.LastSeen).TotalSeconds > 90)
                        {
                            _connectedAgents.TryRemove(agentId, out _);
                            _agentSendLocks.TryRemove(agentId, out _);
                            item.Client?.Close(); // Ép ngắt kết nối socket cũ công nghệ

                            // Cập nhật SQLite và làm mới UI [cite: 885]
                            await SQLiteHelper.FailPendingDownloadsByAgentAsync(agentId);
                            await SQLiteHelper.SetAgentOfflineAsync(agentId);
                            this.BeginInvoke(new Action(async () =>
                            {
                                await LoadAllAgentsFromDbAsync();
                            }));
                        }
                    }
                }
            }
        }
        private void btnLietKe_Click(object sender, EventArgs e)
        {
            tvRemoteFolders.Nodes.Clear();

            TreeNode rootNode = new TreeNode("This Computer");
            rootNode.Tag = "ROOT";

            tvRemoteFolders.Nodes.Add(rootNode);

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                    continue;

                TreeNode driveNode = new TreeNode(drive.Name);

                driveNode.Tag = drive.RootDirectory.FullName;

                int icon = GetIconIndex(drive.RootDirectory.FullName);

                driveNode.ImageIndex = icon;
                driveNode.SelectedImageIndex = icon;

                driveNode.Nodes.Add("Loading...");

                rootNode.Nodes.Add(driveNode);
            }

            rootNode.Expand();
        }

        private void tvRemoteFolders_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            TreeNode currentNode = e.Node;

            // Nếu là nút gốc "This Computer" hoặc nút không có đường dẫn thì bỏ qua
            if (currentNode.Tag == null || currentNode.Tag.ToString() == "ROOT") return;

            if (TryGetRemoteNodeTag(currentNode, out RemoteNodeTag? remoteTag) && remoteTag != null)
            {
                if (currentNode.Nodes.Count == 1 && currentNode.Nodes[0].Text == "Loading...")
                {
                    _ = RequestRemoteDirectoryAsync(remoteTag);
                }

                return;
            }

            // Kiểm tra xem có phải đang chứa nút giả "Loading..." không
            if (currentNode.Nodes.Count == 1 && currentNode.Nodes[0].Text == "Loading...")
            {
                currentNode.Nodes.Clear(); // Xóa nút giả đi

                string fullPath = currentNode.Tag.ToString();

                try
                {
                    // Quét các thư mục con trực tiếp bên trong đường dẫn này
                    string[] subDirectories = Directory.GetDirectories(fullPath);

                    foreach (string dir in subDirectories)
                    {
                        DirectoryInfo dirInfo = new DirectoryInfo(dir);

                        // Bỏ qua các thư mục ẩn hệ thống để đỡ bị lỗi quyền truy cập
                        if ((dirInfo.Attributes & FileAttributes.Hidden) == 0)
                        {
                            int icon = GetIconIndex(dirInfo.FullName);

                            TreeNode subNode = new TreeNode(dirInfo.Name)
                            {
                                Tag = dirInfo.FullName,
                                ImageIndex = icon,
                                SelectedImageIndex = icon
                            };

                            // CHỈ ADD NÚT GIẢ NẾU THỰC SỰ CÓ THƯ MỤC CON BÊN TRONG (Mất dấu + oan)
                            if (HasSubDirectories(dirInfo.FullName))
                            {
                                subNode.Nodes.Add(new TreeNode("Loading..."));
                            }

                            currentNode.Nodes.Add(subNode);
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Xử lý khi gặp thư mục hệ thống bị cấm truy cập (ví dụ System Volume Information)
                    currentNode.Nodes.Add(new TreeNode("[Access Denied]"));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi: " + ex.Message);
                }
            }
        }

        private void tvRemoteFolders_AfterSelect(object sender, TreeViewEventArgs e)
        {
            TreeNode currentNode = e.Node;
            if (currentNode.Tag == null || currentNode.Tag.ToString() == "ROOT") return;

            if (TryGetRemoteNodeTag(currentNode, out RemoteNodeTag? remoteTag) && remoteTag != null)
            {
                selectedAgentId = remoteTag.AgentId;
                lvRemoteFiles.Items.Clear();
                _ = RequestRemoteDirectoryAsync(remoteTag);
                return;
            }

            lvRemoteFiles.Items.Clear();
            string targetPath = currentNode.Tag.ToString();
            if (!Directory.Exists(targetPath)) return;

            try
            {
                DirectoryInfo di = new DirectoryInfo(targetPath);

                // --- CẬP NHẬT ĐỒNG BỘ VÙNG 2 (CÂY THƯ MỤC) CH TRƯỚC ---
                DirectoryInfo[] subDirs = di.GetDirectories();

                bool isNeverExpanded = (currentNode.Nodes.Count == 1 && currentNode.Nodes[0].Text == "Loading...");

                if (!isNeverExpanded)
                {
                    // --- CẬP NHẬT ĐỒNG BỘ VÙNG 2 (Chỉ chạy khi thư mục ĐÃ từng được mở) ---
                    System.Collections.Generic.HashSet<string> realSubDirPaths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (DirectoryInfo dir in subDirs)
                    {
                        if ((dir.Attributes & FileAttributes.Hidden) == 0 && (dir.Attributes & FileAttributes.System) == 0)
                        {
                            realSubDirPaths.Add(dir.FullName);
                        }
                    }

                    // 1. Xóa nút cũ nếu ngoài đời thực đã mất
                    for (int i = currentNode.Nodes.Count - 1; i >= 0; i--)
                    {
                        TreeNode subNode = currentNode.Nodes[i];
                        if (subNode.Text == "Loading...") continue;

                        if (subNode.Tag != null && !realSubDirPaths.Contains(subNode.Tag.ToString()))
                        {
                            currentNode.Nodes.RemoveAt(i);
                        }
                    }

                    // 2. Thêm nút mới nếu ngoài đời thực mới tạo thêm (Ví dụ folder 123)
                    foreach (DirectoryInfo dir in subDirs)
                    {
                        if ((dir.Attributes & FileAttributes.Hidden) == 0 && (dir.Attributes & FileAttributes.System) == 0)
                        {
                            bool isExistOnTree = false;
                            foreach (TreeNode subNode in currentNode.Nodes)
                            {
                                if (subNode.Tag != null && subNode.Tag.ToString().Equals(dir.FullName, StringComparison.OrdinalIgnoreCase))
                                {
                                    isExistOnTree = true;
                                    break;
                                }
                            }

                            if (!isExistOnTree)
                            {
                                int icon = GetIconIndex(dir.FullName);

                                TreeNode newSubNode = new TreeNode(dir.Name)
                                {
                                    Tag = dir.FullName,
                                    ImageIndex = icon,
                                    SelectedImageIndex = icon
                                };
                                if (HasSubDirectories(dir.FullName))
                                {
                                    newSubNode.Nodes.Add(new TreeNode("Loading..."));
                                }
                                currentNode.Nodes.Add(newSubNode);
                            }
                        }
                    }
                } // <--- KẾT THÚC KHỐI BLOCK FIX BUG

                // --- BƯỚC B: HIỂN THỊ LÊN VÙNG 3 (DỰA TRÊN DỮ LIỆU ĐÃ ĐỒNG BỘ) ---
                // Hiện Thư mục ở Vùng 3
                foreach (DirectoryInfo dir in subDirs)
                {
                    if ((dir.Attributes & FileAttributes.Hidden) == 0 && (dir.Attributes & FileAttributes.System) == 0)
                    {
                        int icon = GetIconIndex(dir.FullName);
                        ListViewItem item = new ListViewItem(dir.Name)
                        {
                            ImageIndex = icon
                        };
                        item.SubItems.Add("");
                        item.SubItems.Add("File Folder");
                        item.SubItems.Add(dir.LastWriteTime.ToString("dd/MM/yyyy HH:mm"));
                        lvRemoteFiles.Items.Add(item);
                    }
                }

                // Hiện File ở Vùng 3
                FileInfo[] files = di.GetFiles();
                foreach (FileInfo file in files)
                {
                    if ((file.Attributes & FileAttributes.Hidden) == 0)
                    {
                        int icon = GetIconIndex(file.FullName);

                        ListViewItem item = new ListViewItem(file.Name)
                        {
                            ImageIndex = icon
                        };
                        item.SubItems.Add(FormatSize(file.Length));
                        item.SubItems.Add(file.Extension);
                        item.SubItems.Add(file.LastWriteTime.ToString("dd/MM/yyyy HH:mm"));
                        lvRemoteFiles.Items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                lvRemoteFiles.Items.Add(new ListViewItem("[Lỗi hệ thống: " + ex.Message + "]"));
            }
        }
        private string convertFormatSize(long bytes)
        {
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }
            return $"{dblSByte:0.##} {suffix[i]}";
        }

        private string FormatSpeed(long bytesPerSecond)
        {
            if (bytesPerSecond <= 0)
            {
                return "0 B/s";
            }

            return $"{convertFormatSize(bytesPerSecond)}/s";
        }

        private bool ApplyDownloadRowStyle(DataGridViewRow row, string status)
        {
            bool changed = false;
            DataGridViewCell statusCell = row.Cells["Status"];
            statusCell.Style.Font = null;
            statusCell.Style.ForeColor = dgvDownloads.DefaultCellStyle.ForeColor;
            changed |= SetCellValueIfChanged(statusCell, status);

            if (status.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                changed |= SetCellValueIfChanged(row.Cells["Progress"], 0);
                changed |= SetCellValueIfChanged(statusCell, "✖ Error");
                statusCell.Style.ForeColor = Color.FromArgb(220, 53, 69);
                _downloadStatusBoldFont ??= new Font(dgvDownloads.Font, FontStyle.Bold);
                statusCell.Style.Font = _downloadStatusBoldFont;
            }
            else if (status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            {
                changed |= SetCellValueIfChanged(row.Cells["Progress"], 100);
                changed |= SetCellValueIfChanged(statusCell, "✓ Complete");
                statusCell.Style.ForeColor = Color.FromArgb(25, 135, 84);
                _downloadStatusBoldFont ??= new Font(dgvDownloads.Font, FontStyle.Bold);
                statusCell.Style.Font = _downloadStatusBoldFont;
            }

            return changed;
        }

        private bool SetCellValueIfChanged(DataGridViewCell cell, object value)
        {
            if (Equals(cell.Value, value))
            {
                return false;
            }

            cell.Value = value;
            return true;
        }

        private bool IsFolderItem(ListViewItem item)
        {
            if (item.SubItems.Count > 2 && item.SubItems[2].Text.Equals("File Folder", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return item.SubItems.Count > 1 && item.SubItems[1].Text.Equals("Folder", StringComparison.OrdinalIgnoreCase);
        }

        private void AddFolderFilesToDownloadList(string remoteFolderPath, string localFolderPath, List<(string RemotePath, string LocalPath)> files)
        {
            try
            {
                Directory.CreateDirectory(localFolderPath);
            }
            catch { }

            try
            {
                foreach (string filePath in Directory.GetFiles(remoteFolderPath))
                {
                    files.Add((filePath, Path.Combine(localFolderPath, Path.GetFileName(filePath))));
                }
            }
            catch { }

            try
            {
                foreach (string subFolderPath in Directory.GetDirectories(remoteFolderPath))
                {
                    string childLocalFolder = Path.Combine(localFolderPath, Path.GetFileName(subFolderPath));
                    AddFolderFilesToDownloadList(subFolderPath, childLocalFolder, files);
                }
            }
            catch { }
        }

        private void brndel_Click(object sender, EventArgs e)
        {
            if (TryGetRemoteNodeTag(tvRemoteFolders.SelectedNode, out RemoteNodeTag? deleteRemoteTag) && deleteRemoteTag != null)
            {
                MessageBox.Show("Chức năng xóa dữ liệu remote chưa được bật cho Agent đang chọn.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (lvRemoteFiles.CheckedItems.Count == 0)
            {
                MessageBox.Show("Vui lòng tích chọn ít nhất một mục để xóa!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa {lvRemoteFiles.CheckedItems.Count} mục đã chọn không?\nHành động này sẽ đưa mục vào Thùng rác.",
                "Xác nhận xóa",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (confirm == DialogResult.Yes)
            {
                if (tvRemoteFolders.SelectedNode == null || tvRemoteFolders.SelectedNode.Tag == null) return;

                // Lấy nút cha hiện tại đang được chọn ở Vùng 2
                TreeNode parentNode = tvRemoteFolders.SelectedNode;
                string currentFolderPath = parentNode.Tag.ToString();

                System.Collections.Generic.List<ListViewItem> itemsToRemove = new System.Collections.Generic.List<ListViewItem>();

                foreach (ListViewItem checkedItem in lvRemoteFiles.CheckedItems)
                {
                    string itemName = checkedItem.Text;
                    string fullPath = Path.Combine(currentFolderPath, itemName);

                    try
                    {
                        // THỦ TỤC XOÁ VẬT LÝ VÀ ĐỒNG BỘ CÂY THƯ MỤC
                        if (Directory.Exists(fullPath))
                        {
                            // 1. Xóa Folder dưới ổ đĩa đưa vào thùng rác
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(fullPath, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

                            // 2. ĐỒNG BỘ VÙNG 2: Tìm nút con có tên trùng khớp trên cây TreeView để xóa
                            foreach (TreeNode subNode in parentNode.Nodes)
                            {
                                // Kiểm tra nếu đường dẫn lưu trong Tag trùng khớp với Folder vừa xóa
                                if (subNode.Tag != null && subNode.Tag.ToString().Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    parentNode.Nodes.Remove(subNode); // Xóa nút này khỏi Vùng 2 luôn
                                    break; // Tìm thấy và xóa xong thì thoát vòng lặp ngay
                                }
                            }
                        }
                        else if (File.Exists(fullPath))
                        {
                            // Nếu là File thì chỉ cần xóa dưới ổ đĩa (vì Vùng 2 không hiển thị File nên không cần đồng bộ)
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(fullPath, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        }

                        itemsToRemove.Add(checkedItem);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Lỗi khi xóa mục {itemName}: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }

                // Cập nhật lại giao diện Vùng 3
                foreach (ListViewItem item in itemsToRemove)
                {
                    lvRemoteFiles.Items.Remove(item);
                }

                MessageBox.Show("Đã hoàn thành xóa và đồng bộ dữ liệu!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void tvRemoteFolders_BeforeCollapse(object sender, TreeViewCancelEventArgs e)
        {
            TreeNode currentNode = e.Node;
            if (currentNode.Tag == null || currentNode.Tag.ToString() == "ROOT") return;

            // Khi thu nhỏ lại, xóa hết đi và đưa về trạng thái chờ nạp lại mồi nếy thực sự còn thư mục con
            currentNode.Nodes.Clear();
            if (currentNode.Tag is RemoteNodeTag)
            {
                currentNode.Nodes.Add(new TreeNode("Loading..."));
            }
            else if (HasSubDirectories(currentNode.Tag.ToString()))
            {
                currentNode.Nodes.Add(new TreeNode("Loading..."));
            }
        }

        private void lvRemoteFiles_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // Bật cờ hiệu thông báo: "Tôi đang đúp chuột đây, đừng có tự ý tick chọn nha!"


            // 1. Kiểm tra xem người dùng có đúp chuột trúng cái item nào không
            if (lvRemoteFiles.SelectedItems.Count == 0) return;
            ListViewItem clickedItem = lvRemoteFiles.SelectedItems[0];
            string itemName = clickedItem.Text;

            if (clickedItem.Tag is RemoteFileItemTag remoteItem)
            {
                if (!remoteItem.IsFolder)
                {
                    return;
                }

                TreeNode? parentRemoteNode = tvRemoteFolders.SelectedNode;
                if (parentRemoteNode == null)
                {
                    return;
                }

                TreeNode? targetSubNode = FindRemoteNode(remoteItem.AgentId, remoteItem.FullPath);
                if (targetSubNode == null)
                {
                    targetSubNode = CreateRemoteFolderNode(remoteItem.AgentId, remoteItem.FullPath);
                    parentRemoteNode.Nodes.Add(targetSubNode);
                }

                tvRemoteFolders.SelectedNode = targetSubNode;
                targetSubNode.Expand();
                return;
            }

            // Lấy đường dẫn của thư mục cha hiện tại từ Vùng 2
            if (tvRemoteFolders.SelectedNode == null || tvRemoteFolders.SelectedNode.Tag == null) return;
            TreeNode parentNode = tvRemoteFolders.SelectedNode;
            string parentPath = parentNode.Tag.ToString();

            // Ra đường dẫn đầy đủ của mục vừa đúp chuột
            string fullPath = Path.Combine(parentPath, itemName);

            // =========================================================================
            // TRƯỜNG HỢP 1: ĐÚP CHUỘT VÀO THƯ MỤC -> DI CHUYỂN VÀO TRONG & ĐỒNG BỘ VÙNG 2
            // =========================================================================
            if (Directory.Exists(fullPath))
            {
                // Bung nút cha hiện tại ra trước để đảm bảo các nút con đã được nạp trên cây
                parentNode.Expand();

                TreeNode targetSubNode = null;

                // Tìm xem trên cây Vùng 2 có nút con nào trùng với thư mục vừa đúp chuột không
                foreach (TreeNode subNode in parentNode.Nodes)
                {
                    if (subNode.Tag != null && subNode.Tag.ToString().Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        targetSubNode = subNode;
                        break;
                    }
                }

                // Nếu tìm thấy nút con tương ứng trên cây Vùng 2
                if (targetSubNode != null)
                {
                    // Bước quan trọng nhất: Gán node được chọn của TreeView bằng node này
                    // Lệnh này sẽ tự động kích hoạt hàm `tvRemoteFolders_AfterSelect` để load dữ liệu mới vào Vùng 3 luôn!
                    tvRemoteFolders.SelectedNode = targetSubNode;

                    // Bung tiếp cái thư mục vừa vào ra để hiển thị dấu + và các nhánh con (ví dụ bung 123 để thấy 456)
                    targetSubNode.Expand();
                }
            }
            // =========================================================================
            // TRƯỜNG HỢP 2: ĐÚP CHUỘT VÀO FILE -> CHẠY LỆNH MỞ FILE CỦA WINDOWS
            // =========================================================================
            else if (File.Exists(fullPath))
            {
                try
                {
                    // Dùng Process để gọi Windows mở file bằng phần mềm mặc định
                    System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo(fullPath)
                    {
                        UseShellExecute = true // Bắt buộc phải bật cái này ở .NET thế hệ mới để chạy được file trực tiếp
                    };
                    System.Diagnostics.Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể mở file này: {ex.Message}", "Lỗi mở file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // Sau khi xử lý đúp chuột xong xuôi, tắt cờ hiệu đi để người dùng click đơn lẻ vẫn tick bình thường

        }

        private void lvRemoteFiles_ItemCheck(object sender, ItemCheckEventArgs e)
        {


            // Lấy tọa độ con trỏ chuột hiện tại so với cái ListView
            System.Drawing.Point mousePos = lvRemoteFiles.PointToClient(Cursor.Position);

            // Thử tìm xem tại tọa độ này có trúng vào item nào không
            ListViewHitTestInfo hitTest = lvRemoteFiles.HitTest(mousePos);

            // Kiểm tra vị trí click chuột cụ thể trên dòng đó
            // Nếu KHÔNG PHẢI click chủ động vào ô vuông Checkbox (Location khác ChkBox)
            if (hitTest.Location != ListViewHitTestLocations.StateImage)
            {
                // Chặn đứng hành động đổi trạng thái (giữ nguyên giá trị cũ)
                e.NewValue = e.CurrentValue;

            }
        }

        private async Task LoadAllAgentsFromDbAsync()
        {
            // 1. Xóa sạch danh sách hiển thị cũ trên ListboxAgents trước khi nạp mới
            ListboxAgents.Items.Clear();

            // 2. Kéo toàn bộ danh sách Agent đã lưu từ file SQLite lên
            var dbAgents = await SQLiteHelper.GetAllAgentsAsync();

            // 3. Duyệt qua từng Agent lấy từ DB để đổ lên giao diện [cite: 3522]
            foreach (var agent in dbAgents)
            {
                bool isOnlineStatus = agent["Status"] == "Online";

                // Gọi đúng hàm custom của fen để nạp dữ liệu
                ListboxAgents.AddAgent(
                agent["MachineName"],
                agent["Username"],
                agent["IPAddress"],
                agent["OSVersion"],
                agent["AgentID"],
                isOnlineStatus
                );
            }
        }
        private async void ListboxAgents_SelectedIndexChanged(object sender, EventArgs e)
        {
            // 1. Trích xuất AgentID từ dòng đang chọn
            if (ListboxAgents.SelectedItem == null) return;

            selectedAgentId = "";
            try
            {
                var selectedItem = ListboxAgents.SelectedItem;
                var prop = selectedItem.GetType().GetProperty("AgentID");
                if (prop != null)
                {
                    selectedAgentId = prop.GetValue(selectedItem)?.ToString() ?? "";
                }
                else
                {
                    selectedAgentId = selectedItem.ToString();
                }
            }
            catch { }

            if (string.IsNullOrEmpty(selectedAgentId)) return;

            tvRemoteFolders.Nodes.Clear();
            lvRemoteFiles.Items.Clear();

            // 2. Khởi tạo gói lệnh cào ổ đĩa
            SocketPacket requestPacket = new SocketPacket
            {
                Type = "BROWSE_DRIVES",
                AgentID = selectedAgentId,
                Data = string.Empty
            };

            // 3. ĐIỀU CHỈNH THEO BIẾN CỦA FEN: Tìm Agent trong _connectedAgents
            if (_connectedAgents != null && _connectedAgents.TryGetValue(selectedAgentId, out var agentInfo))
            {
                // Vì agentInfo của fen là Tuple (Client, LastSeen) nên ta bốc agentInfo.Client ra xài
                TcpClient client = agentInfo.Client;

                if (client != null && client.Connected)
                {
                    await SendPacketToAgentAsync(selectedAgentId, client, requestPacket);
                }
            }
        }

        private async Task<string> AddDownloadJobAndSendRequestAsync(string agentId, string remoteFilePath, string localFilePath)
        {
            string fileName = Path.GetFileName(remoteFilePath);
            string downloadId = Guid.NewGuid().ToString();

            await SQLiteHelper.AddToDownloadQueueAsync(downloadId, agentId, remoteFilePath, localFilePath, 0);
            _downloadLocalPathCache[downloadId] = localFilePath;
            await SQLiteHelper.SaveLogAsync("Download", $"Bắt đầu tạo phiên tải file: {fileName} | ID: {downloadId}");

            long offset = await SQLiteHelper.GetDownloadedBytesOffsetAsync(downloadId);

            var downloadRequest = new
            {
                DownloadID = downloadId,
                RemotePath = remoteFilePath,
                Offset = offset
            };

            SocketPacket requestPacket = new SocketPacket
            {
                Type = "REQUEST_DOWNLOAD",
                AgentID = agentId,
                Data = JsonSerializer.Serialize(downloadRequest)
            };

            if (_connectedAgents != null && _connectedAgents.TryGetValue(agentId, out var agentInfo))
            {
                TcpClient client = agentInfo.Client;
                if (client != null && client.Connected)
                {
                    try
                    {
                        await SendPacketToAgentAsync(agentId, client, requestPacket);
                        await SQLiteHelper.UpdateDownloadProgressAsync(downloadId, offset, "Downloading");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Lỗi bắn gói tin tải file {fileName}: {ex.Message}");
                        await SQLiteHelper.UpdateDownloadProgressAsync(downloadId, offset, "Error");
                    }
                }
                else
                {
                    await SQLiteHelper.UpdateDownloadProgressAsync(downloadId, offset, "Waiting Agent");
                }
            }
            else
            {
                await SQLiteHelper.UpdateDownloadProgressAsync(downloadId, offset, "Waiting Agent");
            }

            return downloadId;
        }

        private async void btnCopy_Click(object sender, EventArgs e)
        {
            // 1. Kiểm tra xem người dùng đã chọn Agent chưa
            if (string.IsNullOrEmpty(selectedAgentId))
            {
                MessageBox.Show("Vui lòng chọn một Agent trước!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Kiểm tra danh sách các file được TÍCH CHỌN ô vuông
            if (lvRemoteFiles.CheckedItems.Count == 0)
            {
                MessageBox.Show("Vui lòng tích chọn ít nhất một file từ danh sách để tải!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Lấy đường dẫn thư mục cha hiện tại đang mở trên TreeView
            if (tvRemoteFolders.SelectedNode == null || tvRemoteFolders.SelectedNode.Tag == null)
            {
                MessageBox.Show("Không xác định được thư mục hiện tại của Agent!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string currentFolderPath = tvRemoteFolders.SelectedNode.Tag.ToString();
            bool isRemoteSelection = TryGetRemoteNodeTag(tvRemoteFolders.SelectedNode, out RemoteNodeTag? currentRemoteTag) && currentRemoteTag != null;
            string copyAgentId = currentRemoteTag != null ? currentRemoteTag.AgentId : selectedAgentId;

            // 2. Thay vì SaveFileDialog (bị bật popup liên tục), ta dùng FolderBrowserDialog để chọn một thư mục lưu chung cho tất cả file
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Chọn thư mục trên Server để lưu tất cả các file tải về:";
                if (fbd.ShowDialog() != DialogResult.OK) return;

                string localTargetFolder = fbd.SelectedPath;
                var filesToDownload = new List<(string RemotePath, string LocalPath)>();
                int skippedFolders = 0;
                var folderErrors = new List<string>();

                foreach (ListViewItem selectedItem in lvRemoteFiles.CheckedItems)
                {
                    if (isRemoteSelection)
                    {
                        if (selectedItem.Tag is not RemoteFileItemTag remoteItem ||
                            !remoteItem.AgentId.Equals(copyAgentId, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (remoteItem.IsFolder)
                        {
                            RemoteFolderFilesResponse? folderResponse = await RequestRemoteFolderFilesAsync(copyAgentId, remoteItem.FullPath);
                            if (folderResponse == null)
                            {
                                skippedFolders++;
                                folderErrors.Add(remoteItem.FullPath + ": Agent khong phan hoi.");
                                continue;
                            }

                            if (folderResponse.Errors.Count > 0)
                            {
                                folderErrors.AddRange(folderResponse.Errors);
                            }

                            string localFolderRoot = Path.Combine(localTargetFolder, GetRemoteDisplayName(remoteItem.FullPath));
                            foreach (RemoteFolderFileEntry folderFile in folderResponse.Files)
                            {
                                string safeRelativePath = folderFile.RelativePath
                                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                                    .TrimStart(Path.DirectorySeparatorChar);
                                filesToDownload.Add((folderFile.RemotePath, Path.Combine(localFolderRoot, safeRelativePath)));
                            }
                            continue;
                        }

                        string remoteOnlyFileName = Path.GetFileName(remoteItem.FullPath);
                        filesToDownload.Add((remoteItem.FullPath, Path.Combine(localTargetFolder, remoteOnlyFileName)));
                        continue;
                    }

                    string fileName = selectedItem.Text;
                    string remoteFilePath = Path.Combine(currentFolderPath, fileName);
                    string localFilePath = Path.Combine(localTargetFolder, fileName);

                    if (IsFolderItem(selectedItem) || Directory.Exists(remoteFilePath))
                    {
                        AddFolderFilesToDownloadList(remoteFilePath, localFilePath, filesToDownload);
                    }
                    else
                    {
                        filesToDownload.Add((remoteFilePath, localFilePath));
                    }
                }

                if (filesToDownload.Count == 0)
                {
                    MessageBox.Show("Không tìm thấy file hợp lệ để tải!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var batchIds = new HashSet<string>();
                foreach (var file in filesToDownload)
                {
                    string? localFolder = Path.GetDirectoryName(file.LocalPath);
                    if (!string.IsNullOrEmpty(localFolder))
                    {
                        Directory.CreateDirectory(localFolder);
                    }

                    string downloadId = await AddDownloadJobAndSendRequestAsync(copyAgentId, file.RemotePath, file.LocalPath);
                    batchIds.Add(downloadId);
                }

                _activeDownloadBatchIds = batchIds;
                _activeDownloadBatchNotified = false;

                if (skippedFolders > 0)
                {
                    MessageBox.Show($"Da bo qua {skippedFolders} thu muc remote vi Agent khong phan hoi.", "Thong bao", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                if (folderErrors.Count > 0)
                {
                    MessageBox.Show($"Co {folderErrors.Count} loi khi liet ke thu muc remote. Cac file doc duoc van da duoc them vao hang doi.", "Thong bao", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                MessageBox.Show($"Đã thêm thành công {filesToDownload.Count} file vào hàng đợi tải xuống.", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private async void btncleardrv_Click(object sender, EventArgs e)
        {
            btncleardrv.Enabled = false;
            try
            {
                var selectedDownloadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataGridViewRow row in dgvDownloads.SelectedRows)
                {
                    string downloadId = row.Cells["DownloadID"].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(downloadId))
                    {
                        selectedDownloadIds.Add(downloadId);
                    }
                }

                if (selectedDownloadIds.Count == 0)
                {
                    await SQLiteHelper.ClearDownloadQueueAsync();
                    _downloadSpeedTracker.Clear();
                    _downloadLocalPathCache.Clear();
                    _downloadDbUpdateTracker.Clear();
                    _activeDownloadBatchIds.Clear();

                    dgvDownloads.Rows.Clear();
                    return;
                }

                await SQLiteHelper.DeleteDownloadsAsync(selectedDownloadIds);
                foreach (string downloadId in selectedDownloadIds)
                {
                    _downloadSpeedTracker.TryRemove(downloadId, out _);
                    _downloadLocalPathCache.TryRemove(downloadId, out _);
                    _downloadDbUpdateTracker.TryRemove(downloadId, out _);
                    _activeDownloadBatchIds.Remove(downloadId);
                }

                for (int i = dgvDownloads.Rows.Count - 1; i >= 0; i--)
                {
                    string rowDownloadId = dgvDownloads.Rows[i].Cells["DownloadID"].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(rowDownloadId) && selectedDownloadIds.Contains(rowDownloadId))
                    {
                        dgvDownloads.Rows.RemoveAt(i);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Khong the clear danh sach download: " + ex.Message, "Loi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btncleardrv.Enabled = true;
            }
        }

        private async void tmrUpdateUI_Tick(object sender, EventArgs e)
        {
            if (_isDownloadGridRefreshing)
            {
                return;
            }

            _isDownloadGridRefreshing = true;
            try
            {
                // 1. Gọi SQLite lấy toàn bộ danh sách hàng đợi hiện tại
                var downloadList = await SQLiteHelper.GetAllDownloadsAsync();
                var rowMap = new Dictionary<string, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);
                var liveDownloadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (DataGridViewRow row in dgvDownloads.Rows)
                {
                    string rowDownloadId = row.Cells["DownloadID"].Value?.ToString();
                    if (!string.IsNullOrEmpty(rowDownloadId) && !rowMap.ContainsKey(rowDownloadId))
                    {
                        rowMap[rowDownloadId] = row;
                    }
                }

                foreach (var job in downloadList)
                {
                    liveDownloadIds.Add(job.DownloadID);
                }

                // 2. Duyệt qua từng bản ghi để cập nhật hoặc thêm mới vào DataGridView
                dgvDownloads.SuspendLayout();
                foreach (var job in downloadList)
                {
                    rowMap.TryGetValue(job.DownloadID, out DataGridViewRow existingRow);

                    // Tính toán phần trăm tiến độ (%) dựa trên số byte đã nạp
                    int progressPercent = 0;
                    if (job.TotalBytes > 0)
                    {
                        progressPercent = (int)((job.DownloadedBytes * 100) / job.TotalBytes);
                        if (progressPercent > 100) progressPercent = 100;
                    }

                    if (job.Status == "Completed" || (job.TotalBytes > 0 && job.DownloadedBytes >= job.TotalBytes))
                    {
                        progressPercent = 100;
                    }

                    // Bốc tách lấy tên file gọn gàng từ đường dẫn từ xa (RemotePath)
                    string fileName = System.IO.Path.GetFileName(job.RemotePath);

                    // 🔥 ĐÃ ĐỔI TÊN HÀM TẠI ĐÂY: Sử dụng convertFormatSize chuẩn chỉ
                    string totalSizeStr = convertFormatSize(job.TotalBytes);

                    string speedStr = "0 B/s";
                    if (job.Status == "Downloading")
                    {
                        DateTime now = DateTime.Now;
                        if (_downloadSpeedTracker.TryGetValue(job.DownloadID, out var previous))
                        {
                            double seconds = (now - previous.Time).TotalSeconds;
                            long deltaBytes = job.DownloadedBytes - previous.Bytes;
                            long bytesPerSecond = seconds > 0 ? (long)(Math.Max(0, deltaBytes) / seconds) : 0;
                            speedStr = FormatSpeed(bytesPerSecond);
                        }

                        _downloadSpeedTracker[job.DownloadID] = (job.DownloadedBytes, now);
                    }
                    else
                    {
                        _downloadSpeedTracker.TryRemove(job.DownloadID, out _);
                    }

                    if (existingRow != null)
                    {
                        // 🔄 Nếu dòng đã có sẵn: cập nhật đủ dữ liệu đang thay đổi trong quá trình tải
                        bool rowChanged = false;
                        rowChanged |= SetCellValueIfChanged(existingRow.Cells["FileName"], fileName);
                        rowChanged |= SetCellValueIfChanged(existingRow.Cells["TotalSize"], totalSizeStr);
                        rowChanged |= SetCellValueIfChanged(existingRow.Cells["Progress"], progressPercent);
                        rowChanged |= SetCellValueIfChanged(existingRow.Cells["Speed"], speedStr);
                        rowChanged |= ApplyDownloadRowStyle(existingRow, job.Status);
                        if (rowChanged)
                        {
                            dgvDownloads.InvalidateRow(existingRow.Index);
                        }
                    }
                    else
                    {
                        // ➕ Nếu là file mới được thêm vào hàng đợi: Tạo dòng mới tinh
                        int rowIndex = dgvDownloads.Rows.Add();
                        DataGridViewRow newRow = dgvDownloads.Rows[rowIndex];

                        newRow.Cells["DownloadID"].Value = job.DownloadID;
                        newRow.Cells["FileName"].Value = fileName;
                        newRow.Cells["TotalSize"].Value = totalSizeStr;
                        newRow.Cells["Progress"].Value = progressPercent;
                        newRow.Cells["Speed"].Value = speedStr;
                        ApplyDownloadRowStyle(newRow, job.Status);
                        rowMap[job.DownloadID] = newRow;
                    }
                }
                dgvDownloads.ResumeLayout();

                // 3. Dọn rác: Nếu file đã bị xóa trong DB thì xóa luôn dòng đó trên UI
                for (int i = dgvDownloads.Rows.Count - 1; i >= 0; i--)
                {
                    string gridId = dgvDownloads.Rows[i].Cells["DownloadID"].Value?.ToString();
                    if (string.IsNullOrEmpty(gridId) || !liveDownloadIds.Contains(gridId))
                    {
                        if (!string.IsNullOrEmpty(gridId))
                        {
                            _downloadSpeedTracker.TryRemove(gridId, out _);
                            _downloadLocalPathCache.TryRemove(gridId, out _);
                            _downloadDbUpdateTracker.TryRemove(gridId, out _);
                        }

                        dgvDownloads.Rows.RemoveAt(i);
                    }
                }

                if (!_activeDownloadBatchNotified && _activeDownloadBatchIds.Count > 0)
                {
                    int matchedCount = 0;
                    int completedCount = 0;
                    int errorCount = 0;
                    bool allTerminal = true;

                    foreach (var job in downloadList)
                    {
                        if (!_activeDownloadBatchIds.Contains(job.DownloadID))
                        {
                            continue;
                        }

                        matchedCount++;
                        if (job.Status == "Completed")
                        {
                            completedCount++;
                        }
                        else if (job.Status == "Error")
                        {
                            errorCount++;
                        }
                        else
                        {
                            allTerminal = false;
                        }
                    }

                    if (matchedCount == _activeDownloadBatchIds.Count && allTerminal)
                    {
                        _activeDownloadBatchNotified = true;
                        _activeDownloadBatchIds.Clear();

                        MessageBox.Show(
                            $"Đã hoàn tất tải xuống.\nThành công: {completedCount}\nLỗi: {errorCount}",
                            "Thông báo",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            }
            catch { /* Cách ly lỗi luồng giao diện */ }
            finally
            {
                if (dgvDownloads.IsHandleCreated)
                {
                    try { dgvDownloads.ResumeLayout(); } catch { }
                }
                _isDownloadGridRefreshing = false;
            }
        }
    }
}
