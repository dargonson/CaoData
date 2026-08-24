using AgentControl;
using AgentShared;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgentIntegrationTests;

public sealed class RecoveryFlowTests
{
    [Fact]
    public void RecoveryBrowser_RuntimeUiSharesShellImagesAndMatchesMainBrowserSizing()
    {
        Exception? failure = null;
        using ManualResetEventSlim completed = new(false);
        Thread thread = new(() =>
        {
            try
            {
                object form = Activator.CreateInstance(typeof(frmRecovery), "UI-SMOKE")!;
                try
                {
                    object tree = GetPrivateField(form, "TvBackupFile");
                    object list = GetPrivateField(form, "lvBackupFiles");
                    RecoveryProgressBar progress = Assert.IsType<RecoveryProgressBar>(
                        GetPrivateField(form, "pcbbackup"));
                    object? treeImages = tree.GetType().GetProperty("ImageList")!.GetValue(tree);
                    object? listImages = list.GetType().GetProperty("SmallImageList")!.GetValue(list);

                    Assert.NotNull(treeImages);
                    Assert.Same(treeImages, listImages);
                    Assert.Equal(24, tree.GetType().GetProperty("ItemHeight")!.GetValue(tree));
                    Assert.Equal(true, list.GetType().GetProperty("FullRowSelect")!.GetValue(list));
                    Assert.Equal(false, list.GetType().GetProperty("HideSelection")!.GetValue(list));

                    Assert.Equal("0%", progress.DisplayText);
                    progress.Value = 557;
                    Assert.Equal(55, progress.Percentage);
                    Assert.Equal("55%", progress.DisplayText);
                    progress.Value = progress.Maximum;
                    Assert.Equal("100%", progress.DisplayText);
                    progress.DisplayState = RecoveryProgressDisplayState.Completed;
                    Assert.Equal("Hoàn Thành", progress.DisplayText);
                    progress.DisplayState = RecoveryProgressDisplayState.Error;
                    Assert.Equal("100%", progress.DisplayText);
                    using Bitmap renderedProgress = new(progress.Width, progress.Height);
                    progress.DrawToBitmap(renderedProgress, progress.ClientRectangle);
                    Assert.Equal(
                        Color.FromArgb(220, 53, 69).ToArgb(),
                        renderedProgress.GetPixel(6, 6).ToArgb());
                }
                finally
                {
                    ((IDisposable)form).Dispose();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(15)), "frmRecovery UI smoke bị treo.");
        thread.Join();
        Assert.Null(failure);
    }

    [Fact]
    public async Task RecoveryBrowser_SeparatesChildFoldersFromDirectFilesAndMarksOnlyRealBranches()
    {
        string agentId = "AGT-TREE-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        DateTime date = new(2026, 8, 26);
        string database = Path.Combine(TestEnvironment.CreateDirectory("recovery-tree-db"), "snapshot.db");
        var repository = new RecoverySnapshotRepository(database);

        await repository.RebuildAsync(
            agentId,
            date,
            "tree-signature",
            writer =>
            {
                writer.Upsert(new BackupManifestEntry
                {
                    SourcePath = @"M:\root.txt",
                    RelativeStoragePath = Path.Combine("M", "root.txt"),
                    Size = 10,
                    LastWriteTimeUtc = DateTime.UtcNow
                }, "FIRST-root");
                writer.Upsert(new BackupManifestEntry
                {
                    SourcePath = @"M:\abc\123.txt",
                    RelativeStoragePath = Path.Combine("M", "abc", "123.txt"),
                    Size = 20,
                    LastWriteTimeUtc = DateTime.UtcNow
                }, "FIRST-root");
                writer.Upsert(new BackupManifestEntry
                {
                    SourcePath = @"M:\abc\deep\456.bin",
                    RelativeStoragePath = Path.Combine("M", "abc", "deep", "456.bin"),
                    Size = 30,
                    LastWriteTimeUtc = DateTime.UtcNow
                }, "FIRST-root");
            },
            CancellationToken.None);

        RecoveryDirectoryRecord drive = Assert.Single(
            await repository.GetChildDirectoriesAsync(agentId, date, string.Empty));
        Assert.Equal("M", drive.VirtualPath);
        Assert.True(drive.HasChildren);
        Assert.True(frmRecovery.ShouldAddRecoveryLoadingPlaceholder(drive.HasChildren));

        RecoveryDirectoryRecord abc = Assert.Single(
            await repository.GetChildDirectoriesAsync(agentId, date, "M"));
        Assert.Equal(Path.Combine("M", "abc"), abc.VirtualPath);
        Assert.True(abc.HasChildren);

        RecoveryDirectoryRecord deep = Assert.Single(
            await repository.GetChildDirectoriesAsync(agentId, date, Path.Combine("M", "abc")));
        Assert.False(deep.HasChildren);
        Assert.False(frmRecovery.ShouldAddRecoveryLoadingPlaceholder(deep.HasChildren));

        RecoveryFileRecord rootFile = Assert.Single(
            await repository.GetFilesAsync(agentId, date, "M"));
        Assert.Equal("root.txt", rootFile.FileName);
        RecoveryFileRecord abcFile = Assert.Single(
            await repository.GetFilesAsync(agentId, date, Path.Combine("M", "abc")));
        Assert.Equal("123.txt", abcFile.FileName);
        RecoveryFileRecord deepFile = Assert.Single(
            await repository.GetFilesAsync(agentId, date, Path.Combine("M", "abc", "deep")));
        Assert.Equal("456.bin", deepFile.FileName);
    }

    [Fact]
    public async Task Recovery_ReplaysChain_ExtractsSelection_AndRejectsCorruption()
    {
        string agentId = "AGT-R-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        string storageRoot = TestEnvironment.CreateDirectory("recovery-storage");
        DateTime firstCompleted = new(2026, 8, 20, 2, 0, 0, DateTimeKind.Utc);
        DateTime incCompleted = new(2026, 8, 21, 2, 0, 0, DateTimeKind.Utc);

        byte[] oldA = "old version"u8.ToArray();
        byte[] oldB = "deleted version"u8.ToArray();
        byte[] newA = "new version"u8.ToArray();
        byte[] newC = RandomNumberGenerator.GetBytes(250_000);
        BackupManifestEntry firstA = Entry(@"D:\\Data\\a.txt", Path.Combine("D", "Data", "a.txt"), oldA, firstCompleted);
        BackupManifestEntry firstB = Entry(@"D:\\Data\\b.txt", Path.Combine("D", "Data", "b.txt"), oldB, firstCompleted);
        BackupManifestEntry incA = Entry(firstA.SourcePath, firstA.RelativeStoragePath, newA, incCompleted);
        BackupManifestEntry incC = Entry(@"D:\\Data\\c.bin", Path.Combine("D", "Data", "c.bin"), newC, incCompleted);

        await WriteSessionAsync(
            storageRoot,
            agentId,
            $"FIRST-{agentId}-2026-08-20",
            "FIRST",
            firstCompleted.AddHours(-1),
            firstCompleted,
            new[] { (firstA, oldA), (firstB, oldB) },
            new BackupManifest
            {
                AgentID = agentId,
                SessionName = $"FIRST-{agentId}-2026-08-20",
                BackupType = "FIRST",
                StartedAtUtc = firstCompleted.AddHours(-1),
                CompletedAtUtc = firstCompleted,
                Created = new List<BackupManifestEntry> { firstA, firstB }
            });
        string incRoot = await WriteSessionAsync(
            storageRoot,
            agentId,
            $"INC-{agentId}-2026-08-21",
            "INC",
            incCompleted.AddHours(-1),
            incCompleted,
            new[] { (incA, newA), (incC, newC) },
            new BackupManifest
            {
                AgentID = agentId,
                SessionName = $"INC-{agentId}-2026-08-21",
                BackupType = "INC",
                StartedAtUtc = incCompleted.AddHours(-1),
                CompletedAtUtc = incCompleted,
                Created = new List<BackupManifestEntry> { incC },
                Modified = new List<BackupManifestEntry> { incA },
                Deleted = new List<BackupManifestEntry> { firstB }
            });

        string database = Path.Combine(TestEnvironment.CreateDirectory("recovery-db"), "snapshot.db");
        var repository = new RecoverySnapshotRepository(database);
        var builder = new RecoverySnapshotBuilder(repository);
        RecoveryBuildResult build = await builder.BuildAsync(
            storageRoot, agentId, new DateTime(2026, 8, 21));

        Assert.Equal(1, build.AppliedIncrementalCount);
        List<RecoveryFileRecord> files = await repository.GetFilesAsync(
            agentId, build.SelectedDate, Path.Combine("D", "Data"));
        Assert.Equal(2, files.Count);
        Assert.DoesNotContain(files, file => file.SourcePath.Equals(firstB.SourcePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, file => file.SourcePath.Equals(firstA.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                                      file.SourceSessionRoot.Equals(incRoot, StringComparison.OrdinalIgnoreCase));

        string destination = TestEnvironment.CreateDirectory("recovery-output");
        string runId = Guid.NewGuid().ToString("N");
        await repository.PrepareSelectionAsync(
            runId,
            new[] { Path.Combine("D", "Data") },
            Array.Empty<string>());
        RecoveryExtractionResult extraction = await new RecoveryFileExtractor(repository).ExtractAsync(
            runId,
            agentId,
            build.SelectedDate,
            destination,
            progress: null,
            CancellationToken.None);

        Assert.Equal(2, extraction.CompletedFiles);
        Assert.Equal(0, extraction.FailedFiles);
        Assert.Equal(newA, await File.ReadAllBytesAsync(Path.Combine(destination, "D", "Data", "a.txt")));
        Assert.Equal(newC, await File.ReadAllBytesAsync(Path.Combine(destination, "D", "Data", "c.bin")));
        Assert.False(File.Exists(Path.Combine(destination, "D", "Data", "b.txt")));

        // Làm hỏng file backup nhưng giữ nguyên kích thước: SHA-256 phải chặn restore.
        string corruptSource = Path.Combine(incRoot, "Files", "D", "Data", "c.bin");
        byte[] corrupted = new byte[newC.Length];
        Array.Fill(corrupted, (byte)0xA5);
        await File.WriteAllBytesAsync(corruptSource, corrupted);
        File.Delete(Path.Combine(destination, "D", "Data", "c.bin"));
        string corruptRun = Guid.NewGuid().ToString("N");
        await repository.PrepareSelectionAsync(corruptRun, Array.Empty<string>(), new[] { incC.SourcePath });
        RecoveryExtractionResult corruptResult = await new RecoveryFileExtractor(repository).ExtractAsync(
            corruptRun,
            agentId,
            build.SelectedDate,
            destination,
            progress: null,
            CancellationToken.None);

        Assert.Equal(0, corruptResult.CompletedFiles);
        Assert.Equal(1, corruptResult.FailedFiles);
        Assert.False(File.Exists(Path.Combine(destination, "D", "Data", "c.bin")));
    }

    [Fact]
    public async Task Recovery_RollsBackSnapshotWhenManifestContainsTraversal()
    {
        string agentId = "AGT-EVIL-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string storageRoot = TestEnvironment.CreateDirectory("recovery-traversal");
        DateTime completed = new(2026, 8, 22, 1, 0, 0, DateTimeKind.Utc);
        byte[] content = "evil"u8.ToArray();
        BackupManifestEntry evil = Entry(@"D:\\evil.txt", "..\\evil.txt", content, completed);
        await WriteSessionAsync(
            storageRoot,
            agentId,
            $"FIRST-{agentId}-2026-08-22",
            "FIRST",
            completed.AddMinutes(-1),
            completed,
            Array.Empty<(BackupManifestEntry, byte[])>(),
            new BackupManifest
            {
                AgentID = agentId,
                SessionName = $"FIRST-{agentId}-2026-08-22",
                BackupType = "FIRST",
                StartedAtUtc = completed.AddMinutes(-1),
                CompletedAtUtc = completed,
                Created = new List<BackupManifestEntry> { evil }
            });

        string database = Path.Combine(TestEnvironment.CreateDirectory("recovery-traversal-db"), "snapshot.db");
        var repository = new RecoverySnapshotRepository(database);
        var builder = new RecoverySnapshotBuilder(repository);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            builder.BuildAsync(storageRoot, agentId, new DateTime(2026, 8, 22)));
        Assert.Empty(await repository.GetFilesAsync(agentId, new DateTime(2026, 8, 22), string.Empty));
    }

    private static BackupManifestEntry Entry(
        string sourcePath,
        string relativePath,
        byte[] content,
        DateTime lastWriteUtc)
    {
        return new BackupManifestEntry
        {
            SourcePath = sourcePath,
            RelativeStoragePath = relativePath,
            Size = content.Length,
            LastWriteTimeUtc = lastWriteUtc,
            ContentSha256 = Convert.ToHexString(SHA256.HashData(content))
        };
    }

    private static object GetPrivateField(object instance, string name) =>
        instance.GetType()
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(instance)!;

    private static async Task<string> WriteSessionAsync(
        string storageRoot,
        string agentId,
        string sessionName,
        string backupType,
        DateTime started,
        DateTime completed,
        IEnumerable<(BackupManifestEntry Entry, byte[] Content)> physicalFiles,
        BackupManifest manifest)
    {
        string root = Path.Combine(storageRoot, sessionName);
        foreach ((BackupManifestEntry entry, byte[] content) in physicalFiles)
        {
            string path = PathSafety.GetSafeChildPath(Path.Combine(root, "Files"), entry.RelativeStoragePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, content);
            File.SetLastWriteTimeUtc(path, entry.LastWriteTimeUtc);
        }

        Directory.CreateDirectory(root);
        string manifestPath = Path.Combine(root, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));
        await BackupSessionMetadataStore.WriteAsync(
            root, manifestPath, agentId, sessionName, backupType, started, completed);
        return root;
    }
}
