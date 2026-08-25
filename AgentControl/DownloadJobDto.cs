public class DownloadJobDto
{
    public string DownloadID { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public long TotalBytes { get; set; }        // 🌟 Đảm bảo có trường này
    public long DownloadedBytes { get; set; }   // 🌟 Đảm bảo có trường này
    public string Status { get; set; } = string.Empty;
    public string ChecksumAlgorithm { get; set; } = "None";
}
