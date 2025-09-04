using System.Collections.Concurrent;
using System.Threading.Channels;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;

namespace FileStrider.Scanner;

public class FileSystemScanner : IFileSystemScanner
{
    public async Task<ScanResults> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new ScanResults();
        var scanProgress = new ScanProgress();
        var startTime = DateTime.UtcNow;

        var filesTracker = new TopItemsTracker<FileItem>(options.TopN, (a, b) => b.Size.CompareTo(a.Size));
        var foldersTracker = new TopItemsTracker<FolderItem>(options.TopN, (a, b) => b.RecursiveSize.CompareTo(a.RecursiveSize));
        var folderSizes = new ConcurrentDictionary<string, long>();

        try
        {
            // Create bounded channel for producer-consumer pattern
            var channel = Channel.CreateBounded<FileSystemEntry>(1000);
            var writer = channel.Writer;
            var reader = channel.Reader;

            // Start producer task (directory enumeration)
            var producerTask = Task.Run(async () =>
            {
                await ProduceFileSystemEntriesAsync(options, writer, scanProgress, progress, cancellationToken);
            }, cancellationToken);

            // Start consumer tasks (file processing)
            var consumerTasks = new List<Task>();
            for (int i = 0; i < options.ConcurrencyLimit; i++)
            {
                consumerTasks.Add(Task.Run(async () =>
                {
                    await ConsumeFileSystemEntriesAsync(reader, filesTracker, folderSizes, scanProgress, progress, cancellationToken);
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

    private async Task ProduceFileSystemEntriesAsync(ScanOptions options, ChannelWriter<FileSystemEntry> writer, ScanProgress progress, IProgress<ScanProgress>? progressReporter, CancellationToken cancellationToken)
    {
        try
        {
            var enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = options.IncludeHidden ? FileAttributes.None : FileAttributes.Hidden | FileAttributes.System
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
                    progressReporter?.Report(progress);
                }
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task ConsumeFileSystemEntriesAsync(ChannelReader<FileSystemEntry> reader, TopItemsTracker<FileItem> filesTracker, ConcurrentDictionary<string, long> folderSizes, ScanProgress progress, IProgress<ScanProgress>? progressReporter, CancellationToken cancellationToken)
    {
        await foreach (var entry in reader.ReadAllAsync(cancellationToken))
        {
            if (!entry.IsDirectory)
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

                // Update folder sizes for all parent directories
                var directoryPath = Path.GetDirectoryName(entry.FullPath);
                while (!string.IsNullOrEmpty(directoryPath))
                {
                    folderSizes.AddOrUpdate(directoryPath, entry.Size, (key, value) => value + entry.Size);
                    directoryPath = Path.GetDirectoryName(directoryPath);
                }

                progress.BytesProcessed += entry.Size;
            }
        }
    }

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
                    var isDirectory = (info.Attributes & FileAttributes.Directory) == FileAttributes.Directory;

                    if (!isDirectory && info.Length < scanOptions.MinFileSize)
                        continue;

                    if (!scanOptions.FollowSymlinks && (info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
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

    private bool ShouldSkipDirectory(string path, HashSet<string> excludeDirectories, HashSet<string> excludePatterns)
    {
        var dirName = Path.GetFileName(path);
        return excludeDirectories.Contains(dirName) || 
               excludePatterns.Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(dirName, pattern));
    }

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

internal class FileSystemEntry
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long Size { get; init; }
    public bool IsDirectory { get; init; }
    public DateTime LastModified { get; init; }
}
