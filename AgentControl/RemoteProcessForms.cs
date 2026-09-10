using AgentShared;

namespace AgentControl
{
    internal sealed class RemoteProcessConfirmationDialog : Form
    {
        private readonly TextBox _argumentsTextBox;

        public string ProcessArguments => _argumentsTextBox.Text.Trim();

        public RemoteProcessConfirmationDialog(
            string agentName,
            string agentId,
            string filePath,
            string msiHandling)
        {
            Text = "Xác nhận chạy tiến trình";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(650, 370);

            Controls.Add(new Label
            {
                Left = 18,
                Top = 14,
                Width = 614,
                Height = 28,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Text = "Chạy file trên Agent"
            });

            AddField("Agent:", $"{agentName} ({agentId})", 52);
            AddField("Đường dẫn:", filePath, 96);
            AddField("Quyền chạy:", "LocalSystem (Session 0)", 140);
            AddField("Xử lý MSI:", msiHandling, 184);

            Controls.Add(new Label
            {
                Left = 18,
                Top = 231,
                Width = 120,
                Height = 22,
                Text = "Tham số:"
            });
            _argumentsTextBox = new TextBox
            {
                Left = 140,
                Top = 228,
                Width = 492,
                MaxLength = RemoteProcessRules.MaximumArgumentsLength
            };
            Controls.Add(_argumentsTextBox);

            Controls.Add(new Label
            {
                Left = 18,
                Top = 266,
                Width = 614,
                Height = 42,
                ForeColor = Color.FromArgb(192, 57, 43),
                Text = "MSI luôn chạy /qn /norestart. Với EXE/BAT/CMD vẫn cần tham số silent riêng nếu chương trình yêu cầu tương tác."
            });

            var runButton = new Button
            {
                Left = 456,
                Top = 322,
                Width = 84,
                Height = 30,
                Text = "Chạy",
                DialogResult = DialogResult.OK
            };
            var cancelButton = new Button
            {
                Left = 548,
                Top = 322,
                Width = 84,
                Height = 30,
                Text = "Hủy",
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(runButton);
            Controls.Add(cancelButton);
            AcceptButton = runButton;
            CancelButton = cancelButton;
        }

        private void AddField(string label, string value, int top)
        {
            Controls.Add(new Label
            {
                Left = 18,
                Top = top + 3,
                Width = 120,
                Height = 22,
                Text = label
            });
            Controls.Add(new TextBox
            {
                Left = 140,
                Top = top,
                Width = 492,
                ReadOnly = true,
                Text = value
            });
        }
    }

    internal sealed class RemoteProcessStatusForm : Form
    {
        private readonly Label _statusLabel;
        private readonly ListBox _historyList;

        public RemoteProcessStatusForm(string agentName, string agentId, string filePath)
        {
            Text = "Run Process - " + agentName;
            StartPosition = FormStartPosition.CenterParent;
            Width = 820;
            Height = 430;
            MinimizeBox = true;
            MaximizeBox = false;

            var headerLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 58,
                Padding = new Padding(10, 7, 10, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Text = $"Agent: {agentName} ({agentId}){Environment.NewLine}File: {filePath}"
            };
            _statusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 38,
                Padding = new Padding(10, 0, 10, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Text = "ĐANG GỬI LỆNH"
            };
            _historyList = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                HorizontalScrollbar = true
            };

            Controls.Add(_historyList);
            Controls.Add(_statusLabel);
            Controls.Add(headerLabel);
        }

        public void AddStatus(RemoteProcessStatus status)
        {
            _statusLabel.Text = status.Status;
            _statusLabel.ForeColor = status.Status switch
            {
                RemoteProcessStatuses.Completed => Color.FromArgb(25, 135, 84),
                RemoteProcessStatuses.Error => Color.FromArgb(220, 53, 69),
                RemoteProcessStatuses.Running => Color.FromArgb(13, 110, 253),
                _ => Color.FromArgb(108, 117, 125)
            };

            DateTime timestamp = status.CreatedAtUtc == default
                ? DateTime.Now
                : status.CreatedAtUtc.ToLocalTime();
            string details = status.ProcessId.HasValue ? $" | PID={status.ProcessId}" : string.Empty;
            details += status.ExitCode.HasValue ? $" | ExitCode={status.ExitCode}" : string.Empty;
            details += status.ErrorCode.HasValue ? $" | ErrorCode={status.ErrorCode}" : string.Empty;
            details += status.RequiresRestart ? " | CẦN KHỞI ĐỘNG LẠI" : string.Empty;
            _historyList.Items.Add($"[{timestamp:HH:mm:ss}] {status.Status}: {status.Message}{details}");
            if (!string.IsNullOrWhiteSpace(status.DiagnosticDetails))
            {
                foreach (string line in status.DiagnosticDetails.Split(
                    new[] { "\r\n", "\n" },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    _historyList.Items.Add("    MSI: " + line);
                }
            }
            _historyList.TopIndex = Math.Max(0, _historyList.Items.Count - 1);
        }
    }
}
