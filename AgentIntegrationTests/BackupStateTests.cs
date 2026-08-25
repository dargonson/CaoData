using AgentService;

namespace AgentIntegrationTests;

public sealed class BackupStateTests
{
    [Fact]
    public void Committer_PreservesMissingOldFilesWhenScanHasErrors()
    {
        BackupFileSnapshot oldMissing = Snapshot(@"D:\Data\old.txt", 10);
        BackupFileSnapshot unchanged = Snapshot(@"D:\Data\keep.txt", 20);
        var previous = new Dictionary<string, BackupFileSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [oldMissing.FullPath] = oldMissing,
            [unchanged.FullPath] = unchanged
        };
        var scan = new Dictionary<string, BackupFileSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [unchanged.FullPath] = unchanged
        };

        Dictionary<string, BackupFileSnapshot> committed = BackupInventoryCommitter.Build(
            scan,
            previous,
            Array.Empty<string>(),
            scanHadErrors: true);

        Assert.Same(oldMissing, committed[oldMissing.FullPath]);
        Assert.Equal(2, committed.Count);
    }

    [Fact]
    public void Committer_AllowsCleanScanToKeepDeletionVisible()
    {
        BackupFileSnapshot deleted = Snapshot(@"D:\Data\deleted.txt", 10);
        var previous = new Dictionary<string, BackupFileSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [deleted.FullPath] = deleted
        };

        Dictionary<string, BackupFileSnapshot> committed = BackupInventoryCommitter.Build(
            new Dictionary<string, BackupFileSnapshot>(StringComparer.OrdinalIgnoreCase),
            previous,
            Array.Empty<string>(),
            scanHadErrors: false);

        Assert.Empty(committed);
    }

    [Fact]
    public void Committer_RollsBackFailedModifiedAndNewFiles()
    {
        BackupFileSnapshot oldModified = Snapshot(@"D:\Data\modified.txt", 10);
        BackupFileSnapshot newModified = Snapshot(oldModified.FullPath, 99);
        BackupFileSnapshot failedNew = Snapshot(@"D:\Data\new.txt", 25);
        var previous = new Dictionary<string, BackupFileSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [oldModified.FullPath] = oldModified
        };
        var scan = new Dictionary<string, BackupFileSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [newModified.FullPath] = newModified,
            [failedNew.FullPath] = failedNew
        };

        Dictionary<string, BackupFileSnapshot> committed = BackupInventoryCommitter.Build(
            scan,
            previous,
            new[] { newModified.FullPath, failedNew.FullPath },
            scanHadErrors: false);

        Assert.Same(oldModified, committed[oldModified.FullPath]);
        Assert.DoesNotContain(failedNew.FullPath, committed.Keys);
    }

    [Fact]
    public void ScanErrors_AreBoundedAndKeepSuppressedCount()
    {
        var result = new BackupScanResult();

        for (int i = 0; i < 2500; i++)
        {
            result.AddError("error-" + i);
        }

        Assert.Equal(1001, result.Errors.Count);
        Assert.Contains("1500", result.Errors[^1], StringComparison.Ordinal);
    }

    private static BackupFileSnapshot Snapshot(string path, long size) => new()
    {
        FullPath = path,
        RelativeStoragePath = Path.Combine("D", Path.GetFileName(path)),
        Size = size,
        LastWriteTimeUtc = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc)
    };
}
