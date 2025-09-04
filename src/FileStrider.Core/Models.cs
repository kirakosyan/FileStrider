using System.ComponentModel;

namespace FileStrider.Core.Models;

public class FileItem
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long Size { get; init; }
    public string Type { get; init; } = string.Empty;
    public DateTime LastModified { get; init; }
}

public class FolderItem
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long RecursiveSize { get; init; }
    public int ItemCount { get; init; }
    public DateTime LastModified { get; init; }
}

public record ScanOptions
{
    public string RootPath { get; init; } = string.Empty;
    public int TopN { get; init; } = 20;
    public bool IncludeHidden { get; init; } = false;
    public bool FollowSymlinks { get; init; } = false;
    public int? MaxDepth { get; init; }
    public long MinFileSize { get; init; } = 0;
    public int ConcurrencyLimit { get; init; } = Math.Min(8, Environment.ProcessorCount);
    public HashSet<string> ExcludePatterns { get; init; } = new();
    public HashSet<string> ExcludeDirectories { get; init; } = new() { "node_modules", ".git", "Library/Caches" };
}

public class ScanProgress
{
    public int FilesScanned { get; set; }
    public int FoldersScanned { get; set; }
    public long BytesProcessed { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public string CurrentPath { get; set; } = string.Empty;
}

public class ScanResults
{
    public List<FileItem> TopFiles { get; init; } = new();
    public List<FolderItem> TopFolders { get; init; } = new();
    public ScanProgress Progress { get; set; } = new();
    public bool IsCompleted { get; set; }
    public bool WasCancelled { get; set; }
    public string? ErrorMessage { get; set; }
}
