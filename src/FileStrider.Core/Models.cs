using System.ComponentModel;

namespace FileStrider.Core.Models;

/// <summary>
/// Represents a file discovered during the file system scan with its metadata.
/// </summary>
public class FileItem
{
    /// <summary>
    /// Gets the name of the file including extension.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Gets the full path to the file on the file system.
    /// </summary>
    public string FullPath { get; init; } = string.Empty;
    
    /// <summary>
    /// Gets the size of the file in bytes.
    /// </summary>
    public long Size { get; init; }
    
    /// <summary>
    /// Gets the file extension/type (e.g., ".txt", ".jpg").
    /// </summary>
    public string Type { get; init; } = string.Empty;
    
    /// <summary>
    /// Gets the date and time when the file was last modified.
    /// </summary>
    public DateTime LastModified { get; init; }
}

/// <summary>
/// Represents a folder/directory discovered during the file system scan with aggregated information about its contents.
/// </summary>
public class FolderItem
{
    /// <summary>
    /// Gets the name of the folder.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Gets the full path to the folder on the file system.
    /// </summary>
    public string FullPath { get; init; } = string.Empty;
    
    /// <summary>
    /// Gets the total size of all files within this folder and its subfolders, in bytes.
    /// </summary>
    public long RecursiveSize { get; init; }
    
    /// <summary>
    /// Gets the total number of files and subfolders within this folder.
    /// </summary>
    public int ItemCount { get; init; }
    
    /// <summary>
    /// Gets the date and time when the folder was last modified.
    /// </summary>
    public DateTime LastModified { get; init; }
}

/// <summary>
/// Configuration options for controlling the behavior of the file system scan.
/// </summary>
public record ScanOptions
{
    /// <summary>
    /// Gets the root directory path to start the scan from.
    /// </summary>
    public string RootPath { get; init; } = string.Empty;
    
    /// <summary>
    /// Gets the maximum number of top files and folders to track during the scan.
    /// </summary>
    public int TopN { get; init; } = 20;
    
    /// <summary>
    /// Gets a value indicating whether to include hidden files and folders in the scan.
    /// </summary>
    public bool IncludeHidden { get; init; } = false;
    
    /// <summary>
    /// Gets a value indicating whether to follow symbolic links during the scan.
    /// </summary>
    public bool FollowSymlinks { get; init; } = false;
    
    /// <summary>
    /// Gets a value indicating whether to scan only folders and skip individual files for faster scanning.
    /// </summary>
    public bool FoldersOnly { get; init; } = false;
    
    /// <summary>
    /// Gets the maximum depth to recurse into subdirectories. Null means unlimited depth.
    /// </summary>
    public int? MaxDepth { get; init; }
    
    /// <summary>
    /// Gets the minimum file size in bytes to include in the scan results.
    /// </summary>
    public long MinFileSize { get; init; } = 0;
    
    /// <summary>
    /// Gets the maximum number of concurrent threads to use for scanning.
    /// </summary>
    public int ConcurrencyLimit { get; init; } = Math.Min(8, Environment.ProcessorCount);
    
    /// <summary>
    /// Gets the set of regex patterns for directories and files to exclude from the scan.
    /// </summary>
    public HashSet<string> ExcludePatterns { get; init; } = new();
    
    /// <summary>
    /// Gets the set of directory names to exclude from the scan.
    /// </summary>
    public HashSet<string> ExcludeDirectories { get; init; } = new() { "node_modules", ".git", "Library/Caches" };
}

/// <summary>
/// Provides real-time progress information about the ongoing file system scan.
/// </summary>
public class ScanProgress
{
    /// <summary>
    /// Gets or sets the total number of files that have been scanned so far.
    /// </summary>
    public int FilesScanned { get; set; }
    
    /// <summary>
    /// Gets or sets the total number of folders that have been scanned so far.
    /// </summary>
    public int FoldersScanned { get; set; }
    
    /// <summary>
    /// Gets or sets the total number of bytes processed so far.
    /// </summary>
    public long BytesProcessed { get; set; }
    
    /// <summary>
    /// Gets or sets the time elapsed since the scan started.
    /// </summary>
    public TimeSpan Elapsed { get; set; }
    
    /// <summary>
    /// Gets or sets the estimated time remaining to complete the scan (if available).
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    
    /// <summary>
    /// Gets or sets the path of the current file or folder being processed.
    /// </summary>
    public string CurrentPath { get; set; } = string.Empty;
}

/// <summary>
/// Contains the results of a completed or cancelled file system scan operation.
/// </summary>
public class ScanResults
{
    /// <summary>
    /// Gets the list of top largest files found during the scan.
    /// </summary>
    public List<FileItem> TopFiles { get; init; } = new();
    
    /// <summary>
    /// Gets the list of top largest folders found during the scan.
    /// </summary>
    public List<FolderItem> TopFolders { get; init; } = new();
    
    /// <summary>
    /// Gets or sets the final progress information from the scan.
    /// </summary>
    public ScanProgress Progress { get; set; } = new();
    
    /// <summary>
    /// Gets or sets a value indicating whether the scan completed successfully.
    /// </summary>
    public bool IsCompleted { get; set; }
    
    /// <summary>
    /// Gets or sets a value indicating whether the scan was cancelled before completion.
    /// </summary>
    public bool WasCancelled { get; set; }
    
    /// <summary>
    /// Gets or sets the error message if the scan failed due to an exception.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
