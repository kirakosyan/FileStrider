using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;
using System.Collections.ObjectModel;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace FileStrider.MauiApp.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileSystemScanner _scanner;
    private readonly IFolderPicker _folderPicker;
    private readonly IExportService _exportService;
    private readonly IShellService _shellService;
    private readonly IConfigurationService _configurationService;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private string title = "FileStrider - File & Folder Discovery Tool";

    [ObservableProperty]
    private bool isScanning = false;

    [ObservableProperty]
    private bool canScan = true;

    [ObservableProperty]
    private bool canCancel = false;

    [ObservableProperty]
    private string currentPath = "";

    [ObservableProperty]
    private string scanStats = "Ready to scan...";

    [ObservableProperty]
    private string selectedPath = "";

    [ObservableProperty]
    private int topN = 20;

    [ObservableProperty]
    private bool includeHidden = false;

    [ObservableProperty]
    private long minFileSize = 0;

    [ObservableProperty]
    private bool foldersOnly = false;

    [ObservableProperty]
    private bool hasResults = false;

    public ObservableCollection<FileItem> TopFiles { get; } = new();
    public ObservableCollection<FolderItem> TopFolders { get; } = new();

    public MainWindowViewModel(
        IFileSystemScanner scanner,
        IFolderPicker folderPicker,
        IExportService exportService,
        IShellService shellService,
        IConfigurationService configurationService)
    {
        _scanner = scanner;
        _folderPicker = folderPicker;
        _exportService = exportService;
        _shellService = shellService;
        _configurationService = configurationService;
        
        SelectedPath = Directory.GetCurrentDirectory();
        LoadDefaultSettings();
    }

    private async void LoadDefaultSettings()
    {
        var options = await _configurationService.LoadDefaultOptionsAsync();
        TopN = options.TopN;
        IncludeHidden = options.IncludeHidden;
        MinFileSize = options.MinFileSize;
    }

    [RelayCommand]
    private async Task QuickScan()
    {
        SelectedPath = Directory.GetCurrentDirectory();
        await StartScan();
    }

    [RelayCommand]
    private async Task SelectFolder()
    {
        var path = await _folderPicker.PickFolderAsync();
        if (!string.IsNullOrEmpty(path))
        {
            SelectedPath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task StartScan()
    {
        if (string.IsNullOrEmpty(SelectedPath))
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Error", "Please select a folder to scan");
            await box.ShowAsync();
            return;
        }

        if (!Directory.Exists(SelectedPath))
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Error", "Selected directory does not exist");
            await box.ShowAsync();
            return;
        }

        TopFiles.Clear();
        TopFolders.Clear();
        HasResults = false;

        var options = new ScanOptions
        {
            RootPath = SelectedPath,
            TopN = TopN,
            IncludeHidden = IncludeHidden,
            MinFileSize = MinFileSize,
            FoldersOnly = FoldersOnly
        };

        IsScanning = true;
        CanScan = false;
        CanCancel = true;
        CancelScanCommand.NotifyCanExecuteChanged();

        _cancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<ScanProgress>(p =>
        {
            CurrentPath = TruncatePath(p.CurrentPath, 50);
            ScanStats = $"Files: {p.FilesScanned:N0} | Folders: {p.FoldersScanned:N0} | " +
                       $"Size: {FormatBytes(p.BytesProcessed)} | Time: {p.Elapsed:mm\\:ss}";
        });

        try
        {
            var results = await _scanner.ScanAsync(options, progress, _cancellationTokenSource.Token);

            foreach (var file in results.TopFiles)
            {
                TopFiles.Add(file);
            }

            foreach (var folder in results.TopFolders)
            {
                TopFolders.Add(folder);
            }

            HasResults = TopFiles.Any() || TopFolders.Any();

            if (results.WasCancelled)
            {
                var box = MessageBoxManager.GetMessageBoxStandard("Cancelled", "Scan was cancelled by user");
                await box.ShowAsync();
            }
            else if (!string.IsNullOrEmpty(results.ErrorMessage))
            {
                var box = MessageBoxManager.GetMessageBoxStandard("Error", $"Scan failed: {results.ErrorMessage}");
                await box.ShowAsync();
            }
            else
            {
                var box = MessageBoxManager.GetMessageBoxStandard("Complete", "Scan completed successfully!", ButtonEnum.Ok, Icon.Success);
                await box.ShowAsync();
                ScanStats = $"Scan completed! Found {TopFiles.Count} files and {TopFolders.Count} folders";
            }
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Error", $"Scan failed: {ex.Message}");
            await box.ShowAsync();
        }
        finally
        {
            IsScanning = false;
            CanScan = true;
            CanCancel = false;
            CancelScanCommand.NotifyCanExecuteChanged();
            CurrentPath = "";
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelScan()
    {
        _cancellationTokenSource?.Cancel();
    }

    [RelayCommand]
    private async Task ExportToCsv()
    {
        await ExportResults("csv");
    }

    [RelayCommand]
    private async Task ExportToJson()
    {
        await ExportResults("json");
    }

    private async Task ExportResults(string format)
    {
        if (!HasResults)
        {
            var box = MessageBoxManager.GetMessageBoxStandard("No Results", "No scan results to export");
            await box.ShowAsync();
            return;
        }

        var results = new ScanResults
        {
            TopFiles = TopFiles.ToList(),
            TopFolders = TopFolders.ToList(),
            Progress = new ScanProgress { FilesScanned = TopFiles.Count, FoldersScanned = TopFolders.Count }
        };

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"FileStrider_Results_{timestamp}.{format}";
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);

        try
        {
            if (format == "csv")
                await _exportService.ExportToCsvAsync(results, filePath);
            else
                await _exportService.ExportToJsonAsync(results, filePath);

            var successBox = MessageBoxManager.GetMessageBoxStandard("Success", $"Results exported to: {filePath}", ButtonEnum.Ok, Icon.Success);
            await successBox.ShowAsync();
        }
        catch (Exception ex)
        {
            var errorBox = MessageBoxManager.GetMessageBoxStandard("Error", $"Export failed: {ex.Message}");
            await errorBox.ShowAsync();
        }
    }

    [RelayCommand]
    private async Task OpenFileLocation(FileItem file)
    {
        try
        {
            await _shellService.OpenFileLocationAsync(file.FullPath);
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to open location: {ex.Message}");
            await box.ShowAsync();
        }
    }

    [RelayCommand]
    private async Task CopyFilePath(FileItem file)
    {
        try
        {
            await _shellService.CopyToClipboardAsync(file.FullPath);
            var box = MessageBoxManager.GetMessageBoxStandard("Copied", "File path copied to clipboard", ButtonEnum.Ok, Icon.Success);
            await box.ShowAsync();
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to copy to clipboard: {ex.Message}");
            await box.ShowAsync();
        }
    }

    [RelayCommand]
    private async Task OpenFolderLocation(FolderItem folder)
    {
        try
        {
            await _shellService.OpenFileLocationAsync(folder.FullPath);
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to open location: {ex.Message}");
            await box.ShowAsync();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i;
        double dblSByte = bytes;
        
        for (i = 0; i < suffixes.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return $"{dblSByte:0.##} {suffixes[i]}";
    }

    private static string TruncatePath(string path, int maxLength)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
            return path;
        
        return "..." + path.Substring(path.Length - maxLength + 3);
    }
}