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

            // 2. Lấy giá trị phần trăm tiến độ (0 - 100)
            int progressVal = 0;
            if (value != null && value is int)
            {
                progressVal = (int)value;
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

                // Tính toán độ dài phần % đã tải để tô màu
                int fillWidth = (int)((width - 1) * (progressVal / 100.0));
                if (fillWidth > 0)
                {
                    // Chọn màu xanh lục tươi tắn cho thanh tiến trình
                    using (Brush brush = new SolidBrush(Color.FromArgb(46, 204, 113)))
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

                using (Brush textBrush = new SolidBrush(cellStyle.ForeColor))
                {
                    graphics.DrawString(percentText, font, textBrush, textX, textY);
                }
            }
        }
    }
}