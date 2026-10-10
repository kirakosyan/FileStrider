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
        return Info(saved);
    }

    public async Task<IReadOnlyList<SavedScanInfo>> ListAsync()
    {
        if (!Directory.Exists(_directory)) return [];
        var scans = new List<SavedScanInfo>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try { scans.Add(Info(await LoadAsync(Path.GetFileNameWithoutExtension(file)))); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or ArgumentException)
            { System.Diagnostics.Trace.TraceWarning($"Cannot read saved scan {file}: {ex.Message}"); }
        }
        return scans.OrderByDescending(s => s.ScannedAt).ToList();
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
            r.CoverageIssues is null || r.FileTypeStatistics is null ||
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

    public Task DeleteAsync(string id) { File.Delete(FilePath(id)); return Task.CompletedTask; }

    private string FilePath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid saved scan identifier.", nameof(id));
        return Path.Combine(_directory, id + ".json");
    }

    private static SavedScanInfo Info(SavedScan saved) => new(saved.Id, saved.Results.ScannedAt,
        saved.Results.RootPath, saved.Results.Progress.BytesProcessed);
}
