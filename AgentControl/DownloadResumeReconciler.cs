namespace AgentControl
{
    /// <summary>
    /// Đồng bộ mốc resume trong DB với chiều dài file thật trước khi yêu cầu Agent gửi tiếp.
    /// </summary>
    internal static class DownloadResumeReconciler
    {
        internal static async Task<long> ReconcileAsync(
            string? localPath,
            long databaseOffset,
            Func<long, Task>? persistCorrectedOffset = null)
        {
            long safeOffset = Math.Max(0, databaseOffset);
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                safeOffset = 0;
            }
            else
            {
                using FileStream file = new FileStream(
                    localPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    4096,
                    FileOptions.None);
                if (file.Length < safeOffset)
                {
                    safeOffset = file.Length;
                }
                else if (file.Length > safeOffset)
                {
                    file.SetLength(safeOffset);
                    file.Flush(flushToDisk: true);
                }
            }

            if (safeOffset != databaseOffset && persistCorrectedOffset != null)
            {
                await persistCorrectedOffset(safeOffset);
            }
            return safeOffset;
        }
    }
}
