using System;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using AgentShared; // Gọi thư viện gói tin chung vào để dùng

namespace AgentControl
{
    public class SQLiteHelper
    {
        // Tên file database SQLite sẽ nằm chung thư mục chạy của app Server
        private static readonly string DbName = "AgentManagement.db";
        private static readonly string ConnectionString = $"Data Source={DbName};Version=3;";

        // Hàm khởi tạo Database (Tự động chạy khi Server mở lên)
        public static async Task InitializeDatabaseAsync()
        {
            // Nếu chưa có file DB thì SQLite tự đẻ ra file mới, ta chỉ cần tạo bảng
            using (var connection = new SQLiteConnection(ConnectionString))
            {
                await connection.OpenAsync();

                // 1. Tạo bảng quản lý thông tin các Agent
                string createAgentsTable = @"
                    CREATE TABLE IF NOT EXISTS Agents (
                        AgentID TEXT PRIMARY KEY,
                        MachineName TEXT,
                        Username TEXT,
                        IPAddress TEXT,
                        OSVersion TEXT,
                        AgentVersion TEXT,
                        FirstConnectTime TEXT,
                        LastSeen TEXT,
                        Status TEXT DEFAULT 'Offline'
                    );";

                // 2. Tạo bảng lưu Log lịch sử sao chép file
                string createLogsTable = @"
                    CREATE TABLE IF NOT EXISTS TransferLogs (
                        LogID INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT,
                        AgentID TEXT,
                        ActionType TEXT,
                        FileName TEXT,
                        Status TEXT,
                        FileSize TEXT,
                        Duration TEXT
                    );";

                using (var command = new SQLiteCommand(createAgentsTable, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }

                using (var command = new SQLiteCommand(createLogsTable, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        // Hàm cập nhật hoặc thêm mới một Agent khi nó kết nối tới (Gói gọn tính năng số 4 & 5)
        public static async Task SaveOrUpdateAgentAsync(string agentID, AgentInfo info, bool isOnline)
        {
            using (var connection = new SQLiteConnection(ConnectionString))
            {
                await connection.OpenAsync();

                string currentTime = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                string statusText = isOnline ? "Online" : "Offline";

                // Sử dụng lệnh INSERT OR IGNORE để lưu "Thời gian kết nối đầu tiên" nếu là máy mới tinh
                string insertIgnoreQuery = @"
                    INSERT OR IGNORE INTO Agents (AgentID, FirstConnectTime) 
                    VALUES (@AgentID, @FirstConnectTime);";

                using (var cmd = new SQLiteCommand(insertIgnoreQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@AgentID", agentID);
                    cmd.Parameters.AddWithValue("@FirstConnectTime", currentTime);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Cập nhật tất cả các thông tin mới nhất và cập nhật Last Seen
                string updateQuery = @"
                    UPDATE Agents 
                    SET MachineName = @MachineName,
                        Username = @Username,
                        IPAddress = @IPAddress,
                        OSVersion = @OSVersion,
                        AgentVersion = @AgentVersion,
                        LastSeen = @LastSeen,
                        Status = @Status
                    WHERE AgentID = @AgentID;";

                using (var cmd = new SQLiteCommand(updateQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@AgentID", agentID);
                    cmd.Parameters.AddWithValue("@MachineName", info.MachineName);
                    cmd.Parameters.AddWithValue("@Username", info.Username);
                    cmd.Parameters.AddWithValue("@IPAddress", info.IPAddress);
                    cmd.Parameters.AddWithValue("@OSVersion", info.OSVersion);
                    cmd.Parameters.AddWithValue("@AgentVersion", info.AgentVersion);
                    cmd.Parameters.AddWithValue("@LastSeen", currentTime);
                    cmd.Parameters.AddWithValue("@Status", statusText);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        // Hàm cập nhật trạng thái Offline của Agent (Phục vụ tính năng Heartbeat quá 90 giây)
        public static async Task SetAgentOfflineAsync(string agentID)
        {
            using (var connection = new SQLiteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                string updateQuery = "UPDATE Agents SET Status = 'Offline' WHERE AgentID = @AgentID;";
                using (var cmd = new SQLiteCommand(updateQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@AgentID", agentID);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        // Hàm lấy toàn bộ danh sách Agent đã từng kết nối trong DB lên để nạp vào giao diện lúc mở app
        public static async Task<List<Dictionary<string, string>>> GetAllAgentsAsync()
        {
            var agentList = new List<Dictionary<string, string>>();

            using (var connection = new SQLiteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                string selectQuery = "SELECT * FROM Agents;";

                using (var cmd = new SQLiteCommand(selectQuery, connection))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, string>
                        {
                            { "AgentID", reader["AgentID"].ToString() },
                            { "MachineName", reader["MachineName"].ToString() },
                            { "Username", reader["Username"].ToString() },
                            { "IPAddress", reader["IPAddress"].ToString() },
                            { "OSVersion", reader["OSVersion"].ToString() },
                            { "AgentVersion", reader["AgentVersion"].ToString() },
                            { "FirstConnectTime", reader["FirstConnectTime"].ToString() },
                            { "LastSeen", reader["LastSeen"].ToString() },
                            { "Status", reader["Status"].ToString() }
                        };
                        agentList.Add(row);
                    }
                }
            }
            return agentList;
        }
    }
}