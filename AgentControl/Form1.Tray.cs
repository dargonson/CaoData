namespace AgentControl
{
    public partial class frmToolBackup
    {
        private NotifyIcon? _controlTrayIcon;
        private ContextMenuStrip? _controlTrayMenu;
        private bool _allowControlExit;

        private void InitializeTrayModule()
        {
            _controlTrayMenu = new ContextMenuStrip();
            _controlTrayMenu.Items.Add("Mở AgentControl", null, (_, _) => RestoreFromTray());
            _controlTrayMenu.Items.Add(new ToolStripSeparator());
            _controlTrayMenu.Items.Add("Exit", null, (_, _) => ExitFromTray());

            _controlTrayIcon = new NotifyIcon
            {
                ContextMenuStrip = _controlTrayMenu,
                Icon = Icon ?? SystemIcons.Application,
                Text = "AgentControl - Tool Backup",
                Visible = true
            };
            _controlTrayIcon.DoubleClick += (_, _) => RestoreFromTray();
            FormClosing += frmToolBackup_FormClosingToTray;
            FormClosed += frmToolBackup_FormClosedTrayCleanup;
        }

        private void frmToolBackup_FormClosingToTray(object? sender, FormClosingEventArgs e)
        {
            if (_allowControlExit || e.CloseReason == CloseReason.WindowsShutDown)
            {
                return;
            }

            e.Cancel = true;
            HideToTray();
        }

        private void HideToTray()
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void ExitFromTray()
        {
            _allowControlExit = true;
            Close();
        }

        private void frmToolBackup_FormClosedTrayCleanup(object? sender, FormClosedEventArgs e)
        {
            DisposeControlRuntime();

            if (_controlTrayIcon != null)
            {
                _controlTrayIcon.Visible = false;
                _controlTrayIcon.Dispose();
                _controlTrayIcon = null;
            }

            _controlTrayMenu?.Dispose();
            _controlTrayMenu = null;
        }
    }
}
