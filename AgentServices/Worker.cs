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
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _downloadLock = new SemaphoreSlim(1, 1);

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
            int connectionTimeoutMs = 8000;

            if (_configuration != null)
            {
                lanIP = (_configuration["ConnectionConfig:ServerLAN"] ?? "127.0.0.1").Trim();
                wanIP = (_configuration["ConnectionConfig:ServerWAN"] ?? "127.0.0.1").Trim();
                int.TryParse(_configuration["ConnectionConfig:Port"], out port);
                if (port == 0) port = 9000;
                int.TryParse(_configuration["ConnectionConfig:ConnectionTimeoutMs"], out connectionTimeoutMs);
                if (connectionTimeoutMs < 1000) connectionTimeoutMs = 8000;
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
                        _isConnected = await TryConnectAsync(lanIP, port, connectionTimeoutMs, "LAN", stoppingToken);

                        // 2. Nếu LAN thất bại, tự động chuyển sang WAN
                        if (!_isConnected)
                        {
                            _logger?.LogWarning("Kết nối LAN thất bại. Đang chuyển sang thử WAN...");
                            _isConnected = await TryConnectAsync(wanIP, port, connectionTimeoutMs, "WAN", stoppingToken);
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
        private async Task<bool> TryConnectAsync(string ip, int port, int timeoutMs, string profileName, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                _logger?.LogWarning("Bỏ qua kết nối {Profile}: chưa cấu hình địa chỉ Server.", profileName);
                return false;
            }

            TcpClient? client = null;
            try
            {
                _logger?.LogInformation("Thử kết nối {Profile}: {Ip}:{Port}", profileName, ip, port);
                client = new TcpClient();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(timeoutMs);

                await client.ConnectAsync(ip, port, timeoutCts.Token);
                if (client.Connected)
                {
                    _client?.Close();
                    _client = client;
                    _stream = _client.GetStream();
                    _logger?.LogInformation("Kết nối {Profile} thành công: {Ip}:{Port}", profileName, ip, port);
                    return true;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                _logger?.LogWarning("Kết nối {Profile} timeout sau {TimeoutMs}ms: {Ip}:{Port}", profileName, timeoutMs, ip, port);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Kết nối {Profile} thất bại {Ip}:{Port} - {Message}", profileName, ip, port, ex.Message);
            }
            finally
            {
                if (!ReferenceEquals(client, _client))
                {
                    client?.Close();
                }
            }

            return false;
        }

        // Hàm gửi gói tin chuẩn mã hóa kích thước 4 bytes đầu chống dính gói
        private async Task SendPacketAsync(SocketPacket packet)
        {
            if (!_isConnected || _stream == null) return;

            await _sendLock.WaitAsync();
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
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task SendDownloadErrorAsync(string agentId, string downloadId, string remotePath, string message, long downloadedBytes = 0, long totalBytes = 0)
        {
            var errorPacket = new DownloadErrorPacket
            {
                DownloadID = downloadId,
                RemotePath = remotePath,
                ErrorMessage = message,
                DownloadedBytes = downloadedBytes,
                TotalBytes = totalBytes
            };

            await SendPacketAsync(new SocketPacket
            {
                Type = "DOWNLOAD_ERROR",
                AgentID = agentId,
                Data = JsonSerializer.Serialize(errorPacket)
            });
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

                await SendPacketAsync(registerPacket);
                _logger?.LogInformation("Đã gửi gói REGISTER đăng ký thông tin máy lên Server thành công.");
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
        /*private async Task ListenToServerAsync(CancellationToken token)
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
                        if (packet.Type == "BROWSE_DRIVES")
                        {
                            _logger?.LogInformation("Server yêu cầu lấy danh sách ổ đĩa.");

                            // 1. Cào danh sách các ổ đĩa thực tế đang sẵn sàng trên hệ điều hành
                            var driveList = new System.Collections.Generic.List<string>();
                            foreach (var drive in System.IO.DriveInfo.GetDrives())
                            {
                                if (drive.IsReady)
                                {
                                    driveList.Add(drive.Name); // Kết quả trả về dạng: "C:\", "D:\"
                                }
                            }

                            // 2. Chuyển danh sách thành chuỗi JSON gán vào thuộc tính Data
                            SocketPacket responsePacket = new SocketPacket
                            {
                                Type = "BROWSE_DRIVES_RESPONSE",
                                AgentID = packet.AgentID,
                                Data = System.Text.Json.JsonSerializer.Serialize(driveList)
                            };

                            // 3. Đóng gói chuỗi hóa toàn bộ gói tin để bắn ngược về Server
                            string jsonString = System.Text.Json.JsonSerializer.Serialize(responsePacket);
                            byte[] driveDataBuffer = System.Text.Encoding.UTF8.GetBytes(jsonString); // Đổi tên ở đây
                            byte[] driveLengthPrefix = BitConverter.GetBytes(driveDataBuffer.Length); // Đổi tên ở đây

                            if (_stream != null && _stream.CanWrite)
                            {
                                await _stream.WriteAsync(driveLengthPrefix, 0, driveLengthPrefix.Length);
                                await _stream.WriteAsync(driveDataBuffer, 0, driveDataBuffer.Length);
                                await _stream.FlushAsync();
                            }
                        }

                        if (packet.Type == "GET_DRIVES")
                        {
                            var drives = new System.Collections.Generic.List<string>();
                            foreach (var drive in System.IO.DriveInfo.GetDrives())
                            {
                                if (drive.IsReady) drives.Add(drive.Name); // Trả về dạng "C:\", "D:\"
                            }

                            SocketPacket response = new SocketPacket
                            {
                                Type = "GET_DRIVES_RESPONSE",
                                AgentID = packet.AgentID,
                                Data = System.Text.Json.JsonSerializer.Serialize(drives)
                            };
                            await SendPacketAsync(response); // Gọi hàm gửi mảng byte có lengthPrefix của fen
                        }

                        // 2. NHÁNH XỬ LÝ LAZY LOADING CÀO THƯ MỤC CON THEO ĐƯỜNG DẪN YÊU CẦU
                        else if (packet.Type == "GET_DIRECTORY")
                        {
                            string targetPath = packet.Data; // Đường dẫn Server muốn cào (Ví dụ: C:\Users)
                            var dirContent = new RemoteDirectoryContent();

                            try
                            {
                                if (System.IO.Directory.Exists(targetPath))
                                {
                                    // Lấy danh sách thư mục con
                                    foreach (var dir in System.IO.Directory.GetDirectories(targetPath))
                                    {
                                        var info = new System.IO.DirectoryInfo(dir);
                                        if ((info.Attributes & System.IO.FileAttributes.Hidden) == 0) // Bỏ qua file ẩn
                                        {
                                            dirContent.SubFolders.Add(info.FullName);
                                        }
                                    }
                                    // Lấy danh sách tập tin con
                                    foreach (var file in System.IO.Directory.GetFiles(targetPath))
                                    {
                                        var info = new System.IO.FileInfo(file);
                                        if ((info.Attributes & System.IO.FileAttributes.Hidden) == 0)
                                        {
                                            dirContent.Files.Add(info.FullName);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                dirContent.ErrorMessage = ex.Message; // Trả về lỗi nếu thư mục bị chặn quyền (Access Denied)
                            }

                            SocketPacket response = new SocketPacket
                            {
                                Type = "GET_DIRECTORY_RESPONSE",
                                AgentID = packet.AgentID,
                                Data = System.Text.Json.JsonSerializer.Serialize(dirContent)
                            };
                            await SendPacketAsync(response);
                        }


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
        }*/

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

                        // ====================================================================
                        // 1. LUỒNG CÀO Ổ ĐĨA: GỘP CHUNG VỀ CHUẨN "BROWSE_DRIVES" CHO ĐỒNG BỘ UI
                        // ====================================================================


                        // --- TÍCH HỢP VÀO VÒNG LẶP ĐỌC SOCKET CỦA AGENT ---

                        if (packet.Type == "REQUEST_DOWNLOAD")
                        {
                            if (!string.IsNullOrEmpty(packet.Data))
                            {
                                // Khởi chạy một luồng Task độc lập để băm file, tránh làm nghẽn mạch Socket chính của Agent
                                _ = Task.Run(async () =>
                                {
                                    await _downloadLock.WaitAsync(token);
                                    DownloadRequestModel? request = null;
                                    long currentOffset = 0;
                                    long totalBytes = 0;
                                    try
                                    {
                                        // 1. Giải mã gói yêu cầu từ Server
                                        request = JsonSerializer.Deserialize<DownloadRequestModel>(packet.Data);
                                        if (request == null) return;

                                        string filePath = request.RemotePath;
                                        currentOffset = request.Offset;

                                        // 2. Kiểm tra xem file ngoài đời thực có tồn tại không [cite: 12]
                                        if (!File.Exists(filePath))
                                        {
                                            await SendDownloadErrorAsync(packet.AgentID, request.DownloadID, filePath, "File không còn tồn tại hoặc đã bị antivirus xóa.", currentOffset, totalBytes);
                                            return;
                                        }

                                        FileInfo fileInfo = new FileInfo(filePath);
                                        totalBytes = fileInfo.Length;

                                        if (totalBytes == 0)
                                        {
                                            var emptyChunk = new FileChunkPacket
                                            {
                                                DownloadID = request.DownloadID,
                                                RemotePath = filePath,
                                                TotalBytes = 0,
                                                Offset = 0,
                                                ChunkSize = 0,
                                                IsLastChunk = true,
                                                Base64Data = string.Empty
                                            };

                                            await SendPacketAsync(new SocketPacket
                                            {
                                                Type = "DOWNLOAD_CHUNK",
                                                AgentID = packet.AgentID,
                                                Data = JsonSerializer.Serialize(emptyChunk)
                                            });
                                            return;
                                        }

                                        // Định nghĩa kích thước mỗi khối băm nhỏ (Buffer Size = 256KB tăng tốc độ truyền tải)
                                        int bufferSize = 256 * 1024;
                                        byte[] buffer = new byte[bufferSize];

                                        // 3. Mở FileStream chế độ Read và Share để đọc cuốn chiếu, không khóa file hệ thống
                                        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                        {
                                            // Nhảy cóc đến vị trí Offset được yêu cầu
                                            fs.Seek(currentOffset, SeekOrigin.Begin);

                                            int bytesRead;
                                            // Vòng lặp đọc file cuốn chiếu cho đến hết
                                            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                            {
                                                // Trích xuất mảng byte thực tế đọc được từ file
                                                byte[] actualBytes = new byte[bytesRead];
                                                Buffer.BlockCopy(buffer, 0, actualBytes, 0, bytesRead);

                                                // Đóng gói dữ liệu sang mô hình FileChunkPacket
                                                var chunk = new FileChunkPacket
                                                {
                                                    DownloadID = request.DownloadID,
                                                    RemotePath = filePath,
                                                    TotalBytes = totalBytes,
                                                    Offset = currentOffset,
                                                    ChunkSize = bytesRead,
                                                    IsLastChunk = (currentOffset + bytesRead >= totalBytes), // Đánh dấu nếu là mảnh cuối [cite: 15]
                                                    Base64Data = Convert.ToBase64String(actualBytes) // Chuyển sang Base64 để nhét vào chuỗi JSON [cite: 17]
                                                };

                                                // Gói vào SocketPacket chung để bắn lên mạng
                                                SocketPacket chunkPacket = new SocketPacket
                                                {
                                                    Type = "DOWNLOAD_CHUNK",
                                                    AgentID = packet.AgentID, // Trả ngược lại đúng ID của Agent này
                                                    Data = JsonSerializer.Serialize(chunk)
                                                };

                                                // Chuỗi hóa gói tin và bọc 4 bytes độ dài đầu gói chống dính tin
                                                string jsonString = JsonSerializer.Serialize(chunkPacket);
                                                byte[] dataBuffer = Encoding.UTF8.GetBytes(jsonString);
                                                byte[] lengthPrefix = BitConverter.GetBytes(dataBuffer.Length);

                                                // Bắn lên Server qua luồng NetworkStream ngầm của Agent
                                                // Lưu ý: Biến 'stream' ở đây là NetworkStream kết nối của Agent tới Server nhé fen
                                                await SendPacketAsync(chunkPacket);

                                                // Cập nhật lại vị trí dịch chuyển tiếp theo [cite: 6]
                                                currentOffset += bytesRead;

                                                // Mẹo nhỏ: Thêm một khoảng Delay cực ngắn (1-2ms) nếu muốn giảm tải CPU cho máy Agent khi tải file quá lớn
                                                // await Task.Delay(1);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Lỗi băm file phía Agent: {ex.Message}");
                                        if (request != null)
                                        {
                                            await SendDownloadErrorAsync(packet.AgentID, request.DownloadID, request.RemotePath, ex.Message, currentOffset, totalBytes);
                                        }
                                    }
                                    finally
                                    {
                                        _downloadLock.Release();
                                    }
                                });
                            }
                        }

                        if (packet.Type == "BROWSE_DRIVES" || packet.Type == "GET_DRIVES")
                        {
                            _logger?.LogInformation("Thực hiện cào danh sách ổ đĩa hệ thống...");

                            var driveList = new System.Collections.Generic.List<string>();
                            foreach (var drive in System.IO.DriveInfo.GetDrives())
                            {
                                if (drive.IsReady)
                                {
                                    driveList.Add(drive.Name); // Trả về dạng "C:\", "D:\"
                                }
                            }

                            SocketPacket response = new SocketPacket
                            {
                                Type = "BROWSE_DRIVES_RESPONSE", // Thống nhất dùng chung một loại Response này
                                AgentID = packet.AgentID,
                                Data = System.Text.Json.JsonSerializer.Serialize(driveList)
                            };

                            await SendPacketAsync(response); // Dùng luôn hàm SendPacketAsync cho sạch code
                        }

                        // ====================================================================
                        // 2. LUỒNG CÀO THƯ MỤC LAZY LOADING: TỐI ƯU TỐC ĐỘ, ĐỒNG BỘ KIỂU LỆNH
                        // ====================================================================
                        else if (packet.Type == "GET_DIRECTORY" || packet.Type == "BROWSE_FOLDER")
                        {
                            string targetPath = packet.Data; // Đường dẫn cần cào (Ví dụ: C:\ hoặc C:\Users)
                            _logger?.LogInformation("Tiến hành cào nhanh thư mục: {Path}", targetPath);

                            var dirContent = new RemoteDirectoryContent();

                            try
                            {
                                if (System.IO.Directory.Exists(targetPath))
                                {
                                    // TỐI ƯU HIỆU SUẤT X5: Dùng Enumerate Thay vì Get để lấy đến đâu trả đến đó, không ngâm RAM

                                    // Cào thư mục con nhanh
                                    var dirs = System.IO.Directory.EnumerateDirectories(targetPath);
                                    foreach (var dir in dirs)
                                    {
                                        try
                                        {
                                            var info = new System.IO.DirectoryInfo(dir);
                                            // Bỏ qua thư mục ẩn/hệ thống để tránh bị từ chối quyền (Access Denied) làm chậm luồng
                                            if ((info.Attributes & System.IO.FileAttributes.Hidden) == 0 &&
                                                (info.Attributes & System.IO.FileAttributes.System) == 0)
                                            {
                                                dirContent.SubFolders.Add(info.FullName);
                                            }
                                        }
                                        catch { /* Bỏ qua các folder lỗi quyền riêng lẻ để tiếp tục vòng lặp */ }
                                    }

                                    // Cào tập tin con nhanh
                                    var files = System.IO.Directory.EnumerateFiles(targetPath);
                                    foreach (var file in files)
                                    {
                                        try
                                        {
                                            var info = new System.IO.FileInfo(file);
                                            if ((info.Attributes & System.IO.FileAttributes.Hidden) == 0)
                                            {
                                                dirContent.Files.Add(info.FullName);
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                dirContent.ErrorMessage = ex.Message; // Trả lỗi phân quyền tổng quát nếu có
                            }

                            SocketPacket response = new SocketPacket
                            {
                                Type = "GET_DIRECTORY_RESPONSE",
                                AgentID = packet.AgentID,
                                Data = System.Text.Json.JsonSerializer.Serialize(dirContent)
                            };

                            await SendPacketAsync(response);
                            _logger?.LogInformation("Đã phản hồi dữ liệu cấu trúc thư mục về Server.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("Lỗi trong luồng lắng nghe lệnh: {Message}", ex.Message);
            }
            finally
            {
                _isConnected = false;
                _logger?.LogWarning("Mất kết nối tới Server. Đã kích hoạt trạng thái chờ Reconnect...");
            }
        }
    }
}
