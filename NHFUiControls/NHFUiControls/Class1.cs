using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NHFUiControls
{
    [ToolboxItem(true)]
    [DefaultEvent("SelectedIndexChanged")]
    public class ListBoxNHF : ListBox
    {
        private int hoveredIndex = -1;
        private int lastSelectedIndex = -1;
        private int hoveredDeleteIndex = -1;

        private readonly Font titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        private readonly Font subFont = new Font("Segoe UI", 9f);

        public event EventHandler<AgentDeleteClickedEventArgs>? AgentDeleteClicked;

        [Category("NHF Appearance")]
        public int CardHeight
        {
            get => ItemHeight;
            set
            {
                ItemHeight = value;
                //Refresh();
            }
        }

        [Category("NHF Appearance")]
        public int CardBorderRadius { get; set; } = 12;
        [Category("NHF Appearance")]
        public Color SelectedCardColor { get; set; } = Color.FromArgb(205, 220, 242);
        [Category("NHF Appearance")]
        public Color HoverCardColor { get; set; } = Color.FromArgb(245, 248, 253);
        [Category("NHF Appearance")]
        public Color NormalCardColor { get; set; } = Color.White;

        public ListBoxNHF()
        {
            // Giữ nguyên các dòng cấu hình cũ của fen...
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            //DrawMode = DrawMode.OwnerDrawVariable;
            DrawMode = DrawMode.OwnerDrawFixed;
            CardHeight = 95;
            BorderStyle = BorderStyle.None;
            BackColor = Color.FromArgb(235, 241, 250);
            IntegralHeight = false;
            Font = new Font("Segoe UI", 9.5f);

            this.MouseMove += ListBoxNHF_MouseMove;
            this.MouseLeave += ListBoxNHF_MouseLeave;

            // --- THÊM DÒNG NÀY ĐỂ FIX BUG SELECTED NHIỀU CARD ---
            this.SelectedIndexChanged += ListBoxNHF_SelectedIndexChanged;
        }

        // Hàm xử lý khi thay đổi lựa chọn Card
       

        private void InvalidateItem(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            Invalidate(GetItemRectangle(index));
        }

        private void ListBoxNHF_MouseMove(object sender, MouseEventArgs e)
        {
            int newIndex = IndexFromPoint(e.Location);
            int newDeleteIndex = GetDeleteButtonIndexAt(e.Location);
            if (newIndex == hoveredIndex && newDeleteIndex == hoveredDeleteIndex) return;

            int oldIndex = hoveredIndex;
            int oldDeleteIndex = hoveredDeleteIndex;
            hoveredIndex = newIndex;
            hoveredDeleteIndex = newDeleteIndex;

            InvalidateItem(oldIndex);
            InvalidateItem(newIndex);
            InvalidateItem(oldDeleteIndex);
            InvalidateItem(newDeleteIndex);
            Cursor = hoveredDeleteIndex >= 0 ? Cursors.Hand : Cursors.Default;
        }

        private void ListBoxNHF_MouseLeave(object sender, EventArgs e)
        {
            int oldIndex = hoveredIndex;
            int oldDeleteIndex = hoveredDeleteIndex;
            hoveredIndex = -1;
            hoveredDeleteIndex = -1;
            InvalidateItem(oldIndex);
            InvalidateItem(oldDeleteIndex);
            Cursor = Cursors.Default;
            //this.Refresh();
        }

        private void ListBoxNHF_SelectedIndexChanged(object sender, EventArgs e)
        {
            InvalidateItem(lastSelectedIndex);
            InvalidateItem(SelectedIndex);
            lastSelectedIndex = SelectedIndex;
        }

        protected override void OnMeasureItem(MeasureItemEventArgs e)
        {
            e.ItemHeight = ItemHeight;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int deleteIndex = GetDeleteButtonIndexAt(e.Location);
            if (deleteIndex >= 0 && Items[deleteIndex] is AgentInfo agent)
            {
                AgentDeleteClicked?.Invoke(this, new AgentDeleteClickedEventArgs(agent, deleteIndex));
                return;
            }

            base.OnMouseDown(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var bg = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(bg, ClientRectangle);
        }

        public void AddAgent(
            string computerName,
            string userName,
            string ip,
            string os,
            string agentID,
            bool online)
        {
            Items.Add(new AgentInfo
            {
                ComputerName = computerName,
                UserName = userName,
                Ip = ip,
                Os = os,
                AgentID = agentID,
                IsOnline = online
            });

            Invalidate();
        }

        public void SetOnline(string agentID, bool online)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] is AgentInfo agent && agent.AgentID == agentID)
                {
                    agent.IsOnline = online;
                    InvalidateItem(i);
                    return;
                }
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            if (Items[e.Index] is not AgentInfo agent) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bgBrush = new SolidBrush(BackColor))
                g.FillRectangle(bgBrush, e.Bounds);

            Rectangle card = new Rectangle(
                e.Bounds.Left + 8,
                e.Bounds.Top + 6,
                e.Bounds.Width - 16,
                e.Bounds.Height - 12
            );

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool hovered = e.Index == hoveredIndex;

            Color cardColor = NormalCardColor;
            if (selected) cardColor = SelectedCardColor;
            else if (hovered) cardColor = HoverCardColor;

            using var cardBrush = new SolidBrush(cardColor);
            using var path = RoundRect(card, CardBorderRadius);
            g.FillPath(cardBrush, path);

            Color borderColor = selected
                ? Color.FromArgb(160, 190, 230)
                : Color.FromArgb(218, 226, 237);

            using var borderPen = new Pen(borderColor);
            g.DrawPath(borderPen, path);

            int iconX = card.Left + 14;
            int iconY = card.Top + (card.Height - 32) / 2;

            DrawComputerIcon(g, iconX, iconY);
            DrawStatusDot(g, iconX + 28, iconY + 25, agent.IsOnline);

            int textX = card.Left + 64;
            int textY = card.Top + (card.Height - 98) / 2;
            int textWidth = card.Width - 95;

            TextRenderer.DrawText(g, agent.ComputerName, titleFont,
                new Rectangle(textX, textY, textWidth, 22),
                Color.FromArgb(25, 40, 60),
                TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(g, $"User: {agent.UserName}", subFont,
                new Rectangle(textX, textY + 22, textWidth, 20),
                Color.FromArgb(70, 85, 105),
                TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(g, $"IP: {agent.Ip}", subFont,
                new Rectangle(textX, textY + 42, textWidth, 20),
                Color.FromArgb(70, 85, 105),
                TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(g, $"OS: {agent.Os}", subFont,
                new Rectangle(textX, textY + 62, textWidth, 20),
                Color.FromArgb(70, 85, 105),
                TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(g, $"Agent ID: {agent.AgentID}", subFont,
                new Rectangle(textX, textY + 82, textWidth, 20),
                Color.FromArgb(70, 85, 105),
                TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(g,
                agent.IsOnline ? "Online" : "Offline",
                subFont,
                new Point(card.Right - 78, card.Top + (card.Height / 2)),
                agent.IsOnline
                    ? Color.FromArgb(35, 160, 70)
                    : Color.FromArgb(210, 55, 55));

            DrawDeleteButton(g, GetDeleteButtonBounds(card), e.Index == hoveredDeleteIndex);
        }

        private int GetDeleteButtonIndexAt(Point location)
        {
            int index = IndexFromPoint(location);
            if (index < 0 || index >= Items.Count)
            {
                return -1;
            }

            Rectangle itemBounds = GetItemRectangle(index);
            Rectangle card = new Rectangle(
                itemBounds.Left + 8,
                itemBounds.Top + 6,
                itemBounds.Width - 16,
                itemBounds.Height - 12
            );

            return GetDeleteButtonBounds(card).Contains(location) ? index : -1;
        }

        private Rectangle GetDeleteButtonBounds(Rectangle card)
        {
            return new Rectangle(card.Right - 30, card.Top + 12, 22, 22);
        }

        private void DrawDeleteButton(Graphics g, Rectangle bounds, bool hovered)
        {
            Color color = hovered ? Color.FromArgb(210, 40, 40) : Color.Red;
            if (hovered)
            {
                using var hoverBrush = new SolidBrush(Color.FromArgb(255, 235, 235));
                g.FillEllipse(hoverBrush, bounds);
            }

            using var pen = new Pen(color, 2);
            g.DrawLine(pen, bounds.Left + 6, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            g.DrawLine(pen, bounds.Right - 6, bounds.Top + 6, bounds.Left + 6, bounds.Bottom - 6);
        }

        private GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d <= 0) d = 1;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }

        private void DrawComputerIcon(Graphics g, int x, int y)
        {
            using var p = new Pen(Color.FromArgb(80, 95, 115), 2);
            using var b = new SolidBrush(Color.FromArgb(245, 248, 252));

            Rectangle screen = new Rectangle(x, y, 34, 24);
            g.FillRectangle(b, screen);
            g.DrawRectangle(p, screen);

            g.DrawLine(p, x + 12, y + 28, x + 22, y + 28);
            g.DrawLine(p, x + 17, y + 24, x + 17, y + 30);
        }

        private void DrawStatusDot(Graphics g, int x, int y, bool online)
        {
            Color c = online
                ? Color.FromArgb(40, 180, 75)
                : Color.FromArgb(220, 55, 55);

            using var dot = new SolidBrush(c);
            using var whitePen = new Pen(Color.White, 2);

            g.FillEllipse(dot, x, y, 16, 16);

            if (online)
            {
                g.DrawLines(whitePen, new[]
                {
                    new Point(x + 4, y + 8),
                    new Point(x + 7, y + 11),
                    new Point(x + 12, y + 5)
                });
            }
            else
            {
                g.DrawLine(whitePen, x + 4, y + 4, x + 12, y + 12);
                g.DrawLine(whitePen, x + 12, y + 4, x + 4, y + 12);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont.Dispose();
                subFont.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public class AgentInfo
    {
        public string ComputerName { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Ip { get; set; } = "";
        public string Os { get; set; } = "";
        public string AgentID { get; set; } = "";
        public bool IsOnline { get; set; }

        public override string ToString() => ComputerName;
    }

    public class AgentDeleteClickedEventArgs : EventArgs
    {
        public AgentInfo Agent { get; }
        public int Index { get; }

        public AgentDeleteClickedEventArgs(AgentInfo agent, int index)
        {
            Agent = agent;
            Index = index;
        }
    }
}
