using AgentShared;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;

namespace AgentService
{
    internal sealed class BackupFileScanner
    {
        public BackupScanResult Scan(BackupConfiguration config)
        {
            BackupScanResult result = new BackupScanResult();
            HashSet<string> visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> excludedFolders = BackupExclusionDefaults.FolderNames
                .Concat(config.ExcludedFolders ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => IsSimpleFolderName(value) ? value.Trim() : NormalizePath(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> excludedPatterns = BackupExclusionDefaults.FilePatterns
                .Concat(config.ExcludedPatterns ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string sourceValue in config.SourcePaths ?? Enumerable.Empty<string>())
            {
                string source = NormalizePath(sourceValue);
                if (string.IsNullOrWhiteSpace(source))
                {
                    continue;
                }

                if (IsSystemDriveRoot(source))
                {
                    result.AddError("Bỏ qua toàn bộ ổ C: theo cấu hình an toàn.");
                    continue;
                }

                try
                {
                    if (File.Exists(source))
                    {
                        AddFile(source, excludedPatterns, visitedFiles, result);
                    }
                    else if (Directory.Exists(source))
                    {
                        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                        {
                            result.AddError($"Bỏ qua nguồn là junction/symbolic link: {source}");
                            continue;
                        }
                        ScanDirectory(source, excludedFolders, excludedPatterns, visitedFiles, result);
                    }
                    else
                    {
                        result.AddError($"Nguồn backup không tồn tại: {source}");
                    }
                }
                catch (Exception ex)
                {
                    result.AddError($"{source}: {ex.Message}");
                }
            }

            return result;
        }

        private static void ScanDirectory(
            string root,
            IReadOnlyCollection<string> excludedFolders,
            IReadOnlyCollection<string> excludedPatterns,
            HashSet<string> visitedFiles,
            BackupScanResult result)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (IsExcludedFolder(current, excludedFolders))
                {
                    continue;
                }

                try
                {
                    foreach (string file in Directory.EnumerateFiles(current))
                    {
                        AddFile(file, excludedPatterns, visitedFiles, result);
                    }
                }
                catch (Exception ex)
                {
                    result.AddError($"Không đọc được file trong {current}: {ex.Message}");
                }

                try
                {
                    foreach (string directory in Directory.EnumerateDirectories(current))
                    {
                        try
                        {
                            FileAttributes attributes = File.GetAttributes(directory);
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.AddError($"Không đọc được thuộc tính {directory}: {ex.Message}");
                            continue;
                        }

                        if (!IsExcludedFolder(directory, excludedFolders))
                        {
                            pending.Push(directory);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.AddError($"Không đọc được thư mục con trong {current}: {ex.Message}");
                }
            }
        }

        private static void AddFile(
            string filePath,
            IReadOnlyCollection<string> excludedPatterns,
            HashSet<string> visitedFiles,
            BackupScanResult result)
        {
            string fullPath = NormalizePath(filePath);
            if (!visitedFiles.Add(fullPath) || IsExcludedFile(fullPath, excludedPatterns))
            {
                return;
            }

            try
            {
                FileInfo info = new FileInfo(fullPath);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return;
                }
                result.Files[fullPath] = new BackupFileSnapshot
                {
                    FullPath = fullPath,
                    RelativeStoragePath = ToStorageRelativePath(fullPath),
                    Size = info.Length,
                    LastWriteTimeUtc = info.LastWriteTimeUtc
                };
            }
            catch (Exception ex)
            {
                result.AddError($"Không đọc được file {fullPath}: {ex.Message}");
            }
        }

        private static bool IsExcludedFolder(string path, IEnumerable<string> excludedFolders)
        {
            string normalized = NormalizePath(path);
            string folderName = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar));
            foreach (string excluded in excludedFolders)
            {
                if (normalized.Equals(excluded, StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(excluded.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    (!excluded.Contains(Path.DirectorySeparatorChar) && folderName.Equals(excluded, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSimpleFolderName(string value)
        {
            string candidate = (value ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(candidate) &&
                   !Path.IsPathRooted(candidate) &&
                   candidate.IndexOf(Path.DirectorySeparatorChar) < 0 &&
                   candidate.IndexOf(Path.AltDirectorySeparatorChar) < 0;
        }

        private static bool IsExcludedFile(string filePath, IEnumerable<string> patterns)
        {
            string fileName = Path.GetFileName(filePath);
            string extension = Path.GetExtension(filePath);
            foreach (string rawPattern in patterns)
            {
                string pattern = (rawPattern ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                if (pattern.StartsWith('.') && !pattern.Contains('*') && !pattern.Contains('?'))
                {
                    if (extension.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    continue;
                }

                if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ToStorageRelativePath(string fullPath)
        {
            string normalized = NormalizePath(fullPath);
            if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return Path.Combine("UNC", normalized.TrimStart('\\'));
            }

            string root = Path.GetPathRoot(normalized) ?? string.Empty;
            string rootName = root.TrimEnd(Path.DirectorySeparatorChar).Replace(":", string.Empty);
            string remainder = normalized.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
            return Path.Combine(string.IsNullOrWhiteSpace(rootName) ? "ROOT" : rootName, remainder);
        }

        private static string NormalizePath(string path)
        {
            string value = (path ?? string.Empty).Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            try
            {
                value = Path.GetFullPath(value);
            }
            catch
            {
            }

            if (value.Length > 3)
            {
                value = value.TrimEnd(Path.DirectorySeparatorChar);
            }
            return value;
        }

        private static bool IsSystemDriveRoot(string path)
        {
            string normalized = NormalizePath(path);
            return normalized.Equals(@"C:\", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("C:", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class BackupScanResult
    {
        private const int MaxDetailedErrors = 1000;
        private int _suppressedErrors;

        public Dictionary<string, BackupFileSnapshot> Files { get; } =
            new Dictionary<string, BackupFileSnapshot>(StringComparer.OrdinalIgnoreCase);
        public List<string> Errors { get; } = new List<string>();

        internal void AddError(string message)
        {
            if (Errors.Count < MaxDetailedErrors)
            {
                Errors.Add(message);
                return;
            }

            _suppressedErrors++;
            string summary = $"Đã lược bớt {_suppressedErrors} lỗi quét bổ sung để manifest không vượt giới hạn truyền.";
            if (Errors.Count == MaxDetailedErrors)
            {
                Errors.Add(summary);
            }
            else
            {
                Errors[MaxDetailedErrors] = summary;
            }
        }
    }

    internal sealed class BackupFileSnapshot
    {
        public string FullPath { get; set; } = string.Empty;
        public string RelativeStoragePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string ContentSha256 { get; set; } = string.Empty;
    }
}
