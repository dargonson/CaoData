
using AgentShared; // ⬅️ Thêm mới    
using System.Collections.Concurrent;
using System.Net; // ⬅️ Thêm mới
using System.Net.Sockets; // ⬅️ Thêm mới
using System.Text; // ⬅️ Thêm mới
using System.Text.Json; // ⬅️ Thêm mới


namespace AgentControl
{
    public partial class Form1 : Form
    {

        // --- BIẾN KHỞI TẠO SOCKET SERVER --- [cite: 989]
        private TcpListener _serverListener;
        private bool _isListening = false;
        // Quản lý danh sách kết nối: key là AgentID, Value gồm TcpClient và thời gian LastSeen
        private ConcurrentDictionary<string, (TcpClient Client, DateTime LastSeen)> _connectedAgents = new ConcurrentDictionary<string, (TcpClient, DateTime)>();
        
        public Form1()
        {
            InitializeComponent();
            lvRemoteFiles.View = View.Details;// Đảm bảo ListView hiển thị dạng bảng và có cột lúc chạy
        }

        private ImageList shellImages = new ImageList();
        private Dictionary<string, int> iconCache = new Dictionary<string, int>();

        private string selectedAgentId = string.Empty;

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
        private async void Form1_Load(object sender, EventArgs e)
        {
            ListboxAgents.AddAgent("PC-NHF-01", "Administrator", "192.168.1.15", "Windows 11", "adsadsads", true);
            ListboxAgents.ItemHeight = 123;
            shellImages.ImageSize = new Size(16, 16);
            shellImages.ColorDepth = ColorDepth.Depth32Bit;
            tvRemoteFolders.ImageList = shellImages;
            tvRemoteFolders.Font = new Font("Segoe UI", 9F);
            tvRemoteFolders.ItemHeight = 24;
            lvRemoteFiles.SmallImageList = shellImages;

            // Khởi tạo Database SQLite ngầm
            await SQLiteHelper.InitializeDatabaseAsync();

            // Load lại danh sách các Agent cũ đã từng kết nối lên giao diện
            await LoadAllAgentsFromDbAsync();
            // Kích hoạt luồng chạy ngầm quét Tim mạch (Heartbeat) chu kỳ 5 giây/lần
            _ = StartServerHeartbeatMonitorAsync();
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
                        int bytesRead = await stream.ReadAsync(sizeBuffer, 0, 4);
                        if (bytesRead == 0) break;

                        int packetSize = BitConverter.ToInt32(sizeBuffer, 0);
                        byte[] dataBuffer = new byte[packetSize];

                        // 2. Đọc đủ số lượng byte của gói dữ liệu JSON
                        int totalReceived = 0;
                        while (totalReceived < packetSize)
                        {
                            int read = await stream.ReadAsync(dataBuffer, totalReceived, packetSize - totalReceived);
                            if (read == 0) break;
                            totalReceived += read;
                        }

                        string jsonStr = Encoding.UTF8.GetString(dataBuffer);
                        var packet = JsonSerializer.Deserialize<SocketPacket>(jsonStr);

                        if (packet != null)
                        {
                            currentAgentID = packet.AgentID;

                            // --- PHÂN LOẠI CÁC LOẠI GÓI TIN ĐỔ VỀ ---

                            if (packet.Type == "BROWSE_DRIVES_RESPONSE")
                            {
                                var drives = JsonSerializer.Deserialize<List<string>>(packet.Data);

                                if (drives != null)
                                {
                                    this.Invoke(new Action(() =>
                                    {
                                        tvRemoteFolders.Nodes.Clear();

                                        foreach (var drive in drives)
                                        {
                                            TreeNode driveNode = new TreeNode(drive);
                                            driveNode.Tag = drive;

                                            TreeNode dummyNode = new TreeNode("*");
                                            driveNode.Nodes.Add(dummyNode);

                                            tvRemoteFolders.Nodes.Add(driveNode);
                                        }
                                    }));
                                }
                            }
                            else if (packet.Type == "REGISTER")
                            {
                                var info = JsonSerializer.Deserialize<AgentInfo>(packet.Data);
                                if (info != null)
                                {
                                    await SQLiteHelper.SaveOrUpdateAgentAsync(packet.AgentID, info, true);
                                    _connectedAgents[packet.AgentID] = (client, DateTime.Now);

                                    this.Invoke(new Action(async () =>
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
                                    try
                                    {
                                        var chunk = JsonSerializer.Deserialize<FileChunkPacket>(packet.Data);
                                        if (chunk != null)
                                        {
                                            // Vì Agent gửi gói tin chỉ kèm DownloadID hoặc FileName, 
                                            // Ta cần lấy chính xác đường dẫn LocalPath lưu trên máy Server đã đăng ký trong SQLite
                                            string localPath = await SQLiteHelper.GetLocalPathByDownloadIdAsync(chunk.DownloadID);

                                            if (string.IsNullOrEmpty(localPath)) return; // Nếu không tìm thấy phiên tải thì bỏ qua

                                            byte[] realBytes = Convert.FromBase64String(chunk.Base64Data);

                                            // FIX LỖI FILESTREAM: Truyền chuẩn chỉ theo C# FileStream(path, mode, access, share)
                                            using (FileStream fs = new FileStream(localPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                                            {
                                                fs.Seek(chunk.Offset, SeekOrigin.Begin);
                                                fs.Write(realBytes, 0, realBytes.Length);
                                            }

                                            long currentDownloaded = chunk.Offset + realBytes.Length;
                                            string status = chunk.IsLastChunk ? "Completed" : "Downloading";

                                            await SQLiteHelper.UpdateDownloadProgressAsync(chunk.DownloadID, currentDownloaded, status);

                                            double percentage = chunk.TotalBytes > 0 ? ((double)currentDownloaded / chunk.TotalBytes) * 100 : 0;

                                            this.Invoke(new Action(() =>
                                            {
                                                if (chunk.IsLastChunk)
                                                {
                                                    MessageBox.Show($"Tải file thành công viên mãn:\n{Path.GetFileName(localPath)}", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                                }
                                            }));
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // Do hàm LogAsync bị lỗi (xem Cụm 2), tạm thời cất lỗi vào Console hoặc xử lý sau
                                        Console.WriteLine($"Lỗi ghi file băm nhỏ: {ex.Message}");
                                    }
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
                        _connectedAgents.TryRemove(currentAgentID, out _);
                        await SQLiteHelper.SetAgentOfflineAsync(currentAgentID);
                        this.Invoke(new Action(async () =>
                        {
                            await LoadAllAgentsFromDbAsync();
                        }));
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
                            item.Client?.Close(); // Ép ngắt kết nối socket cũ công nghệ

                            // Cập nhật SQLite và làm mới UI [cite: 885]
                            await SQLiteHelper.SetAgentOfflineAsync(agentId);
                            this.Invoke(new Action(async () =>
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
        private string FormatSize(long bytes)
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

        private void brndel_Click(object sender, EventArgs e)
        {
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
            if (HasSubDirectories(currentNode.Tag.ToString()))
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
                    var stream = client.GetStream();

                    // Chuỗi hóa gói tin packet
                    string jsonString = System.Text.Json.JsonSerializer.Serialize(requestPacket);
                    byte[] dataBuffer = System.Text.Encoding.UTF8.GetBytes(jsonString);
                    byte[] lengthPrefix = BitConverter.GetBytes(dataBuffer.Length);

                    // Bắn dữ liệu thẳng xuống luồng Socket của máy Agent đang chọn
                    await stream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
                    await stream.WriteAsync(dataBuffer, 0, dataBuffer.Length);
                    await stream.FlushAsync();
                }
            }
        }

        private async void btnCopy_Click(object sender, EventArgs e)
        {
            // 1. Kiểm tra xem người dùng đã chọn Agent và chọn File cần tải chưa
            // Giả sử fen đang lưu ID của Agent được chọn vào biến selectedAgentId lúc click ListBoxAgents
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

            // Bốc phần tử đầu tiên trong danh sách các file được tích chọn
            ListViewItem selectedItem = lvRemoteFiles.CheckedItems[0];

            // Lấy đường dẫn thư mục cha hiện tại đang mở ở Vùng 2 (tvRemoteFolders)
            if (tvRemoteFolders.SelectedNode == null || tvRemoteFolders.SelectedNode.Tag == null)
            {
                MessageBox.Show("Không xác định được thư mục hiện tại của Agent!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string currentFolderPath = tvRemoteFolders.SelectedNode.Tag.ToString();

            // Kết hợp thư mục hiện tại với tên file để ra đường dẫn tuyệt đối chính xác trên máy Agent
            string fileName = selectedItem.Text;
            string remoteFilePath = Path.Combine(currentFolderPath, fileName);

            // Kiểm tra xem có phải là Folder không (Nếu cơ chế phân biệt folder của fen dựa trên Icon hoặc SubItem)
            // Tạm thời xử lý luồng tải File trước cho chuẩn bài băm nhỏ byte
            if (selectedItem.SubItems.Count > 1 && selectedItem.SubItems[1].Text == "Folder")
            {
                MessageBox.Show("Tính năng tải nguyên Thư mục đang được tối ưu, vui lòng chọn File!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2. Cho người dùng chọn nơi lưu file trên Server bằng SaveFileDialog
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.FileName = fileName;
                if (sfd.ShowDialog() != DialogResult.OK) return;

                string localFilePath = sfd.FileName;

                // 3. Khởi tạo một phiên tải xuống độc lập (Bất tử luồng bằng GUID)
                string downloadId = Guid.NewGuid().ToString();

                // 4. Đưa vào SQLite dưới trạng thái 'Waiting'
                // Mẹo: Vì lúc này chưa biết chính xác dung lượng thật từ Agent, ta tạm để 0, Agent sẽ phản hồi kèm TotalBytes sau.
                await SQLiteHelper.AddToDownloadQueueAsync(downloadId, selectedAgentId, remoteFilePath, localFilePath, 0);
                await SQLiteHelper.SaveLogAsync("Download", $"Bắt đầu tạo phiên tải file: {fileName} | ID: {downloadId}");

                // 5. Đóng gói tin ra lệnh gửi xuống Agent đề nghị "Mở luồng đọc file"
                // Gửi kèm vị trí Offset = 0 (Tải mới) hoặc nếu là Resume thì quét DB xem đã có bao nhiêu byte trước đó
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
                    AgentID = selectedAgentId,
                    Data = JsonSerializer.Serialize(downloadRequest)
                };

                // 6. Bắn gói tin lệnh qua luồng Socket điều khiển
                if (_connectedAgents != null && _connectedAgents.TryGetValue(selectedAgentId, out var agentInfo))
                {
                    TcpClient client = agentInfo.Client;
                    if (client != null && client.Connected)
                    {
                        try
                        {
                            var stream = client.GetStream();
                            string jsonString = JsonSerializer.Serialize(requestPacket);
                            byte[] dataBuffer = Encoding.UTF8.GetBytes(jsonString);
                            byte[] lengthPrefix = BitConverter.GetBytes(dataBuffer.Length);

                            await stream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
                            await stream.WriteAsync(dataBuffer, 0, dataBuffer.Length);
                            await stream.FlushAsync();

                            MessageBox.Show("Đã gửi yêu cầu tải file xuống Hàng đợi của Agent!", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            await SQLiteHelper.SaveLogAsync("Error", $"Lỗi bắn gói tin tải file: {ex.Message}");
                            await SQLiteHelper.UpdateDownloadProgressAsync(downloadId, offset, "Error");
                        }
                    }
                }
            }
        }
    }
}
