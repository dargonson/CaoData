namespace AgentShared
{
    /// <summary>
    /// Kiem tra moi duong dan tu peer truoc khi ghep vao thu muc local.
    /// </summary>
    public static class PathSafety
    {
        private static readonly HashSet<string> ReservedWindowsNames = new(
            new[]
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            },
            StringComparer.OrdinalIgnoreCase);

        public static string NormalizeRelativePath(string? relativePath)
        {
            string value = (relativePath ?? string.Empty)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            if (string.IsNullOrWhiteSpace(value) ||
                Path.IsPathRooted(value) ||
                value.StartsWith(Path.DirectorySeparatorChar))
            {
                throw new InvalidDataException("Duong dan tuong doi khong hop le.");
            }

            string[] parts = value.Split(Path.DirectorySeparatorChar);
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (string part in parts)
            {
                string deviceStem = Path.GetFileNameWithoutExtension(part);
                if (string.IsNullOrEmpty(part) ||
                    part is "." or ".." ||
                    part.EndsWith(' ') ||
                    part.EndsWith('.') ||
                    part.IndexOfAny(invalidChars) >= 0 ||
                    ReservedWindowsNames.Contains(deviceStem))
                {
                    throw new InvalidDataException("Duong dan tuong doi khong hop le.");
                }
            }

            return value;
        }

        public static string GetSafeChildPath(string root, string? relativePath)
        {
            string relative = NormalizeRelativePath(relativePath);
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Duong dan vuot ra ngoai thu muc goc.");
            }

            return fullPath;
        }
    }
}
