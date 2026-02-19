using System.Diagnostics;
using System.Text.Json;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;

namespace FileStrider.Infrastructure.Configuration;

/// <summary>
/// Service for loading and saving application configuration settings to persistent storage.
/// Stores configuration as JSON files in the user's local application data directory.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly string _configFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationService"/> class.
    /// Creates the configuration directory if it doesn't exist.
    /// </summary>
    public ConfigurationService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var configDirectory = Path.Combine(appDataPath, "FileStrider");
        Directory.CreateDirectory(configDirectory);
        _configFilePath = Path.Combine(configDirectory, "config.json");
    }

    /// <summary>
    /// Loads the default scan options from persistent storage.
    /// Returns default ScanOptions if no configuration file exists or if loading fails.
    /// </summary>
    /// <returns>A task that represents the asynchronous load operation, returning the configured scan options or defaults if none exist.</returns>
    public async Task<ScanOptions> LoadDefaultOptionsAsync()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = await File.ReadAllTextAsync(_configFilePath);
                var options = JsonSerializer.Deserialize<ScanOptions>(json, JsonOptions);
                return options ?? GetDefaultOptions();
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to load configuration: {ex.Message}");
        }

        return GetDefaultOptions();
    }

    /// <summary>
    /// Saves the specified scan options as the new default configuration.
    /// Creates the configuration directory if it doesn't exist.
    /// </summary>
    /// <param name="options">The scan options to save as defaults.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    public async Task SaveDefaultOptionsAsync(ScanOptions options)
    {
        try
        {
            var json = JsonSerializer.Serialize(options, JsonOptions);
            await File.WriteAllTextAsync(_configFilePath, json);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to save configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the default scan options with sensible defaults for first-time use.
    /// </summary>
    /// <returns>A new ScanOptions instance with default values.</returns>
    private static ScanOptions GetDefaultOptions()
    {
        return new ScanOptions
        {
            RootPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            TopN = 20,
            IncludeHidden = false,
            FollowSymlinks = false,
            MaxDepth = null,
            MinFileSize = 0,
            ConcurrencyLimit = Math.Min(8, Environment.ProcessorCount),
            ExcludePatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*.tmp", "*.temp", "*.log" },
            ExcludeDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node_modules", ".git", "Library/Caches", ".vs", "bin", "obj" }
        };
    }
}
