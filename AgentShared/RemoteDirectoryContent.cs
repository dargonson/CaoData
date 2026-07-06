public class RemoteDirectoryContent
{
    public List<string> SubFolders { get; set; } = new List<string>();
    public List<string> Files { get; set; } = new List<string>();
    public string ErrorMessage { get; set; } = string.Empty;
}