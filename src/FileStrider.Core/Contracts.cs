using FileStrider.Core.Models;

namespace FileStrider.Core.Contracts;

public interface IFileSystemScanner
{
    Task<ScanResults> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

public interface IFolderPicker
{
    Task<string?> PickFolderAsync();
}

public interface IShellService
{
    Task OpenFileLocationAsync(string filePath);
    Task CopyToClipboardAsync(string text);
}

public interface IExportService
{
    Task ExportToCsvAsync(ScanResults results, string filePath);
    Task ExportToJsonAsync(ScanResults results, string filePath);
}

public interface IConfigurationService
{
    Task<ScanOptions> LoadDefaultOptionsAsync();
    Task SaveDefaultOptionsAsync(ScanOptions options);
}

public interface ITopItemsTracker<T>
{
    void Add(T item);
    IReadOnlyList<T> GetTop();
    void Clear();
}