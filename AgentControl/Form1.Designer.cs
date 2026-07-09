namespace AgentControl
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            panelHeader = new Panel();
            btnKetNoi = new Button();
            btnCopy = new Button();
            brndel = new Button();
            panelZone1 = new Panel();
            ListboxAgents = new NHFUiControls.ListBoxNHF();
            panelZone2 = new Panel();
            tvRemoteFolders = new TreeView();
            lvRemoteFiles = new ListView();
            colName = new ColumnHeader();
            ColSize = new ColumnHeader();
            ColType = new ColumnHeader();
            ColDate = new ColumnHeader();
            dgvDownloads = new DataGridView();
            tmrUpdateUI = new System.Windows.Forms.Timer(components);
            btncleardrv = new Button();
            panelHeader.SuspendLayout();
            panelZone1.SuspendLayout();
            panelZone2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvDownloads).BeginInit();
            SuspendLayout();
            // 
            // panelHeader
            // 
            panelHeader.Controls.Add(btncleardrv);
            panelHeader.Controls.Add(btnKetNoi);
            panelHeader.Controls.Add(btnCopy);
            panelHeader.Controls.Add(brndel);
            panelHeader.Dock = DockStyle.Top;
            panelHeader.Location = new Point(0, 0);
            panelHeader.MaximumSize = new Size(0, 60);
            panelHeader.Name = "panelHeader";
            panelHeader.Size = new Size(1870, 60);
            panelHeader.TabIndex = 0;
            // 
            // btnKetNoi
            // 
            btnKetNoi.Location = new Point(1016, 12);
            btnKetNoi.Name = "btnKetNoi";
            btnKetNoi.Size = new Size(126, 42);
            btnKetNoi.TabIndex = 2;
            btnKetNoi.Text = "Kết Nối";
            btnKetNoi.UseVisualStyleBackColor = true;
            btnKetNoi.Click += btnKetNoi_Click;
            // 
            // btnCopy
            // 
            btnCopy.Location = new Point(1148, 12);
            btnCopy.Name = "btnCopy";
            btnCopy.Size = new Size(124, 42);
            btnCopy.TabIndex = 1;
            btnCopy.Text = "Copy";
            btnCopy.UseVisualStyleBackColor = true;
            btnCopy.Click += btnCopy_Click;
            // 
            // brndel
            // 
            brndel.Location = new Point(1278, 12);
            brndel.Name = "brndel";
            brndel.Size = new Size(150, 42);
            brndel.TabIndex = 0;
            brndel.Text = "Xoá";
            brndel.UseVisualStyleBackColor = true;
            brndel.Click += brndel_Click;
            // 
            // panelZone1
            // 
            panelZone1.Controls.Add(ListboxAgents);
            panelZone1.Dock = DockStyle.Left;
            panelZone1.Location = new Point(0, 60);
            panelZone1.Name = "panelZone1";
            panelZone1.Size = new Size(308, 601);
            panelZone1.TabIndex = 1;
            // 
            // ListboxAgents
            // 
            ListboxAgents.BackColor = Color.FromArgb(235, 241, 250);
            ListboxAgents.BorderStyle = BorderStyle.None;
            ListboxAgents.CardBorderRadius = 12;
            ListboxAgents.CardHeight = 125;
            ListboxAgents.Dock = DockStyle.Fill;
            ListboxAgents.DrawMode = DrawMode.OwnerDrawVariable;
            ListboxAgents.Font = new Font("Segoe UI", 9.5F);
            ListboxAgents.FormattingEnabled = true;
            ListboxAgents.HoverCardColor = Color.FromArgb(245, 248, 253);
            ListboxAgents.IntegralHeight = false;
            ListboxAgents.ItemHeight = 125;
            ListboxAgents.Location = new Point(0, 0);
            ListboxAgents.Name = "ListboxAgents";
            ListboxAgents.NormalCardColor = Color.White;
            ListboxAgents.SelectedCardColor = Color.FromArgb(205, 220, 242);
            ListboxAgents.Size = new Size(308, 601);
            ListboxAgents.TabIndex = 4;
            ListboxAgents.SelectedIndexChanged += ListboxAgents_SelectedIndexChanged;
            // 
            // panelZone2
            // 
            panelZone2.Controls.Add(tvRemoteFolders);
            panelZone2.Dock = DockStyle.Left;
            panelZone2.Location = new Point(308, 60);
            panelZone2.Name = "panelZone2";
            panelZone2.Size = new Size(313, 601);
            panelZone2.TabIndex = 2;
            // 
            // tvRemoteFolders
            // 
            tvRemoteFolders.BorderStyle = BorderStyle.FixedSingle;
            tvRemoteFolders.Dock = DockStyle.Fill;
            tvRemoteFolders.Location = new Point(0, 0);
            tvRemoteFolders.Name = "tvRemoteFolders";
            tvRemoteFolders.Size = new Size(313, 601);
            tvRemoteFolders.TabIndex = 0;
            tvRemoteFolders.BeforeCollapse += tvRemoteFolders_BeforeCollapse;
            tvRemoteFolders.BeforeExpand += tvRemoteFolders_BeforeExpand;
            tvRemoteFolders.AfterSelect += tvRemoteFolders_AfterSelect;
            // 
            // lvRemoteFiles
            // 
            lvRemoteFiles.CheckBoxes = true;
            lvRemoteFiles.Columns.AddRange(new ColumnHeader[] { colName, ColSize, ColType, ColDate });
            lvRemoteFiles.Dock = DockStyle.Left;
            lvRemoteFiles.GridLines = true;
            lvRemoteFiles.Location = new Point(621, 60);
            lvRemoteFiles.Name = "lvRemoteFiles";
            lvRemoteFiles.Size = new Size(487, 601);
            lvRemoteFiles.TabIndex = 3;
            lvRemoteFiles.UseCompatibleStateImageBehavior = false;
            lvRemoteFiles.View = View.Details;
            lvRemoteFiles.ItemCheck += lvRemoteFiles_ItemCheck;
            lvRemoteFiles.MouseDoubleClick += lvRemoteFiles_MouseDoubleClick;
            // 
            // colName
            // 
            colName.Text = "Name";
            colName.Width = 250;
            // 
            // ColSize
            // 
            ColSize.Text = "Size";
            // 
            // ColType
            // 
            ColType.Text = "Type";
            // 
            // ColDate
            // 
            ColDate.Text = "Date";
            // 
            // dgvDownloads
            // 
            dgvDownloads.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvDownloads.Dock = DockStyle.Right;
            dgvDownloads.Location = new Point(1114, 60);
            dgvDownloads.Name = "dgvDownloads";
            dgvDownloads.Size = new Size(756, 601);
            dgvDownloads.TabIndex = 4;
            // 
            // tmrUpdateUI
            // 
            tmrUpdateUI.Interval = 1000;
            tmrUpdateUI.Tick += tmrUpdateUI_Tick;
            // 
            // btncleardrv
            // 
            btncleardrv.Location = new Point(1434, 12);
            btncleardrv.Name = "btncleardrv";
            btncleardrv.Size = new Size(112, 42);
            btncleardrv.TabIndex = 3;
            btncleardrv.Text = "Clear";
            btncleardrv.UseVisualStyleBackColor = true;
            btncleardrv.Click += btncleardrv_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1870, 661);
            Controls.Add(dgvDownloads);
            Controls.Add(lvRemoteFiles);
            Controls.Add(panelZone2);
            Controls.Add(panelZone1);
            Controls.Add(panelHeader);
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            panelHeader.ResumeLayout(false);
            panelZone1.ResumeLayout(false);
            panelZone2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dgvDownloads).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private Panel panelHeader;
        private Panel panelZone1;
        private Panel panelZone2;
        private TreeView tvRemoteFolders;
        private ListView lvRemoteFiles;
        private ColumnHeader colName;
        private ColumnHeader ColSize;
        private ColumnHeader ColType;
        private ColumnHeader ColDate;
        private Button brndel;
        private Button btnCopy;
        private Button btnKetNoi;
        private NHFUiControls.ListBoxNHF lvAgents;
        private NHFUiControls.ListBoxNHF ListboxAgents;
        private DataGridView dgvDownloads;
        private System.Windows.Forms.Timer tmrUpdateUI;
        private Button btncleardrv;
    }
}
