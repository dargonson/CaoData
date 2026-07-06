using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AgentShared;
using System.Management;
using System.Windows.Forms;
// Dùng chung định dạng gói tin với Server

namespace AgentService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;

        private TcpClient? _client;
        private NetworkStream? _stream;
        private bool _isConnected = false;

        // Chu kỳ Reconnect lũy tiến theo yêu cầu: 5s, 10s, 20s, 30s
        private readonly int[] _reconnectDelays = { 5, 10, 20, 30 };
        private int _reconnectIndex = 0;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }




        // Hãy dán hàm phụ trợ này vào bất kỳ vị trí trống nào trong lòng class Worker : BackgroundService
        private AgentInfo CollectSystemInfo()
        {
            string machineName = Environment.MachineName;
            string username = Environment.UserName;
            string osVersion = "Windows Unknown";
            var os = Environment.OSVersion;

            if (os.Platform == PlatformID.Win32NT)
            {
                // Dựa vào cơ chế quản lý phiên bản của Microsoft để lọc tên thương mại
                switch (os.Version.Major)
                {
                    case 10:
                        // Từ Build 22000 trở lên Microsoft chính thức gọi là Windows 11
                        if (os.Version.Build >= 22000)
                            osVersion = "Windows 11";
                        else
                            osVersion = "Windows 10";
                        break;
                    case 6:
                        switch (os.Version.Minor)
                        {
                            case 3: osVersion = "Windows 8.1"; break;
                            case 2: osVersion = "Windows 8"; break;
                            case 1: osVersion = "Windows 7"; break;
                            case 0: osVersion = "Windows Vista"; break;
                        }
                        break;
                    case 5:
                        osVersion = "Windows XP";
                        break;
                }
            }

            // 1. Cào thông tin phần cứng vật lý cứng (Mainboard & CPU) để tạo ID bất tử
            string hardwareRawData = "";
            try
            {
                // Lấy mã Serial của Bo mạch chủ (Mainboard)
                using (ManagementObjectSearcher mos = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject mo in mos.Get())
                    {
                        hardwareRawData += mo["SerialNumber"]?.ToString() ?? "";
                    }
                }

                // Lấy mã ID của Chip xử lý (CPU ID)
                using (ManagementObjectSearcher mos = new ManagementObjectSearcher("SELECT ProcessorId FROM Win3er_Processor"))
                {
                    foreach (ManagementObject mo in mos.Get())
                    {
                        hardwareRawData += mo["ProcessorId"]?.ToString() ?? "";
                    }
                }
            }
            catch
            {
                // Phòng hờ nếu phân quyền máy lỗi không đọc được WMI, lấy tạm thông tin MacAddress làm phương án dự phòng
                hardwareRawData = machineName;
            }

            // Nếu chuỗi cào được quá ngắn hoặc trống, đắp thêm chuỗi dự phòng chống lỗi
            if (string.IsNullOrWhiteSpace(hardwareRawData) || hardwareRawData.Length < 5)
            {
                hardwareRawData += "NHF-AGENT-DEFAULT-HARDWARE-STRING";
            }

            // 2. Mã hóa MD5 chuỗi phần cứng để tạo ra một chuỗi Hex cố định, duy nhất
            string agentId = "";
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hardwareRawData));
                string hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToUpper(); // Chuỗi dạng HEX viết hoa

                // 3. Định dạng chuỗi theo đúng chuẩn xxxxx-xxxxx (10 ký tự, phân tách ở giữa)
                string part1 = hashString.Substring(0, 5);
                string part2 = hashString.Substring(5, 5);
                agentId = $"{part1}-{part2}"; // Kết quả: XXXXX-XXXXX
            }

            return new AgentInfo
            {
                AgentID = agentId, // Mã 10 ký tự bất tử đã được gán vào đây
                MachineName = machineName,
                Username = username,
                IPAddress = GetLocalIPAddress(), // Gọi hàm lấy IP LAN
                OSVersion = osVersion,
                AgentVersion = "1.0.0"
            };
            MessageBox.Show(osVersion);
        }

        // Hàm phụ trợ lấy IP LAN gọn gàng cho fen
        private string GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Bọc lót chống Null tuyệt đối: Nếu không tìm thấy thẻ trong JSON thì tự lấy mặc định
            string lanIP = "127.0.0.1";
            string wanIP = "127.0.0.1";
            int port = 9000;

            if (_configuration != null)
            {
                lanIP = _configuration["ConnectionConfig:ServerLAN"] ?? "127.0.0.1";
                wanIP = _configuration["ConnectionConfig:ServerWAN"] ?? "127.0.0.1";
                int.TryParse(_configuration["ConnectionConfig:Port"], out port);
                if (port == 0) port = 9000;
            }

            string agentID = HardwareInfo.GetUniqueAgentID();

            // Kiểm tra logger trước khi ghi để tránh NullReferenceException
            _logger?.LogInformation("Agent Service bắt đầu khởi chạy với ID: {AgentID}", agentID);

            // Vòng lặp duy trì kết nối vĩnh viễn...
            while (!stoppingToken.IsCancellationRequested)
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!_isConnected)
                    {
                        _logger?.LogInformation("Đang thử kết nối tới Server...");

                        // 1. Ưu tiên kết nối mạng LAN trước
                        _isConnected = await TryConnectAsync(lanIP, port, stoppingToken);

                        // 2. Nếu LAN thất bại, tự động chuyển sang WAN
                        if (!_isConnected)
                        {
                            _logger?.LogWarning("Kết nối LAN thất bại. Đang chuyển sang thử WAN...");
                            _isConnected = await TryConnectAsync(wanIP, port, stoppingToken);
                        }

                        if (_isConnected)
                        {
                            _logger?.LogInformation("KẾT NỐI SERVER THÀNH CÔNG!");
                            _reconnectIndex = 0; // Reset lại chu kỳ chờ reconnect về mức 5s

                            // Đăng ký thông tin máy với Server ngay khi vừa kết nối
                            await SendRegisterInfoAsync();

                            // Kích hoạt luồng gửi Tim Mạch (Heartbeat) song song định kỳ 30 giây
                            _ = StartHeartbeatLoopAsync(agentID, stoppingToken);

                            // Kích hoạt luồng đứng lắng nghe lệnh từ Server đổ về (Sẽ dùng cho Copy/Duyệt file sau)
                            _ = ListenToServerAsync(stoppingToken);
                        }
                        else
                        {
                            // Xử lý Reconnect lũy tiến nếu cả LAN và WAN đều sập
                            int delaySeconds = _reconnectDelays[_reconnectIndex];
                            _logger?.LogError("Không thể kết nối tới Server. Thử lại sau {delay} giây...", delaySeconds);

                            if (_reconnectIndex < _reconnectDelays.Length - 1)
                                _reconnectIndex++; // Tăng tiến dần từ 5s -> 10s -> 20s -> 30s

                            await Task.Delay(delaySeconds * 1000, stoppingToken);
                        }
                    }
                    else
                    {
                        // Nếu vẫn đang online ổn định thì nghỉ ngơi 1 giây rồi check tiếp
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
        }

        // Hàm thử kết nối Socket
        private async Task<bool> TryConnectAsync(string ip, int port, CancellationToken token)
        {
            try
            {
                _client = new TcpClient();
                // Sử dụng Timeout ngắn để tránh việc đợi mạng WAN quá lâu gây nghẽn luồng
                var connectTask = _client.ConnectAsync(ip, port, token).AsTask();
                if (await Task.WhenAny(connectTask, Task.Delay(4000, token)) == connectTask)
                {
                    await connectTask; // Hoàn thành kết nối thành công
                    _stream = _client.GetStream();
                    return true;
                }
                return false; // Quá 4 giây không kết nối được xem như tạch
            }
            catch
            {
                return false;
            }
        }

        // Hàm gửi gói tin chuẩn mã hóa kích thước 4 bytes đầu chống dính gói
        private async Task SendPacketAsync(SocketPacket packet)
        {
            try
            {
                if (!_isConnected || _stream == null) return;

                string json = JsonSerializer.Serialize(packet);
                byte[] dataBytes = Encoding.UTF8.GetBytes(json);
                byte[] sizeBytes = BitConverter.GetBytes(dataBytes.Length);

                // Gửi 4 bytes chiều dài trước, gửi dữ liệu chuỗi JSON theo sau
                await _stream.WriteAsync(sizeBytes, 0, 4);
                await _stream.WriteAsync(dataBytes, 0, dataBytes.Length);
                await _stream.FlushAsync();
            }
            catch
            {
                _isConnected = false; // Gửi lỗi lập tức coi như mất mạng để kích hoạt luồng Reconnect
            }
        }

        // Tính năng số 1: Gửi thông tin máy cho Server
        private async Task SendRegisterInfoAsync()
        {
            try
            {
                // 1. Tự lấy thông tin máy
                AgentInfo myInfo = CollectSystemInfo();

                // 2. Đóng gói payload chi tiết của Agent thành chuỗi JSON
                string jsonPayload = System.Text.Json.JsonSerializer.Serialize(myInfo);

                // 3. Khởi tạo gói tin KHỚP 100% cấu trúc class SocketPacket của fen
                SocketPacket registerPacket = new SocketPacket
                {
                    Type = "REGISTER",
                    AgentID = myInfo.AgentID, // Điền luôn mã định danh máy con vào vỏ gói tin
                    Data = jsonPayload        // Điền chuỗi JSON thông tin chi tiết vào thuộc tính Data kiểu string
                };

                // 4. Chuỗi hóa toàn bộ gói tin để gửi qua Socket
                string jsonString = System.Text.Json.JsonSerializer.Serialize(registerPacket);
                byte[] dataBuffer = System.Text.Encoding.UTF8.GetBytes(jsonString);
                byte[] lengthPrefix = BitConverter.GetBytes(dataBuffer.Length);

                // 5. Bắn qua luồng mạng Socket
                if (_stream != null && _stream.CanWrite)
                {
                    await _stream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
                    await _stream.WriteAsync(dataBuffer, 0, dataBuffer.Length);
                    await _stream.FlushAsync();

                    _logger?.LogInformation("Đã gửi gói REGISTER đăng ký thông tin máy lên Server thành công.");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("Lỗi khi gửi gói REGISTER: {Message}", ex.Message);
            }
        }

        // Tính năng số 7: Vòng lặp bắn Tim Mạch (Heartbeat) 30 giây một lần
        private async Task StartHeartbeatLoopAsync(string agentID, CancellationToken token)
        {
            while (_isConnected && !token.IsCancellationRequested)
            {
                await Task.Delay(30000, token); // Đợi đúng 30 giây
                if (!_isConnected) break;

                var heartbeatPacket = new SocketPacket
                {
                    Type = "HEARTBEAT",
                    AgentID = agentID,
                    Data = ""
                };

                await SendPacketAsync(heartbeatPacket);
                _logger?.LogInformation("-> Đã gửi Heartbeat (30s/lần)");
            }
        }

        // Luồng lắng nghe lệnh tối ưu, sẵn sàng phục vụ các tính năng duyệt file/copy ở lượt chat tới
        private async Task ListenToServerAsync(CancellationToken token)
        {
            byte[] sizeBuffer = new byte[4];
            try
            {
                while (_isConnected && !token.IsCancellationRequested && _stream != null)
                {
                    int bytesRead = await _stream.ReadAsync(sizeBuffer, 0, 4, token);
                    if (bytesRead == 0) break; // Server chủ động ngắt kết nối

                    int packetSize = BitConverter.ToInt32(sizeBuffer, 0);
                    byte[] dataBuffer = new byte[packetSize];

                    int totalBytesReceived = 0;
                    while (totalBytesReceived < packetSize)
                    {
                        int read = await _stream.ReadAsync(dataBuffer, totalBytesReceived, packetSize - totalBytesReceived, token);
                        if (read == 0) break;
                        totalBytesReceived += read;
                    }

                    string jsonStr = Encoding.UTF8.GetString(dataBuffer);
                    var packet = JsonSerializer.Deserialize<SocketPacket>(jsonStr);

                    if (packet != null)
                    {
                        _logger?.LogInformation("Nhận lệnh từ Server: {Type}", packet.Type);
                        // Kế hoạch xử lý các lệnh: BROWSE_DRIVES, COPY_FILE... sẽ nằm ở đây
                    }
                }
            }
            catch
            {
                // Gặp lỗi ngắt kết nối
            }
            finally
            {
                _isConnected = false;
                _logger?.LogWarning("Mất kết nối tới Server. Đã kích hoạt trạng thái chờ Reconnect...");
            }
        }
    }
}