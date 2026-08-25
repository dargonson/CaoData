using AgentControl;
using AgentShared;
using System.Text.Json;

namespace AgentIntegrationTests;

public sealed class ManifestStreamingTests
{
    [Fact]
    public async Task ManifestReader_StreamsOneHundredThousandEntries()
    {
        const int entryCount = 100_000;
        string sessionRoot = TestEnvironment.CreateDirectory("large-manifest");
        string manifestPath = Path.Combine(sessionRoot, "manifest.json");
        DateTime started = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
        DateTime completed = started.AddHours(3);

        await using (FileStream stream = new(
            manifestPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("AgentID", "AGT-LARGE-MANIFEST");
            writer.WriteString("SessionName", "FIRST-AGT-LARGE-MANIFEST-2026-08-20");
            writer.WriteString("BackupType", "FIRST");
            writer.WriteString("StartedAtUtc", started);
            writer.WriteString("CompletedAtUtc", completed);
            writer.WritePropertyName("Created");
            writer.WriteStartArray();
            for (int i = 0; i < entryCount; i++)
            {
                JsonSerializer.Serialize(writer, new BackupManifestEntry
                {
                    SourcePath = $@"D:\\Large\\file-{i:D6}.dat",
                    RelativeStoragePath = Path.Combine("D", "Large", $"file-{i:D6}.dat"),
                    Size = i,
                    LastWriteTimeUtc = started
                });
                if (i > 0 && i % 5_000 == 0)
                {
                    writer.Flush();
                    await stream.FlushAsync();
                }
            }
            writer.WriteEndArray();
            writer.WritePropertyName("Modified");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("Deleted");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("Errors");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
            await stream.FlushAsync();
            stream.Flush(flushToDisk: true);
        }

        BackupManifestMetadata metadata = BackupManifestStreamReader.ReadMetadata(manifestPath);
        int counted = 0;
        string? first = null;
        string? last = null;
        BackupManifestStreamReader.ReadEntries(manifestPath, (section, entry) =>
        {
            Assert.Equal(ManifestEntrySection.Created, section);
            first ??= entry.SourcePath;
            last = entry.SourcePath;
            counted++;
        }, CancellationToken.None);

        Assert.Equal("AGT-LARGE-MANIFEST", metadata.AgentID);
        Assert.Equal(started, metadata.StartedAtUtc);
        Assert.Equal(completed, metadata.CompletedAtUtc);
        Assert.Equal(entryCount, counted);
        Assert.Equal(@"D:\\Large\\file-000000.dat", first);
        Assert.Equal(@"D:\\Large\\file-099999.dat", last);
    }

    [Fact]
    public async Task SessionMetadata_DetectsChangedManifest()
    {
        string root = TestEnvironment.CreateDirectory("metadata-integrity");
        string manifestPath = Path.Combine(root, "manifest.json");
        DateTime now = DateTime.UtcNow;
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new BackupManifest
        {
            AgentID = "AGT-META",
            SessionName = "INC-AGT-META-2026-08-25",
            BackupType = "INC",
            StartedAtUtc = now.AddMinutes(-1),
            CompletedAtUtc = now
        }));
        await BackupSessionMetadataStore.WriteAsync(
            root,
            manifestPath,
            "AGT-META",
            "INC-AGT-META-2026-08-25",
            "INC",
            now.AddMinutes(-1),
            now);

        BackupSessionMetadata valid = BackupSessionMetadataStore.ReadVerified(root, manifestPath);
        Assert.Equal("AGT-META", valid.AgentID);

        await File.AppendAllTextAsync(manifestPath, " ");
        Assert.Throws<InvalidDataException>(() => BackupSessionMetadataStore.ReadVerified(root, manifestPath));
    }

    [Fact]
    public async Task SessionMetadata_DetectsSameLengthChangeWithRestoredTimestamp()
    {
        string root = TestEnvironment.CreateDirectory("metadata-same-size");
        string manifestPath = Path.Combine(root, "manifest.json");
        DateTime now = DateTime.UtcNow;
        string original = JsonSerializer.Serialize(new BackupManifest
        {
            AgentID = "AGT-META-A",
            SessionName = "INC-AGT-META-A-2026-08-25",
            BackupType = "INC",
            StartedAtUtc = now.AddMinutes(-1),
            CompletedAtUtc = now
        });
        await File.WriteAllTextAsync(manifestPath, original);
        await BackupSessionMetadataStore.WriteAsync(
            root,
            manifestPath,
            "AGT-META-A",
            "INC-AGT-META-A-2026-08-25",
            "INC",
            now.AddMinutes(-1),
            now);

        DateTime originalWriteTime = File.GetLastWriteTimeUtc(manifestPath);
        string changed = original.Replace("AGT-META-A", "AGT-META-B", StringComparison.Ordinal);
        Assert.Equal(original.Length, changed.Length);
        await File.WriteAllTextAsync(manifestPath, changed);
        File.SetLastWriteTimeUtc(manifestPath, originalWriteTime);

        Assert.Throws<InvalidDataException>(() =>
            BackupSessionMetadataStore.ReadVerified(root, manifestPath));
    }
}
