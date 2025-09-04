using System.Text.Json;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;

namespace FileStrider.Infrastructure.Configuration;

public class ConfigurationService : IConfigurationService
{
    private readonly string _configFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConfigurationService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var configDirectory = Path.Combine(appDataPath, "FileStrider");
        Directory.CreateDirectory(configDirectory);
        _configFilePath = Path.Combine(configDirectory, "config.json");
    }

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
        catch (Exception)
        {
            // If config file is corrupted or unreadable, return defaults
        }

        return GetDefaultOptions();
    }

    public async Task SaveDefaultOptionsAsync(ScanOptions options)
    {
        try
        {
            var json = JsonSerializer.Serialize(options, JsonOptions);
            await File.WriteAllTextAsync(_configFilePath, json);
        }
        catch (Exception)
        {
            // Silently fail if unable to save configuration
        }
    }

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
            ExcludePatterns = new HashSet<string> { "*.tmp", "*.temp", "*.log" },
            ExcludeDirectories = new HashSet<string> { "node_modules", ".git", "Library/Caches", ".vs", "bin", "obj" }
        };
    }
}