using AgentShared;

namespace AgentControl
{
    internal sealed class AgentUpdateStatusForm : Form
    {
        private readonly ListBox _statusList;
        private readonly Label _headerLabel;

        public AgentUpdateStatusForm(string agentId)
        {
            Text = "Agent Update - " + agentId;
            StartPosition = FormStartPosition.CenterParent;
            Width = 760;
            Height = 420;
            MinimizeBox = true;
            MaximizeBox = false;

            _headerLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 10, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Text = "Agent: " + agentId
            };

            _statusList = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                HorizontalScrollbar = true
            };

            Controls.Add(_statusList);
            Controls.Add(_headerLabel);
        }

        public void AddStatus(AgentUpdateStatus status)
        {
            string timeText = string.IsNullOrWhiteSpace(status.CreatedAt)
                ? DateTime.Now.ToString("HH:mm:ss")
                : status.CreatedAt;

            string source = string.IsNullOrWhiteSpace(status.Source)
                ? "Control"
                : status.Source;

            string line = $"[{timeText}] [{source}] {status.Status}: {status.Message}";
            _statusList.Items.Add(line);
            _statusList.TopIndex = Math.Max(0, _statusList.Items.Count - 1);
        }
    }
}
