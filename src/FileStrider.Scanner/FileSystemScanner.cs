using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Threading.Channels;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;

namespace FileStrider.Scanner;

/// <summary>Scans metadata without opening file contents. Memory usage is bounded by directories and top-N results.</summary>
public class FileSystemScanner(IFileTypeAnalyzer fileTypeAnalyzer) : IFileSystemScanner
{
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<ScanResults> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RootPath))
            throw new ArgumentException("Root path cannot be empty.", nameof(options));
        if (!Directory.Exists(options.RootPath))
            throw new DirectoryNotFoundException($"Root path does not exist: {options.RootPath}");
        if (options.TopN < 1 || options.TopN > 200)
            throw new ArgumentOutOfRangeException(nameof(options), "TopN must be between 1 and 200.");
        if (options.ConcurrencyLimit < 1 || options.ConcurrencyLimit > Environment.ProcessorCount * 2)
            throw new ArgumentOutOfRangeException(nameof(options), $"ConcurrencyLimit must be between 1 and {Environment.ProcessorCount * 2}.");
        if (options.MinFileSize < 0 || options.MaxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "File size and depth cannot be negative.");

        options = options with { RootPath = NormalizePath(options.RootPath) };
        var results = new ScanResults { RootPath = options.RootPath };
        var state = new ScanProgress();
        var timer = Stopwatch.StartNew();
        using var topFiles = new TopItemsTracker<FileItem>(options.TopN, (a, b) => b.Size.CompareTo(a.Size));
        var folders = new ConcurrentDictionary<string, FolderAccumulator>(PathComparer);
        var categories = new ConcurrentDictionary<string, CategoryAccumulator>(StringComparer.OrdinalIgnoreCase);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<Entry>(new BoundedChannelOptions(2000) { SingleWriter = true });
        var tasks = new List<Task>();

        // Do not pass a token to Task.Run: even a pre-cancelled scan must run the producer's finally.
        async Task Supervise(Func<Task> work)
        {
            try { await work().ConfigureAwait(false); }
            catch { await linked.CancelAsync().ConfigureAwait(false); throw; }
        }
        tasks.Add(Task.Run(() => Supervise(async () =>
        {
            try
            {
                await foreach (var entry in EnumerateAsync(options, state, linked.Token).ConfigureAwait(false))
                    await channel.Writer.WriteAsync(entry, linked.Token).ConfigureAwait(false);
            }
            finally { channel.Writer.TryComplete(); }
        })));
        for (var i = 0; i < options.ConcurrencyLimit; i++)
        {
            tasks.Add(Task.Run(() => Supervise(async () =>
            {
                await foreach (var entry in channel.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (entry.IsDirectory)
                    {
                        folders.GetOrAdd(entry.Path, _ => new FolderAccumulator(entry.Modified));
                        if (!PathComparer.Equals(entry.Path, options.RootPath)) state.IncrementFoldersScanned();
                        continue;
                    }
                    if (!options.FoldersOnly)
                    {
                        var category = fileTypeAnalyzer.GetFileCategory(Path.GetExtension(entry.Path));
                        categories.GetOrAdd(category, _ => new CategoryAccumulator()).Add(entry.Size);
                        if (entry.Size >= options.MinFileSize)
                            topFiles.Add(new FileItem { Name = entry.Name, FullPath = entry.Path, Size = entry.Size,
                                Type = Path.GetExtension(entry.Path), LastModified = entry.Modified });
                    }

                    var directParent = Path.GetDirectoryName(entry.Path);
                    for (var parent = directParent; parent is not null; parent = Path.GetDirectoryName(parent))
                    {
                        parent = NormalizePath(parent);
                        if (!IsWithinRoot(parent, options.RootPath)) break;
                        folders.GetOrAdd(parent, _ => new FolderAccumulator(DateTime.MinValue))
                            .Add(entry.Size, PathComparer.Equals(parent, directParent));
                        if (PathComparer.Equals(parent, options.RootPath)) break;
                    }
                    state.IncrementFilesScanned();
                    state.AddBytesProcessed(entry.Size);
                    state.CurrentPath = entry.Path;
                    if (state.FilesScanned % 100 == 0)
                    {
                        var snapshot = state.CreateSnapshot();
                        snapshot.Elapsed = timer.Elapsed;
                        progress?.Report(snapshot);
                    }
                }
            })));
        }

        try
        {
            // Observe every worker before reading results or disposing shared state.
            // A worker failure cancels the producer even when it is blocked on a full channel.
            await Task.WhenAll(tasks).ConfigureAwait(false);
            results.WasCancelled = cancellationToken.IsCancellationRequested;
            results.IsCompleted = !results.WasCancelled;
        }
        catch
        {
            var failure = tasks.Where(t => t.Exception is not null)
                .SelectMany(t => t.Exception!.Flatten().InnerExceptions)
                .FirstOrDefault(e => e is not OperationCanceledException);
            if (failure is null) results.WasCancelled = true;
            else results.ErrorMessage = failure.Message;
        }

        results.TopFiles.AddRange(topFiles.GetTop());
        foreach (var (path, value) in folders)
        {
            results.Folders.Add(new FolderItem { Name = new DirectoryInfo(path).Name, FullPath = path,
                RecursiveSize = value.TotalSize, DirectSize = value.DirectSize, ItemCount = value.ItemCount,
                LastModified = value.Modified });
        }
        results.TopFolders.AddRange(results.Folders.OrderByDescending(f => f.RecursiveSize)
            .ThenBy(f => f.FullPath, PathComparer).Take(options.TopN));
        results.FileTypeStatistics.AddRange(categories.Select(pair => new FileTypeStats {
            Category = pair.Key, FileCount = pair.Value.Count, TotalSize = pair.Value.Size,
            Percentage = state.BytesProcessed > 0 ? 100.0 * pair.Value.Size / state.BytesProcessed : 0
        }).OrderByDescending(c => c.TotalSize));
        state.Elapsed = timer.Elapsed;
        results.Progress = state.CreateSnapshot();
        try { progress?.Report(results.Progress.CreateSnapshot()); }
        catch (Exception ex) { results.ErrorMessage ??= ex.Message; results.IsCompleted = false; }
        return results;
    }

    private static async IAsyncEnumerable<Entry> EnumerateAsync(ScanOptions options, ScanProgress progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var stack = new Stack<(string Path, string PhysicalPath, int Depth)>();
        var root = new DirectoryInfo(options.RootPath);
        stack.Push((root.FullName, root.ResolveLinkTarget(true)?.FullName ?? root.FullName, 0));
        var visited = new HashSet<string>(PathComparer);
        var enumeration = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
        while (stack.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(NormalizePath(current.PhysicalPath)))
            {
                progress.IncrementExcludedItems();
                continue;
            }
            yield return new Entry(new DirectoryInfo(current.Path).Name, current.Path, 0, true, SafeModified(current.Path));
            foreach (var info in ReadDirectory(current.Path, enumeration, progress))
            {
                token.ThrowIfCancellationRequested();
                Entry? entry = null;
                try
                {
                    var attributes = info.Attributes;
                    var directory = (attributes & FileAttributes.Directory) != 0;
                    if ((attributes & FileAttributes.Offline) != 0)
                    {
                        progress.IncrementOfflineItems();
                        continue;
                    }
                    if ((!options.IncludeHidden && ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || info.Name.StartsWith('.')))
                        || (!options.FollowSymlinks && (attributes & FileAttributes.ReparsePoint) != 0)
                        || MatchesPattern(info.Name, options.ExcludePatterns)
                        || (directory && IsExcludedDirectory(info.FullName, options)))
                    {
                        progress.IncrementExcludedItems();
                        continue;
                    }
                    if (directory)
                    {
                        if (options.MaxDepth.HasValue && current.Depth >= options.MaxDepth.Value)
                        {
                            progress.IncrementDepthLimitedDirectories();
                            continue;
                        }
                        var physical = (attributes & FileAttributes.ReparsePoint) != 0
                            ? info.ResolveLinkTarget(true)?.FullName ?? info.FullName
                            : Path.Combine(current.PhysicalPath, info.Name);
                        stack.Push((NormalizePath(info.FullName), physical, current.Depth + 1));
                    }
                    else
                    {
                        entry = new Entry(info.Name, info.FullName, ((FileInfo)info).Length, false, info.LastWriteTime);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    progress.IncrementInaccessibleItems();
                }
                if (entry is not null) yield return entry;
            }
            await Task.Yield();
        }
    }

    // MoveNext may fail after enumeration has begun (removed directories, unavailable shares, permissions).
    private static IEnumerable<FileSystemInfo> ReadDirectory(string path, EnumerationOptions options, ScanProgress progress)
    {
        IEnumerator<FileSystemInfo>? iterator = null;
        try { iterator = new DirectoryInfo(path).EnumerateFileSystemInfos("*", options).GetEnumerator(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { progress.IncrementInaccessibleItems(); }
        if (iterator is null) yield break;
        using (iterator)
        {
            while (true)
            {
                FileSystemInfo? item = null;
                try { if (iterator.MoveNext()) item = iterator.Current; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { progress.IncrementInaccessibleItems(); }
                if (item is null) yield break;
                yield return item;
            }
        }
    }

    private static DateTime SafeModified(string path)
    {
        try { return Directory.GetLastWriteTime(path); }
        catch (IOException) { return DateTime.MinValue; }
        catch (UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    private static bool IsExcludedDirectory(string path, ScanOptions options)
    {
        var relative = Path.GetRelativePath(options.RootPath, path).Replace('\\', '/');
        var name = Path.GetFileName(path);
        return (options.ExcludeDirectories ?? []).Any(excluded =>
            string.Equals(name, excluded, PathComparison) ||
            relative.Equals(excluded.Replace('\\', '/').Trim('/'), PathComparison) ||
            relative.EndsWith("/" + excluded.Replace('\\', '/').Trim('/'), PathComparison));
    }

    private static bool MatchesPattern(string name, IEnumerable<string>? patterns) =>
        patterns?.Any(p => !string.IsNullOrWhiteSpace(p) &&
            FileSystemName.MatchesSimpleExpression(p, name, OperatingSystem.IsWindows())) == true;

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool IsWithinRoot(string path, string root) => PathComparer.Equals(path, root) ||
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, PathComparison);

    private sealed record Entry(string Name, string Path, long Size, bool IsDirectory, DateTime Modified);
    private sealed class FolderAccumulator(DateTime modified)
    {
        private long _total, _direct;
        private int _count;
        public DateTime Modified { get; } = modified;
        public long TotalSize => Interlocked.Read(ref _total);
        public long DirectSize => Interlocked.Read(ref _direct);
        public int ItemCount => Volatile.Read(ref _count);
        public void Add(long size, bool direct)
        {
            Interlocked.Add(ref _total, size);
            Interlocked.Increment(ref _count);
            if (direct) Interlocked.Add(ref _direct, size);
        }
    }
    private sealed class CategoryAccumulator
    {
        private long _size;
        private int _count;
        public long Size => Interlocked.Read(ref _size);
        public int Count => Volatile.Read(ref _count);
        public void Add(long size) { Interlocked.Add(ref _size, size); Interlocked.Increment(ref _count); }
    }
}
