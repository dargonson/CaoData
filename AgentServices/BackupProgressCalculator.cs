namespace AgentService
{
    internal static class BackupProgressCalculator
    {
        public static int CalculatePercentage(
            long plannedFileCount,
            long processedFileCount,
            long plannedTotalBytes,
            long processedBytes)
        {
            if (plannedTotalBytes > 0)
            {
                decimal ratio = (decimal)Math.Clamp(processedBytes, 0, plannedTotalBytes) * 100m /
                                plannedTotalBytes;
                return (int)Math.Clamp(decimal.Floor(ratio), 0m, 100m);
            }
            if (plannedFileCount > 0)
            {
                decimal ratio = (decimal)Math.Clamp(processedFileCount, 0, plannedFileCount) * 100m /
                                plannedFileCount;
                return (int)Math.Clamp(decimal.Floor(ratio), 0m, 100m);
            }
            return 100;
        }
    }
}
