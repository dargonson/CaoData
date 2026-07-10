public class DownloadRequestModel
{
    public string DownloadID { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
    public long Offset { get; set; }
    public string ChecksumAlgorithm { get; set; } = "None";
}
