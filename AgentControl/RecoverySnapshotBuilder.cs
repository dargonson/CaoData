using AgentShared;
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentControl
{
    /// <summary>
    /// Quet cac folder FIRST/INC va replay manifest den ngay user chon.
    /// Manifest duoc doc streaming de khong nap danh sach lon vao RAM.
    /// </summary>
    internal sealed class RecoverySnapshotBuilder
    {
        private readonly RecoverySnapshotRepository _repository;

        public RecoverySnapshotBuilder(RecoverySnapshotRepository repository)
        {
            _repository = repository;
        }

        public Task<List<RecoveryPointDate>> DiscoverDatesAsync(
            string storageRoot, string agentId, CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                List<RecoveryBackupSession> sessions = DiscoverSessions(storageRoot, agentId, token);
                return sessions
                    .GroupBy(session => session.Date.Date)
                    .OrderByDescending(group => group.Key)
                    .Select(group => new RecoveryPointDate(group.Key, group.Count()))
                    .ToList();
            }, token);
        }

        public async Task<RecoveryBuildResult> BuildAsync(
            string storageRoot,
            string agentId,
            DateTime selectedDate,
            CancellationToken token = default)
        {
            List<RecoveryBackupSession> allSessions = await Task.Run(
                () => DiscoverSessions(storageRoot, agentId, token), token);
            DateTime targetDate = selectedDate.Date;
            RecoveryBackupSession? baseFull = allSessions
                .Where(session => session.Type == RecoverySessionType.First && session.Date.Date <= targetDate)
                .OrderBy(session => session.CompletedAtUtc)
                .LastOrDefault();
            if (baseFull == null)
            {
                throw new InvalidDataException("Không tìm thấy FIRST hợp lệ trước ngày đã chọn.");
            }

            List<RecoveryBackupSession> replaySessions = new List<RecoveryBackupSession> { baseFull };
            replaySessions.AddRange(allSessions
                .Where(session => session.Type == RecoverySessionType.Incremental &&
                                  session.Date.Date <= targetDate &&
                                  session.CompletedAtUtc > baseFull.CompletedAtUtc)
                .OrderBy(session => session.CompletedAtUtc));

            string signature = CreateSignature(replaySessions);
            if (!await _repository.IsCurrentAsync(agentId, targetDate, signature))
            {
                await _repository.RebuildAsync(
                    agentId,
                    targetDate,
                    signature,
                    writer =>
                    {
                        foreach (RecoveryBackupSession session in replaySessions)
                        {
                            token.ThrowIfCancellationRequested();
                            if (session.Type == RecoverySessionType.First)
                            {
                                writer.ClearFiles();
                            }

                            BackupManifestStreamReader.ReadEntries(
                                session.ManifestPath,
                                (section, entry) =>
                                {
                                    if (section == ManifestEntrySection.Deleted)
                                    {
                                        writer.Delete(entry);
                                    }
                                    else if (section == ManifestEntrySection.Created ||
                                             section == ManifestEntrySection.Modified)
                                    {
                                        writer.Upsert(entry, session.SessionRoot);
                                    }
                                },
                                token);
                        }
                    },
                    token);
            }

            return new RecoveryBuildResult
            {
                SelectedDate = targetDate,
                BaseFullName = baseFull.Name,
                AppliedIncrementalCount = replaySessions.Count - 1
            };
        }

        private static List<RecoveryBackupSession> DiscoverSessions(
            string storageRoot, string agentId, CancellationToken token)
        {
            string root = Path.GetFullPath(storageRoot);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("Thư mục lưu backup trên Control không tồn tại: " + root);
            }

            string safeAgent = SanitizeName(agentId);
            string firstPrefix = "FIRST-" + safeAgent + "-";
            string incPrefix = "INC-" + safeAgent + "-";
            List<RecoveryBackupSession> sessions = new List<RecoveryBackupSession>();
            foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileName(directory);
                RecoverySessionType? type = name.StartsWith(firstPrefix, StringComparison.OrdinalIgnoreCase)
                    ? RecoverySessionType.First
                    : name.StartsWith(incPrefix, StringComparison.OrdinalIgnoreCase)
                        ? RecoverySessionType.Incremental
                        : null;
                if (type == null || name.Length < 10)
                {
                    continue;
                }

                string dateText = name.Substring(name.Length - 10);
                if (!DateTime.TryParseExact(
                        dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime date))
                {
                    continue;
                }

                string manifestPath = Path.Combine(directory, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                BackupSessionMetadata metadata = BackupSessionMetadataStore.ReadVerified(
                    directory,
                    manifestPath,
                    token);
                string expectedType = type == RecoverySessionType.First ? "FIRST" : "INC";
                if (!metadata.AgentID.Equals(agentId, StringComparison.OrdinalIgnoreCase) ||
                    !metadata.SessionName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    !metadata.BackupType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Metadata không khớp tên/Agent của thư mục backup: {name}");
                }

                sessions.Add(new RecoveryBackupSession
                {
                    Name = name,
                    Type = type.Value,
                    Date = date,
                    SessionRoot = Path.GetFullPath(directory),
                    ManifestPath = manifestPath,
                    CompletedAtUtc = metadata.CompletedAtUtc,
                    ManifestSha256 = metadata.ManifestSha256
                });
            }
            return sessions;
        }

        private static string CreateSignature(IEnumerable<RecoveryBackupSession> sessions)
        {
            StringBuilder value = new StringBuilder();
            foreach (RecoveryBackupSession session in sessions)
            {
                value.Append(session.Name).Append('|')
                    .Append(session.CompletedAtUtc.Ticks).Append('|')
                    .Append(session.ManifestSha256).AppendLine();
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
        }

        private static string SanitizeName(string value)
        {
            string result = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '_');
            }
            return result;
        }
    }

    internal static class BackupManifestStreamReader
    {
        public static void ReadEntries(
            string manifestPath,
            Action<ManifestEntrySection, BackupManifestEntry> onEntry,
            CancellationToken token)
        {
            const int initialBufferSize = 128 * 1024;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(initialBufferSize);
            try
            {
                using FileStream stream = new FileStream(
                    manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    initialBufferSize, FileOptions.SequentialScan);
                JsonReaderState state = new JsonReaderState(new JsonReaderOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                int preserved = 0;
                ManifestEntrySection section = ManifestEntrySection.None;
                string propertyName = string.Empty;
                BackupManifestEntry? entry = null;

                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (preserved == buffer.Length)
                    {
                        byte[] larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        Buffer.BlockCopy(buffer, 0, larger, 0, preserved);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = larger;
                    }

                    int read = stream.Read(buffer, preserved, buffer.Length - preserved);
                    int total = preserved + read;
                    bool isFinal = read == 0;
                    Utf8JsonReader reader = new Utf8JsonReader(
                        new ReadOnlySpan<byte>(buffer, 0, total), isFinal, state);

                    while (reader.Read())
                    {
                        token.ThrowIfCancellationRequested();
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            propertyName = reader.GetString() ?? string.Empty;
                            continue;
                        }

                        if (reader.TokenType == JsonTokenType.StartArray && reader.CurrentDepth == 1)
                        {
                            section = propertyName switch
                            {
                                "Created" => ManifestEntrySection.Created,
                                "Modified" => ManifestEntrySection.Modified,
                                "Deleted" => ManifestEntrySection.Deleted,
                                _ => ManifestEntrySection.None
                            };
                            continue;
                        }

                        if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == 1)
                        {
                            section = ManifestEntrySection.None;
                            continue;
                        }

                        if (section != ManifestEntrySection.None &&
                            reader.TokenType == JsonTokenType.StartObject &&
                            reader.CurrentDepth == 2)
                        {
                            entry = new BackupManifestEntry();
                            continue;
                        }

                        if (entry != null && reader.CurrentDepth == 3)
                        {
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                string text = reader.GetString() ?? string.Empty;
                                switch (propertyName)
                                {
                                    case "SourcePath": entry.SourcePath = text; break;
                                    case "RelativeStoragePath": entry.RelativeStoragePath = text; break;
                                    case "ContentSha256": entry.ContentSha256 = text; break;
                                    case "LastWriteTimeUtc":
                                        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                                        {
                                            entry.LastWriteTimeUtc = parsed;
                                        }
                                        break;
                                }
                            }
                            else if (reader.TokenType == JsonTokenType.Number && propertyName == "Size")
                            {
                                entry.Size = reader.GetInt64();
                            }
                        }

                        if (entry != null &&
                            reader.TokenType == JsonTokenType.EndObject &&
                            reader.CurrentDepth == 2)
                        {
                            if (!string.IsNullOrWhiteSpace(entry.SourcePath))
                            {
                                onEntry(section, entry);
                            }
                            entry = null;
                        }
                    }

                    int consumed = checked((int)reader.BytesConsumed);
                    preserved = total - consumed;
                    if (preserved > 0)
                    {
                        Buffer.BlockCopy(buffer, consumed, buffer, 0, preserved);
                    }
                    state = reader.CurrentState;

                    if (isFinal)
                    {
                        if (preserved != 0)
                        {
                            throw new JsonException("Manifest kết thúc khi token JSON chưa hoàn chỉnh.");
                        }
                        break;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static BackupManifestMetadata ReadMetadata(
            string manifestPath,
            CancellationToken token = default)
        {
            const int initialBufferSize = 128 * 1024;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(initialBufferSize);
            try
            {
                using FileStream stream = new FileStream(
                    manifestPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    initialBufferSize,
                    FileOptions.SequentialScan);
                JsonReaderState state = new JsonReaderState(new JsonReaderOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                BackupManifestMetadata metadata = new BackupManifestMetadata();
                int preserved = 0;
                string rootProperty = string.Empty;

                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (preserved == buffer.Length)
                    {
                        byte[] larger = ArrayPool<byte>.Shared.Rent(checked(buffer.Length * 2));
                        Buffer.BlockCopy(buffer, 0, larger, 0, preserved);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = larger;
                    }

                    int read = stream.Read(buffer, preserved, buffer.Length - preserved);
                    int total = preserved + read;
                    bool isFinal = read == 0;
                    Utf8JsonReader reader = new Utf8JsonReader(
                        new ReadOnlySpan<byte>(buffer, 0, total),
                        isFinal,
                        state);

                    while (reader.Read())
                    {
                        token.ThrowIfCancellationRequested();
                        if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
                        {
                            rootProperty = reader.GetString() ?? string.Empty;
                            continue;
                        }

                        if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.String)
                        {
                            continue;
                        }

                        string value = reader.GetString() ?? string.Empty;
                        switch (rootProperty)
                        {
                            case "AgentID": metadata.AgentID = value; break;
                            case "SessionName": metadata.SessionName = value; break;
                            case "BackupType": metadata.BackupType = value; break;
                            case "StartedAtUtc":
                                if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime started))
                                {
                                    metadata.StartedAtUtc = started;
                                }
                                break;
                            case "CompletedAtUtc":
                                if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime completed))
                                {
                                    metadata.CompletedAtUtc = completed;
                                }
                                break;
                        }
                    }

                    int consumed = checked((int)reader.BytesConsumed);
                    preserved = total - consumed;
                    if (preserved > 0)
                    {
                        Buffer.BlockCopy(buffer, consumed, buffer, 0, preserved);
                    }
                    state = reader.CurrentState;
                    if (isFinal)
                    {
                        if (preserved != 0)
                        {
                            throw new JsonException("Manifest kết thúc khi metadata JSON chưa hoàn chỉnh.");
                        }
                        break;
                    }
                }

                return metadata;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    internal enum ManifestEntrySection
    {
        None,
        Created,
        Modified,
        Deleted
    }

    internal sealed class BackupManifestMetadata
    {
        public string AgentID { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public string BackupType { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public DateTime CompletedAtUtc { get; set; }
    }

    internal enum RecoverySessionType
    {
        First,
        Incremental
    }

    internal sealed class RecoveryBackupSession
    {
        public string Name { get; set; } = string.Empty;
        public RecoverySessionType Type { get; set; }
        public DateTime Date { get; set; }
        public string SessionRoot { get; set; } = string.Empty;
        public string ManifestPath { get; set; } = string.Empty;
        public DateTime CompletedAtUtc { get; set; }
        public string ManifestSha256 { get; set; } = string.Empty;
    }

    internal sealed class RecoveryPointDate
    {
        public DateTime Date { get; }
        public int SessionCount { get; }

        public RecoveryPointDate(DateTime date, int sessionCount)
        {
            Date = date.Date;
            SessionCount = sessionCount;
        }

        public override string ToString() => Date.ToString("yyyy-MM-dd");
    }

    internal sealed class RecoveryBuildResult
    {
        public DateTime SelectedDate { get; set; }
        public string BaseFullName { get; set; } = string.Empty;
        public int AppliedIncrementalCount { get; set; }
    }
}
