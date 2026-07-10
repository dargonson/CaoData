using System;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using AgentShared; // Gọi thư viện gói tin chung vào để dùng

namespace AgentControl
{
    public class SQLiteHelper
    {
        // Tên file database SQLite sẽ nằm chung thư mục chạy của app Server
        private static readonly string DbName = "AgentManagement.db";
        private static readonly string ConnectionString = $"Data Source={DbName};Version=3;Default Timeout=30;BusyTimeout=5000;";
        private static readonly SemaphoreSlim DbLock = new SemaphoreSlim(1, 1);

        private static async Task<SQLiteConnection> OpenConnectionAsync()
        {
            var connection = new SQLiteConnection(ConnectionString);
            await connection.OpenAsync();

            using (var cmd = new SQLiteCommand("PRAGMA busy_timeout = 5000;", connection))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            return connection;
        }

        private static async Task EnsureColumnExistsAsync(SQLiteConnection connection, string tableName, string columnName, string columnDefinition)
        {
            using (var cmd = new SQLiteCommand($"PRAGMA table_info({tableName});", connection))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            using (var alterCmd = new SQLiteCommand($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};", connection))
            {
                await alterCmd.ExecuteNonQueryAsync();
            }
        }

        // Hàm khởi tạo Database (Tự động chạy khi Server mở lên)
        public static async Task InitializeDatabaseAsync()
        {
            await DbLock.WaitAsync();
            try
            {
                // Nếu chưa có file DB thì SQLite tự đẻ ra file mới, ta chỉ cần tạo bảng
                using (var connection = await OpenConnectionAsync())
                {
                    using (var pragmaCmd = new SQLiteCommand("PRAGMA journal_mode = WAL;", connection))
                    {
                        await pragmaCmd.ExecuteNonQueryAsync();
                    }

                    // 1. Tạo bảng quản lý thông tin các Agent (Giữ nguyên cấu trúc chuẩn của fen)
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

                    // 2. Tạo bảng lưu Log hệ thống (Giữ nguyên tên bảng Logs chuẩn ban đầu của fen)
                    string createLogsTable = @"
            CREATE TABLE IF NOT EXISTS Logs (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                LogTime TEXT,
                LogType TEXT,
                Message TEXT
            );";

                    // 3. THÊM MỚI: Bảng quản lý Hàng đợi Download (Phục vụ Resume & hàng đợi)
                    string createDownloadQueueTable = @"
            CREATE TABLE IF NOT EXISTS DownloadQueue (
                DownloadID TEXT PRIMARY KEY,
                AgentID TEXT,
                RemotePath TEXT,
                LocalPath TEXT,
                TotalBytes INTEGER DEFAULT 0,
                DownloadedBytes INTEGER DEFAULT 0,
                Status TEXT DEFAULT 'Waiting',
                ChecksumAlgorithm TEXT DEFAULT 'None',
                CreatedTime TEXT,
                UpdatedTime TEXT
            );";

                    // Gom hết vào 1 bộ cmd chạy tuần tự là sạch đẹp nhất
                    using (var cmd = new SQLiteCommand(connection))
                    {
                        cmd.CommandText = createAgentsTable;
                        await cmd.ExecuteNonQueryAsync();

                        cmd.CommandText = createLogsTable;
                        await cmd.ExecuteNonQueryAsync();

                        cmd.CommandText = createDownloadQueueTable;
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await EnsureColumnExistsAsync(connection, "DownloadQueue", "ChecksumAlgorithm", "TEXT DEFAULT 'None'");
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        // Hàm cập nhật hoặc thêm mới một Agent khi nó kết nối tới (Gói gọn tính năng số 4 & 5)
        public static async Task SaveOrUpdateAgentAsync(string agentID, AgentInfo info, bool isOnline)
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {

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
            finally
            {
                DbLock.Release();
            }
        }

        // Hàm cập nhật trạng thái Offline của Agent (Phục vụ tính năng Heartbeat quá 90 giây)
        public static async Task SetAgentOfflineAsync(string agentID)
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    string updateQuery = "UPDATE Agents SET Status = 'Offline' WHERE AgentID = @AgentID;";
                    using (var cmd = new SQLiteCommand(updateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@AgentID", agentID);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task SetAllAgentsOfflineAsync()
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    string updateQuery = "UPDATE Agents SET Status = 'Offline';";
                    using (var cmd = new SQLiteCommand(updateQuery, connection))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        // Hàm lấy toàn bộ danh sách Agent đã từng kết nối trong DB lên để nạp vào giao diện lúc mở app
        public static async Task DeleteAgentAsync(string agentID)
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    string deleteQuery = "DELETE FROM Agents WHERE AgentID = @AgentID;";
                    using (var cmd = new SQLiteCommand(deleteQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@AgentID", agentID);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task<List<Dictionary<string, string>>> GetAllAgentsAsync()
        {
            var agentList = new List<Dictionary<string, string>>();

            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
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
            }
            finally
            {
                DbLock.Release();
            }

            return agentList;
        }
        public static async Task AddToDownloadQueueAsync(string downloadId, string agentId, string remotePath, string localPath, long totalBytes, string checksumAlgorithm = "None")
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    string insertQuery = @"
            INSERT INTO DownloadQueue (DownloadID, AgentID, RemotePath, LocalPath, TotalBytes, DownloadedBytes, Status, ChecksumAlgorithm, CreatedTime, UpdatedTime)
            VALUES (@DownloadID, @AgentID, @RemotePath, @LocalPath, @TotalBytes, 0, 'Waiting', @ChecksumAlgorithm, @Time, @Time);";

                    using (var cmd = new SQLiteCommand(insertQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@DownloadID", downloadId);
                        cmd.Parameters.AddWithValue("@AgentID", agentId);
                        cmd.Parameters.AddWithValue("@RemotePath", remotePath);
                        cmd.Parameters.AddWithValue("@LocalPath", localPath);
                        cmd.Parameters.AddWithValue("@TotalBytes", totalBytes);
                        cmd.Parameters.AddWithValue("@ChecksumAlgorithm", string.IsNullOrWhiteSpace(checksumAlgorithm) ? "None" : checksumAlgorithm);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        // Hàm cập nhật tiến độ (số byte đã tải về ổ cứng) theo thời gian thực
        public static async Task UpdateDownloadProgressAsync(string downloadId, long downloadedBytes, string status)
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    string updateQuery = @"
            UPDATE DownloadQueue
            SET DownloadedBytes = CASE WHEN @DownloadedBytes > DownloadedBytes THEN @DownloadedBytes ELSE DownloadedBytes END,
                Status = CASE
                    WHEN Status = 'Completed' OR Status = 'ChecksumMatched' OR Status = 'ChecksumFailed' THEN Status
                    ELSE @Status
                END,
                UpdatedTime = @Time
            WHERE DownloadID = @DownloadID;";

                    using (var cmd = new SQLiteCommand(updateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@DownloadID", downloadId);
                        cmd.Parameters.AddWithValue("@DownloadedBytes", downloadedBytes);
                        cmd.Parameters.AddWithValue("@Status", status);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task UpdateDownloadProgressAsync(string downloadId, long downloadedBytes, long totalBytes, string status)
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    string updateQuery = @"
            UPDATE DownloadQueue
            SET DownloadedBytes = CASE
                    WHEN @Status = 'Completed' AND @TotalBytes > 0 THEN @TotalBytes
                    WHEN @DownloadedBytes > DownloadedBytes THEN @DownloadedBytes
                    ELSE DownloadedBytes
                END,
                TotalBytes = CASE WHEN @TotalBytes > 0 THEN @TotalBytes ELSE TotalBytes END,
                Status = CASE
                    WHEN Status = 'Completed' OR Status = 'ChecksumMatched' OR Status = 'ChecksumFailed' THEN Status
                    WHEN @Status = 'Completed' THEN 'Completed'
                    WHEN @Status = 'Verifying' THEN 'Verifying'
                    WHEN @Status = 'ChecksumMatched' THEN 'ChecksumMatched'
                    WHEN @Status = 'ChecksumFailed' THEN 'ChecksumFailed'
                    WHEN @TotalBytes > 0 AND @DownloadedBytes >= @TotalBytes THEN 'Completed'
                    ELSE @Status
                END,
                UpdatedTime = @Time
            WHERE DownloadID = @DownloadID;";

                    using (var cmd = new SQLiteCommand(updateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@DownloadID", downloadId);
                        cmd.Parameters.AddWithValue("@DownloadedBytes", downloadedBytes);
                        cmd.Parameters.AddWithValue("@TotalBytes", totalBytes);
                        cmd.Parameters.AddWithValue("@Status", status);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        // Hàm lấy thông tin một file để kiểm tra trạng thái Resume (Xem byte đã tải trước đó)
        public static async Task<long> GetDownloadedBytesOffsetAsync(string downloadId)
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    string selectQuery = "SELECT DownloadedBytes FROM DownloadQueue WHERE DownloadID = @DownloadID;";

                    using (var cmd = new SQLiteCommand(selectQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@DownloadID", downloadId);
                        var result = await cmd.ExecuteScalarAsync();
                        return result != null ? Convert.ToInt64(result) : 0;
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }
        public static async Task<string> GetLocalPathByDownloadIdAsync(string downloadId)
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    string selectQuery = "SELECT LocalPath FROM DownloadQueue WHERE DownloadID = @DownloadID;";
                    using (var cmd = new SQLiteCommand(selectQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@DownloadID", downloadId);
                        var result = await cmd.ExecuteScalarAsync();
                        return result != null ? result.ToString() : string.Empty;
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task<string> GetChecksumAlgorithmByDownloadIdAsync(string downloadId)
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    string selectQuery = "SELECT ChecksumAlgorithm FROM DownloadQueue WHERE DownloadID = @DownloadID;";
                    using (var cmd = new SQLiteCommand(selectQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@DownloadID", downloadId);
                        var result = await cmd.ExecuteScalarAsync();
                        return result != null ? result.ToString() ?? "None" : "None";
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task DeleteDownloadsAsync(IEnumerable<string> downloadIds)
        {
            var ids = downloadIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ids.Count == 0)
            {
                return;
            }

            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                using (var transaction = connection.BeginTransaction())
                {
                    using (var cmd = new SQLiteCommand("DELETE FROM DownloadQueue WHERE DownloadID = @DownloadID;", connection, transaction))
                    {
                        var idParameter = cmd.Parameters.Add("@DownloadID", System.Data.DbType.String);
                        foreach (string id in ids)
                        {
                            idParameter.Value = id;
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    transaction.Commit();
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        public static async Task ClearDownloadQueueAsync()
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                using (var cmd = new SQLiteCommand("DELETE FROM DownloadQueue;", connection))
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                DbLock.Release();
            }
        }








        public static async Task SaveLogAsync(string logType, string message)
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    // Ghi chuẩn chỉ vào đúng bảng Logs của fen đang có sẵn
                    string insertLogQuery = @"
            INSERT INTO Logs (LogTime, LogType, Message)
            VALUES (@LogTime, @LogType, @Message);";

                    using (var cmd = new SQLiteCommand(insertLogQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@LogTime", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@LogType", logType);
                        cmd.Parameters.AddWithValue("@Message", message);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }
        // 1. Khi Agent đứt mạch (Disconnect): Tìm các file đang tải (Downloading) hoặc Waiting của Agent đó và chuyển sang 'Waiting Agent'
        public static async Task FailPendingDownloadsByAgentAsync(string agentId)
        {
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {

                    string updateQuery = @"
                    UPDATE DownloadQueue
                    SET Status = 'Waiting Agent',
                        UpdatedTime = @Time
                    WHERE AgentID = @AgentID AND (Status = 'Downloading' OR Status = 'Waiting');";

                    using (var cmd = new SQLiteCommand(updateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@AgentID", agentId);
                        cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }
        }

        // 2. Khi Agent kết nối lại (REGISTER): Quét tìm các Job 'Waiting Agent' của nó để bốc Offset lên Auto-Resume
        public static async Task<List<DownloadJobDto>> GetPendingDownloadsByAgentAsync(string agentId)
        {
            var pendingJobs = new List<DownloadJobDto>();

            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {

                    string selectQuery = @"
            SELECT DownloadID, RemotePath, DownloadedBytes, ChecksumAlgorithm
            FROM DownloadQueue
            WHERE AgentID = @AgentID
              AND Status IN ('Waiting Agent', 'Downloading', 'Waiting', 'Verifying')
              AND DownloadedBytes >= 0;";

                    using (var cmd = new SQLiteCommand(selectQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@AgentID", agentId);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                pendingJobs.Add(new DownloadJobDto
                                {
                                    DownloadID = reader["DownloadID"].ToString(),
                                    RemotePath = reader["RemotePath"].ToString(),

                                    // 🔥 SỬA TẠI ĐÂY: Đổi từ 'Offset' thành 'DownloadedBytes' cho đúng thuộc tính của Class
                                    DownloadedBytes = Convert.ToInt64(reader["DownloadedBytes"]),
                                    ChecksumAlgorithm = reader["ChecksumAlgorithm"]?.ToString() ?? "None"
                                });
                            }
                        }
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }

            return pendingJobs;
        }

        // 3. Hàm bốc toàn bộ dữ liệu Queue nuôi con DataGridView (Cột: ID, File, Tổng size, Đã tải, Trạng thái)
        public static async Task<System.Data.DataTable> GetDownloadQueueTableAsync()
        {
            var table = new System.Data.DataTable();
            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {
                    string selectQuery = "SELECT DownloadID, RemotePath, TotalBytes, DownloadedBytes, Status, ChecksumAlgorithm FROM DownloadQueue ORDER BY UpdatedTime DESC;";
                    using (var cmd = new SQLiteCommand(selectQuery, connection))
                    {
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(table);
                        }
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }

            return table;
        }


        public static async Task<List<DownloadJobDto>> GetAllDownloadsAsync()
        {
            var list = new List<DownloadJobDto>();

            await DbLock.WaitAsync();
            try
            {
                using (var connection = await OpenConnectionAsync())
                {

                    // Lấy tất cả các file trong hàng đợi, sắp xếp theo thời gian hoặc ID
                    string sql = "SELECT DownloadID, RemotePath, LocalPath, TotalBytes, DownloadedBytes, Status, ChecksumAlgorithm FROM DownloadQueue;";

                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                list.Add(new DownloadJobDto
                                {
                                    DownloadID = reader["DownloadID"].ToString(),
                                    RemotePath = reader["RemotePath"].ToString(),
                                    LocalPath = reader["LocalPath"].ToString(),
                                    // Ép kiểu chính xác sang long cho dung lượng và tiến độ
                                    TotalBytes = reader["TotalBytes"] != DBNull.Value ? Convert.ToInt64(reader["TotalBytes"]) : 0,
                                    DownloadedBytes = reader["DownloadedBytes"] != DBNull.Value ? Convert.ToInt64(reader["DownloadedBytes"]) : 0,
                                    Status = reader["Status"].ToString(),
                                    ChecksumAlgorithm = reader["ChecksumAlgorithm"]?.ToString() ?? "None"
                                });
                            }
                        }
                    }
                }
            }
            finally
            {
                DbLock.Release();
            }

            return list;
        }

    }
}
