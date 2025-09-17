using System.Collections.Concurrent;
using System.Threading.Channels;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;

namespace FileStrider.Scanner;

/// <summary>
/// High-performance, concurrent file system scanner that uses a producer-consumer pattern 
/// to efficiently discover and analyze files and folders for size analysis.
/// </summary>
public class FileSystemScanner : IFileSystemScanner
{
    private readonly IFileTypeAnalyzer _fileTypeAnalyzer;

    /// <summary>
    /// Initializes a new instance of the FileSystemScanner with the specified dependencies.
    /// </summary>
    /// <param name="fileTypeAnalyzer">The service for analyzing file types.</param>
    public FileSystemScanner(IFileTypeAnalyzer fileTypeAnalyzer)
    {
        _fileTypeAnalyzer = fileTypeAnalyzer;
    }
    /// <summary>
    /// Scans the file system starting from the specified root path and returns the largest files and folders.
    /// Uses a producer-consumer pattern with bounded channels for memory-efficient, concurrent processing.
    /// </summary>
    /// <param name="options">The configuration options for the scan operation.</param>
    /// <param name="progress">Optional progress reporter to receive real-time updates during the scan.</param>
    /// <param name="cancellationToken">Token to request cancellation of the scan operation.</param>
    /// <returns>A task that represents the asynchronous scan operation, containing the scan results.</returns>
    public async Task<ScanResults> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new ScanResults();
        var scanProgress = new ScanProgress();
        var startTime = DateTime.UtcNow;

        var filesTracker = new TopItemsTracker<FileItem>(options.TopN, (a, b) => b.Size.CompareTo(a.Size));
        var foldersTracker = new TopItemsTracker<FolderItem>(options.TopN, (a, b) => b.RecursiveSize.CompareTo(a.RecursiveSize));
        var folderSizes = new ConcurrentDictionary<string, long>(System.StringComparer.OrdinalIgnoreCase);

        try
        {
            // Create bounded channel for producer-consumer pattern
            var channel = Channel.CreateBounded<FileSystemEntry>(1000);
            var writer = channel.Writer;
            var reader = channel.Reader;

            // Start producer task (directory enumeration)
            var producerTask = Task.Run(async () =>
            {
                await ProduceFileSystemEntriesAsync(options, writer, scanProgress, progress, startTime, cancellationToken);
            }, cancellationToken);

            // Start consumer tasks (file processing)
            var consumerTasks = new List<Task>();
            for (int i = 0; i < options.ConcurrencyLimit; i++)
            {
                consumerTasks.Add(Task.Run(async () =>
                {
                    await ConsumeFileSystemEntriesAsync(reader, filesTracker, folderSizes, scanProgress, progress, startTime, options, cancellationToken);
                }, cancellationToken));
            }

            // Wait for producer to complete
            await producerTask;

            // Wait for all items to be processed
            await reader.Completion;

            // Wait for consumers to finish
            await Task.WhenAll(consumerTasks);

            // Compute folder sizes and top folders
            var topFolders = ComputeTopFolders(folderSizes, options, startTime);

            results.TopFiles.AddRange(filesTracker.GetTop());
            results.TopFolders.AddRange(topFolders);
            
            // Analyze file types if not in FoldersOnly mode
            if (!options.FoldersOnly)
            {
                results.FileTypeStatistics.AddRange(_fileTypeAnalyzer.AnalyzeFileTypes(filesTracker.GetTop()));
            }
            
            results.Progress = scanProgress;
            results.IsCompleted = true;
        }
        catch (OperationCanceledException)
        {
            results.WasCancelled = true;
        }
        catch (Exception ex)
        {
            results.ErrorMessage = ex.Message;
        }

        scanProgress.Elapsed = DateTime.UtcNow - startTime;
        progress?.Report(scanProgress);

        return results;
    }

    /// <summary>
    /// Producer task that recursively enumerates all files and directories in the specified path
    /// and writes them to a channel for processing by consumer tasks.
    /// </summary>
    private async Task ProduceFileSystemEntriesAsync(ScanOptions options, ChannelWriter<FileSystemEntry> writer, ScanProgress progress, IProgress<ScanProgress>? progressReporter, DateTime startTime, CancellationToken cancellationToken)
    {
        try
        {
            var enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                // We manage recursion manually using a stack, avoid double traversal.
                RecurseSubdirectories = false,
                // Also skip cloud placeholder files (e.g., OneDrive files that are not available locally)
                AttributesToSkip = options.IncludeHidden
                    ? FileAttributes.Offline
                    : FileAttributes.Hidden | FileAttributes.System | FileAttributes.Offline
            };

            await foreach (var entry in EnumerateFileSystemEntriesAsync(options.RootPath, enumerationOptions, options, cancellationToken))
            {
                await writer.WriteAsync(entry, cancellationToken);
                
                progress.CurrentPath = entry.FullPath;
                if (entry.IsDirectory)
                    progress.FoldersScanned++;
                else
                    progress.FilesScanned++;

                // Throttle progress updates
                if (progress.FilesScanned % 100 == 0)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    progress.Elapsed = elapsed;
                    progressReporter?.Report(progress);
                }
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <summary>
    /// Consumer task that processes file system entries from the channel, tracking top files
    /// and accumulating folder size information for later analysis.
    /// </summary>
    private async Task ConsumeFileSystemEntriesAsync(ChannelReader<FileSystemEntry> reader, TopItemsTracker<FileItem> filesTracker, ConcurrentDictionary<string, long> folderSizes, ScanProgress progress, IProgress<ScanProgress>? progressReporter, DateTime startTime, ScanOptions scanOptions, CancellationToken cancellationToken)
    {
        // Normalize the root path once for comparisons and as a boundary for aggregation
        var rootFullPathTrimmed = Path.GetFullPath(scanOptions.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        await foreach (var entry in reader.ReadAllAsync(cancellationToken))
        {
            if (!entry.IsDirectory)
            {
                // Only process files if not in FoldersOnly mode
                if (!scanOptions.FoldersOnly)
                {
                    // Track top files
                    var fileItem = new FileItem
                    {
                        Name = entry.Name,
                        FullPath = entry.FullPath,
                        Size = entry.Size,
                        Type = Path.GetExtension(entry.Name),
                        LastModified = entry.LastModified
                    };

                    filesTracker.Add(fileItem);
                }

                // Update folder sizes for parent directories up to the scan root only
                var directoryPath = Path.GetDirectoryName(entry.FullPath);
                while (!string.IsNullOrEmpty(directoryPath))
                {
                    var dirFullTrimmed = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    // Stop if we climbed above the root
                    if (!dirFullTrimmed.StartsWith(rootFullPathTrimmed, StringComparison.OrdinalIgnoreCase))
                        break;

                    // Use normalized full path as the key to prevent duplicates
                    folderSizes.AddOrUpdate(dirFullTrimmed, entry.Size, (key, value) => value + entry.Size);

                    // If we've reached the root directory, stop climbing further
                    if (string.Equals(dirFullTrimmed, rootFullPathTrimmed, StringComparison.OrdinalIgnoreCase))
                        break;

                    directoryPath = Path.GetDirectoryName(directoryPath);
                }

                progress.BytesProcessed += entry.Size;
            }
        }
    }

    /// <summary>
    /// Asynchronously enumerates all file system entries in the specified directory tree,
    /// respecting the scan options for exclusions, depth limits, and other filters.
    /// </summary>
    private async IAsyncEnumerable<FileSystemEntry> EnumerateFileSystemEntriesAsync(string rootPath, EnumerationOptions options, ScanOptions scanOptions, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stack = new Stack<(string path, int depth)>();
        stack.Push((rootPath, 0));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (currentPath, depth) = stack.Pop();

            if (scanOptions.MaxDepth.HasValue && depth > scanOptions.MaxDepth.Value)
                continue;

            if (ShouldSkipDirectory(currentPath, scanOptions.ExcludeDirectories, scanOptions.ExcludePatterns))
                continue;

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(currentPath, "*", options);
            }
            catch (UnauthorizedAccessException)
            {
                continue; // Skip inaccessible directories
            }
            catch (DirectoryNotFoundException)
            {
                continue; // Skip if directory was deleted during scan
            }

            foreach (var entryPath in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileSystemEntry? entry = null;
                try
                {
                    var info = new FileInfo(entryPath);
                    var attributes = info.Attributes;
                    var isDirectory = (attributes & FileAttributes.Directory) == FileAttributes.Directory;

                    // Skip cloud placeholder files or directories (e.g., OneDrive items not available offline)
                    if ((attributes & FileAttributes.Offline) == FileAttributes.Offline)
                        continue;

                    if (!isDirectory && info.Length < scanOptions.MinFileSize)
                        continue;

                    if (!scanOptions.FollowSymlinks && (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                        continue;

                    entry = new FileSystemEntry
                    {
                        Name = Path.GetFileName(entryPath),
                        FullPath = entryPath,
                        Size = isDirectory ? 0 : info.Length,
                        IsDirectory = isDirectory,
                        LastModified = info.LastWriteTime
                    };

                    if (isDirectory)
                    {
                        stack.Push((entryPath, depth + 1));
                    }
                }
                catch (Exception)
                {
                    // Skip problematic files/directories
                    continue;
                }

                if (entry != null)
                {
                    yield return entry;
                }

                // Yield control periodically
                if (stack.Count % 10 == 0)
                    await Task.Yield();
            }
        }
    }

    /// <summary>
    /// Determines whether a directory should be skipped based on the exclusion rules.
    /// </summary>
    private bool ShouldSkipDirectory(string path, HashSet<string> excludeDirectories, HashSet<string> excludePatterns)
    {
        var dirName = Path.GetFileName(path);
        return excludeDirectories.Contains(dirName) || 
               excludePatterns.Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(dirName, pattern));
    }

    /// <summary>
    /// Computes the top largest folders from the accumulated folder size data.
    /// </summary>
    private List<FolderItem> ComputeTopFolders(ConcurrentDictionary<string, long> folderSizes, ScanOptions options, DateTime startTime)
    {
        var folderItems = new List<FolderItem>();

        foreach (var kvp in folderSizes)
        {
            try
            {
                var dirInfo = new DirectoryInfo(kvp.Key);
                var folderItem = new FolderItem
                {
                    Name = dirInfo.Name,
                    FullPath = kvp.Key,
                    RecursiveSize = kvp.Value,
                    ItemCount = 0, // Could be computed if needed
                    LastModified = dirInfo.LastWriteTime
                };
                folderItems.Add(folderItem);
            }
            catch
            {
                // Skip problematic directories
            }
        }

        return folderItems
            .OrderByDescending(f => f.RecursiveSize)
            .Take(options.TopN)
            .ToList();
    }
}

/// <summary>
/// Internal data structure used to represent a file or directory entry during the scanning process.
/// </summary>
internal class FileSystemEntry
{
    /// <summary>
    /// Gets the name of the file or directory.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Gets the full path to the file or directory.
    /// </summary>
    public string FullPath { get; init; } = string.Empty;
    
    /// <summary>
    /// Gets the size of the file in bytes (0 for directories).
    /// </summary>
    public long Size { get; init; }
    
    /// <summary>
    /// Gets a value indicating whether this entry represents a directory.
    /// </summary>
    public bool IsDirectory { get; init; }
    
    /// <summary>
    /// Gets the date and time when the entry was last modified.
    /// </summary>
    public DateTime LastModified { get; init; }
}
