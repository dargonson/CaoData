public class RemoteDirectoryContent
{
    public string CurrentPath { get; set; } = string.Empty;
    public List<string> SubFolders { get; set; } = new List<string>();
    public List<string> Files { get; set; } = new List<string>();
    public List<RemoteFileSystemEntry> Folders { get; set; } = new List<RemoteFileSystemEntry>();
    public List<RemoteFileSystemEntry> FileEntries { get; set; } = new List<RemoteFileSystemEntry>();
    public string ErrorMessage { get; set; } = string.Empty;
}

public class RemoteFileSystemEntry
{
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public string Extension { get; set; } = string.Empty;
}
