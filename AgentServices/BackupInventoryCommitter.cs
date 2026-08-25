namespace AgentService
{
    /// <summary>
    /// BO SUNG MODULE BACKUP: tao state Agent sau mot phien da duoc Control chot.
    /// Tach rieng de quy tac bao toan inventory khi scan loi co the kiem thu doc lap.
    /// </summary>
    internal static class BackupInventoryCommitter
    {
        internal static Dictionary<string, BackupFileSnapshot> Build(
            IReadOnlyDictionary<string, BackupFileSnapshot> scannedFiles,
            IReadOnlyDictionary<string, BackupFileSnapshot> previousFiles,
            IEnumerable<string> failedUploadPaths,
            bool scanHadErrors)
        {
            var committed = new Dictionary<string, BackupFileSnapshot>(
                scannedFiles,
                StringComparer.OrdinalIgnoreCase);

            // Neu mot nhanh thu muc bi Access Denied/loi I/O, file cu vang mat trong ket
            // qua scan chua the duoc ket luan la da xoa. Giu lai de lan scan sach tiep theo
            // van co the phat sinh Deleted dung cho Control.
            if (scanHadErrors)
            {
                foreach ((string path, BackupFileSnapshot previous) in previousFiles)
                {
                    committed.TryAdd(path, previous);
                }
            }

            // File upload that bai khong duoc chot metadata moi vao state Agent. Neu la
            // file moi thi bo khoi state; neu la file sua thi giu phien ban cu de thu lai.
            foreach (string path in failedUploadPaths)
            {
                if (previousFiles.TryGetValue(path, out BackupFileSnapshot? previous))
                {
                    committed[path] = previous;
                }
                else
                {
                    committed.Remove(path);
                }
            }

            return committed;
        }
    }
}
