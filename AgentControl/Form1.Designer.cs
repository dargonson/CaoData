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
            grbchecksum = new GroupBox();
            radnone = new RadioButton();
            radmd5 = new RadioButton();
            radsha256 = new RadioButton();
            btncleardrv = new Button();
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
            panelHeader.SuspendLayout();
            grbchecksum.SuspendLayout();
            panelZone1.SuspendLayout();
            panelZone2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvDownloads).BeginInit();
            SuspendLayout();
            // 
            // panelHeader
            // 
            panelHeader.AutoSize = true;
            panelHeader.Controls.Add(grbchecksum);
            panelHeader.Controls.Add(btncleardrv);
            panelHeader.Controls.Add(btnKetNoi);
            panelHeader.Controls.Add(btnCopy);
            panelHeader.Controls.Add(brndel);
            panelHeader.Dock = DockStyle.Top;
            panelHeader.Location = new Point(0, 0);
            panelHeader.MaximumSize = new Size(0, 60);
            panelHeader.Name = "panelHeader";
            panelHeader.Size = new Size(1747, 57);
            panelHeader.TabIndex = 0;
            // 
            // grbchecksum
            // 
            grbchecksum.Controls.Add(radnone);
            grbchecksum.Controls.Add(radmd5);
            grbchecksum.Controls.Add(radsha256);
            grbchecksum.Location = new Point(12, 3);
            grbchecksum.Name = "grbchecksum";
            grbchecksum.Size = new Size(288, 48);
            grbchecksum.TabIndex = 4;
            grbchecksum.TabStop = false;
            grbchecksum.Text = "CheckSum";
            // 
            // radnone
            // 
            radnone.AutoSize = true;
            radnone.Location = new Point(211, 22);
            radnone.Name = "radnone";
            radnone.Size = new Size(54, 19);
            radnone.TabIndex = 2;
            radnone.TabStop = true;
            radnone.Text = "None";
            radnone.UseVisualStyleBackColor = true;
            // 
            // radmd5
            // 
            radmd5.AutoSize = true;
            radmd5.Location = new Point(124, 21);
            radmd5.Name = "radmd5";
            radmd5.Size = new Size(50, 19);
            radmd5.TabIndex = 1;
            radmd5.TabStop = true;
            radmd5.Text = "MD5";
            radmd5.UseVisualStyleBackColor = true;
            // 
            // radsha256
            // 
            radsha256.AutoSize = true;
            radsha256.Location = new Point(6, 21);
            radsha256.Name = "radsha256";
            radsha256.Size = new Size(71, 19);
            radsha256.TabIndex = 0;
            radsha256.TabStop = true;
            radsha256.Text = "SHA-256";
            radsha256.UseVisualStyleBackColor = true;
            // 
            // btncleardrv
            // 
            btncleardrv.Location = new Point(746, 12);
            btncleardrv.Name = "btncleardrv";
            btncleardrv.Size = new Size(112, 42);
            btncleardrv.TabIndex = 3;
            btncleardrv.Text = "Clear";
            btncleardrv.UseVisualStyleBackColor = true;
            btncleardrv.Click += btncleardrv_Click;
            // 
            // btnKetNoi
            // 
            btnKetNoi.Location = new Point(312, 12);
            btnKetNoi.Name = "btnKetNoi";
            btnKetNoi.Size = new Size(126, 42);
            btnKetNoi.TabIndex = 2;
            btnKetNoi.Text = "Kết Nối";
            btnKetNoi.UseVisualStyleBackColor = true;
            btnKetNoi.Click += btnKetNoi_Click;
            // 
            // btnCopy
            // 
            btnCopy.Location = new Point(444, 12);
            btnCopy.Name = "btnCopy";
            btnCopy.Size = new Size(124, 42);
            btnCopy.TabIndex = 1;
            btnCopy.Text = "Copy";
            btnCopy.UseVisualStyleBackColor = true;
            btnCopy.Click += btnCopy_Click;
            // 
            // brndel
            // 
            brndel.Location = new Point(574, 12);
            brndel.Name = "brndel";
            brndel.Size = new Size(139, 42);
            brndel.TabIndex = 0;
            brndel.Text = "Xoá";
            brndel.UseVisualStyleBackColor = true;
            brndel.Click += brndel_Click;
            // 
            // panelZone1
            // 
            panelZone1.Controls.Add(ListboxAgents);
            panelZone1.Dock = DockStyle.Left;
            panelZone1.Location = new Point(0, 57);
            panelZone1.Name = "panelZone1";
            panelZone1.Size = new Size(308, 604);
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
            ListboxAgents.Size = new Size(308, 604);
            ListboxAgents.TabIndex = 4;
            ListboxAgents.SelectedIndexChanged += ListboxAgents_SelectedIndexChanged;
            // 
            // panelZone2
            // 
            panelZone2.Controls.Add(tvRemoteFolders);
            panelZone2.Dock = DockStyle.Left;
            panelZone2.Location = new Point(308, 57);
            panelZone2.Name = "panelZone2";
            panelZone2.Size = new Size(294, 604);
            panelZone2.TabIndex = 2;
            // 
            // tvRemoteFolders
            // 
            tvRemoteFolders.BorderStyle = BorderStyle.FixedSingle;
            tvRemoteFolders.Dock = DockStyle.Fill;
            tvRemoteFolders.Location = new Point(0, 0);
            tvRemoteFolders.Name = "tvRemoteFolders";
            tvRemoteFolders.Size = new Size(294, 604);
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
            lvRemoteFiles.Location = new Point(602, 57);
            lvRemoteFiles.Name = "lvRemoteFiles";
            lvRemoteFiles.Size = new Size(471, 604);
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
            ColDate.Width = 95;
            // 
            // dgvDownloads
            // 
            dgvDownloads.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvDownloads.Dock = DockStyle.Right;
            dgvDownloads.Location = new Point(1079, 57);
            dgvDownloads.Name = "dgvDownloads";
            dgvDownloads.Size = new Size(668, 604);
            dgvDownloads.TabIndex = 4;
            // 
            // tmrUpdateUI
            // 
            tmrUpdateUI.Interval = 1000;
            tmrUpdateUI.Tick += tmrUpdateUI_Tick;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1747, 661);
            Controls.Add(dgvDownloads);
            Controls.Add(lvRemoteFiles);
            Controls.Add(panelZone2);
            Controls.Add(panelZone1);
            Controls.Add(panelHeader);
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            panelHeader.ResumeLayout(false);
            grbchecksum.ResumeLayout(false);
            grbchecksum.PerformLayout();
            panelZone1.ResumeLayout(false);
            panelZone2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dgvDownloads).EndInit();
            ResumeLayout(false);
            PerformLayout();
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
        private GroupBox grbchecksum;
        private RadioButton radsha256;
        private RadioButton radnone;
        private RadioButton radmd5;
    }
}
