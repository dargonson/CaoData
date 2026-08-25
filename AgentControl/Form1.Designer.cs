namespace AgentControl
{
    partial class frmToolBackup
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmToolBackup));
            groupBox1 = new GroupBox();
            radlistdown = new RadioButton();
            radlistup = new RadioButton();
            btnupload = new Button();
            lblver = new Label();
            txtxoa = new TextBox();
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
            dvgUploads = new DataGridView();
            dgvDownloads = new DataGridView();
            tmrUpdateUI = new System.Windows.Forms.Timer(components);
            lvRemoteFiles = new ListView();
            colName = new ColumnHeader();
            ColSize = new ColumnHeader();
            ColType = new ColumnHeader();
            ColDate = new ColumnHeader();
            panel1 = new Panel();
            panel2 = new Panel();
            PanelHeader = new Panel();
            pictureBox1 = new PictureBox();
            groupBox4 = new GroupBox();
            btnDeploy = new Button();
            btnDeleteExt = new Button();
            btnAddExt = new Button();
            listBox2 = new ListBox();
            label6 = new Label();
            button1 = new Button();
            textBox1 = new TextBox();
            btndeleteExcFolder = new Button();
            label1 = new Label();
            btnAddExcFolder = new Button();
            listBox1 = new ListBox();
            label5 = new Label();
            numericUpDown1 = new NumericUpDown();
            dateTimePicker1 = new DateTimePicker();
            label2 = new Label();
            label4 = new Label();
            numericUpDown2 = new NumericUpDown();
            label3 = new Label();
            groupBox3 = new GroupBox();
            groupBox2 = new GroupBox();
            toolTip1 = new ToolTip(components);
            btnrecovery = new Button();
            groupBox1.SuspendLayout();
            grbchecksum.SuspendLayout();
            panelZone1.SuspendLayout();
            panelZone2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dvgUploads).BeginInit();
            ((System.ComponentModel.ISupportInitialize)dgvDownloads).BeginInit();
            panel1.SuspendLayout();
            panel2.SuspendLayout();
            PanelHeader.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            groupBox4.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDown1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown2).BeginInit();
            groupBox3.SuspendLayout();
            groupBox2.SuspendLayout();
            SuspendLayout();
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(radlistdown);
            groupBox1.Controls.Add(radlistup);
            groupBox1.Location = new Point(29, 82);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(365, 48);
            groupBox1.TabIndex = 8;
            groupBox1.TabStop = false;
            groupBox1.Text = "Danh sách file Upload/Download";
            // 
            // radlistdown
            // 
            radlistdown.AutoSize = true;
            radlistdown.Location = new Point(200, 21);
            radlistdown.Name = "radlistdown";
            radlistdown.Size = new Size(156, 19);
            radlistdown.TabIndex = 1;
            radlistdown.TabStop = true;
            radlistdown.Text = "Danh sách file Download";
            radlistdown.UseVisualStyleBackColor = true;
            radlistdown.CheckedChanged += radlistdown_CheckedChanged;
            // 
            // radlistup
            // 
            radlistup.AutoSize = true;
            radlistup.Location = new Point(15, 22);
            radlistup.Name = "radlistup";
            radlistup.Size = new Size(140, 19);
            radlistup.TabIndex = 0;
            radlistup.TabStop = true;
            radlistup.Text = "Danh sách file Upload";
            radlistup.UseVisualStyleBackColor = true;
            radlistup.CheckedChanged += radlistup_CheckedChanged;
            // 
            // btnupload
            // 
            btnupload.Location = new Point(410, 15);
            btnupload.Name = "btnupload";
            btnupload.Size = new Size(103, 34);
            btnupload.TabIndex = 7;
            btnupload.Text = "Upload";
            btnupload.UseVisualStyleBackColor = true;
            // 
            // lblver
            // 
            lblver.AutoSize = true;
            lblver.Enabled = false;
            lblver.Location = new Point(1797, 10);
            lblver.Name = "lblver";
            lblver.Size = new Size(38, 15);
            lblver.TabIndex = 6;
            lblver.Text = "label1";
            lblver.Visible = false;
            // 
            // txtxoa
            // 
            txtxoa.Font = new Font("Segoe UI", 15F);
            txtxoa.Location = new Point(17, 35);
            txtxoa.Name = "txtxoa";
            txtxoa.Size = new Size(139, 34);
            txtxoa.TabIndex = 5;
            toolTip1.SetToolTip(txtxoa, "Nhập vào ngày giờ hiện tại theo dạng HHmm để xoá file");
            // 
            // grbchecksum
            // 
            grbchecksum.Controls.Add(radnone);
            grbchecksum.Controls.Add(radmd5);
            grbchecksum.Controls.Add(radsha256);
            grbchecksum.Location = new Point(29, 26);
            grbchecksum.Name = "grbchecksum";
            grbchecksum.Size = new Size(258, 52);
            grbchecksum.TabIndex = 4;
            grbchecksum.TabStop = false;
            grbchecksum.Text = "CheckSum";
            // 
            // radnone
            // 
            radnone.AutoSize = true;
            radnone.Location = new Point(200, 21);
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
            radmd5.Location = new Point(118, 21);
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
            radsha256.Location = new Point(15, 21);
            radsha256.Name = "radsha256";
            radsha256.Size = new Size(71, 19);
            radsha256.TabIndex = 0;
            radsha256.TabStop = true;
            radsha256.Text = "SHA-256";
            radsha256.UseVisualStyleBackColor = true;
            // 
            // btncleardrv
            // 
            btncleardrv.Location = new Point(410, 96);
            btncleardrv.Name = "btncleardrv";
            btncleardrv.Size = new Size(105, 34);
            btncleardrv.TabIndex = 3;
            btncleardrv.Text = "Clear List";
            btncleardrv.UseVisualStyleBackColor = true;
            btncleardrv.Click += btncleardrv_Click;
            // 
            // btnKetNoi
            // 
            btnKetNoi.AllowDrop = true;
            btnKetNoi.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
            btnKetNoi.ForeColor = Color.Red;
            btnKetNoi.Location = new Point(635, 104);
            btnKetNoi.Name = "btnKetNoi";
            btnKetNoi.Size = new Size(268, 56);
            btnKetNoi.TabIndex = 2;
            btnKetNoi.Text = "Kết nối Agnet";
            btnKetNoi.UseVisualStyleBackColor = true;
            btnKetNoi.Click += btnKetNoi_Click;
            // 
            // btnCopy
            // 
            btnCopy.Location = new Point(410, 56);
            btnCopy.Name = "btnCopy";
            btnCopy.Size = new Size(103, 34);
            btnCopy.TabIndex = 1;
            btnCopy.Text = "Download";
            btnCopy.UseVisualStyleBackColor = true;
            btnCopy.Click += btnCopy_Click;
            // 
            // brndel
            // 
            brndel.Location = new Point(17, 82);
            brndel.Name = "brndel";
            brndel.Size = new Size(139, 34);
            brndel.TabIndex = 0;
            brndel.Text = "Xoá";
            brndel.UseVisualStyleBackColor = true;
            brndel.Click += brndel_Click;
            // 
            // panelZone1
            // 
            panelZone1.Controls.Add(ListboxAgents);
            panelZone1.Dock = DockStyle.Left;
            panelZone1.Location = new Point(0, 172);
            panelZone1.Name = "panelZone1";
            panelZone1.Size = new Size(291, 687);
            panelZone1.TabIndex = 1;
            // 
            // ListboxAgents
            // 
            ListboxAgents.BackColor = Color.FromArgb(235, 241, 250);
            ListboxAgents.BorderStyle = BorderStyle.None;
            ListboxAgents.CardBorderRadius = 12;
            ListboxAgents.CardHeight = 145;
            ListboxAgents.Dock = DockStyle.Fill;
            ListboxAgents.DrawMode = DrawMode.OwnerDrawVariable;
            ListboxAgents.Font = new Font("Segoe UI", 9.5F);
            ListboxAgents.FormattingEnabled = true;
            ListboxAgents.HoverCardColor = Color.FromArgb(245, 248, 253);
            ListboxAgents.IntegralHeight = false;
            ListboxAgents.ItemHeight = 145;
            ListboxAgents.Location = new Point(0, 0);
            ListboxAgents.Name = "ListboxAgents";
            ListboxAgents.NormalCardColor = Color.White;
            ListboxAgents.SelectedCardColor = Color.FromArgb(205, 220, 242);
            ListboxAgents.Size = new Size(291, 687);
            ListboxAgents.TabIndex = 4;
            ListboxAgents.SelectedIndexChanged += ListboxAgents_SelectedIndexChanged;
            // 
            // panelZone2
            // 
            panelZone2.Controls.Add(tvRemoteFolders);
            panelZone2.Dock = DockStyle.Left;
            panelZone2.Location = new Point(291, 172);
            panelZone2.Name = "panelZone2";
            panelZone2.Size = new Size(294, 687);
            panelZone2.TabIndex = 2;
            // 
            // tvRemoteFolders
            // 
            tvRemoteFolders.BorderStyle = BorderStyle.FixedSingle;
            tvRemoteFolders.CheckBoxes = true;
            tvRemoteFolders.Dock = DockStyle.Fill;
            tvRemoteFolders.Location = new Point(0, 0);
            tvRemoteFolders.Name = "tvRemoteFolders";
            tvRemoteFolders.Size = new Size(294, 687);
            tvRemoteFolders.TabIndex = 0;
            tvRemoteFolders.BeforeCollapse += tvRemoteFolders_BeforeCollapse;
            tvRemoteFolders.BeforeExpand += tvRemoteFolders_BeforeExpand;
            tvRemoteFolders.AfterSelect += tvRemoteFolders_AfterSelect;
            // 
            // dvgUploads
            // 
            dvgUploads.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dvgUploads.Dock = DockStyle.Fill;
            dvgUploads.Location = new Point(0, 0);
            dvgUploads.Name = "dvgUploads";
            dvgUploads.Size = new Size(719, 687);
            dvgUploads.TabIndex = 8;
            // 
            // dgvDownloads
            // 
            dgvDownloads.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvDownloads.Dock = DockStyle.Fill;
            dgvDownloads.Location = new Point(0, 0);
            dgvDownloads.Name = "dgvDownloads";
            dgvDownloads.Size = new Size(719, 687);
            dgvDownloads.TabIndex = 4;
            // 
            // tmrUpdateUI
            // 
            tmrUpdateUI.Interval = 1000;
            tmrUpdateUI.Tick += tmrUpdateUI_Tick;
            // 
            // lvRemoteFiles
            // 
            lvRemoteFiles.CheckBoxes = true;
            lvRemoteFiles.Columns.AddRange(new ColumnHeader[] { colName, ColSize, ColType, ColDate });
            lvRemoteFiles.Dock = DockStyle.Fill;
            lvRemoteFiles.GridLines = true;
            lvRemoteFiles.Location = new Point(0, 0);
            lvRemoteFiles.Name = "lvRemoteFiles";
            lvRemoteFiles.Size = new Size(543, 687);
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
            // panel1
            // 
            panel1.Controls.Add(lvRemoteFiles);
            panel1.Dock = DockStyle.Left;
            panel1.Location = new Point(585, 172);
            panel1.Name = "panel1";
            panel1.Size = new Size(543, 687);
            panel1.TabIndex = 5;
            // 
            // panel2
            // 
            panel2.Controls.Add(dvgUploads);
            panel2.Controls.Add(dgvDownloads);
            panel2.Dock = DockStyle.Fill;
            panel2.Location = new Point(1128, 172);
            panel2.Name = "panel2";
            panel2.Size = new Size(719, 687);
            panel2.TabIndex = 6;
            // 
            // PanelHeader
            // 
            PanelHeader.AutoSize = true;
            PanelHeader.Controls.Add(pictureBox1);
            PanelHeader.Controls.Add(groupBox4);
            PanelHeader.Controls.Add(groupBox3);
            PanelHeader.Controls.Add(groupBox2);
            PanelHeader.Controls.Add(lblver);
            PanelHeader.Dock = DockStyle.Top;
            PanelHeader.Location = new Point(0, 0);
            PanelHeader.Name = "PanelHeader";
            PanelHeader.Size = new Size(1847, 172);
            PanelHeader.TabIndex = 9;
            // 
            // pictureBox1
            // 
            pictureBox1.Image = Properties.Resources.LOGO;
            pictureBox1.Location = new Point(12, 5);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(131, 163);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.TabIndex = 18;
            pictureBox1.TabStop = false;
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(btnrecovery);
            groupBox4.Controls.Add(btnDeploy);
            groupBox4.Controls.Add(btnKetNoi);
            groupBox4.Controls.Add(btnDeleteExt);
            groupBox4.Controls.Add(btnAddExt);
            groupBox4.Controls.Add(listBox2);
            groupBox4.Controls.Add(label6);
            groupBox4.Controls.Add(button1);
            groupBox4.Controls.Add(textBox1);
            groupBox4.Controls.Add(btndeleteExcFolder);
            groupBox4.Controls.Add(label1);
            groupBox4.Controls.Add(btnAddExcFolder);
            groupBox4.Controls.Add(listBox1);
            groupBox4.Controls.Add(label5);
            groupBox4.Controls.Add(numericUpDown1);
            groupBox4.Controls.Add(dateTimePicker1);
            groupBox4.Controls.Add(label2);
            groupBox4.Controls.Add(label4);
            groupBox4.Controls.Add(numericUpDown2);
            groupBox4.Controls.Add(label3);
            groupBox4.Font = new Font("Segoe UI", 9F);
            groupBox4.Location = new Point(149, 3);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(924, 166);
            groupBox4.TabIndex = 17;
            groupBox4.TabStop = false;
            groupBox4.Text = "Backup";
            // 
            // btnDeploy
            // 
            btnDeploy.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnDeploy.ForeColor = Color.Green;
            btnDeploy.Location = new Point(635, 45);
            btnDeploy.Name = "btnDeploy";
            btnDeploy.Size = new Size(131, 54);
            btnDeploy.TabIndex = 4;
            btnDeploy.Text = "Send Config Backup";
            btnDeploy.UseVisualStyleBackColor = true;
            // 
            // btnDeleteExt
            // 
            btnDeleteExt.Location = new Point(270, 136);
            btnDeleteExt.Name = "btnDeleteExt";
            btnDeleteExt.Size = new Size(100, 24);
            btnDeleteExt.TabIndex = 16;
            btnDeleteExt.Text = "Xóa";
            btnDeleteExt.UseVisualStyleBackColor = true;
            // 
            // btnAddExt
            // 
            btnAddExt.Location = new Point(270, 112);
            btnAddExt.Name = "btnAddExt";
            btnAddExt.Size = new Size(100, 24);
            btnAddExt.TabIndex = 15;
            btnAddExt.Text = "Thêm";
            btnAddExt.UseVisualStyleBackColor = true;
            // 
            // listBox2
            // 
            listBox2.FormattingEnabled = true;
            listBox2.ItemHeight = 15;
            listBox2.Location = new Point(9, 111);
            listBox2.Name = "listBox2";
            listBox2.Size = new Size(255, 49);
            listBox2.TabIndex = 14;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(61, 93);
            label6.Name = "label6";
            label6.Size = new Size(147, 15);
            label6.TabIndex = 13;
            label6.Text = "Extension / pattern loại trừ";
            // 
            // button1
            // 
            button1.Location = new Point(847, 14);
            button1.Name = "button1";
            button1.Size = new Size(66, 23);
            button1.TabIndex = 3;
            button1.Text = "Browse";
            button1.UseVisualStyleBackColor = true;
            // 
            // textBox1
            // 
            textBox1.Location = new Point(462, 15);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(379, 23);
            textBox1.TabIndex = 4;
            // 
            // btndeleteExcFolder
            // 
            btndeleteExcFolder.Location = new Point(270, 63);
            btndeleteExcFolder.Name = "btndeleteExcFolder";
            btndeleteExcFolder.Size = new Size(100, 24);
            btndeleteExcFolder.TabIndex = 12;
            btndeleteExcFolder.Text = "Xóa";
            btndeleteExcFolder.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(390, 22);
            label1.Name = "label1";
            label1.Size = new Size(66, 15);
            label1.TabIndex = 2;
            label1.Text = "Đường dẫn";
            // 
            // btnAddExcFolder
            // 
            btnAddExcFolder.Location = new Point(270, 38);
            btnAddExcFolder.Name = "btnAddExcFolder";
            btnAddExcFolder.Size = new Size(100, 24);
            btnAddExcFolder.TabIndex = 11;
            btnAddExcFolder.Text = "Thêm";
            btnAddExcFolder.UseVisualStyleBackColor = true;
            // 
            // listBox1
            // 
            listBox1.FormattingEnabled = true;
            listBox1.ItemHeight = 15;
            listBox1.Location = new Point(9, 38);
            listBox1.Name = "listBox1";
            listBox1.Size = new Size(255, 49);
            listBox1.TabIndex = 10;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(85, 18);
            label5.Name = "label5";
            label5.Size = new Size(94, 15);
            label5.TabIndex = 9;
            label5.Text = "Thư mục loại trừ";
            // 
            // numericUpDown1
            // 
            numericUpDown1.Location = new Point(527, 52);
            numericUpDown1.Maximum = new decimal(new int[] { 3650, 0, 0, 0 });
            numericUpDown1.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDown1.Name = "numericUpDown1";
            numericUpDown1.Size = new Size(85, 23);
            numericUpDown1.TabIndex = 4;
            numericUpDown1.TextAlign = HorizontalAlignment.Center;
            numericUpDown1.Value = new decimal(new int[] { 60, 0, 0, 0 });
            // 
            // dateTimePicker1
            // 
            dateTimePicker1.CustomFormat = "HH:mm";
            dateTimePicker1.Format = DateTimePickerFormat.Custom;
            dateTimePicker1.Location = new Point(527, 87);
            dateTimePicker1.Name = "dateTimePicker1";
            dateTimePicker1.ShowUpDown = true;
            dateTimePicker1.Size = new Size(84, 23);
            dateTimePicker1.TabIndex = 8;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(390, 54);
            label2.Name = "label2";
            label2.Size = new Size(131, 15);
            label2.TabIndex = 2;
            label2.Text = "Số ngày tạo full backup";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(390, 89);
            label4.Name = "label4";
            label4.Size = new Size(98, 15);
            label4.TabIndex = 7;
            label4.Text = "Thời gian backup";
            // 
            // numericUpDown2
            // 
            numericUpDown2.Location = new Point(527, 123);
            numericUpDown2.Maximum = new decimal(new int[] { 365, 0, 0, 0 });
            numericUpDown2.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDown2.Name = "numericUpDown2";
            numericUpDown2.Size = new Size(84, 23);
            numericUpDown2.TabIndex = 6;
            numericUpDown2.TextAlign = HorizontalAlignment.Center;
            numericUpDown2.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(390, 126);
            label3.Name = "label3";
            label3.Size = new Size(107, 15);
            label3.TabIndex = 5;
            label3.Text = "Backup mỗi (ngày)";
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(brndel);
            groupBox3.Controls.Add(txtxoa);
            groupBox3.Location = new Point(1079, 3);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(169, 135);
            groupBox3.TabIndex = 10;
            groupBox3.TabStop = false;
            groupBox3.Text = "Delete File";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(groupBox1);
            groupBox2.Controls.Add(btncleardrv);
            groupBox2.Controls.Add(btnupload);
            groupBox2.Controls.Add(btnCopy);
            groupBox2.Controls.Add(grbchecksum);
            groupBox2.Location = new Point(1254, 3);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(526, 135);
            groupBox2.TabIndex = 9;
            groupBox2.TabStop = false;
            groupBox2.Text = "Download/Upload";
            // 
            // btnrecovery
            // 
            btnrecovery.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnrecovery.ForeColor = Color.Blue;
            btnrecovery.Location = new Point(772, 45);
            btnrecovery.Name = "btnrecovery";
            btnrecovery.Size = new Size(131, 54);
            btnrecovery.TabIndex = 4;
            btnrecovery.Text = "Khôi phục dữ liệu";
            btnrecovery.UseVisualStyleBackColor = true;
            // 
            // frmToolBackup
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1847, 859);
            Controls.Add(panel2);
            Controls.Add(panel1);
            Controls.Add(panelZone2);
            Controls.Add(panelZone1);
            Controls.Add(PanelHeader);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "frmToolBackup";
            Text = "Tool Backup";
            Load += Form1_Load;
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            grbchecksum.ResumeLayout(false);
            grbchecksum.PerformLayout();
            panelZone1.ResumeLayout(false);
            panelZone2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dvgUploads).EndInit();
            ((System.ComponentModel.ISupportInitialize)dgvDownloads).EndInit();
            panel1.ResumeLayout(false);
            panel2.ResumeLayout(false);
            PanelHeader.ResumeLayout(false);
            PanelHeader.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDown1).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown2).EndInit();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox2.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Panel panelZone1;
        private Panel panelZone2;
        private TreeView tvRemoteFolders;
        private Button brndel;
        private Button btnCopy;
        private Button btnKetNoi;
        private NHFUiControls.ListBoxNHF ListboxAgents;
        private DataGridView dgvDownloads;
        private System.Windows.Forms.Timer tmrUpdateUI;
        private Button btncleardrv;
        private GroupBox grbchecksum;
        private RadioButton radsha256;
        private RadioButton radnone;
        private RadioButton radmd5;
        private ListView lvRemoteFiles;
        private ColumnHeader colName;
        private ColumnHeader ColSize;
        private ColumnHeader ColType;
        private ColumnHeader ColDate;
        private Panel panel1;
        private Panel panel2;
        private TextBox txtxoa;
        private Label lblver;
        private Button btnupload;
        private GroupBox groupBox1;
        private RadioButton radlistdown;
        private RadioButton radlistup;
        private DataGridView dvgUploads;
        private Panel PanelHeader;
        private GroupBox groupBox2;
        private GroupBox groupBox3;
        private ToolTip toolTip1;
        private TextBox textBox1;
        private Label label1;
        private Button button1;
        private NumericUpDown numericUpDown1;
        private Label label2;
        private NumericUpDown numericUpDown2;
        private Label label3;
        private DateTimePicker dateTimePicker1;
        private Label label4;
        private Button btnDeleteExt;
        private Button btnAddExt;
        private ListBox listBox2;
        private Label label6;
        private Button btndeleteExcFolder;
        private Button btnAddExcFolder;
        private ListBox listBox1;
        private Label label5;
        private GroupBox groupBox4;
        private PictureBox pictureBox1;
        private Button btnDeploy;
        private Button btnrecovery;
    }
}
