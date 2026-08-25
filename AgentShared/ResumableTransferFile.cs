namespace AgentShared
{
    /// <summary>
    /// Ghi một binary chunk theo offset xuống file đích. Dùng chung cho download,
    /// upload và backup để ba luồng có cùng quy tắc resume/durability.
    /// </summary>
    public static class ResumableTransferFile
    {
        public static async Task<long> WriteChunkAsync(
            Stream source,
            string destinationPath,
            long offset,
            long totalBytes,
            int bodySize,
            bool isLastChunk,
            CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("Đường dẫn file đích đang trống.", nameof(destinationPath));
            }
            if (offset < 0 || totalBytes < 0 || bodySize < 0 ||
                offset > totalBytes || bodySize > totalBytes - offset ||
                (!isLastChunk && bodySize == 0) ||
                (isLastChunk && offset + bodySize != totalBytes) ||
                (!isLastChunk && offset + bodySize >= totalBytes))
            {
                throw new InvalidDataException("Offset/kích thước chunk không hợp lệ.");
            }

            string? folder = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            await using FileStream destination = new FileStream(
                destinationPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (offset == 0)
            {
                destination.SetLength(0);
            }
            else if (destination.Length != offset)
            {
                throw new InvalidDataException(
                    $"Offset file không khớp. File={destination.Length}, gói={offset}.");
            }

            destination.Seek(offset, SeekOrigin.Begin);
            await TransferFrameProtocol.CopyExactToAsync(source, destination, bodySize, token);
            if (isLastChunk)
            {
                destination.SetLength(totalBytes);
            }
            await destination.FlushAsync(token);
            destination.Flush(flushToDisk: true);
            return offset + bodySize;
        }
    }
}
