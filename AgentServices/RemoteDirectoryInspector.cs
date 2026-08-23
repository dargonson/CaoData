namespace AgentService
{
    internal static class RemoteDirectoryInspector
    {
        public static bool HasVisibleSubdirectories(string path)
        {
            try
            {
                foreach (string childPath in Directory.EnumerateDirectories(path))
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(childPath);
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Mot thu muc con loi quyen khong duoc lam hong ket qua liet ke thu muc cha.
                    }
                }
            }
            catch
            {
                // Khong doc duoc thi coi nhu chua xac nhan co thu muc con hien thi duoc.
            }

            return false;
        }
    }
}
