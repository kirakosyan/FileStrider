using System.Text.Json;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;

namespace FileStrider.Infrastructure.Export;

/// <summary>
/// Service for exporting file system scan results to various file formats including CSV and JSON.
/// </summary>
public class ExportService : IExportService
{
    /// <summary>
    /// Exports the scan results to a CSV (Comma Separated Values) file with separate sections for files and folders.
    /// Includes proper CSV escaping for fields containing special characters.
    /// </summary>
    /// <param name="results">The scan results to export.</param>
    /// <param name="filePath">The file path where the CSV file should be saved.</param>
    /// <returns>A task that represents the asynchronous export operation.</returns>
    public async Task ExportToCsvAsync(ScanResults results, string filePath)
    {
        using var writer = new StreamWriter(filePath);
        
        // Export top files
        await writer.WriteLineAsync("Top Files");
        await writer.WriteLineAsync("Name,Full Path,Size (bytes),Size (MB),Type,Last Modified");
        
        foreach (var file in results.TopFiles)
        {
            var sizeInMB = file.Size / (1024.0 * 1024.0);
            await writer.WriteLineAsync($"{EscapeCsvField(file.Name)},{EscapeCsvField(file.FullPath)},{file.Size},{sizeInMB:F2},{EscapeCsvField(file.Type)},{file.LastModified:yyyy-MM-dd HH:mm:ss}");
        }
        
        await writer.WriteLineAsync();
        
        // Export top folders
        await writer.WriteLineAsync("Top Folders");
        await writer.WriteLineAsync("Name,Full Path,Recursive Size (bytes),Recursive Size (MB),Item Count,Last Modified");
        
        foreach (var folder in results.TopFolders)
        {
            var sizeInMB = folder.RecursiveSize / (1024.0 * 1024.0);
            await writer.WriteLineAsync($"{EscapeCsvField(folder.Name)},{EscapeCsvField(folder.FullPath)},{folder.RecursiveSize},{sizeInMB:F2},{folder.ItemCount},{folder.LastModified:yyyy-MM-dd HH:mm:ss}");
        }
    }

    /// <summary>
    /// Exports the scan results to a structured JSON file with comprehensive metadata and formatted data.
    /// Uses camelCase naming and includes both raw byte values and human-readable MB values.
    /// </summary>
    /// <param name="results">The scan results to export.</param>
    /// <param name="filePath">The file path where the JSON file should be saved.</param>
    /// <returns>A task that represents the asynchronous export operation.</returns>
    public async Task ExportToJsonAsync(ScanResults results, string filePath)
    {
        var exportData = new
        {
            ExportedAt = DateTime.UtcNow,
            ScanCompleted = results.IsCompleted,
            ScanCancelled = results.WasCancelled,
            ErrorMessage = results.ErrorMessage,
            Progress = new
            {
                results.Progress.FilesScanned,
                results.Progress.FoldersScanned,
                results.Progress.BytesProcessed,
                results.Progress.Elapsed
            },
            TopFiles = results.TopFiles.Select(f => new
            {
                f.Name,
                f.FullPath,
                f.Size,
                SizeInMB = f.Size / (1024.0 * 1024.0),
                f.Type,
                f.LastModified
            }),
            TopFolders = results.TopFolders.Select(f => new
            {
                f.Name,
                f.FullPath,
                f.RecursiveSize,
                RecursiveSizeInMB = f.RecursiveSize / (1024.0 * 1024.0),
                f.ItemCount,
                f.LastModified
            })
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(exportData, options);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Escapes a CSV field according to RFC 4180 standards by wrapping in quotes and escaping internal quotes.
    /// </summary>
    /// <param name="field">The field value to escape.</param>
    /// <returns>The properly escaped CSV field value.</returns>
    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "";

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}
