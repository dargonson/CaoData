using System;
using System.Drawing;
using System.Windows.Forms;

namespace AgentControl
{
    public class DataGridViewProgressBarColumn : DataGridViewImageColumn
    {
        public DataGridViewProgressBarColumn()
        {
            CellTemplate = new DataGridViewProgressBarCell();
        }
    }

    public class DataGridViewProgressBarCell : DataGridViewImageCell
    {
        public DataGridViewProgressBarCell()
        {
            ValueType = typeof(int); // Giá trị nhận vào từ 0 đến 100
        }

        protected override object GetFormattedValue(object value, int rowIndex, ref DataGridViewCellStyle cellStyle, System.ComponentModel.TypeConverter valueTypeConverter, System.ComponentModel.TypeConverter formattedValueTypeConverter, DataGridViewDataErrorContexts context)
        {
            return new Bitmap(1, 1); // Trả về ảnh rỗng mặc định để tránh lỗi WinForms
        }

        // 🔥 ĐÃ SỬA CHUẨN THAM SỐ TẠI ĐÂY: advancedBorderStyle và các kiểu dữ liệu chuẩn của WinForms
        protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
        {
            // 1. Vẽ nền ô mặc định
            base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue, errorText, cellStyle, advancedBorderStyle, paintParts & ~DataGridViewPaintParts.ContentForeground);

            string status = string.Empty;
            if (DataGridView != null && rowIndex >= 0 && rowIndex < DataGridView.Rows.Count && DataGridView.Columns.Contains("Status"))
            {
                status = DataGridView.Rows[rowIndex].Cells["Status"].Value?.ToString() ?? string.Empty;
            }

            // 2. Lấy giá trị phần trăm tiến độ (0 - 100)
            int progressVal = 0;
            if (value != null && value is int)
            {
                progressVal = (int)value;
            }

            bool isError = status.Contains("Error", StringComparison.OrdinalIgnoreCase);
            bool isCompleted = status.Contains("Complete", StringComparison.OrdinalIgnoreCase);
            if (isError)
            {
                progressVal = 0;
            }
            else if (isCompleted)
            {
                progressVal = 100;
            }

            // 3. Tính toán kích thước thanh Progress lọt lòng ô
            int posX = cellBounds.X + 4;
            int posY = cellBounds.Y + 4;
            int width = cellBounds.Width - 9;
            int height = cellBounds.Height - 9;

            if (width > 0 && height > 0)
            {
                // Vẽ khung viền thanh Progress (Màu xám nhẹ)
                using (Pen pen = new Pen(Color.LightGray, 1))
                {
                    graphics.DrawRectangle(pen, posX, posY, width, height);
                }

                Color fillColor = Color.FromArgb(46, 204, 113);
                if (isError)
                {
                    fillColor = Color.FromArgb(220, 53, 69);
                    using (Brush errorBackBrush = new SolidBrush(Color.FromArgb(255, 235, 238)))
                    {
                        graphics.FillRectangle(errorBackBrush, posX + 1, posY + 1, width - 1, height - 1);
                    }
                }
                else if (isCompleted)
                {
                    fillColor = Color.FromArgb(25, 135, 84);
                }

                // Tính toán độ dài phần % đã tải để tô màu
                int fillWidth = isError ? width - 1 : (int)((width - 1) * (progressVal / 100.0));
                if (fillWidth > 0)
                {
                    using (Brush brush = new SolidBrush(fillColor))
                    {
                        graphics.FillRectangle(brush, posX + 1, posY + 1, fillWidth, height - 1);
                    }
                }

                // 4. Vẽ text số phần trăm (%) đè lên giữa thanh cho trực quan
                string percentText = progressVal + "%";
                Font font = cellStyle.Font ?? SystemFonts.DefaultFont;
                Size textSize = TextRenderer.MeasureText(percentText, font);
                int textX = cellBounds.X + (cellBounds.Width - textSize.Width) / 2;
                int textY = cellBounds.Y + (cellBounds.Height - textSize.Height) / 2;
                Color textColor = isError || isCompleted ? Color.White : cellStyle.ForeColor;

                using (Brush textBrush = new SolidBrush(textColor))
                {
                    graphics.DrawString(percentText, font, textBrush, textX, textY);
                }
            }
        }
    }
}
