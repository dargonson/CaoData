using AgentShared;
using System.Security.Cryptography;

namespace AgentIntegrationTests;

public sealed class TransferFileTests
{
    [Fact]
    public async Task ResumableWriter_CompletesUploadAndDownloadAcrossChunks()
    {
        string root = TestEnvironment.CreateDirectory("up-down-resume");
        byte[] source = RandomNumberGenerator.GetBytes(1_350_777);

        foreach (string direction in new[] { "Control-to-Agent-upload", "Agent-to-Control-download" })
        {
            string destination = Path.Combine(root, direction, "large.bin");
            const int firstLength = 512_000;
            await using (MemoryStream first = new(source, 0, firstLength, writable: false))
            {
                long offset = await ResumableTransferFile.WriteChunkAsync(
                    first, destination, 0, source.Length, firstLength, isLastChunk: false);
                Assert.Equal(firstLength, offset);
            }

            Assert.Equal(firstLength, new FileInfo(destination).Length);
            await using (MemoryStream second = new(
                source,
                firstLength,
                source.Length - firstLength,
                writable: false))
            {
                long completed = await ResumableTransferFile.WriteChunkAsync(
                    second,
                    destination,
                    firstLength,
                    source.Length,
                    source.Length - firstLength,
                    isLastChunk: true);
                Assert.Equal(source.Length, completed);
            }

            Assert.Equal(source, await File.ReadAllBytesAsync(destination));
        }
    }

    [Fact]
    public async Task ResumableWriter_RejectsOffsetMismatchWithoutAppending()
    {
        string destination = Path.Combine(TestEnvironment.CreateDirectory("offset-mismatch"), "file.bin");
        await File.WriteAllBytesAsync(destination, new byte[100]);
        byte[] next = RandomNumberGenerator.GetBytes(50);
        await using MemoryStream body = new(next, writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(() => ResumableTransferFile.WriteChunkAsync(
            body,
            destination,
            offset: 90,
            totalBytes: 140,
            bodySize: next.Length,
            isLastChunk: true));
        Assert.Equal(100, new FileInfo(destination).Length);
    }

    [Fact]
    public async Task ResumableWriter_ResetAtZeroRemovesStaleTail()
    {
        string destination = Path.Combine(TestEnvironment.CreateDirectory("offset-reset"), "file.bin");
        await File.WriteAllBytesAsync(destination, RandomNumberGenerator.GetBytes(4096));
        byte[] replacement = "replacement"u8.ToArray();
        await using MemoryStream body = new(replacement, writable: false);

        await ResumableTransferFile.WriteChunkAsync(
            body,
            destination,
            0,
            replacement.Length,
            replacement.Length,
            isLastChunk: true);

        Assert.Equal(replacement, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task ResumableWriter_RejectsZeroLengthNonFinalChunk()
    {
        string destination = Path.Combine(
            TestEnvironment.CreateDirectory("zero-non-final"),
            "file.bin");
        await using MemoryStream empty = new(Array.Empty<byte>(), writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(() => ResumableTransferFile.WriteChunkAsync(
            empty,
            destination,
            offset: 0,
            totalBytes: 10,
            bodySize: 0,
            isLastChunk: false));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ResumableWriter_CreatesAValidZeroByteFile()
    {
        string destination = Path.Combine(
            TestEnvironment.CreateDirectory("zero-byte-final"),
            "empty.bin");
        await using MemoryStream empty = new(Array.Empty<byte>(), writable: false);

        long completed = await ResumableTransferFile.WriteChunkAsync(
            empty,
            destination,
            offset: 0,
            totalBytes: 0,
            bodySize: 0,
            isLastChunk: true);

        Assert.Equal(0, completed);
        Assert.True(File.Exists(destination));
        Assert.Equal(0, new FileInfo(destination).Length);
    }
}
