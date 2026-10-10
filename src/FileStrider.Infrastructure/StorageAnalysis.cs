using FileStrider.Core.Models;

namespace FileStrider.Infrastructure.Analysis;

public static class ScanComparison
{
    private static StringComparer Paths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // Return a resource key explaining why a comparison cannot be trusted.
    public static string? Validate(ScanResults before, ScanResults after)
    {
        if (!Paths.Equals(before.RootPath, after.RootPath)) return "CompareDifferentRoot";
        if (!Complete(before) || !Complete(after)) return "CompareIncomplete";
        var a = before.Options;
        var b = after.Options;
        if (a is null || b is null || a.IncludeHidden != b.IncludeHidden || a.FollowSymlinks != b.FollowSymlinks ||
            a.MaxDepth != b.MaxDepth || !new HashSet<string>(a.ExcludePatterns, StringComparer.OrdinalIgnoreCase).SetEquals(b.ExcludePatterns) ||
            !new HashSet<string>(a.ExcludeDirectories, Paths).SetEquals(b.ExcludeDirectories)) return "CompareDifferentFilters";
        return null;
    }

    public static IReadOnlyList<FolderChange> Compare(ScanResults before, ScanResults after)
    {
        if (Validate(before, after) is { } error) throw new InvalidOperationException(error);
        var previous = before.Folders.ToDictionary(f => f.FullPath, f => f.RecursiveSize, Paths);
        var current = after.Folders.ToDictionary(f => f.FullPath, f => f.RecursiveSize, Paths);
        return previous.Keys.Union(current.Keys, Paths)
            .Select(path => new FolderChange(path, previous.GetValueOrDefault(path), current.GetValueOrDefault(path)))
            .Where(change => change.Delta != 0).OrderByDescending(change => Math.Abs(change.Delta))
            .ThenBy(change => change.FullPath, Paths).ToList();
    }

    private static bool Complete(ScanResults scan) => scan.IsCompleted && !scan.WasCancelled &&
        scan.ErrorMessage is null && !scan.Progress.HasIncompleteCoverage;
}

public static class DeveloperStorageAnalysis
{
    private static readonly HashSet<string> Dependencies = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".venv", "venv", "packages", "vendor" };
    private static readonly HashSet<string> Builds = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", "target", "dist", "build", ".vs" };
    private static readonly HashSet<string> Caches = new(StringComparer.OrdinalIgnoreCase)
        { ".cache", ".npm", ".nuget", ".gradle", ".yarn", "Caches", "__pycache__" };

    public static IReadOnlyList<StorageInsight> Analyze(ScanResults results)
    {
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var candidates = results.Folders.Where(f => f.RecursiveSize > 0).Select(f => new
        {
            Folder = f,
            Kind = Dependencies.Contains(f.Name) ? "InsightDependencies" : Builds.Contains(f.Name) ? "InsightBuilds" :
                Caches.Contains(f.Name) ? "InsightCaches" : null
        }).Where(x => x.Kind is not null).OrderBy(x => x.Folder.FullPath.Length).ToList();
        var selected = new List<StorageInsight>();
        foreach (var candidate in candidates)
        {
            // A dependency tree can contain thousands of nested build folders. Count it once.
            var nested = false;
            for (var parent = Path.GetDirectoryName(candidate.Folder.FullPath); parent is not null; parent = Path.GetDirectoryName(parent))
                if (paths.Contains(parent)) { nested = true; break; }
            if (nested) continue;
            var folder = candidate.Folder;
            paths.Add(folder.FullPath);
            selected.Add(new(folder.FullPath, candidate.Kind!, folder.RecursiveSize, folder.LastModified,
                folder.LastModified != DateTime.MinValue && folder.LastModified.ToUniversalTime() < results.ScannedAt.UtcDateTime.AddDays(-180)));
        }
        return selected.OrderByDescending(x => x.Size).ToList();
    }
}
