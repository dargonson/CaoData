public class DownloadJobDto
{
    public string DownloadID { get; set; }
    public string RemotePath { get; set; }
    public string LocalPath { get; set; }
    public long TotalBytes { get; set; }        // 🌟 Đảm bảo có trường này
    public long DownloadedBytes { get; set; }   // 🌟 Đảm bảo có trường này
    public string Status { get; set; }
    public string ChecksumAlgorithm { get; set; } = "None";
}
