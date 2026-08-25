namespace AgentUpdater
{
    /// <summary>
    /// Giao dich thay AgentServices.exe. Tach khoi sc.exe de co the kiem thu day du
    /// cac diem loi copy/start/rollback ma khong cham vao Windows Service that.
    /// </summary>
    internal static class AgentUpdateWorkflow
    {
        internal static async Task ExecuteAsync(
            string currentExe,
            string newExe,
            string backupPath,
            AgentUpdateWorkflowOperations operations)
        {
            bool backupCreated = false;
            bool replacementAttempted = false;
            try
            {
                await operations.ReportStatusAsync("StoppingService", "Chuẩn bị tắt AgentServices.");
                await operations.StopServiceAsync();
                await operations.WaitUntilUnlockedAsync();
                await operations.ReportStatusAsync("ServiceStopped", "Đã tắt thành công.");

                await operations.CopyFileAsync(currentExe, backupPath, true);
                backupCreated = true;
                await operations.LogAsync("Đã backup: " + backupPath);

                await operations.ReportStatusAsync("UpdatingFile", "Chuẩn bị update file mới.");
                // Dat co truoc copy: File.Copy co the cat ngan file dich roi moi nem loi.
                replacementAttempted = true;
                await operations.CopyFileAsync(newExe, currentExe, true);
                await operations.LogAsync("Đã copy file mới vào: " + currentExe);
                await operations.ReportStatusAsync("FileUpdated", "Đã update file mới.");

                await operations.ReportStatusAsync("StartingService", "Khởi động lại services......");
                // Marker phai xuat hien truoc service moi de khong bo lo lan reconnect rat nhanh.
                await operations.WriteCompletionMarkerAsync();
                await operations.StartAndVerifyServiceAsync();
            }
            catch (Exception updateError)
            {
                await operations.DeleteCompletionMarkerAsync();
                try
                {
                    await operations.ReportStatusAsync(
                        "RollingBack",
                        "Bản mới không khởi động được, đang phục hồi bản cũ.");
                    if (replacementAttempted)
                    {
                        await operations.TryStopServiceAsync();
                        await operations.WaitUntilUnlockedAsync();
                        if (!backupCreated || !File.Exists(backupPath))
                        {
                            throw new FileNotFoundException(
                                "Không có file backup để rollback AgentServices.",
                                backupPath);
                        }

                        await operations.CopyFileAsync(backupPath, currentExe, true);
                        await operations.LogAsync("Đã rollback file cũ.");
                    }

                    // Loi co the xay ra ngay luc tao backup, khi EXE cu chua bi thay. Trong
                    // truong hop do van phai khoi dong lai service da stop.
                    await operations.EnsureServiceRunningAsync();
                    await operations.ReportStatusAsync(
                        "RolledBack",
                        "Đã phục hồi và khởi động bản AgentServices cũ.");
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "Update thất bại và rollback không hoàn tất.",
                        updateError,
                        rollbackError);
                }

                throw new InvalidOperationException(
                    "Update không thành công; AgentServices cũ đã được khôi phục và chạy lại.",
                    updateError);
            }
        }
    }

    internal sealed class AgentUpdateWorkflowOperations
    {
        internal required Func<Task> StopServiceAsync { get; init; }
        internal required Func<Task> TryStopServiceAsync { get; init; }
        internal required Func<Task> WaitUntilUnlockedAsync { get; init; }
        internal required Func<Task> StartAndVerifyServiceAsync { get; init; }
        internal required Func<Task> EnsureServiceRunningAsync { get; init; }
        internal required Func<Task> WriteCompletionMarkerAsync { get; init; }
        internal required Func<Task> DeleteCompletionMarkerAsync { get; init; }
        internal required Func<string, string, bool, Task> CopyFileAsync { get; init; }
        internal required Func<string, string, Task> ReportStatusAsync { get; init; }
        internal required Func<string, Task> LogAsync { get; init; }
    }
}
