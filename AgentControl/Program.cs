using AgentShared;

namespace AgentControl
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            using IDisposable sleepBlock = SystemSleepBlocker.PreventSystemSleep(
                "AgentControl đang chạy máy chủ truyền file và backup.");
            Application.Run(new frmToolBackup());
        }
    }
}
