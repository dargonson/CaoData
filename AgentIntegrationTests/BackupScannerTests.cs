using AgentService;
using AgentShared;

namespace AgentIntegrationTests;

public sealed class BackupScannerTests
{
    [Fact]
    public void Scanner_AppliesGlobalFolderAndPatternExclusions()
    {
        string root = TestEnvironment.CreateDirectory("scanner");
        Directory.CreateDirectory(Path.Combine(root, "Temp"));
        Directory.CreateDirectory(Path.Combine(root, "Nested"));
        File.WriteAllText(Path.Combine(root, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(root, "ignored.tmp"), "tmp");
        File.WriteAllText(Path.Combine(root, "Temp", "inside.bin"), "excluded folder");
        File.WriteAllText(Path.Combine(root, "Nested", "~scratch.dat"), "excluded pattern");
        File.WriteAllText(Path.Combine(root, "Nested", "data.bin"), "keep nested");

        var config = new BackupConfiguration
        {
            SourcePaths = new List<string> { root },
            ExcludedFolders = new List<string> { "Temp" },
            ExcludedPatterns = new List<string> { ".tmp", "~*" }
        };

        BackupScanResult result = new BackupFileScanner().Scan(config);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Files.Count);
        Assert.Contains(Path.GetFullPath(Path.Combine(root, "keep.txt")), result.Files.Keys);
        Assert.Contains(Path.GetFullPath(Path.Combine(root, "Nested", "data.bin")), result.Files.Keys);
    }

    [Fact]
    public void Scanner_RejectsWholeSystemDriveC()
    {
        var config = new BackupConfiguration
        {
            SourcePaths = new List<string> { @"C:\" }
        };

        BackupScanResult result = new BackupFileScanner().Scan(config);

        Assert.Empty(result.Files);
        Assert.Contains(result.Errors, error => error.Contains("Bỏ qua toàn bộ ổ C", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_DeduplicatesOverlappingSources()
    {
        string root = TestEnvironment.CreateDirectory("scanner-overlap");
        string child = Path.Combine(root, "Child");
        Directory.CreateDirectory(child);
        string file = Path.Combine(child, "one.dat");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3 });

        var config = new BackupConfiguration
        {
            SourcePaths = new List<string> { root, child, file }
        };

        BackupScanResult result = new BackupFileScanner().Scan(config);

        Assert.Single(result.Files);
    }
}
