using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NHFUiControls
{
    [ToolboxItem(true)]
    [DefaultEvent("SelectedIndexChanged")]
    public class ListBoxNHF : ListBox
    {
        private int hoveredIndex = -1;
        private int hoveredDeleteIndex = -1;
        private int hoveredOwnerIndex = -1;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_SETREDRAW = 0x000B;
        private const int WM_VSCROLL = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private readonly System.Windows.Forms.Timer scrollSettleTimer;
        private bool isScrolling;
        private bool visualUpdatesSuspended;

        private readonly Font titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        private readonly Font subFont = new Font("Segoe UI", 9f);
        private readonly Font versionFont = new Font("Segoe UI", 8f, FontStyle.Bold);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public event EventHandler<AgentDeleteClickedEventArgs>? AgentDeleteClicked;
        public event EventHandler<AgentOwnerEditRequestedEventArgs>? AgentOwnerEditRequested;

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

            scrollSettleTimer = new System.Windows.Forms.Timer { Interval = 120 };
            scrollSettleTimer.Tick += ScrollSettleTimer_Tick;
        }

        // Hàm xử lý khi thay đổi lựa chọn Card
       

        private void InvalidateItem(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            Invalidate(GetItemRectangle(index));
        }

        private void InvalidateItems(params int[] indexes)
        {
            var invalidated = new HashSet<int>();
            foreach (int index in indexes)
            {
                if (invalidated.Add(index))
                {
                    InvalidateItem(index);
                }
            }
        }

        public void SetVisualUpdatesSuspended(bool suspended)
        {
            if (visualUpdatesSuspended == suspended)
            {
                return;
            }

            visualUpdatesSuspended = suspended;
            if (IsHandleCreated)
            {
                SendMessage(Handle, WM_SETREDRAW, suspended ? IntPtr.Zero : new IntPtr(1), IntPtr.Zero);
            }

            if (!suspended)
            {
                Invalidate();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (visualUpdatesSuspended)
            {
                SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void ListBoxNHF_MouseMove(object? sender, MouseEventArgs e)
        {
            if (visualUpdatesSuspended || isScrolling)
            {
                return;
            }

            UpdateHoverState(e.Location, true);
        }

        private void UpdateHoverState(Point location, bool invalidateChangedItems)
        {
            int newIndex = IndexFromPoint(location);
            int newDeleteIndex = GetDeleteButtonIndexAt(location);
            int newOwnerIndex = GetOwnerNameIndexAt(location);
            if (newIndex == hoveredIndex && newDeleteIndex == hoveredDeleteIndex && newOwnerIndex == hoveredOwnerIndex) return;

            int oldIndex = hoveredIndex;
            int oldDeleteIndex = hoveredDeleteIndex;
            int oldOwnerIndex = hoveredOwnerIndex;
            hoveredIndex = newIndex;
            hoveredDeleteIndex = newDeleteIndex;
            hoveredOwnerIndex = newOwnerIndex;

            if (invalidateChangedItems)
            {
                InvalidateItems(oldIndex, newIndex, oldDeleteIndex, newDeleteIndex, oldOwnerIndex, newOwnerIndex);
            }

            Cursor = hoveredDeleteIndex >= 0 || hoveredOwnerIndex >= 0 ? Cursors.Hand : Cursors.Default;
        }

        private void ListBoxNHF_MouseLeave(object? sender, EventArgs e)
        {
            if (visualUpdatesSuspended)
            {
                return;
            }

            ClearHoverState(true);
        }

        private void ClearHoverState(bool invalidateChangedItems)
        {
            int oldIndex = hoveredIndex;
            int oldDeleteIndex = hoveredDeleteIndex;
            int oldOwnerIndex = hoveredOwnerIndex;
            hoveredIndex = -1;
            hoveredDeleteIndex = -1;
            hoveredOwnerIndex = -1;
            if (invalidateChangedItems)
            {
                InvalidateItems(oldIndex, oldDeleteIndex, oldOwnerIndex);
            }

            Cursor = Cursors.Default;
        }

        private void BeginScrollVisualThrottle()
        {
            isScrolling = true;
            ClearHoverState(false);
            scrollSettleTimer.Stop();
            scrollSettleTimer.Start();
        }

        private void ScrollSettleTimer_Tick(object? sender, EventArgs e)
        {
            scrollSettleTimer.Stop();
            isScrolling = false;

            Point mousePoint = PointToClient(Cursor.Position);
            if (ClientRectangle.Contains(mousePoint))
            {
                UpdateHoverState(mousePoint, true);
            }
        }

        protected override void OnMeasureItem(MeasureItemEventArgs e)
        {
            e.ItemHeight = ItemHeight;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL || m.Msg == WM_MOUSEHWHEEL)
            {
                BeginScrollVisualThrottle();
            }

            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int deleteIndex = GetDeleteButtonIndexAt(e.Location);
            if (deleteIndex >= 0 && Items[deleteIndex] is AgentInfo agent)
            {
                AgentDeleteClicked?.Invoke(this, new AgentDeleteClickedEventArgs(agent, deleteIndex));
                return;
            }

            int ownerIndex = GetOwnerNameIndexAt(e.Location);
            if (ownerIndex >= 0 && Items[ownerIndex] is AgentInfo ownerAgent)
            {
                SelectedIndex = ownerIndex;
                AgentOwnerEditRequested?.Invoke(this, new AgentOwnerEditRequestedEventArgs(ownerAgent, ownerIndex));
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
            string agentVersion,
            bool online,
            string ownerName = "")
        {
            int newIndex = Items.Add(new AgentInfo
            {
                ComputerName = computerName,
                UserName = userName,
                Ip = ip,
                Os = os,
                AgentID = agentID,
                AgentVersion = agentVersion,
                OwnerName = ownerName,
                IsOnline = online
            });

            InvalidateItem(newIndex);
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
            bool hovered = !isScrolling && e.Index == hoveredIndex;

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
            int textY = card.Top + 8;
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
                new Rectangle(textX, textY + 82, Math.Max(70, card.Right - textX - 10), 20),
                Color.FromArgb(70, 85, 105),
                TextFormatFlags.EndEllipsis);

            Rectangle ownerBounds = GetOwnerNameBounds(card);
            string ownerText = string.IsNullOrWhiteSpace(agent.OwnerName)
                ? "Người sử dụng"
                : agent.OwnerName;

            bool hasOwner = !string.IsNullOrWhiteSpace(agent.OwnerName);

            Color ownerColor = hasOwner
                ? Color.FromArgb(0, 120, 40)      // Xanh lá đậm
                : Color.FromArgb(120, 130, 145);  // Xám

            using var ownerFont = new Font(
                subFont.FontFamily,
                hasOwner ? subFont.Size + 2f : subFont.Size - 1f,
                hasOwner ? FontStyle.Bold : FontStyle.Regular);

            if (!isScrolling && e.Index == hoveredOwnerIndex)
            {
                using var ownerHoverBrush = new SolidBrush(Color.FromArgb(232, 240, 252));
                g.FillRectangle(ownerHoverBrush, ownerBounds);
            }

            TextRenderer.DrawText(
                g,
                ownerText,
                ownerFont,
                ownerBounds,
                ownerColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            /* string versionText = string.IsNullOrWhiteSpace(agent.AgentVersion)
                 ? "ver ?"
                 : $"ver {agent.AgentVersion}";
             TextRenderer.DrawText(g, versionText, versionFont,
                 new Rectangle(card.Right - 68, textY + 82, 60, 20),
                 Color.FromArgb(235, 126, 25),
                 TextFormatFlags.Right | TextFormatFlags.EndEllipsis);*/
            //
            //
            //
            string statusText = agent.IsOnline ? "Online" : "Offline";

            string versionText = string.IsNullOrWhiteSpace(agent.AgentVersion)
                ? "ver ?"
                : $"ver {agent.AgentVersion}";

            int lineY = textY + 55;
            int gap = 1;

            int statusX = card.Right - 78;

            Size statusSize = TextRenderer.MeasureText(
                statusText,
                subFont,
                Size.Empty,
                TextFormatFlags.NoPadding);

            Size versionSize = TextRenderer.MeasureText(
                versionText,
                versionFont,
                Size.Empty,
                TextFormatFlags.NoPadding);

            int statusCenterX = statusX + statusSize.Width / 2;

            TextRenderer.DrawText(
                g,
                statusText,
                subFont,
                new Point(
                    statusX,
                    lineY - statusSize.Height - gap
                ),
                agent.IsOnline
                    ? Color.FromArgb(35, 160, 70)
                    : Color.FromArgb(210, 55, 55),
                TextFormatFlags.NoPadding
            );

            TextRenderer.DrawText(
                g,
                versionText,
                versionFont,
                new Point(
                    statusCenterX - versionSize.Width / 2,
                    lineY + gap
                ),
                Color.FromArgb(34, 4, 202),
                TextFormatFlags.NoPadding
            );

            //
            //
            //

            DrawDeleteButton(g, GetDeleteButtonBounds(card), !isScrolling && e.Index == hoveredDeleteIndex);
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

        private int GetOwnerNameIndexAt(Point location)
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

            return GetOwnerNameBounds(card).Contains(location) ? index : -1;
        }

        private Rectangle GetDeleteButtonBounds(Rectangle card)
        {
            return new Rectangle(card.Right - 30, card.Top + 12, 22, 22);
        }

        private Rectangle GetOwnerNameBounds(Rectangle card)
        {
            int textX = card.Left + 64;
            return new Rectangle(textX, card.Top + 110, Math.Max(70, card.Right - textX - 10), 20);
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
                scrollSettleTimer.Dispose();
                titleFont.Dispose();
                subFont.Dispose();
                versionFont.Dispose();
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
        public string AgentVersion { get; set; } = "";
        public string OwnerName { get; set; } = "";
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

    public class AgentOwnerEditRequestedEventArgs : EventArgs
    {
        public AgentInfo Agent { get; }
        public int Index { get; }

        public AgentOwnerEditRequestedEventArgs(AgentInfo agent, int index)
        {
            Agent = agent;
            Index = index;
        }
    }
}
