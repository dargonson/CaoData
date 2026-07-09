using System;
using System.Collections.Generic;

namespace AgentShared
{
    // 1. Gói tin yêu cầu danh sách ổ đĩa / thư mục (Server -> Agent)
    public class BrowseRequest
    {
        // Đường dẫn cần quét. Nếu trống hoặc là "ROOT" thì hiểu là xin danh sách ổ đĩa (C:, D:, E:)
        public string Path { get; set; } = string.Empty;
    }

    // 2. Gói tin trả về danh sách thư mục và file từ xa (Agent -> Server)
    public class BrowseResponse
    {
        public string CurrentPath { get; set; } = string.Empty;
        public List<FolderItemDto> SubFolders { get; set; } = new List<FolderItemDto>();
        public List<FileItemDto> Files { get; set; } = new List<FileItemDto>();
    }

    public class FolderItemDto
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool HasSubDirs { get; set; } // Để Server biết đường mồi hoặc ẩn dấu + ở Vùng 2
        public DateTime LastWriteTime { get; set; }
    }

    public class FileItemDto
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Extension { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
    }

    // 3. Gói tin gửi lệnh Copy dữ liệu (Server <-> Agent)
    public class CopyTaskRequest
    {
        public string TaskID { get; set; } = string.Empty;   // Mã phiên truyền tải (để hủy hoặc resume)
        public string RemoteFilePath { get; set; } = string.Empty; // Đường dẫn file trên máy Agent
        public long StartPosition { get; set; } = 0;          // Vị trí byte bắt đầu đọc (Phục vụ Resume)
    }

    // 4. Gói tin chứa mảnh dữ liệu cắt nhỏ (Agent -> Server)
    public class FileChunkPacket
    {
        public string DownloadID { get; set; } = string.Empty;   // Mã GUID định danh luồng tải này
        public string RemotePath { get; set; } = string.Empty;   // Đường dẫn file trên Agent
        public long TotalBytes { get; set; }                    // Tổng kích thước file
        public long Offset { get; set; }                        // Vị trí bắt đầu đọc file (Dùng cho cả Resume)
        public int ChunkSize { get; set; }                      // Độ dài thực của mảng byte đợt này
        public bool IsLastChunk { get; set; }                   // Đánh dấu đây có phải khúc cuối cùng chưa
        public string Base64Data { get; set; } = string.Empty;  // Dữ liệu nhị phân băm nhỏ đã chuyển sang chuỗi Base64
    }

    public class DownloadErrorPacket
    {
        public string DownloadID { get; set; } = string.Empty;
        public string RemotePath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
    }
}
