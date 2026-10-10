using System.Text.Json;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;

namespace FileStrider.Infrastructure.History;

public sealed class ScanHistoryService : IScanHistoryService
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IgnoreReadOnlyProperties = true
    };

    public ScanHistoryService(string? directory = null) => _directory = Path.GetFullPath(directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileStrider", "history"));

    public async Task<SavedScanInfo> SaveAsync(ScanResults results)
    {
        if (results.Options is null || results.Folders.Count == 0)
            throw new InvalidOperationException("This scan does not contain the metadata needed for a snapshot.");
        var saved = new SavedScan { Results = results };
        Directory.CreateDirectory(_directory);
        var destination = FilePath(saved.Id);
        var temporary = destination + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await JsonSerializer.SerializeAsync(stream, saved, JsonOptions);
            File.Move(temporary, destination);
        }
        finally { Configuration.ConfigurationService.DeleteTemporaryFile(temporary); }
        var info = Info(saved);
        await CacheInfoAsync(destination, info);
        return info;
    }

    public Task<IReadOnlyList<SavedScanInfo>> ListAsync() => Task.Run(ListCoreAsync);

    private async Task<IReadOnlyList<SavedScanInfo>> ListCoreAsync()
    {
        if (!Directory.Exists(_directory)) return [];
        var scans = new List<SavedScanInfo>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            if (file.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (await ReadCachedInfoAsync(file, id) is { } cached) { scans.Add(cached); continue; }
                // Older snapshots migrate once. Subsequent listing reads only the small sidecar.
                var info = Info(await LoadAsync(id));
                scans.Add(info);
                await CacheInfoAsync(file, info);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
            { System.Diagnostics.Trace.TraceWarning($"Cannot read saved scan {file}: {ex.Message}"); }
        }
        return scans.OrderByDescending(s => s.ScannedAt).ToList();
    }

    private sealed record HistoryMetadata(int Version, long Length, DateTime LastWriteTimeUtc, SavedScanInfo Info);

    private static async Task<SavedScanInfo?> ReadCachedInfoAsync(string snapshotPath, string id)
    {
        var metadataPath = Path.ChangeExtension(snapshotPath, ".meta.json");
        try
        {
            if (!File.Exists(metadataPath) || new FileInfo(metadataPath).Length > 65536) return null;
            await using var metadataStream = File.OpenRead(metadataPath);
            var metadata = await JsonSerializer.DeserializeAsync<HistoryMetadata>(metadataStream, JsonOptions);
            var file = new FileInfo(snapshotPath);
            if (metadata?.Version != 1 || metadata.Info?.Id != id || metadata.Length != file.Length ||
                metadata.LastWriteTimeUtc != file.LastWriteTimeUtc) return null;
            // Check accessibility without parsing or reading the large snapshot payload.
            using var snapshotStream = File.OpenRead(snapshotPath);
            return metadata.Info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    private static async Task CacheInfoAsync(string snapshotPath, SavedScanInfo info)
    {
        var metadataPath = Path.ChangeExtension(snapshotPath, ".meta.json");
        var temporary = metadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var file = new FileInfo(snapshotPath);
            var metadata = new HistoryMetadata(1, file.Length, file.LastWriteTimeUtc, info);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions);
            File.Move(temporary, metadataPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Trace.TraceWarning($"Cannot cache scan summary: {ex.Message}"); }
        finally { Configuration.ConfigurationService.DeleteTemporaryFile(temporary); }
    }

    public async Task<SavedScan> LoadAsync(string id)
    {
        await using var stream = File.OpenRead(FilePath(id));
        var saved = await JsonSerializer.DeserializeAsync<SavedScan>(stream, JsonOptions)
            ?? throw new InvalidDataException("Empty saved scan.");
        if (!IsValid(saved, id))
            throw new InvalidDataException("Unsupported or incomplete saved scan.");
        return saved;
    }

    private static bool IsValid(SavedScan saved, string id)
    {
        var r = saved.Results;
        if (saved.SchemaVersion != 1 || saved.Id != id || r?.Options is null ||
            r.Options.ExcludePatterns is null || r.Options.ExcludeDirectories is null ||
            r.Progress is null || r.Folders is null || r.TopFolders is null || r.TopFiles is null ||
            r.CoverageIssues is null || r.FileTypeStatistics is null || r.ProjectDirectories is null ||
            string.IsNullOrWhiteSpace(r.RootPath) || !Path.IsPathFullyQualified(r.RootPath)) return false;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(r.RootPath) ? r.RootPath : r.RootPath + Path.DirectorySeparatorChar;
        var paths = new HashSet<string>(comparer);
        foreach (var folder in r.Folders)
            if (folder is null || string.IsNullOrEmpty(folder.FullPath) || folder.RecursiveSize < 0 || folder.DirectSize < 0 ||
                !paths.Add(folder.FullPath) || !(comparer.Equals(folder.FullPath, r.RootPath) || folder.FullPath.StartsWith(prefix, comparison))) return false;
        return paths.Contains(r.RootPath) && r.TopFolders.All(f => f is not null && paths.Contains(f.FullPath)) &&
            r.TopFiles.All(f => f is not null && f.Size >= 0 && !string.IsNullOrEmpty(f.FullPath)) &&
            r.CoverageIssues.All(i => i is not null && !string.IsNullOrEmpty(i.FullPath)) &&
            r.FileTypeStatistics.All(s => s is not null);
    }

    public Task DeleteAsync(string id)
    {
        var path = FilePath(id);
        File.Delete(path);
        Configuration.ConfigurationService.DeleteTemporaryFile(Path.ChangeExtension(path, ".meta.json"));
        return Task.CompletedTask;
    }

    private string FilePath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid saved scan identifier.", nameof(id));
        return Path.Combine(_directory, id + ".json");
    }

    private static SavedScanInfo Info(SavedScan saved) => new(saved.Id, saved.Results.ScannedAt,
        saved.Results.RootPath, saved.Results.Progress.BytesProcessed, SavedScanInfo.StatusOf(saved.Results));
}
