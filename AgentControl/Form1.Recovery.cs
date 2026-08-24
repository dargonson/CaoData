namespace AgentControl
{
    public partial class frmToolBackup
    {
        private void InitializeRecoveryModule()
        {
            btnrecovery.Click += btnrecovery_Click;
        }

        private void btnrecovery_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedAgentId))
            {
                MessageBox.Show(
                    "Fen hãy chọn một Agent trước khi mở khôi phục dữ liệu.",
                    "Khôi phục dữ liệu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using frmRecovery recovery = new frmRecovery(selectedAgentId);
            recovery.ShowDialog(this);
        }
    }
}
