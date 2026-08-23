namespace AgentControl
{
    public sealed class BackupDashboardProgressBarColumn : DataGridViewColumn
    {
        public BackupDashboardProgressBarColumn()
            : base(new BackupDashboardProgressBarCell())
        {
        }
    }

    public sealed class BackupDashboardProgressBarCell : DataGridViewTextBoxCell
    {
        public BackupDashboardProgressBarCell()
        {
            ValueType = typeof(int);
        }

        protected override void Paint(
            Graphics graphics,
            Rectangle clipBounds,
            Rectangle cellBounds,
            int rowIndex,
            DataGridViewElementStates elementState,
            object? value,
            object? formattedValue,
            string? errorText,
            DataGridViewCellStyle cellStyle,
            DataGridViewAdvancedBorderStyle advancedBorderStyle,
            DataGridViewPaintParts paintParts)
        {
            base.Paint(
                graphics,
                clipBounds,
                cellBounds,
                rowIndex,
                elementState,
                value,
                formattedValue,
                errorText,
                cellStyle,
                advancedBorderStyle,
                paintParts & ~DataGridViewPaintParts.ContentForeground);

            if (DataGridView == null || rowIndex < 0 || rowIndex >= DataGridView.Rows.Count ||
                DataGridView.Rows[rowIndex].Tag is not BackupDashboardAgentState state)
            {
                return;
            }

            if (state.ProgressMode == BackupDashboardProgressMode.Waiting)
            {
                DrawCenteredText(
                    graphics,
                    cellBounds,
                    cellStyle,
                    state.ProgressDisplayText,
                    Color.FromArgb(25, 135, 84),
                    true);
                return;
            }

            if (state.ProgressMode == BackupDashboardProgressMode.None)
            {
                return;
            }

            int progress = Math.Clamp(state.ProgressPercentage, 0, 100);
            Rectangle bar = Rectangle.Inflate(cellBounds, -4, -5);
            if (bar.Width <= 1 || bar.Height <= 1)
            {
                return;
            }

            bool isRed = state.ProgressMode is BackupDashboardProgressMode.Disconnected or BackupDashboardProgressMode.Error;
            Color fillColor = isRed ? Color.FromArgb(220, 53, 69) : Color.FromArgb(46, 204, 113);
            Color emptyColor = isRed ? Color.FromArgb(255, 235, 238) : Color.FromArgb(245, 247, 249);
            using (Brush emptyBrush = new SolidBrush(emptyColor))
            {
                graphics.FillRectangle(emptyBrush, bar);
            }
            using (Pen borderPen = new Pen(Color.LightGray))
            {
                graphics.DrawRectangle(borderPen, bar);
            }

            int fillWidth = (int)Math.Round((bar.Width - 1) * (progress / 100d));
            if (fillWidth > 0)
            {
                using Brush fillBrush = new SolidBrush(fillColor);
                graphics.FillRectangle(fillBrush, bar.X + 1, bar.Y + 1, fillWidth, bar.Height - 1);
            }

            DrawCenteredText(graphics, cellBounds, cellStyle, $"{progress}%", cellStyle.ForeColor, false);
        }

        private static void DrawCenteredText(
            Graphics graphics,
            Rectangle bounds,
            DataGridViewCellStyle style,
            string text,
            Color color,
            bool bold)
        {
            Font baseFont = style.Font ?? SystemFonts.DefaultFont;
            using Font? ownedFont = bold
                ? new Font(baseFont.FontFamily, baseFont.Size + 0.5F, FontStyle.Bold)
                : null;
            Font font = ownedFont ?? baseFont;
            TextRenderer.DrawText(
                graphics,
                text,
                font,
                bounds,
                color,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }
}
