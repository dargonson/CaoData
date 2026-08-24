using System.ComponentModel;

namespace AgentControl
{
    public enum RecoveryProgressDisplayState
    {
        Normal,
        Completed,
        Error
    }

    public sealed class RecoveryProgressBar : Control
    {
        private int _minimum;
        private int _maximum = 100;
        private int _value;
        private RecoveryProgressDisplayState _displayState;

        public RecoveryProgressBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.ControlText;
            Size = new Size(120, 23);
        }

        [DefaultValue(0)]
        public int Minimum
        {
            get => _minimum;
            set
            {
                if (value >= _maximum)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Minimum phải nhỏ hơn Maximum.");
                }

                _minimum = value;
                _value = Math.Clamp(_value, _minimum, _maximum);
                Invalidate();
            }
        }

        [DefaultValue(100)]
        public int Maximum
        {
            get => _maximum;
            set
            {
                if (value <= _minimum)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Maximum phải lớn hơn Minimum.");
                }

                _maximum = value;
                _value = Math.Clamp(_value, _minimum, _maximum);
                Invalidate();
            }
        }

        [DefaultValue(0)]
        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Clamp(value, _minimum, _maximum);
                if (_value == clamped)
                {
                    return;
                }

                _value = clamped;
                Invalidate();
            }
        }

        [DefaultValue(RecoveryProgressDisplayState.Normal)]
        public RecoveryProgressDisplayState DisplayState
        {
            get => _displayState;
            set
            {
                if (_displayState == value)
                {
                    return;
                }

                _displayState = value;
                Invalidate();
            }
        }

        [Browsable(false)]
        public int Percentage => (int)Math.Floor(
            (_value - _minimum) * 100d / (_maximum - _minimum));

        [Browsable(false)]
        public string DisplayText => _displayState == RecoveryProgressDisplayState.Completed
            ? "Hoàn Thành"
            : $"{Percentage}%";

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);

            if (_displayState == RecoveryProgressDisplayState.Completed)
            {
                DrawCompletedText(e.Graphics);
                return;
            }

            Rectangle bar = Rectangle.Inflate(ClientRectangle, -4, -4);
            if (bar.Width <= 1 || bar.Height <= 1)
            {
                return;
            }

            bool isError = _displayState == RecoveryProgressDisplayState.Error;
            Color fillColor = isError
                ? Color.FromArgb(220, 53, 69)
                : Color.FromArgb(46, 204, 113);
            Color emptyColor = isError
                ? Color.FromArgb(255, 235, 238)
                : Color.FromArgb(245, 247, 249);

            using (Brush emptyBrush = new SolidBrush(emptyColor))
            {
                e.Graphics.FillRectangle(emptyBrush, bar);
            }
            using (Pen borderPen = new Pen(Color.LightGray))
            {
                e.Graphics.DrawRectangle(borderPen, bar);
            }

            int fillWidth = (int)Math.Round((bar.Width - 1) * (Percentage / 100d));
            if (fillWidth > 0)
            {
                using Brush fillBrush = new SolidBrush(fillColor);
                e.Graphics.FillRectangle(fillBrush, bar.X + 1, bar.Y + 1, fillWidth, bar.Height - 1);
            }

            TextRenderer.DrawText(
                e.Graphics,
                DisplayText,
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }

        private void DrawCompletedText(Graphics graphics)
        {
            using Font completeFont = new Font(Font.FontFamily, Font.Size + 1F, FontStyle.Bold);
            TextRenderer.DrawText(
                graphics,
                DisplayText,
                completeFont,
                ClientRectangle,
                Color.FromArgb(25, 135, 84),
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }
    }
}
