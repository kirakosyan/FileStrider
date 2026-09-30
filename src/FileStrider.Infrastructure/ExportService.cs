using System.Globalization;
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
    /// Includes scan metadata and proper CSV escaping for fields containing special characters.
    /// </summary>
    /// <param name="results">The scan results to export.</param>
    /// <param name="filePath">The file path where the CSV file should be saved.</param>
    /// <returns>A task that represents the asynchronous export operation.</returns>
    public async Task ExportToCsvAsync(ScanResults results, string filePath)
    {
        using var writer = new StreamWriter(filePath);
        var metadata = CreateMetadata(results);
        await writer.WriteLineAsync("Scan Metadata");
        await writer.WriteLineAsync("Field,Value");
        foreach (var (field, value) in metadata.CsvFields())
            await writer.WriteLineAsync($"{field},{EscapeCsvField(value)}");
        await writer.WriteLineAsync();
        
        // Export top files
        await writer.WriteLineAsync("Top Files");
        await writer.WriteLineAsync("Name,Full Path,Size (bytes),Size (MB),Type,Last Modified");
        
        foreach (var file in results.TopFiles)
        {
            var sizeInMB = file.Size / (1024.0 * 1024.0);
            await writer.WriteLineAsync(FormattableString.Invariant($"{EscapeCsvField(file.Name)},{EscapeCsvField(file.FullPath)},{file.Size},{sizeInMB:F2},{EscapeCsvField(file.Type)},{file.LastModified:yyyy-MM-dd HH:mm:ss}"));
        }
        
        await writer.WriteLineAsync();
        
        // Export top folders
        await writer.WriteLineAsync("Top Folders");
        await writer.WriteLineAsync("Name,Full Path,Recursive Size (bytes),Recursive Size (MB),Item Count,Last Modified");
        
        foreach (var folder in results.TopFolders)
        {
            var sizeInMB = folder.RecursiveSize / (1024.0 * 1024.0);
            await writer.WriteLineAsync(FormattableString.Invariant($"{EscapeCsvField(folder.Name)},{EscapeCsvField(folder.FullPath)},{folder.RecursiveSize},{sizeInMB:F2},{folder.ItemCount},{folder.LastModified:yyyy-MM-dd HH:mm:ss}"));
        }

        // Export file type statistics if available
        if (results.FileTypeStatistics.Any())
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("File Type Statistics");
            await writer.WriteLineAsync("Extension,Category,File Count,Total Size (bytes),Total Size (MB),Percentage,Average Size (bytes)");
            
            foreach (var stat in results.FileTypeStatistics)
            {
                var totalSizeInMB = stat.TotalSize / (1024.0 * 1024.0);
                await writer.WriteLineAsync(FormattableString.Invariant($"{EscapeCsvField(stat.Extension)},{EscapeCsvField(stat.Category)},{stat.FileCount},{stat.TotalSize},{totalSizeInMB:F2},{stat.Percentage:F1}%,{stat.AverageSize}"));
            }
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
        var metadata = CreateMetadata(results);
        var exportData = new
        {
            metadata.ExportedAt,
            metadata.RootPath,
            metadata.ScanCompleted,
            metadata.ScanCancelled,
            metadata.ErrorMessage,
            metadata.Progress,
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
            }),
            FileTypeStatistics = results.FileTypeStatistics.Select(s => new
            {
                s.Extension,
                s.Category,
                s.FileCount,
                s.TotalSize,
                TotalSizeInMB = s.TotalSize / (1024.0 * 1024.0),
                s.Percentage,
                s.AverageSize,
                AverageSizeInMB = s.AverageSize / (1024.0 * 1024.0)
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

    private static ScanMetadata CreateMetadata(ScanResults results)
    {
        var progress = results.Progress.CreateSnapshot();
        return new(DateTime.UtcNow, results.RootPath, results.IsCompleted, results.WasCancelled, results.ErrorMessage,
            new(progress.FilesScanned, progress.FoldersScanned, progress.BytesProcessed, progress.Elapsed,
                progress.SkippedItems, progress.InaccessibleItems, progress.OfflineItems, progress.ExcludedItems,
                progress.DepthLimitedDirectories, progress.HasIncompleteCoverage));
    }

    private sealed record ProgressMetadata(int FilesScanned, int FoldersScanned, long BytesProcessed, TimeSpan Elapsed,
        int SkippedItems, int InaccessibleItems, int OfflineItems, int ExcludedItems,
        int DepthLimitedDirectories, bool HasIncompleteCoverage);

    private sealed record ScanMetadata(DateTime ExportedAt, string RootPath, bool ScanCompleted, bool ScanCancelled,
        string? ErrorMessage, ProgressMetadata Progress)
    {
        public IEnumerable<(string Field, string Value)> CsvFields()
        {
            yield return ("Exported At (UTC)", ExportedAt.ToString("O", CultureInfo.InvariantCulture));
            yield return ("Root Path", RootPath);
            yield return ("Scan Completed", ScanCompleted.ToString());
            yield return ("Scan Cancelled", ScanCancelled.ToString());
            yield return ("Error Message", ErrorMessage ?? "");
            yield return ("Files Scanned", Progress.FilesScanned.ToString(CultureInfo.InvariantCulture));
            yield return ("Folders Scanned", Progress.FoldersScanned.ToString(CultureInfo.InvariantCulture));
            yield return ("Bytes Processed", Progress.BytesProcessed.ToString(CultureInfo.InvariantCulture));
            yield return ("Elapsed", Progress.Elapsed.ToString("c", CultureInfo.InvariantCulture));
            yield return ("Skipped Items", Progress.SkippedItems.ToString(CultureInfo.InvariantCulture));
            yield return ("Inaccessible Items", Progress.InaccessibleItems.ToString(CultureInfo.InvariantCulture));
            yield return ("Offline Items", Progress.OfflineItems.ToString(CultureInfo.InvariantCulture));
            yield return ("Excluded Items", Progress.ExcludedItems.ToString(CultureInfo.InvariantCulture));
            yield return ("Depth Limited Directories", Progress.DepthLimitedDirectories.ToString(CultureInfo.InvariantCulture));
            yield return ("Incomplete Coverage", Progress.HasIncompleteCoverage.ToString());
        }
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

        // Delimiter escaping alone does not prevent spreadsheet formula evaluation.
        var trimmed = field.TrimStart();
        if (trimmed.Length > 0 && ("=+-@".Contains(trimmed[0]) || field[0] is '\t' or '\r' or '\n'))
            field = "'" + field;

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}
