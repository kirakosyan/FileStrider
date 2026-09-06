using System.Text.Json;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;

namespace FileStrider.Infrastructure.Configuration;

public class ConfigurationService : IConfigurationService
{
    private readonly string _configFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true
    };

    // Tests supply a private path. Construction never creates or modifies the real profile.
    public ConfigurationService(string? configFilePath = null) =>
        _configFilePath = Path.GetFullPath(configFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileStrider", "config.json"));

    public async Task<ScanOptions> LoadDefaultOptionsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var candidate in new[] { _configFilePath, _configFilePath + ".bak" })
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        var options = JsonSerializer.Deserialize<ScanOptions>(await File.ReadAllTextAsync(candidate), JsonOptions);
                        if (options is not null) return Normalize(options);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { System.Diagnostics.Trace.TraceWarning($"Cannot load settings: {ex.Message}"); }
            }
            return GetDefaultOptions();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveDefaultOptionsAsync(ScanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _gate.WaitAsync();
        var temporary = _configFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(Normalize(options), JsonOptions));
            if (File.Exists(_configFilePath))
            {
                // Only replace the recovery copy when the current file is readable JSON.
                try
                {
                    using var current = JsonDocument.Parse(await File.ReadAllTextAsync(_configFilePath));
                    File.Copy(_configFilePath, _configFilePath + ".bak", true);
                }
                catch (JsonException) { }
            }
            File.Move(temporary, _configFilePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }

    private static ScanOptions Normalize(ScanOptions options) => options with
    {
        RootPath = string.IsNullOrWhiteSpace(options.RootPath) ? GetDefaultOptions().RootPath : options.RootPath,
        TopN = Math.Clamp(options.TopN, 1, 200),
        MinFileSize = Math.Max(0, options.MinFileSize),
        MaxDepth = options.MaxDepth is < 0 ? null : options.MaxDepth,
        ConcurrencyLimit = Math.Clamp(options.ConcurrencyLimit, 1, Math.Max(1, Environment.ProcessorCount * 2)),
        ExcludePatterns = new HashSet<string>(options.ExcludePatterns ?? [], StringComparer.OrdinalIgnoreCase),
        ExcludeDirectories = new HashSet<string>(options.ExcludeDirectories ?? [], StringComparer.OrdinalIgnoreCase),
        RecentPaths = (options.RecentPaths ?? []).Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Take(8).ToList(),
        Language = new[] { "en", "fr", "es", "sv" }.Contains(options.Language) ? options.Language : "en"
    };

    private static ScanOptions GetDefaultOptions() => new()
    {
        RootPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ExcludePatterns = new(StringComparer.OrdinalIgnoreCase) { "*.tmp", "*.temp", "*.log" },
        ExcludeDirectories = new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".git", "Library/Caches", ".vs", "bin", "obj" }
    };
}
