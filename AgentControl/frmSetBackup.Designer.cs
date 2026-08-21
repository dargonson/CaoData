namespace AgentControl
{
    partial class frmSetBackup
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
            btnDeploy = new Button();
            btnCancle = new Button();
            TvCheckBackup = new TreeView();
            label1 = new Label();
            btnbrowser = new Button();
            txtduongdanbk = new TextBox();
            label2 = new Label();
            txtperiodbk = new TextBox();
            txtDayToBackup = new TextBox();
            label3 = new Label();
            textBox1 = new TextBox();
            label4 = new Label();
            numBackupIntervalDays = new NumericUpDown();
            numFullBackupPeriodDays = new NumericUpDown();
            dtpBackupTime = new DateTimePicker();
            label5 = new Label();
            lstExcludeFolders = new ListBox();
            btnAddExcludeFolder = new Button();
            btnRemoveExcludeFolder = new Button();
            label6 = new Label();
            lstExcludeRules = new ListBox();
            btnAddExcludeRule = new Button();
            btnRemoveExcludeRule = new Button();
            ((System.ComponentModel.ISupportInitialize)numBackupIntervalDays).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numFullBackupPeriodDays).BeginInit();
            SuspendLayout();
            // 
            // btnDeploy
            // 
            btnDeploy.Location = new Point(897, 572);
            btnDeploy.Name = "btnDeploy";
            btnDeploy.Size = new Size(143, 41);
            btnDeploy.TabIndex = 0;
            btnDeploy.Text = "Deploy";
            btnDeploy.UseVisualStyleBackColor = true;
            // 
            // btnCancle
            // 
            btnCancle.Location = new Point(1067, 572);
            btnCancle.Name = "btnCancle";
            btnCancle.Size = new Size(143, 41);
            btnCancle.TabIndex = 0;
            btnCancle.Text = "Cancle";
            btnCancle.UseVisualStyleBackColor = true;
            // 
            // TvCheckBackup
            // 
            TvCheckBackup.Dock = DockStyle.Left;
            TvCheckBackup.CheckBoxes = true;
            TvCheckBackup.Location = new Point(0, 0);
            TvCheckBackup.Name = "TvCheckBackup";
            TvCheckBackup.Size = new Size(332, 643);
            TvCheckBackup.TabIndex = 1;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(371, 39);
            label1.Name = "label1";
            label1.Size = new Size(66, 15);
            label1.TabIndex = 2;
            label1.Text = "Đường dẫn";
            // 
            // btnbrowser
            // 
            btnbrowser.Location = new Point(1135, 35);
            btnbrowser.Name = "btnbrowser";
            btnbrowser.Size = new Size(75, 23);
            btnbrowser.TabIndex = 3;
            btnbrowser.Text = "Browse";
            btnbrowser.UseVisualStyleBackColor = true;
            // 
            // txtduongdanbk
            // 
            txtduongdanbk.Location = new Point(583, 35);
            txtduongdanbk.Name = "txtduongdanbk";
            txtduongdanbk.Size = new Size(534, 23);
            txtduongdanbk.TabIndex = 4;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(371, 80);
            label2.Name = "label2";
            label2.Size = new Size(131, 15);
            label2.TabIndex = 2;
            label2.Text = "Số ngày tạo full backup";
            // 
            // txtperiodbk
            // 
            txtperiodbk.Location = new Point(583, 77);
            txtperiodbk.Name = "txtperiodbk";
            txtperiodbk.Size = new Size(534, 23);
            txtperiodbk.TabIndex = 4;
            txtperiodbk.Visible = false;
            // 
            // txtDayToBackup
            // 
            txtDayToBackup.Location = new Point(583, 123);
            txtDayToBackup.Name = "txtDayToBackup";
            txtDayToBackup.Size = new Size(534, 23);
            txtDayToBackup.TabIndex = 6;
            txtDayToBackup.Visible = false;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(371, 126);
            label3.Name = "label3";
            label3.Size = new Size(206, 15);
            label3.TabIndex = 5;
            label3.Text = "Backup mỗi (ngày)";
            // 
            // textBox1
            // 
            textBox1.Location = new Point(583, 169);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(534, 23);
            textBox1.TabIndex = 8;
            textBox1.Visible = false;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(371, 177);
            label4.Name = "label4";
            label4.Size = new Size(152, 15);
            label4.TabIndex = 7;
            label4.Text = "Thời gian backup";

            // numBackupIntervalDays
            numBackupIntervalDays.Location = new Point(583, 119);
            numBackupIntervalDays.Maximum = 365;
            numBackupIntervalDays.Minimum = 1;
            numBackupIntervalDays.Name = "numBackupIntervalDays";
            numBackupIntervalDays.Size = new Size(534, 23);
            numBackupIntervalDays.TabIndex = 6;
            numBackupIntervalDays.Value = 1;

            // numFullBackupPeriodDays
            numFullBackupPeriodDays.Location = new Point(583, 77);
            numFullBackupPeriodDays.Maximum = 3650;
            numFullBackupPeriodDays.Minimum = 1;
            numFullBackupPeriodDays.Name = "numFullBackupPeriodDays";
            numFullBackupPeriodDays.Size = new Size(534, 23);
            numFullBackupPeriodDays.TabIndex = 4;
            numFullBackupPeriodDays.Value = 60;

            // dtpBackupTime
            dtpBackupTime.Format = DateTimePickerFormat.Custom;
            dtpBackupTime.CustomFormat = "HH:mm";
            dtpBackupTime.ShowUpDown = true;
            dtpBackupTime.Location = new Point(583, 165);
            dtpBackupTime.Name = "dtpBackupTime";
            dtpBackupTime.Size = new Size(534, 23);
            dtpBackupTime.TabIndex = 8;

            // label5
            label5.AutoSize = true;
            label5.Location = new Point(371, 223);
            label5.Name = "label5";
            label5.Size = new Size(135, 15);
            label5.TabIndex = 9;
            label5.Text = "Thư mục loại trừ";

            // lstExcludeFolders
            lstExcludeFolders.FormattingEnabled = true;
            lstExcludeFolders.ItemHeight = 15;
            lstExcludeFolders.Location = new Point(371, 245);
            lstExcludeFolders.Name = "lstExcludeFolders";
            lstExcludeFolders.Size = new Size(370, 154);
            lstExcludeFolders.TabIndex = 10;

            // btnAddExcludeFolder
            btnAddExcludeFolder.Location = new Point(750, 245);
            btnAddExcludeFolder.Name = "btnAddExcludeFolder";
            btnAddExcludeFolder.Size = new Size(100, 30);
            btnAddExcludeFolder.TabIndex = 11;
            btnAddExcludeFolder.Text = "Thêm";
            btnAddExcludeFolder.UseVisualStyleBackColor = true;

            // btnRemoveExcludeFolder
            btnRemoveExcludeFolder.Location = new Point(750, 281);
            btnRemoveExcludeFolder.Name = "btnRemoveExcludeFolder";
            btnRemoveExcludeFolder.Size = new Size(100, 30);
            btnRemoveExcludeFolder.TabIndex = 12;
            btnRemoveExcludeFolder.Text = "Xóa";
            btnRemoveExcludeFolder.UseVisualStyleBackColor = true;

            // label6
            label6.AutoSize = true;
            label6.Location = new Point(871, 223);
            label6.Name = "label6";
            label6.Size = new Size(156, 15);
            label6.TabIndex = 13;
            label6.Text = "Extension / pattern loại trừ";

            // lstExcludeRules
            lstExcludeRules.FormattingEnabled = true;
            lstExcludeRules.ItemHeight = 15;
            lstExcludeRules.Location = new Point(871, 245);
            lstExcludeRules.Name = "lstExcludeRules";
            lstExcludeRules.Size = new Size(339, 154);
            lstExcludeRules.TabIndex = 14;

            // btnAddExcludeRule
            btnAddExcludeRule.Location = new Point(871, 410);
            btnAddExcludeRule.Name = "btnAddExcludeRule";
            btnAddExcludeRule.Size = new Size(100, 30);
            btnAddExcludeRule.TabIndex = 15;
            btnAddExcludeRule.Text = "Thêm";
            btnAddExcludeRule.UseVisualStyleBackColor = true;

            // btnRemoveExcludeRule
            btnRemoveExcludeRule.Location = new Point(980, 410);
            btnRemoveExcludeRule.Name = "btnRemoveExcludeRule";
            btnRemoveExcludeRule.Size = new Size(100, 30);
            btnRemoveExcludeRule.TabIndex = 16;
            btnRemoveExcludeRule.Text = "Xóa";
            btnRemoveExcludeRule.UseVisualStyleBackColor = true;
            btnbrowser.Click += btnbrowser_Click;
            btnCancle.Click += btnCancle_Click;
            btnAddExcludeFolder.Click += btnAddExcludeFolder_Click;
            btnRemoveExcludeFolder.Click += btnRemoveExcludeFolder_Click;
            btnAddExcludeRule.Click += btnAddExcludeRule_Click;
            btnRemoveExcludeRule.Click += btnRemoveExcludeRule_Click;
            // 
            // frmSetBackup
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1231, 643);
            Controls.Add(btnRemoveExcludeRule);
            Controls.Add(btnAddExcludeRule);
            Controls.Add(lstExcludeRules);
            Controls.Add(label6);
            Controls.Add(btnRemoveExcludeFolder);
            Controls.Add(btnAddExcludeFolder);
            Controls.Add(lstExcludeFolders);
            Controls.Add(label5);
            Controls.Add(dtpBackupTime);
            Controls.Add(numBackupIntervalDays);
            Controls.Add(numFullBackupPeriodDays);
            Controls.Add(textBox1);
            Controls.Add(label4);
            Controls.Add(txtDayToBackup);
            Controls.Add(label3);
            Controls.Add(txtperiodbk);
            Controls.Add(txtduongdanbk);
            Controls.Add(label2);
            Controls.Add(btnbrowser);
            Controls.Add(label1);
            Controls.Add(TvCheckBackup);
            Controls.Add(btnCancle);
            Controls.Add(btnDeploy);
            Name = "frmSetBackup";
            Text = "frmSetBackup";
            ((System.ComponentModel.ISupportInitialize)numBackupIntervalDays).EndInit();
            ((System.ComponentModel.ISupportInitialize)numFullBackupPeriodDays).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnDeploy;
        private Button btnCancle;
        private TreeView TvCheckBackup;
        private Label label1;
        private Button btnbrowser;
        private TextBox txtduongdanbk;
        private Label label2;
        private TextBox txtperiodbk;
        private TextBox txtDayToBackup;
        private Label label3;
        private TextBox textBox1;
        private Label label4;
        private NumericUpDown numBackupIntervalDays;
        private NumericUpDown numFullBackupPeriodDays;
        private DateTimePicker dtpBackupTime;
        private Label label5;
        private ListBox lstExcludeFolders;
        private Button btnAddExcludeFolder;
        private Button btnRemoveExcludeFolder;
        private Label label6;
        private ListBox lstExcludeRules;
        private Button btnAddExcludeRule;
        private Button btnRemoveExcludeRule;
    }
}
