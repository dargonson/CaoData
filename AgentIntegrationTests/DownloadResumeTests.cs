using AgentControl;

namespace AgentIntegrationTests;

public sealed class DownloadResumeTests
{
    [Theory]
    [InlineData("None", 0, "None")]
    [InlineData("None", 10, "SHA256")]
    [InlineData("MD5", 10, "MD5")]
    [InlineData("SHA-256", 10, "SHA256")]
    public void ResumedDownload_AlwaysUsesIntegrityCheck(
        string requested,
        long offset,
        string expected)
    {
        Assert.Equal(expected, frmToolBackup.ResolveDownloadChecksumForOffset(requested, offset));
    }

    [Fact]
    public async Task Reconcile_TruncatesBytesAheadOfDatabaseCheckpoint()
    {
        string path = Path.Combine(TestEnvironment.CreateDirectory("download-ahead"), "partial.bin");
        await File.WriteAllBytesAsync(path, new byte[1_000]);
        long? persisted = null;

        long offset = await DownloadResumeReconciler.ReconcileAsync(
            path,
            700,
            value =>
            {
                persisted = value;
                return Task.CompletedTask;
            });

        Assert.Equal(700, offset);
        Assert.Equal(700, new FileInfo(path).Length);
        Assert.Null(persisted);
    }

    [Fact]
    public async Task Reconcile_LowersDatabaseCheckpointWhenFileIsShorter()
    {
        string path = Path.Combine(TestEnvironment.CreateDirectory("download-short"), "partial.bin");
        await File.WriteAllBytesAsync(path, new byte[321]);
        long? persisted = null;

        long offset = await DownloadResumeReconciler.ReconcileAsync(
            path,
            900,
            value =>
            {
                persisted = value;
                return Task.CompletedTask;
            });

        Assert.Equal(321, offset);
        Assert.Equal(321, persisted);
    }

    [Fact]
    public async Task Reconcile_ResetsMissingFileToZero()
    {
        string missing = Path.Combine(TestEnvironment.CreateDirectory("download-missing"), "missing.bin");
        long? persisted = null;

        long offset = await DownloadResumeReconciler.ReconcileAsync(
            missing,
            1_000,
            value =>
            {
                persisted = value;
                return Task.CompletedTask;
            });

        Assert.Equal(0, offset);
        Assert.Equal(0, persisted);
    }
}
