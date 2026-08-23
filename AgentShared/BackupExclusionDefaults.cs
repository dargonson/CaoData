using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AgentShared
{
    /// <summary>
    /// BO SUNG MODULE BACKUP - TEMPLATE LOAI TRU BAT BUOC:
    /// Cac ten he thong nay bi loai tru toan cuc, khong phu thuoc o dia hay vi tri.
    /// </summary>
    public static class BackupExclusionDefaults
    {
        public static IReadOnlyList<string> FolderNames { get; } =
            new ReadOnlyCollection<string>(new[]
            {
                "$Recycle.Bin",
                "Program Files",
                "Program Files (x86)",
                "Recovery",
                "System Volume Information",
                "Windows"
            });

        public static IReadOnlyList<string> FilePatterns { get; } =
            new ReadOnlyCollection<string>(new[]
            {
                ".tmp",
                ".temp",
                "~*",
                "~$*",
                "hiberfil.sys",
                "pagefile.sys",
                "swapfile.sys"
            });

        public static void EnsureIncluded(BackupConfiguration config)
        {
            config.ExcludedFolders ??= new List<string>();
            config.ExcludedPatterns ??= new List<string>();
            AddMissing(config.ExcludedFolders, FolderNames);
            AddMissing(config.ExcludedPatterns, FilePatterns);
        }

        private static void AddMissing(ICollection<string> destination, IEnumerable<string> defaults)
        {
            foreach (string value in defaults)
            {
                bool exists = false;
                foreach (string current in destination)
                {
                    if (string.Equals(current?.Trim(), value, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    destination.Add(value);
                }
            }
        }
    }
}
