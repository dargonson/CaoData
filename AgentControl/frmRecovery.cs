using AgentShared;
using System.Runtime.InteropServices;

namespace AgentControl
{
    public partial class frmRecovery : Form
    {
        private const int PbmSetState = 0x0410;
        private const int ProgressNormal = 0x0001;
        private const int ProgressPaused = 0x0003;
        private readonly string _agentId;
        private readonly RecoverySnapshotRepository _repository = new RecoverySnapshotRepository();
        private readonly RecoverySnapshotBuilder _snapshotBuilder;
        private readonly RecoveryFileExtractor _extractor;
        private readonly Dictionary<string, SelectedRecoveryFile> _selectedFiles =
            new Dictionary<string, SelectedRecoveryFile>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _loadCancellation;
        private CancellationTokenSource? _extractCancellation;
        private string _storageRoot = string.Empty;
        private DateTime? _loadedDate;
        private bool _suppressTreeChecks;
        private bool _suppressFileChecks;
        private bool _suppressDateSelection;
        private bool _extracting;
        private bool _closeAfterExtractionStops;

        public frmRecovery(string agentId)
        {
            _agentId = agentId;
            InitializeComponent();
            _snapshotBuilder = new RecoverySnapshotBuilder(_repository);
            _extractor = new RecoveryFileExtractor(_repository);
            ConfigureRuntimeUi();
        }

        private void ConfigureRuntimeUi()
        {
            Text = $"Khôi phục dữ liệu - {_agentId}";
            cbxlistday.DropDownStyle = ComboBoxStyle.DropDownList;
            btnbrowsepathbk.Text = "Browse";
            btnSaveFileBackup.Text = "Lưu file backup";
            pcbbackup.Minimum = 0;
            pcbbackup.Maximum = 1000;
            pcbbackup.Value = 0;

            Load += frmRecovery_Load;
            FormClosing += frmRecovery_FormClosing;
            cbxlistday.SelectedIndexChanged += cbxlistday_SelectedIndexChanged;
            TvBackupFile.BeforeExpand += TvBackupFile_BeforeExpand;
            TvBackupFile.AfterSelect += TvBackupFile_AfterSelect;
            TvBackupFile.AfterCheck += TvBackupFile_AfterCheck;
            lvBackupFiles.ItemCheck += lvBackupFiles_ItemCheck;
            btnbrowsepathbk.Click += btnbrowsepathbk_Click;
            btnSaveFileBackup.Click += btnSaveFileBackup_Click;
        }

        private async void frmRecovery_Load(object? sender, EventArgs e)
        {
            try
            {
                BackupConfiguration? config = await BackupRepository.GetConfigAsync(_agentId);
                if (config == null || string.IsNullOrWhiteSpace(config.ControlStoragePath))
                {
                    throw new InvalidOperationException("Agent chưa có đường dẫn lưu backup trên Control.");
                }
                _storageRoot = Path.GetFullPath(config.ControlStoragePath);

                List<RecoveryPointDate> dates = new List<RecoveryPointDate>();
                await RunWithLoadingAsync("Đang quét các ngày có backup...", async token =>
                {
                    dates = await _snapshotBuilder.DiscoverDatesAsync(_storageRoot, _agentId, token);
                });

                _suppressDateSelection = true;
                cbxlistday.Items.Clear();
                foreach (RecoveryPointDate date in dates) cbxlistday.Items.Add(date);
                _suppressDateSelection = false;

                if (dates.Count == 0)
                {
                    MessageBox.Show(
                        "Không tìm thấy folder FIRST/INC hoàn chỉnh của Agent này.",
                        "Khôi phục dữ liệu",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                cbxlistday.SelectedIndex = 0;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Không thể nạp danh sách backup: " + ex.Message,
                    "Khôi phục dữ liệu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async void cbxlistday_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_suppressDateSelection || cbxlistday.SelectedItem is not RecoveryPointDate point)
            {
                return;
            }

            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = new CancellationTokenSource();
            CancellationToken token = _loadCancellation.Token;
            try
            {
                await RunWithLoadingAsync(
                    $"Đang nạp manifest đến ngày {point.Date:yyyy-MM-dd}...",
                    async _ => await _snapshotBuilder.BuildAsync(
                        _storageRoot, _agentId, point.Date, token));

                token.ThrowIfCancellationRequested();
                _loadedDate = point.Date;
                _selectedFiles.Clear();
                await LoadRootDirectoriesAsync(token);
                lvBackupFiles.Items.Clear();
                Text = $"Khôi phục dữ liệu - {_agentId} - {point.Date:yyyy-MM-dd}";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _loadedDate = null;
                TvBackupFile.Nodes.Clear();
                lvBackupFiles.Items.Clear();
                MessageBox.Show(
                    "Không thể dựng dữ liệu tại ngày đã chọn: " + ex.Message,
                    "Khôi phục dữ liệu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async Task LoadRootDirectoriesAsync(CancellationToken token)
        {
            if (_loadedDate == null) return;
            List<RecoveryDirectoryRecord> directories = await _repository.GetChildDirectoriesAsync(
                _agentId, _loadedDate.Value, string.Empty);
            token.ThrowIfCancellationRequested();
            _suppressTreeChecks = true;
            TvBackupFile.BeginUpdate();
            try
            {
                TvBackupFile.Nodes.Clear();
                foreach (RecoveryDirectoryRecord directory in directories)
                {
                    TvBackupFile.Nodes.Add(CreateDirectoryNode(directory, inheritedChecked: false));
                }
            }
            finally
            {
                TvBackupFile.EndUpdate();
                _suppressTreeChecks = false;
            }
        }

        private TreeNode CreateDirectoryNode(RecoveryDirectoryRecord directory, bool inheritedChecked)
        {
            RecoveryDirectoryNodeTag tag = new RecoveryDirectoryNodeTag(directory.VirtualPath);
            TreeNode node = new TreeNode(directory.DisplayName)
            {
                Tag = tag,
                Checked = inheritedChecked
            };
            if (directory.HasChildren)
            {
                node.Nodes.Add(new TreeNode("Loading...") { Tag = null });
            }
            return node;
        }

        private async Task EnsureChildrenLoadedAsync(TreeNode node)
        {
            if (_loadedDate == null || node.Tag is not RecoveryDirectoryNodeTag tag || tag.ChildrenLoaded)
            {
                return;
            }

            List<RecoveryDirectoryRecord> directories = await _repository.GetChildDirectoriesAsync(
                _agentId, _loadedDate.Value, tag.VirtualPath);
            _suppressTreeChecks = true;
            try
            {
                node.Nodes.Clear();
                foreach (RecoveryDirectoryRecord directory in directories)
                {
                    node.Nodes.Add(CreateDirectoryNode(directory, node.Checked));
                }
                tag.ChildrenLoaded = true;
            }
            finally
            {
                _suppressTreeChecks = false;
            }
        }

        private async void TvBackupFile_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
        {
            if (e.Node == null) return;
            try
            {
                await EnsureChildrenLoadedAsync(e.Node);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể mở thư mục: " + ex.Message, "Khôi phục dữ liệu");
            }
        }

        private async void TvBackupFile_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (_loadedDate == null || e.Node?.Tag is not RecoveryDirectoryNodeTag tag) return;
            try
            {
                await EnsureChildrenLoadedAsync(e.Node);
                List<RecoveryFileRecord> files = await _repository.GetFilesAsync(
                    _agentId, _loadedDate.Value, tag.VirtualPath);
                _suppressFileChecks = true;
                lvBackupFiles.BeginUpdate();
                try
                {
                    lvBackupFiles.Items.Clear();
                    foreach (RecoveryFileRecord file in files)
                    {
                        ListViewItem item = new ListViewItem(file.FileName)
                        {
                            Tag = file,
                            Checked = e.Node.Checked || _selectedFiles.ContainsKey(file.SourcePath)
                        };
                        item.SubItems.Add(FormatSize(file.Size));
                        item.SubItems.Add(Path.GetExtension(file.FileName));
                        item.SubItems.Add(file.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                        lvBackupFiles.Items.Add(item);
                    }
                }
                finally
                {
                    lvBackupFiles.EndUpdate();
                    _suppressFileChecks = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể nạp danh sách file: " + ex.Message, "Khôi phục dữ liệu");
            }
        }

        private void TvBackupFile_AfterCheck(object? sender, TreeViewEventArgs e)
        {
            if (_suppressTreeChecks || e.Node?.Tag is not RecoveryDirectoryNodeTag tag) return;
            _suppressTreeChecks = true;
            try
            {
                SetLoadedDescendantsChecked(e.Node.Nodes, e.Node.Checked);
                if (!e.Node.Checked)
                {
                    TreeNode? parent = e.Node.Parent;
                    while (parent != null)
                    {
                        parent.Checked = false;
                        parent = parent.Parent;
                    }
                }
                RemoveExplicitFilesUnder(tag.VirtualPath);
            }
            finally
            {
                _suppressTreeChecks = false;
            }
            RefreshVisibleFileChecks();
        }

        private void lvBackupFiles_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_suppressFileChecks || e.Index < 0 || e.Index >= lvBackupFiles.Items.Count ||
                lvBackupFiles.Items[e.Index].Tag is not RecoveryFileRecord file)
            {
                return;
            }

            bool willBeChecked = e.NewValue == CheckState.Checked;
            TreeNode? currentNode = TvBackupFile.SelectedNode;
            if (!willBeChecked && currentNode?.Checked == true)
            {
                _suppressTreeChecks = true;
                try
                {
                    currentNode.Checked = false;
                    TreeNode? parent = currentNode.Parent;
                    while (parent != null)
                    {
                        parent.Checked = false;
                        parent = parent.Parent;
                    }
                }
                finally
                {
                    _suppressTreeChecks = false;
                }

                foreach (ListViewItem other in lvBackupFiles.Items)
                {
                    if (other.Index != e.Index && other.Checked && other.Tag is RecoveryFileRecord otherFile)
                    {
                        _selectedFiles[otherFile.SourcePath] = new SelectedRecoveryFile(
                            otherFile.SourcePath, otherFile.VirtualDirectory);
                    }
                }
            }

            if (willBeChecked)
            {
                _selectedFiles[file.SourcePath] = new SelectedRecoveryFile(file.SourcePath, file.VirtualDirectory);
            }
            else
            {
                _selectedFiles.Remove(file.SourcePath);
            }
        }

        private void btnbrowsepathbk_Click(object? sender, EventArgs e)
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "Chọn thư mục trên máy AgentControl để lưu dữ liệu khôi phục",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(txtpathsavebk.Text) ? txtpathsavebk.Text : string.Empty
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                txtpathsavebk.Text = dialog.SelectedPath;
            }
        }

        private async void btnSaveFileBackup_Click(object? sender, EventArgs e)
        {
            if (_extracting || _loadedDate == null) return;
            string destination = txtpathsavebk.Text.Trim();
            if (string.IsNullOrWhiteSpace(destination))
            {
                MessageBox.Show("Fen hãy chọn thư mục lưu dữ liệu khôi phục.", "Khôi phục dữ liệu");
                return;
            }
            try
            {
                string fullDestination = Path.GetFullPath(destination);
                string fullStorageRoot = Path.GetFullPath(_storageRoot);
                if (IsSameOrChildPath(fullDestination, fullStorageRoot))
                {
                    MessageBox.Show(
                        "Không thể lưu dữ liệu khôi phục bên trong thư mục đang chứa các bản backup.",
                        "Khôi phục dữ liệu",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đường dẫn lưu không hợp lệ: " + ex.Message, "Khôi phục dữ liệu");
                return;
            }

            List<string> folders = CollectCheckedFolders();
            List<string> files = _selectedFiles.Values.Select(value => value.SourcePath).ToList();
            if (folders.Count == 0 && files.Count == 0)
            {
                MessageBox.Show("Fen hãy tick ít nhất một file hoặc thư mục.", "Khôi phục dữ liệu");
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "Dữ liệu sẽ được lưu theo cấu trúc ổ đĩa vào thư mục đã chọn. " +
                "Nếu file đích đã tồn tại, bản khôi phục sẽ ghi đè file đó. Tiếp tục?",
                "Khôi phục dữ liệu",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            string runId = Guid.NewGuid().ToString("N");
            _extractCancellation = new CancellationTokenSource();
            _extracting = true;
            SetUiEnabled(false);
            pcbbackup.Value = 0;
            SetProgressState(ProgressNormal);
            try
            {
                await _repository.PrepareSelectionAsync(runId, folders, files);
                (long count, _) = await _repository.GetSelectionStatsAsync(runId, _agentId, _loadedDate.Value);
                if (count == 0)
                {
                    await _repository.ClearSelectionAsync(runId);
                    MessageBox.Show("Không có file nào trong phần đã chọn.", "Khôi phục dữ liệu");
                    return;
                }

                Progress<RecoveryExtractionProgress> progress = new Progress<RecoveryExtractionProgress>(UpdateProgress);
                RecoveryExtractionResult result = await _extractor.ExtractAsync(
                    runId,
                    _agentId,
                    _loadedDate.Value,
                    destination,
                    progress,
                    _extractCancellation.Token);

                pcbbackup.Value = pcbbackup.Maximum;
                SetProgressState(ProgressPaused);
                string errorText = result.Errors.Count == 0
                    ? string.Empty
                    : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Take(10));
                MessageBox.Show(
                    $"Đã hoàn thành khôi phục.\nThành công: {result.CompletedFiles}\nLỗi: {result.FailedFiles}{errorText}",
                    "Khôi phục dữ liệu",
                    MessageBoxButtons.OK,
                    result.FailedFiles == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show(
                    "Đã dừng khôi phục. File .restoring được giữ lại để tiếp tục ở lần sau.",
                    "Khôi phục dữ liệu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Khôi phục thất bại: " + ex.Message, "Khôi phục dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _extracting = false;
                _extractCancellation?.Dispose();
                _extractCancellation = null;
                SetUiEnabled(true);
                if (_closeAfterExtractionStops && !IsDisposed)
                {
                    _closeAfterExtractionStops = false;
                    BeginInvoke(Close);
                }
            }
        }

        private async Task RunWithLoadingAsync(string message, Func<CancellationToken, Task> action)
        {
            using RecoveryLoadingForm loading = new RecoveryLoadingForm(message);
            Enabled = false;
            loading.Show(this);
            loading.Refresh();
            try
            {
                CancellationToken token = _loadCancellation?.Token ?? CancellationToken.None;
                await action(token);
            }
            finally
            {
                loading.Close();
                if (!IsDisposed) Enabled = true;
            }
        }

        private void UpdateProgress(RecoveryExtractionProgress progress)
        {
            int value = progress.TotalBytes <= 0
                ? (progress.TotalFiles <= 0 ? 0 : (int)Math.Min(1000, progress.CompletedFiles * 1000 / progress.TotalFiles))
                : (int)Math.Min(1000, progress.CompletedBytes * 1000 / progress.TotalBytes);
            pcbbackup.Value = Math.Max(pcbbackup.Minimum, Math.Min(pcbbackup.Maximum, value));
            btnSaveFileBackup.Text = $"{progress.CompletedFiles}/{progress.TotalFiles}";
        }

        private void SetUiEnabled(bool enabled)
        {
            cbxlistday.Enabled = enabled;
            TvBackupFile.Enabled = enabled;
            lvBackupFiles.Enabled = enabled;
            btnbrowsepathbk.Enabled = enabled;
            txtpathsavebk.Enabled = enabled;
            btnSaveFileBackup.Enabled = enabled;
            if (enabled) btnSaveFileBackup.Text = "Lưu file backup";
        }

        private List<string> CollectCheckedFolders()
        {
            List<string> result = new List<string>();
            CollectCheckedFolders(TvBackupFile.Nodes, ancestorChecked: false, result);
            return result;
        }

        private static void CollectCheckedFolders(
            TreeNodeCollection nodes, bool ancestorChecked, List<string> result)
        {
            foreach (TreeNode node in nodes)
            {
                bool currentChecked = node.Checked && node.Tag is RecoveryDirectoryNodeTag;
                if (currentChecked && !ancestorChecked && node.Tag is RecoveryDirectoryNodeTag tag)
                {
                    result.Add(tag.VirtualPath);
                }
                CollectCheckedFolders(node.Nodes, ancestorChecked || currentChecked, result);
            }
        }

        private static void SetLoadedDescendantsChecked(TreeNodeCollection nodes, bool value)
        {
            foreach (TreeNode child in nodes)
            {
                if (child.Tag is RecoveryDirectoryNodeTag)
                {
                    child.Checked = value;
                    SetLoadedDescendantsChecked(child.Nodes, value);
                }
            }
        }

        private void RemoveExplicitFilesUnder(string virtualDirectory)
        {
            foreach (string sourcePath in _selectedFiles
                         .Where(pair => IsSameOrChildDirectory(pair.Value.VirtualDirectory, virtualDirectory))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _selectedFiles.Remove(sourcePath);
            }
        }

        private static bool IsSameOrChildDirectory(string candidate, string parent) =>
            candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        private static bool IsSameOrChildPath(string candidate, string parent)
        {
            string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
            string normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
            return normalizedCandidate.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase) ||
                   normalizedCandidate.StartsWith(
                       normalizedParent + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshVisibleFileChecks()
        {
            if (TvBackupFile.SelectedNode?.Tag is not RecoveryDirectoryNodeTag) return;
            _suppressFileChecks = true;
            try
            {
                foreach (ListViewItem item in lvBackupFiles.Items)
                {
                    if (item.Tag is RecoveryFileRecord file)
                    {
                        item.Checked = TvBackupFile.SelectedNode.Checked || _selectedFiles.ContainsKey(file.SourcePath);
                    }
                }
            }
            finally
            {
                _suppressFileChecks = false;
            }
        }

        private void frmRecovery_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_extracting)
            {
                DialogResult result = MessageBox.Show(
                    "Đang khôi phục dữ liệu. Fen có muốn dừng và đóng cửa sổ không?",
                    "Khôi phục dữ liệu",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                e.Cancel = true;
                _closeAfterExtractionStops = true;
                _extractCancellation?.Cancel();
                return;
            }
            _loadCancellation?.Cancel();
        }

        private void SetProgressState(int state)
        {
            if (pcbbackup.IsHandleCreated)
            {
                SendMessage(pcbbackup.Handle, PbmSetState, (IntPtr)state, IntPtr.Zero);
            }
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = Math.Max(0, bytes);
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.##} {units[unit]}";
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        private sealed class RecoveryDirectoryNodeTag
        {
            public string VirtualPath { get; }
            public bool ChildrenLoaded { get; set; }

            public RecoveryDirectoryNodeTag(string virtualPath)
            {
                VirtualPath = virtualPath;
            }
        }

        private sealed class SelectedRecoveryFile
        {
            public string SourcePath { get; }
            public string VirtualDirectory { get; }

            public SelectedRecoveryFile(string sourcePath, string virtualDirectory)
            {
                SourcePath = sourcePath;
                VirtualDirectory = virtualDirectory;
            }
        }
    }

    internal sealed class RecoveryLoadingForm : Form
    {
        public RecoveryLoadingForm(string message)
        {
            Text = "Khôi phục dữ liệu";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ControlBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 92);
            Label label = new Label
            {
                Left = 14,
                Top = 12,
                Width = 400,
                Height = 24,
                Text = message
            };
            ProgressBar progress = new ProgressBar
            {
                Left = 14,
                Top = 46,
                Width = 400,
                Height = 20,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 25
            };
            Controls.Add(label);
            Controls.Add(progress);
        }
    }
}
