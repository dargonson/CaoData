using AgentControl;
using AgentShared;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgentIntegrationTests;

public sealed class BackupFlowTests
{
    [Fact]
    public async Task FirstBackup_WithNoFiles_CompletesAsAnEmptySnapshot()
    {
        string agentId = NewAgentId();
        string storageRoot = TestEnvironment.CreateDirectory("first-empty");
        await SaveConfigAsync(agentId, storageRoot);
        DateTime started = DateTime.UtcNow.AddMinutes(-1);
        string workingName = $"FIRST-{agentId}";
        var receiver = new BackupReceiver();

        await receiver.BeginSessionAsync(new BackupSessionBegin
        {
            AgentID = agentId,
            SessionName = workingName,
            BackupType = "FIRST",
            StartedAtUtc = started,
            IsResumableFirst = true,
            PlannedFileCount = 0,
            PlannedTotalBytes = 0
        });

        BackupSessionResult result = await receiver.CompleteSessionAsync(new BackupManifest
        {
            AgentID = agentId,
            SessionName = workingName,
            BackupType = "FIRST",
            StartedAtUtc = started,
            CompletedAtUtc = DateTime.UtcNow,
            IsResumableFirst = true
        });

        Assert.True(result.Success, result.Message);
        string finalRoot = Assert.Single(Directory.GetDirectories(
            storageRoot,
            $"FIRST-{agentId}-*",
            SearchOption.TopDirectoryOnly));
        BackupManifest manifest = JsonSerializer.Deserialize<BackupManifest>(
            await File.ReadAllTextAsync(Path.Combine(finalRoot, "manifest.json")))!;
        Assert.Empty(manifest.Created);
        Assert.Empty(manifest.Modified);
        Assert.Empty(manifest.Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstBackup_RecoversFinalizationAcrossPowerLoss(bool folderWasAlreadyMoved)
    {
        string agentId = NewAgentId();
        string storageRoot = TestEnvironment.CreateDirectory("first-finalizing");
        await SaveConfigAsync(agentId, storageRoot);
        DateTime started = DateTime.UtcNow.AddDays(-1);
        DateTime completed = DateTime.UtcNow;
        string workingName = $"FIRST-{agentId}";
        var begin = new BackupSessionBegin
        {
            AgentID = agentId,
            SessionName = workingName,
            BackupType = "FIRST",
            StartedAtUtc = started,
            IsResumableFirst = true,
            PlannedFileCount = 0,
            PlannedTotalBytes = 0
        };
        var interruptedReceiver = new BackupReceiver();
        await interruptedReceiver.BeginSessionAsync(begin);

        string workingRoot = Path.Combine(storageRoot, workingName + ".inprogress");
        string finalName = $"FIRST-{agentId}-{completed.ToLocalTime():yyyy-MM-dd}";
        string finalRoot = Path.Combine(storageRoot, finalName);
        string manifestPath = Path.Combine(workingRoot, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new BackupManifest
        {
            AgentID = agentId,
            SessionName = finalName,
            BackupType = "FIRST",
            StartedAtUtc = started,
            CompletedAtUtc = completed,
            IsResumableFirst = true
        }));
        await BackupSessionMetadataStore.WriteAsync(
            workingRoot,
            manifestPath,
            agentId,
            finalName,
            "FIRST",
            started,
            completed);
        await FirstBackupStore.MarkFinalizingAsync(agentId, finalName, finalRoot);
        if (folderWasAlreadyMoved)
        {
            Directory.Move(workingRoot, finalRoot);
        }

        var recoveredReceiver = new BackupReceiver();
        await recoveredReceiver.BeginSessionAsync(begin);
        BackupSessionResult result = await recoveredReceiver.CompleteSessionAsync(new BackupManifest
        {
            AgentID = agentId,
            SessionName = workingName,
            BackupType = "FIRST",
            IsResumableFirst = true
        });

        Assert.True(result.Success, result.Message);
        Assert.True(Directory.Exists(finalRoot));
        Assert.False(Directory.Exists(workingRoot));
        Assert.Equal(
            finalRoot,
            await BackupRepository.GetSuccessfulSessionStoragePathAsync(agentId, finalName));
    }

    [Fact]
    public async Task FirstBackup_ResumesAfterReceiverRestart_AndSkipsUnreadableFile()
    {
        string agentId = NewAgentId();
        string storageRoot = TestEnvironment.CreateDirectory("first-resume");
        await SaveConfigAsync(agentId, storageRoot);

        byte[] content = RandomNumberGenerator.GetBytes(900_321);
        byte[] skippedContent = RandomNumberGenerator.GetBytes(17);
        DateTime fileTime = DateTime.UtcNow.AddMinutes(-5);
        var begin = new BackupSessionBegin
        {
            AgentID = agentId,
            SessionName = $"FIRST-{agentId}",
            BackupType = "FIRST",
            StartedAtUtc = DateTime.UtcNow.AddDays(-2),
            IsResumableFirst = true,
            PlannedFileCount = 2,
            PlannedTotalBytes = content.Length + skippedContent.Length
        };
        var query = new BackupFirstFileResumeQuery
        {
            AgentID = agentId,
            SessionName = begin.SessionName,
            SourcePath = @"D:\\Data\\large.bin",
            RelativeStoragePath = Path.Combine("D", "Data", "large.bin"),
            TotalBytes = content.Length,
            LastWriteTimeUtc = fileTime
        };

        var firstReceiver = new BackupReceiver();
        await firstReceiver.BeginSessionAsync(begin);
        BackupFirstFileResumeInfo initial = await firstReceiver.GetFirstFileResumeInfoAsync(query);
        Assert.True(initial.Success);
        Assert.Equal(0, initial.Offset);

        const int interruptedAt = 400_000;
        await SendBackupChunkAsync(
            firstReceiver,
            CreateHeader(query, 0, interruptedAt, isLast: false, contentHash: string.Empty),
            content.AsMemory(0, interruptedAt).ToArray());

        // Mô phỏng Control bị tắt/mất điện rồi tạo receiver mới.
        var resumedReceiver = new BackupReceiver();
        await resumedReceiver.BeginSessionAsync(begin);
        BackupFirstFileResumeInfo resumed = await resumedReceiver.GetFirstFileResumeInfoAsync(query);
        Assert.True(resumed.Success);
        Assert.Equal(interruptedAt, resumed.Offset);

        string contentHash = Convert.ToHexString(SHA256.HashData(content));
        await SendBackupChunkAsync(
            resumedReceiver,
            CreateHeader(
                query,
                interruptedAt,
                content.Length - interruptedAt,
                isLast: true,
                contentHash),
            content.AsMemory(interruptedAt).ToArray());

        await resumedReceiver.SkipFirstFileAsync(new BackupFirstFileSkip
        {
            AgentID = agentId,
            SessionName = begin.SessionName,
            SourcePath = @"D:\\Data\\locked.dat",
            RelativeStoragePath = Path.Combine("D", "Data", "locked.dat"),
            Size = skippedContent.Length,
            LastWriteTimeUtc = fileTime,
            Reason = "File đang được ứng dụng khác khóa."
        });

        BackupSessionResult result = await resumedReceiver.CompleteSessionAsync(new BackupManifest
        {
            AgentID = agentId,
            SessionName = begin.SessionName,
            BackupType = "FIRST",
            StartedAtUtc = begin.StartedAtUtc,
            CompletedAtUtc = DateTime.UtcNow,
            IsResumableFirst = true
        });

        Assert.True(result.Success, result.Message);
        string finalRoot = Assert.Single(Directory.GetDirectories(
            storageRoot,
            $"FIRST-{agentId}-*",
            SearchOption.TopDirectoryOnly));
        Assert.DoesNotContain(".inprogress", finalRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(finalRoot, "Files", "D", "Data", "large.bin")));
        Assert.False(File.Exists(Path.Combine(finalRoot, "Files", "D", "Data", "locked.dat")));

        BackupManifest manifest = JsonSerializer.Deserialize<BackupManifest>(
            await File.ReadAllTextAsync(Path.Combine(finalRoot, "manifest.json")))!;
        Assert.Single(manifest.Created);
        Assert.Equal(contentHash, manifest.Created[0].ContentSha256);
        Assert.Contains(manifest.Errors, error => error.Contains("File đang được ứng dụng khác khóa", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(finalRoot, BackupSessionMetadataStore.FileName)));

        // Mô phỏng Agent mất ACK sau khi Control đã chốt FIRST.
        var acknowledgementReceiver = new BackupReceiver();
        await acknowledgementReceiver.BeginSessionAsync(begin);
        BackupSessionResult acknowledged = await acknowledgementReceiver.CompleteSessionAsync(new BackupManifest
        {
            AgentID = agentId,
            SessionName = begin.SessionName,
            BackupType = "FIRST",
            IsResumableFirst = true
        });
        Assert.True(acknowledged.Success, acknowledged.Message);
    }

    [Fact]
    public async Task BackupReceiver_RejectsBadHash_ThenAcceptsCleanRetry()
    {
        string agentId = NewAgentId();
        string storageRoot = TestEnvironment.CreateDirectory("hash-retry");
        await SaveConfigAsync(agentId, storageRoot);
        string sessionName = $"INC-{agentId}-2026-08-24";
        byte[] content = RandomNumberGenerator.GetBytes(300_017);
        DateTime fileTime = DateTime.UtcNow.AddMinutes(-1);
        DateTime sessionStarted = DateTime.UtcNow;
        var receiver = new BackupReceiver();
        await receiver.BeginSessionAsync(new BackupSessionBegin
        {
            AgentID = agentId,
            SessionName = sessionName,
            BackupType = "INC",
            StartedAtUtc = sessionStarted
        });
        var query = new BackupFirstFileResumeQuery
        {
            AgentID = agentId,
            SessionName = sessionName,
            SourcePath = @"D:\\Data\\hash.bin",
            RelativeStoragePath = Path.Combine("D", "Data", "hash.bin"),
            TotalBytes = content.Length,
            LastWriteTimeUtc = fileTime
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => SendBackupChunkAsync(
            receiver,
            CreateHeader(query, 0, content.Length, true, new string('0', 64)),
            content));
        Assert.False(File.Exists(Path.Combine(storageRoot, sessionName, "Files", "D", "Data", "hash.bin.incoming")));

        string hash = Convert.ToHexString(SHA256.HashData(content));
        await SendBackupChunkAsync(
            receiver,
            CreateHeader(query, 0, content.Length, true, hash),
            content);
        BackupSessionResult result = await receiver.CompleteSessionAsync(new BackupManifest
        {
            AgentID = agentId,
            SessionName = sessionName,
            BackupType = "INC",
            StartedAtUtc = sessionStarted,
            CompletedAtUtc = DateTime.UtcNow,
            Created = new List<BackupManifestEntry>
            {
                Entry(query.SourcePath, query.RelativeStoragePath, content, fileTime)
            }
        });

        Assert.True(result.Success, result.Message);
        Assert.Equal(content, await File.ReadAllBytesAsync(
            Path.Combine(storageRoot, sessionName, "Files", "D", "Data", "hash.bin")));
    }

    [Fact]
    public async Task IncrementalManifest_CannotClaimAFileThatControlDidNotReceive()
    {
        string agentId = NewAgentId();
        string storageRoot = TestEnvironment.CreateDirectory("manifest-unreceived");
        await SaveConfigAsync(agentId, storageRoot);
        DateTime started = DateTime.UtcNow.AddMinutes(-1);
        string sessionName = $"INC-{agentId}-2026-08-25";
        var receiver = new BackupReceiver();
        await receiver.BeginSessionAsync(new BackupSessionBegin
        {
            AgentID = agentId,
            SessionName = sessionName,
            BackupType = "INC",
            StartedAtUtc = started
        });

        BackupSessionResult result = await receiver.CompleteSessionAsync(new BackupManifest
        {
            AgentID = agentId,
            SessionName = sessionName,
            BackupType = "INC",
            StartedAtUtc = started,
            CompletedAtUtc = DateTime.UtcNow,
            Created = new List<BackupManifestEntry>
            {
                new()
                {
                    SourcePath = @"D:\\Data\\never-sent.bin",
                    RelativeStoragePath = Path.Combine("D", "Data", "never-sent.bin"),
                    Size = 10,
                    LastWriteTimeUtc = started,
                    ContentSha256 = new string('A', 64)
                }
            }
        });

        Assert.False(result.Success);
        Assert.Contains("không khớp file Control đã nhận", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await BackupRepository.GetSuccessfulSessionStoragePathAsync(agentId, sessionName));
    }

    [Fact]
    public async Task FailedRetry_CannotDowngradeAnAlreadySuccessfulSession()
    {
        string agentId = NewAgentId();
        string sessionName = $"INC-{agentId}-2026-08-25";
        string successfulPath = TestEnvironment.CreateDirectory("successful-session");
        DateTime started = DateTime.UtcNow.AddMinutes(-2);
        var manifest = new BackupManifest
        {
            AgentID = agentId,
            SessionName = sessionName,
            BackupType = "INC",
            StartedAtUtc = started,
            CompletedAtUtc = started.AddMinutes(1)
        };

        await BackupRepository.SaveSessionAsync(manifest, successfulPath, true, "ok");
        await BackupRepository.SaveSessionAsync(
            manifest,
            TestEnvironment.CreateDirectory("failed-retry"),
            false,
            "retry failed");

        Assert.Equal(
            successfulPath,
            await BackupRepository.GetSuccessfulSessionStoragePathAsync(agentId, sessionName));
    }

    [Fact]
    public async Task SyntheticFull_ReplaysCreateModifyDelete_AndIsNotMutatedByIncRetry()
    {
        string agentId = NewAgentId();
        string storageRoot = TestEnvironment.CreateDirectory("synthetic");
        await SaveConfigAsync(agentId, storageRoot);
        DateTime fileTime = new(2026, 8, 20, 1, 2, 3, DateTimeKind.Utc);
        byte[] oldA = "old-a"u8.ToArray();
        byte[] oldB = "old-b"u8.ToArray();

        string firstName = $"FIRST-{agentId}-2026-08-20";
        var firstReceiver = new BackupReceiver();
        await firstReceiver.BeginSessionAsync(new BackupSessionBegin
        {
            AgentID = agentId,
            SessionName = firstName,
            BackupType = "FIRST",
            StartedAtUtc = fileTime
        });
        BackupManifestEntry firstA = Entry(@"D:\\Data\\a.txt", Path.Combine("D", "Data", "a.txt"), oldA, fileTime);
        BackupManifestEntry firstB = Entry(@"D:\\Data\\b.txt", Path.Combine("D", "Data", "b.txt"), oldB, fileTime);
        await SendWholeFileAsync(firstReceiver, agentId, firstName, firstA, oldA);
        await SendWholeFileAsync(firstReceiver, agentId, firstName, firstB, oldB);
        BackupSessionResult firstResult = await firstReceiver.CompleteSessionAsync(new BackupManifest
        {
            AgentID = agentId,
            SessionName = firstName,
            BackupType = "FIRST",
            StartedAtUtc = fileTime,
            CompletedAtUtc = fileTime.AddMinutes(1),
            Created = new List<BackupManifestEntry> { firstA, firstB }
        });
        Assert.True(firstResult.Success, firstResult.Message);

        byte[] newA = "new-a"u8.ToArray();
        byte[] newC = "new-c"u8.ToArray();
        string incName = $"INC-{agentId}-2026-08-21";
        var incReceiver = new BackupReceiver();
        await incReceiver.BeginSessionAsync(new BackupSessionBegin
        {
            AgentID = agentId,
            SessionName = incName,
            BackupType = "INC",
            StartedAtUtc = fileTime.AddDays(1)
        });
        BackupManifestEntry modifiedA = Entry(firstA.SourcePath, firstA.RelativeStoragePath, newA, fileTime.AddDays(1));
        BackupManifestEntry createdC = Entry(@"D:\\Data\\c.txt", Path.Combine("D", "Data", "c.txt"), newC, fileTime.AddDays(1));
        await SendWholeFileAsync(incReceiver, agentId, incName, modifiedA, newA);
        await SendWholeFileAsync(incReceiver, agentId, incName, createdC, newC);
        BackupSessionResult incResult = await incReceiver.CompleteSessionAsync(new BackupManifest
        {
            AgentID = agentId,
            SessionName = incName,
            BackupType = "INC",
            StartedAtUtc = fileTime.AddDays(1),
            CompletedAtUtc = fileTime.AddDays(1).AddMinutes(2),
            CreateSyntheticFull = true,
            Created = new List<BackupManifestEntry> { createdC },
            Modified = new List<BackupManifestEntry> { modifiedA },
            Deleted = new List<BackupManifestEntry> { firstB }
        });
        Assert.True(incResult.Success, incResult.Message);

        string syntheticRoot = Path.Combine(storageRoot, $"FIRST-{agentId}-2026-08-21");
        string syntheticA = Path.Combine(syntheticRoot, "Files", "D", "Data", "a.txt");
        Assert.Equal(newA, await File.ReadAllBytesAsync(syntheticA));
        Assert.Equal(newC, await File.ReadAllBytesAsync(Path.Combine(syntheticRoot, "Files", "D", "Data", "c.txt")));
        Assert.False(File.Exists(Path.Combine(syntheticRoot, "Files", "D", "Data", "b.txt")));

        // Retry cùng INC phải replace inode của INC, không sửa hard-link/copy đã chốt trong Synthetic Full.
        byte[] retryA = "retry-after-full"u8.ToArray();
        var retryReceiver = new BackupReceiver();
        await retryReceiver.BeginSessionAsync(new BackupSessionBegin
        {
            AgentID = agentId,
            SessionName = incName,
            BackupType = "INC",
            StartedAtUtc = fileTime.AddDays(1)
        });
        BackupManifestEntry retryEntry = Entry(firstA.SourcePath, firstA.RelativeStoragePath, retryA, fileTime.AddDays(1).AddHours(1));
        await SendWholeFileAsync(retryReceiver, agentId, incName, retryEntry, retryA);

        Assert.Equal(newA, await File.ReadAllBytesAsync(syntheticA));
        Assert.Equal(retryA, await File.ReadAllBytesAsync(
            Path.Combine(storageRoot, incName, "Files", "D", "Data", "a.txt")));
    }

    [Fact]
    public async Task SyntheticFailure_DoesNotRollBackCommittedIncremental()
    {
        string agentId = NewAgentId();
        string storageRoot = TestEnvironment.CreateDirectory("synthetic-failure");
        await SaveConfigAsync(agentId, storageRoot);
        DateTime firstTime = new(2026, 8, 20, 1, 2, 3, DateTimeKind.Utc);
        byte[] content = "source-will-disappear"u8.ToArray();
        string firstName = $"FIRST-{agentId}-2026-08-20";
        BackupManifestEntry entry = Entry(
            @"D:\\Data\\missing.txt",
            Path.Combine("D", "Data", "missing.txt"),
            content,
            firstTime);

        var firstReceiver = new BackupReceiver();
        await firstReceiver.BeginSessionAsync(new BackupSessionBegin
        {
            AgentID = agentId,
            SessionName = firstName,
            BackupType = "FIRST",
            StartedAtUtc = firstTime
        });
        await SendWholeFileAsync(firstReceiver, agentId, firstName, entry, content);
        BackupSessionResult first = await firstReceiver.CompleteSessionAsync(new BackupManifest
        {
            AgentID = agentId,
            SessionName = firstName,
            BackupType = "FIRST",
            StartedAtUtc = firstTime,
            CompletedAtUtc = firstTime.AddMinutes(1),
            Created = new List<BackupManifestEntry> { entry }
        });
        Assert.True(first.Success, first.Message);

        File.Delete(Path.Combine(storageRoot, firstName, "Files", entry.RelativeStoragePath));
        string incName = $"INC-{agentId}-2026-08-21";
        var incReceiver = new BackupReceiver();
        await incReceiver.BeginSessionAsync(new BackupSessionBegin
        {
            AgentID = agentId,
            SessionName = incName,
            BackupType = "INC",
            StartedAtUtc = firstTime.AddDays(1)
        });
        BackupSessionResult result = await incReceiver.CompleteSessionAsync(new BackupManifest
        {
            AgentID = agentId,
            SessionName = incName,
            BackupType = "INC",
            StartedAtUtc = firstTime.AddDays(1),
            CompletedAtUtc = firstTime.AddDays(1).AddMinutes(1),
            CreateSyntheticFull = true
        });

        Assert.True(result.Success, result.Message);
        Assert.False(result.SyntheticFullCompleted);
        Assert.Contains("INC đã hoàn tất", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            Path.Combine(storageRoot, incName),
            await BackupRepository.GetSuccessfulSessionStoragePathAsync(agentId, incName));
        Assert.False(Directory.Exists(Path.Combine(storageRoot, $"FIRST-{agentId}-2026-08-21")));
    }

    private static async Task SaveConfigAsync(string agentId, string storageRoot)
    {
        await BackupRepository.SaveConfigAsync(new BackupConfiguration
        {
            AgentID = agentId,
            ControlStoragePath = storageRoot,
            SourcePaths = new List<string> { @"D:\\Data" },
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    private static string NewAgentId() => "AGT-T-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

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

    private static BackupFileChunkHeader CreateHeader(
        BackupFirstFileResumeQuery query,
        long offset,
        int count,
        bool isLast,
        string contentHash)
    {
        return new BackupFileChunkHeader
        {
            AgentID = query.AgentID,
            SessionName = query.SessionName,
            SourcePath = query.SourcePath,
            RelativeStoragePath = query.RelativeStoragePath,
            TotalBytes = query.TotalBytes,
            Offset = offset,
            ChunkSize = count,
            IsLastChunk = isLast,
            LastWriteTimeUtc = query.LastWriteTimeUtc,
            ContentSha256 = contentHash
        };
    }

    private static Task SendWholeFileAsync(
        BackupReceiver receiver,
        string agentId,
        string sessionName,
        BackupManifestEntry entry,
        byte[] content)
    {
        var query = new BackupFirstFileResumeQuery
        {
            AgentID = agentId,
            SessionName = sessionName,
            SourcePath = entry.SourcePath,
            RelativeStoragePath = entry.RelativeStoragePath,
            TotalBytes = content.Length,
            LastWriteTimeUtc = entry.LastWriteTimeUtc
        };
        return SendBackupChunkAsync(
            receiver,
            CreateHeader(query, 0, content.Length, true, entry.ContentSha256),
            content);
    }

    private static async Task SendBackupChunkAsync(
        BackupReceiver receiver,
        BackupFileChunkHeader header,
        byte[] body)
    {
        await using MemoryStream wire = new();
        await TransferFrameProtocol.WriteBinaryBackupChunkAsync(wire, header, body, body.Length);
        wire.Position = 0;
        byte[] sizeBytes = new byte[4];
        await TransferFrameProtocol.ReadExactAsync(wire, sizeBytes, 0, sizeBytes.Length);
        int frameSize = BitConverter.ToInt32(sizeBytes);
        Assert.Equal(TransferFrameProtocol.BinaryBackupChunkMarker, wire.ReadByte());
        await receiver.HandleFileChunkAsync(wire, frameSize, header.AgentID);
    }
}
