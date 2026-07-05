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
        public string TaskID { get; set; } = string.Empty;
        public long ChunkIndex { get; set; }       // Vị trí thứ tự của mảnh dữ liệu
        public int ChunkSize { get; set; }         // Kích thước của mảnh này (Bytes)
        public string Base64Data { get; set; } = string.Empty; // Dữ liệu nhị phân băm ra chuỗi an toàn
        public bool IsLastChunk { get; set; }      // Đã tới mảnh cuối cùng chưa?
        public string FileHash { get; set; } = string.Empty;   // Mã SHA256/CRC32 gửi ở mảnh cuối để check lỗi
    }
}