using System;

namespace AgentShared
{
    public class SocketPacket
    {
        // Loại lệnh/gói tin (Ví dụ: REGISTER, HEARTBEAT, BROWSE_DRIVES, COPY_REQUEST,...)
        public string Type { get; set; } = string.Empty;

        // Định danh duy nhất của mỗi máy con (AgentID) để Server phân biệt
        public string AgentID { get; set; } = string.Empty;

        // Dữ liệu chi tiết của gói tin (Sẽ là chuỗi JSON được mã hóa từ các Class bên dưới)
        public string Data { get; set; } = string.Empty;
    }
}