using System;
using System.Collections.Generic;

namespace AgentShared
{
    // ========================================================================
    // BO SUNG MODULE BACKUP: model va packet dung chung giua Control va Agent.
    // Khong thay doi cac model download/upload dang co.
    // ========================================================================
    public static class BackupPacketTypes
    {
        public const string ConfigDeploy = "BACKUP_CONFIG_DEPLOY";
        public const string ConfigAck = "BACKUP_CONFIG_ACK";
        public const string SessionBegin = "BACKUP_SESSION_BEGIN";
        public const string SessionReady = "BACKUP_SESSION_READY";
        public const string SessionComplete = "BACKUP_SESSION_COMPLETE";
        public const string SessionResult = "BACKUP_SESSION_RESULT";
        public const string FirstFileResumeQuery = "BACKUP_FIRST_FILE_RESUME_QUERY";
        public const string FirstFileResumeInfo = "BACKUP_FIRST_FILE_RESUME_INFO";
        public const string FirstFileSkip = "BACKUP_FIRST_FILE_SKIP";
    }

    public sealed class BackupConfiguration
    {
        public bool Enabled { get; set; } = true;
        public string AgentID { get; set; } = string.Empty;
        public string ControlStoragePath { get; set; } = string.Empty;
        public int BackupIntervalDays { get; set; } = 1;
        public int FullBackupPeriodDays { get; set; } = 60;
        public string BackupTime { get; set; } = "00:00";
        public List<string> SourcePaths { get; set; } = new List<string>();
        public List<string> ExcludedFolders { get; set; } = new List<string>();
        public List<string> ExcludedPatterns { get; set; } = new List<string>();
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class BackupConfigAck
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class BackupSessionBegin
    {
        public string AgentID { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public string BackupType { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public bool IsResumableFirst { get; set; }
        public long PlannedFileCount { get; set; }
        public long PlannedTotalBytes { get; set; }
    }

    public sealed class BackupFileChunkHeader
    {
        public string AgentID { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string RelativeStoragePath { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public long Offset { get; set; }
        public int ChunkSize { get; set; }
        public bool IsLastChunk { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
    }

    public sealed class BackupManifest
    {
        public string AgentID { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public string BackupType { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public DateTime CompletedAtUtc { get; set; }
        // BO SUNG MODULE BACKUP: yeu cau Control chot mot Synthetic Full sau phien INC.
        public bool CreateSyntheticFull { get; set; }
        // BO SUNG MODULE BACKUP: FIRST ban dau duoc Control chot tu journal/file state, khong nhan manifest lon.
        public bool IsResumableFirst { get; set; }
        public List<BackupManifestEntry> Created { get; set; } = new List<BackupManifestEntry>();
        public List<BackupManifestEntry> Modified { get; set; } = new List<BackupManifestEntry>();
        public List<BackupManifestEntry> Deleted { get; set; } = new List<BackupManifestEntry>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public sealed class BackupManifestEntry
    {
        public string SourcePath { get; set; } = string.Empty;
        public string RelativeStoragePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
    }

    public sealed class BackupSessionResult
    {
        public string SessionName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    // BO SUNG MODULE BACKUP - FIRST RESUME: hoi offset cua mot file truoc khi tiep tuc upload.
    public sealed class BackupFirstFileResumeQuery
    {
        public string AgentID { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string RelativeStoragePath { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
    }

    public sealed class BackupFirstFileResumeInfo
    {
        public string SessionName { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public bool Completed { get; set; }
        public bool Skipped { get; set; }
        public long Offset { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class BackupFirstFileSkip
    {
        public string AgentID { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string RelativeStoragePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
