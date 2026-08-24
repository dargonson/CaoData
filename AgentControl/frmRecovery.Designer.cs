namespace AgentControl
{
    partial class frmRecovery
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            panelZone2 = new Panel();
            TvBackupFile = new TreeView();
            panel1 = new Panel();
            lvBackupFiles = new ListView();
            colNameBK = new ColumnHeader();
            ColSizeBK = new ColumnHeader();
            ColTypeBK = new ColumnHeader();
            ColDateBK = new ColumnHeader();
            btnSaveFileBackup = new Button();
            pcbbackup = new ProgressBar();
            btnbrowsepathbk = new Button();
            txtpathsavebk = new TextBox();
            cbxlistday = new ComboBox();
            panelZone2.SuspendLayout();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // panelZone2
            // 
            panelZone2.Controls.Add(TvBackupFile);
            panelZone2.Dock = DockStyle.Left;
            panelZone2.Location = new Point(0, 0);
            panelZone2.Name = "panelZone2";
            panelZone2.Size = new Size(294, 641);
            panelZone2.TabIndex = 3;
            // 
            // TvBackupFile
            // 
            TvBackupFile.BorderStyle = BorderStyle.FixedSingle;
            TvBackupFile.CheckBoxes = true;
            TvBackupFile.Dock = DockStyle.Fill;
            TvBackupFile.Location = new Point(0, 0);
            TvBackupFile.Name = "TvBackupFile";
            TvBackupFile.Size = new Size(294, 641);
            TvBackupFile.TabIndex = 0;
            // 
            // panel1
            // 
            panel1.Controls.Add(lvBackupFiles);
            panel1.Dock = DockStyle.Left;
            panel1.Location = new Point(294, 0);
            panel1.Name = "panel1";
            panel1.Size = new Size(598, 641);
            panel1.TabIndex = 6;
            // 
            // lvBackupFiles
            // 
            lvBackupFiles.CheckBoxes = true;
            lvBackupFiles.Columns.AddRange(new ColumnHeader[] { colNameBK, ColSizeBK, ColTypeBK, ColDateBK });
            lvBackupFiles.Dock = DockStyle.Fill;
            lvBackupFiles.GridLines = true;
            lvBackupFiles.Location = new Point(0, 0);
            lvBackupFiles.Name = "lvBackupFiles";
            lvBackupFiles.Size = new Size(598, 641);
            lvBackupFiles.TabIndex = 3;
            lvBackupFiles.UseCompatibleStateImageBehavior = false;
            lvBackupFiles.View = View.Details;
            // 
            // colNameBK
            // 
            colNameBK.Text = "Name";
            colNameBK.Width = 250;
            // 
            // ColSizeBK
            // 
            ColSizeBK.Text = "Size";
            // 
            // ColTypeBK
            // 
            ColTypeBK.Text = "Type";
            // 
            // ColDateBK
            // 
            ColDateBK.Text = "Date";
            ColDateBK.Width = 95;
            // 
            // btnSaveFileBackup
            // 
            btnSaveFileBackup.Location = new Point(909, 290);
            btnSaveFileBackup.Name = "btnSaveFileBackup";
            btnSaveFileBackup.Size = new Size(151, 82);
            btnSaveFileBackup.TabIndex = 8;
            btnSaveFileBackup.Text = "Lưu";
            btnSaveFileBackup.UseVisualStyleBackColor = true;
            // 
            // pcbbackup
            // 
            pcbbackup.Location = new Point(909, 252);
            pcbbackup.Name = "pcbbackup";
            pcbbackup.Size = new Size(356, 23);
            pcbbackup.TabIndex = 9;
            // 
            // btnbrowsepathbk
            // 
            btnbrowsepathbk.Location = new Point(917, 437);
            btnbrowsepathbk.Name = "btnbrowsepathbk";
            btnbrowsepathbk.Size = new Size(75, 23);
            btnbrowsepathbk.TabIndex = 10;
            btnbrowsepathbk.Text = "button1";
            btnbrowsepathbk.UseVisualStyleBackColor = true;
            // 
            // txtpathsavebk
            // 
            txtpathsavebk.Location = new Point(917, 388);
            txtpathsavebk.Name = "txtpathsavebk";
            txtpathsavebk.Size = new Size(324, 23);
            txtpathsavebk.TabIndex = 11;
            // 
            // cbxlistday
            // 
            cbxlistday.FormattingEnabled = true;
            cbxlistday.Location = new Point(909, 185);
            cbxlistday.Name = "cbxlistday";
            cbxlistday.Size = new Size(309, 23);
            cbxlistday.TabIndex = 12;
            // 
            // frmRecovery
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1277, 641);
            Controls.Add(cbxlistday);
            Controls.Add(txtpathsavebk);
            Controls.Add(btnbrowsepathbk);
            Controls.Add(pcbbackup);
            Controls.Add(btnSaveFileBackup);
            Controls.Add(panel1);
            Controls.Add(panelZone2);
            Name = "frmRecovery";
            Text = "frmRecovery";
            panelZone2.ResumeLayout(false);
            panel1.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Panel panelZone2;
        private TreeView TvBackupFile;
        private Panel panel1;
        private ListView lvBackupFiles;
        private ColumnHeader colNameBK;
        private ColumnHeader ColSizeBK;
        private ColumnHeader ColTypeBK;
        private ColumnHeader ColDateBK;
        private Button btnSaveFileBackup;
        private ProgressBar pcbbackup;
        private Button btnbrowsepathbk;
        private TextBox txtpathsavebk;
        private ComboBox cbxlistday;
    }
}