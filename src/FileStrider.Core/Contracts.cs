using FileStrider.Core.Models;

namespace FileStrider.Core.Contracts;

/// <summary>
/// Provides functionality to scan the file system and discover files and folders.
/// </summary>
public interface IFileSystemScanner
{
    /// <summary>
    /// Scans the file system starting from the specified root path and returns the largest files and folders.
    /// </summary>
    /// <param name="options">The configuration options for the scan operation.</param>
    /// <param name="progress">Optional progress reporter to receive real-time updates during the scan.</param>
    /// <param name="cancellationToken">Token to request cancellation of the scan operation.</param>
    /// <returns>A task that represents the asynchronous scan operation, containing the scan results.</returns>
    Task<ScanResults> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides functionality to prompt the user to select a folder for scanning.
/// </summary>
public interface IFolderPicker
{
    /// <summary>
    /// Prompts the user to select a folder and returns the selected path.
    /// </summary>
    /// <returns>A task that represents the asynchronous folder selection operation, returning the selected folder path or null if cancelled.</returns>
    Task<string?> PickFolderAsync();
}

/// <summary>
/// Provides cross-platform shell integration services for file system operations.
/// </summary>
public interface IShellService
{
    /// <summary>
    /// Opens the file or folder location in the platform's default file manager (Explorer, Finder, etc.).
    /// </summary>
    /// <param name="filePath">The path of the file or folder to open in the file manager.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task OpenFileLocationAsync(string filePath);
    
    /// <summary>
    /// Copies the specified text to the system clipboard.
    /// </summary>
    /// <param name="text">The text to copy to the clipboard.</param>
    /// <returns>A task that represents the asynchronous clipboard operation.</returns>
    Task CopyToClipboardAsync(string text);
}

/// <summary>
/// Provides functionality to export scan results to various file formats.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Exports the scan results to a CSV (Comma Separated Values) file.
    /// </summary>
    /// <param name="results">The scan results to export.</param>
    /// <param name="filePath">The file path where the CSV file should be saved.</param>
    /// <returns>A task that represents the asynchronous export operation.</returns>
    Task ExportToCsvAsync(ScanResults results, string filePath);
    
    /// <summary>
    /// Exports the scan results to a JSON file.
    /// </summary>
    /// <param name="results">The scan results to export.</param>
    /// <param name="filePath">The file path where the JSON file should be saved.</param>
    /// <returns>A task that represents the asynchronous export operation.</returns>
    Task ExportToJsonAsync(ScanResults results, string filePath);
}

/// <summary>
/// Provides functionality to load and save application configuration settings.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Loads the default scan options from persistent storage.
    /// </summary>
    /// <returns>A task that represents the asynchronous load operation, returning the configured scan options or defaults if none exist.</returns>
    Task<ScanOptions> LoadDefaultOptionsAsync();
    
    /// <summary>
    /// Saves the specified scan options as the new default configuration.
    /// </summary>
    /// <param name="options">The scan options to save as defaults.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    Task SaveDefaultOptionsAsync(ScanOptions options);
}

/// <summary>
/// Provides functionality to track the top N items of a specific type based on a comparison function.
/// </summary>
/// <typeparam name="T">The type of items to track.</typeparam>
public interface ITopItemsTracker<T>
{
    /// <summary>
    /// Adds an item to the tracker. If the item should be in the top N, it will be stored and maintained in sorted order.
    /// </summary>
    /// <param name="item">The item to add to the tracker.</param>
    void Add(T item);
    
    /// <summary>
    /// Gets the current top items in order (best to worst according to the comparison function).
    /// </summary>
    /// <returns>A read-only list of the top items currently being tracked.</returns>
    IReadOnlyList<T> GetTop();
    
    /// <summary>
    /// Clears all tracked items from the tracker.
    /// </summary>
    void Clear();
}