namespace FileStrider.Core.Models;

public enum CoverageReason { Filtered, Inaccessible, Offline, DepthLimit, Duplicate, IdentityUnavailable }

public sealed record CoverageIssue(string FullPath, CoverageReason Reason, bool IsDirectory);

public sealed class FolderContents
{
    public List<FileItem> Files { get; init; } = [];
    public List<CoverageIssue> Issues { get; init; } = [];
    public int OmittedIssues { get; init; }
}

public static class ScanPresets
{
    public static ScanOptions Full(ScanOptions options) => options with
    {
        IncludeHidden = true, FollowSymlinks = false, FoldersOnly = false,
        MaxDepth = null, MinFileSize = 0, ExcludePatterns = [], ExcludeDirectories = []
    };

    public static ScanOptions SourceFiles(ScanOptions options) => Full(options) with
    {
        IncludeHidden = false,
        ExcludePatterns = ["*.tmp", "*.temp", "*.log"],
        ExcludeDirectories = ["node_modules", ".git", "Library/Caches", ".vs", "bin", "obj", ".venv", "target", "dist"]
    };
}

public sealed class SavedScan
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;
    public ScanResults Results { get; init; } = new();
}

public sealed record SavedScanInfo(string Id, DateTimeOffset ScannedAt, string RootPath, long Bytes)
{
    public string DisplayName => $"{ScannedAt.ToLocalTime():g} · {ByteSize.Format(Bytes)} · {RootPath}";
}

public sealed record FolderChange(string FullPath, long Before, long After)
{
    public long Delta => After - Before;
    public string DisplayDelta => (Delta > 0 ? "+" : Delta < 0 ? "−" : "") + ByteSize.Format(Math.Abs(Delta));
}

public sealed record StorageInsight(string FullPath, string Kind, long Size, DateTime LastModified, bool IsOld)
{
    public string DisplaySize => ByteSize.Format(Size);
}
