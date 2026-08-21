using System;
using System.Drawing;
using System.Windows.Forms;

namespace AgentControl
{
    public partial class frmSetBackup : Form
    {
        public frmSetBackup()
        {
            InitializeComponent();
            txtduongdanbk.ReadOnly = true;
            dtpBackupTime.Value = DateTime.Today.AddHours(23);
        }

        private void btnbrowser_Click(object? sender, EventArgs e)
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "Chọn thư mục lưu backup trên máy AgentControl",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                txtduongdanbk.Text = dialog.SelectedPath;
            }
        }

        private void btnCancle_Click(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnAddExcludeFolder_Click(object? sender, EventArgs e)
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "Chọn thư mục cần loại trừ khỏi backup",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog(this) == DialogResult.OK &&
                !lstExcludeFolders.Items.Contains(dialog.SelectedPath))
            {
                lstExcludeFolders.Items.Add(dialog.SelectedPath);
            }
        }

        private void btnRemoveExcludeFolder_Click(object? sender, EventArgs e)
        {
            if (lstExcludeFolders.SelectedIndex >= 0)
            {
                lstExcludeFolders.Items.RemoveAt(lstExcludeFolders.SelectedIndex);
            }
        }

        private void btnAddExcludeRule_Click(object? sender, EventArgs e)
        {
            using Form inputForm = new Form
            {
                Text = "Thêm extension hoặc pattern loại trừ",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(420, 120)
            };

            Label label = new Label
            {
                AutoSize = true,
                Location = new Point(12, 15),
                Text = "Nhập ví dụ: .tmp, .cache, ~* hoặc ~$*"
            };
            TextBox input = new TextBox
            {
                Location = new Point(12, 40),
                Width = 396
            };
            Button okButton = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(252, 75),
                Size = new Size(75, 28),
                Text = "OK"
            };
            Button cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(333, 75),
                Size = new Size(75, 28),
                Text = "Hủy"
            };

            inputForm.Controls.AddRange(new Control[] { label, input, okButton, cancelButton });
            inputForm.AcceptButton = okButton;
            inputForm.CancelButton = cancelButton;

            if (inputForm.ShowDialog(this) == DialogResult.OK)
            {
                string value = input.Text.Trim();
                if (!string.IsNullOrWhiteSpace(value) && !lstExcludeRules.Items.Contains(value))
                {
                    lstExcludeRules.Items.Add(value);
                }
            }
        }

        private void btnRemoveExcludeRule_Click(object? sender, EventArgs e)
        {
            if (lstExcludeRules.SelectedIndex >= 0)
            {
                lstExcludeRules.Items.RemoveAt(lstExcludeRules.SelectedIndex);
            }
        }
    }
}
