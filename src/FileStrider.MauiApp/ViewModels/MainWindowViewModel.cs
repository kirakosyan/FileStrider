using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;
using FileStrider.MauiApp.Models;
using System.Collections.ObjectModel;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace FileStrider.MauiApp.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const string SourceRepositoryUrl = "https://github.com/kirakosyan/FileStrider";

    private enum ScanStatsMode
    {
        Ready,
        InProgress,
        Completed
    }

    private readonly IFileSystemScanner _scanner;
    private readonly IFolderPicker _folderPicker;
    private readonly IExportService _exportService;
    private readonly IShellService _shellService;
    private readonly IConfigurationService _configurationService;
    private readonly ILocalizationService _localizationService;
    private readonly IFileTypeAnalyzer _fileTypeAnalyzer;
    private CancellationTokenSource? _cancellationTokenSource;
    private ScanStatsMode _scanStatsMode = ScanStatsMode.Ready;
    private ScanProgress? _lastProgressSnapshot;
    private int _lastCompletedFilesCount;
    private int _lastCompletedFoldersCount;
    private readonly List<FileTypeStats> _lastFileTypeStatistics = new();

    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private bool isScanning = false;

    [ObservableProperty]
    private bool canScan = true;

    [ObservableProperty]
    private bool canCancel = false;

    [ObservableProperty]
    private string currentPath = "";

    [ObservableProperty]
    private string scanStats = "";

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
    public ObservableCollection<TreemapItem> TreemapItems { get; } = new();
    public ObservableCollection<FileTypeStats> FileTypeStatistics { get; } = new();

    public MainWindowViewModel(
        IFileSystemScanner scanner,
        IFolderPicker folderPicker,
        IExportService exportService,
        IShellService shellService,
        IConfigurationService configurationService,
        ILocalizationService localizationService,
        IFileTypeAnalyzer fileTypeAnalyzer)
    {
        _scanner = scanner;
        _folderPicker = folderPicker;
        _exportService = exportService;
        _shellService = shellService;
        _configurationService = configurationService;
        _localizationService = localizationService;
        _fileTypeAnalyzer = fileTypeAnalyzer;

        // Subscribe to localization changes
        _localizationService.PropertyChanged += (s, e) => UpdateLocalizedProperties();

        SelectedPath = Directory.GetCurrentDirectory();
        LoadDefaultSettings();
        UpdateLocalizedProperties();
    }

    private void UpdateLocalizedProperties()
    {
        Title = _localizationService.GetString("AppTitle");
        RefreshLocalizedScanStats();
        RefreshLocalizedFileTypeStatistics();

        // Notify about all localized properties
        OnPropertyChanged(nameof(AvailableLanguages));
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(AppSubtitle));
        OnPropertyChanged(nameof(ScanPathLabel));
        OnPropertyChanged(nameof(BrowseLabel));
        OnPropertyChanged(nameof(QuickScanLabel));
        OnPropertyChanged(nameof(StartScanLabel));
        OnPropertyChanged(nameof(TopNLabel));
        OnPropertyChanged(nameof(IncludeHiddenLabel));
        OnPropertyChanged(nameof(FoldersOnlyLabel));
        OnPropertyChanged(nameof(FoldersOnlyTooltip));
        OnPropertyChanged(nameof(MinSizeLabel));
        OnPropertyChanged(nameof(LargestFilesLabel));
        OnPropertyChanged(nameof(LargestFoldersLabel));
        OnPropertyChanged(nameof(CancelLabel));
        OnPropertyChanged(nameof(ExportCsvLabel));
        OnPropertyChanged(nameof(ExportJsonLabel));
        OnPropertyChanged(nameof(AboutLabel));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(ModifiedLabel));
        OnPropertyChanged(nameof(ItemsLabel));
        OnPropertyChanged(nameof(BytesLabel));
        OnPropertyChanged(nameof(DiskUsageVisualizationLabel));
        OnPropertyChanged(nameof(FileTypeBreakdownLabel));
        OnPropertyChanged(nameof(AverageSizeLabel));
        OnPropertyChanged(nameof(ShareOfScanLabel));
        OnPropertyChanged(nameof(TreemapEmptyText));
        OnPropertyChanged(nameof(FileSizeFormat));
        OnPropertyChanged(nameof(FileModifiedFormat));
        OnPropertyChanged(nameof(FolderSizeFormat));
        OnPropertyChanged(nameof(FolderItemsFormat));
        OnPropertyChanged(nameof(FolderModifiedFormat));
    }

    private void RefreshLocalizedScanStats()
    {
        if (_scanStatsMode == ScanStatsMode.InProgress && _lastProgressSnapshot is not null)
        {
            ScanStats = string.Format(
                _localizationService.GetString("ScanProgressStats"),
                _lastProgressSnapshot.FilesScanned,
                _lastProgressSnapshot.FoldersScanned,
                FormatBytes(_lastProgressSnapshot.BytesProcessed),
                _lastProgressSnapshot.Elapsed);
            return;
        }

        if (_scanStatsMode == ScanStatsMode.Completed)
        {
            ScanStats = string.Format(
                _localizationService.GetString("ScanCompletedStats"),
                _lastCompletedFilesCount,
                _lastCompletedFoldersCount);
            return;
        }

        ScanStats = _localizationService.GetString("ReadyToScan");
    }

    private void SetReadyScanStats()
    {
        _scanStatsMode = ScanStatsMode.Ready;
        _lastProgressSnapshot = null;
        RefreshLocalizedScanStats();
    }

    private void SetProgressScanStats(ScanProgress progress)
    {
        _scanStatsMode = ScanStatsMode.InProgress;
        _lastProgressSnapshot = progress;
        RefreshLocalizedScanStats();
    }

    private void SetCompletedScanStats(int filesCount, int foldersCount)
    {
        _scanStatsMode = ScanStatsMode.Completed;
        _lastProgressSnapshot = null;
        _lastCompletedFilesCount = filesCount;
        _lastCompletedFoldersCount = foldersCount;
        RefreshLocalizedScanStats();
    }

    private void RefreshLocalizedFileTypeStatistics()
    {
        if (_lastFileTypeStatistics.Count == 0)
        {
            FileTypeStatistics.Clear();
            return;
        }

        var localized = _lastFileTypeStatistics
            .Select(LocalizeFileTypeStat)
            .ToList();

        FileTypeStatistics.Clear();
        foreach (var item in localized)
        {
            FileTypeStatistics.Add(item);
        }
    }

    private FileTypeStats LocalizeFileTypeStat(FileTypeStats stats)
    {
        return new FileTypeStats
        {
            Extension = stats.Extension,
            Category = GetLocalizedCategory(stats.Category),
            FileCount = stats.FileCount,
            TotalSize = stats.TotalSize,
            Percentage = stats.Percentage
        };
    }

    private string GetLocalizedCategory(string category)
    {
        var key = category switch
        {
            "Images" => "CategoryImages",
            "Videos" => "CategoryVideos",
            "Audio" => "CategoryAudio",
            "Documents" => "CategoryDocuments",
            "Archives" => "CategoryArchives",
            "Code" => "CategoryCode",
            "Config" => "CategoryConfig",
            "Database" => "CategoryDatabase",
            "Logs" => "CategoryLogs",
            "Executables" => "CategoryExecutables",
            "Other" => "CategoryOther",
            _ => null
        };

        return key is null ? category : _localizationService.GetString(key);
    }

    // Localization Properties
    public IReadOnlyList<LanguageInfo> AvailableLanguages => _localizationService.AvailableLanguages;

    public LanguageInfo SelectedLanguage
    {
        get => _localizationService.AvailableLanguages.First(l => l.Code == _localizationService.CurrentLanguage);
        set => _localizationService.ChangeLanguage(value.Code);
    }

    // Localized UI Strings
    public string AppSubtitle => _localizationService.GetString("AppSubtitle");
    public string ScanPathLabel => _localizationService.GetString("ScanPath");
    public string BrowseLabel => _localizationService.GetString("Browse");
    public string QuickScanLabel => _localizationService.GetString("QuickScan");
    public string StartScanLabel => _localizationService.GetString("StartScan");
    public string TopNLabel => _localizationService.GetString("TopN");
    public string IncludeHiddenLabel => _localizationService.GetString("IncludeHidden");
    public string FoldersOnlyLabel => _localizationService.GetString("FoldersOnly");
    public string FoldersOnlyTooltip => _localizationService.GetString("FoldersOnlyTooltip");
    public string MinSizeLabel => _localizationService.GetString("MinSize");
    public string LargestFilesLabel => _localizationService.GetString("LargestFiles");
    public string LargestFoldersLabel => _localizationService.GetString("LargestFolders");
    public string CancelLabel => _localizationService.GetString("Cancel");
    public string ExportCsvLabel => _localizationService.GetString("ExportCsv");
    public string ExportJsonLabel => _localizationService.GetString("ExportJson");
    public string AboutLabel => _localizationService.GetString("About");
    public string LanguageLabel => _localizationService.GetString("Language");
    public string SizeLabel => _localizationService.GetString("Size");
    public string ModifiedLabel => _localizationService.GetString("Modified");
    public string ItemsLabel => _localizationService.GetString("Items");
    public string BytesLabel => _localizationService.GetString("Bytes");
    public string DiskUsageVisualizationLabel => _localizationService.GetString("DiskUsageVisualization");
    public string FileTypeBreakdownLabel => _localizationService.GetString("FileTypeBreakdown");
    public string AverageSizeLabel => _localizationService.GetString("AverageSize");
    public string ShareOfScanLabel => _localizationService.GetString("ShareOfScan");
    public string TreemapEmptyText => _localizationService.GetString("TreemapEmptyText");

    // Format strings for templates
    public string FileSizeFormat => $"{SizeLabel} {{0:N0}} {BytesLabel}";
    public string FileModifiedFormat => $"{ModifiedLabel} {{0:yyyy-MM-dd HH:mm}}";
    public string FolderSizeFormat => $"{SizeLabel} {{0:N0}} {BytesLabel}";
    public string FolderItemsFormat => $"{ItemsLabel} {{0:N0}}";
    public string FolderModifiedFormat => $"{ModifiedLabel} {{0:yyyy-MM-dd HH:mm}}";

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
            var normalizedPath = NormalizeSelectedPath(path);
            if (!string.IsNullOrEmpty(normalizedPath))
            {
                SelectedPath = normalizedPath;
            }
        }
    }

    private static string NormalizeSelectedPath(string path)
    {
        var trimmed = path?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        if (OperatingSystem.IsWindows())
        {
            var normalized = trimmed.Replace('/', Path.DirectorySeparatorChar);

            if (IsDriveRoot(normalized))
            {
                return $"{char.ToUpperInvariant(normalized[0])}:{Path.DirectorySeparatorChar}";
            }

            trimmed = normalized;
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private static bool IsDriveRoot(string path)
    {
        if (string.IsNullOrEmpty(path) || !OperatingSystem.IsWindows())
        {
            return false;
        }

        if (path.Length == 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return true;
        }

        return path.Length == 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == Path.DirectorySeparatorChar || path[2] == Path.AltDirectorySeparatorChar);
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task StartScan()
    {
        if (string.IsNullOrEmpty(SelectedPath))
        {
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), _localizationService.GetString("SelectFolderToScan"));
            await box.ShowAsync();
            return;
        }

        if (!Directory.Exists(SelectedPath))
        {
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), _localizationService.GetString("DirectoryNotExist"));
            await box.ShowAsync();
            return;
        }

        TopFiles.Clear();
        TopFolders.Clear();
        FileTypeStatistics.Clear();
        TreemapItems.Clear();
        _lastFileTypeStatistics.Clear();
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
            SetProgressScanStats(p);
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

            _lastFileTypeStatistics.Clear();
            _lastFileTypeStatistics.AddRange(results.FileTypeStatistics);
            RefreshLocalizedFileTypeStatistics();

            // Populate treemap data
            PopulateTreemapData(results);

            HasResults = TopFiles.Any() || TopFolders.Any();

            if (results.WasCancelled)
            {
                var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Cancelled"), _localizationService.GetString("ScanCancelledMessage"));
                await box.ShowAsync();
                SetReadyScanStats();
            }
            else if (!string.IsNullOrEmpty(results.ErrorMessage))
            {
                var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), string.Format(_localizationService.GetString("ScanFailedMessage"), results.ErrorMessage));
                await box.ShowAsync();
                SetReadyScanStats();
            }
            else
            {
                var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Complete"), _localizationService.GetString("ScanCompletedMessage"), ButtonEnum.Ok, Icon.Success);
                await box.ShowAsync();
                SetCompletedScanStats(TopFiles.Count, TopFolders.Count);
            }
        }
        catch (OperationCanceledException)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Cancelled"), _localizationService.GetString("ScanCancelledMessage"));
            await box.ShowAsync();
            SetReadyScanStats();
        }
        catch (UnauthorizedAccessException ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), string.Format(_localizationService.GetString("AccessDeniedMessage"), ex.Message));
            await box.ShowAsync();
            SetReadyScanStats();
        }
        catch (DirectoryNotFoundException ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), string.Format(_localizationService.GetString("DirectoryNotFoundMessage"), ex.Message));
            await box.ShowAsync();
            SetReadyScanStats();
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), string.Format(_localizationService.GetString("ScanFailedMessage"), ex.Message));
            await box.ShowAsync();
            SetReadyScanStats();
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
        try
        {
            _cancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Token source was already disposed, which is fine
        }
    }

    [RelayCommand]
    private async Task ShowAbout()
    {
        var sourceLinkButton = new Button
        {
            Content = new TextBlock
            {
                Text = SourceRepositoryUrl,
                TextDecorations = TextDecorations.Underline
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = Brushes.DodgerBlue,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        sourceLinkButton.Click += async (_, _) =>
        {
            try
            {
                await _shellService.OpenUrlAsync(SourceRepositoryUrl);
            }
            catch (Exception ex)
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    _localizationService.GetString("Error"),
                    string.Format(_localizationService.GetString("FailedToOpenLink"), ex.Message));
                await box.ShowAsync();
            }
        };

        var closeButton = new Button
        {
            Content = _localizationService.GetString("Close"),
            MinWidth = 90
        };

        var aboutWindow = new Window
        {
            Title = _localizationService.GetString("AboutTitle"),
            Width = 620,
            Height = 230,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(20),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = _localizationService.GetString("AboutDescription"),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = _localizationService.GetString("SourceCode"),
                                    VerticalAlignment = VerticalAlignment.Center
                                },
                                sourceLinkButton
                            }
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children = { closeButton }
                        }
                    }
                }
            }
        };

        closeButton.Click += (_, _) => aboutWindow.Close();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is Window mainWindow)
        {
            await aboutWindow.ShowDialog(mainWindow);
            return;
        }

        aboutWindow.Show();
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
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("NoResults"), _localizationService.GetString("NoResultsMessage"));
            await box.ShowAsync();
            return;
        }

        // Show save file dialog
        string? filePath = null;
        var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime
            ? desktopLifetime.MainWindow
            : null;

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var suggestedFileName = $"FileStrider_Results_{timestamp}";

        if (topLevel?.StorageProvider is { } storageProvider)
        {
            var fileType = format == "csv"
                ? new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } }
                : new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } };

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = _localizationService.GetString("SaveFileDialogTitle"),
                SuggestedFileName = suggestedFileName,
                FileTypeChoices = new[] { fileType },
                DefaultExtension = format
            });

            if (file is null) return;

            filePath = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(filePath))
            {
                filePath = file.Path?.LocalPath;
            }
        }

        if (string.IsNullOrEmpty(filePath))
        {
            filePath = Path.Combine(Directory.GetCurrentDirectory(), $"{suggestedFileName}.{format}");
        }

        var results = new ScanResults
        {
            TopFiles = TopFiles.ToList(),
            TopFolders = TopFolders.ToList(),
            FileTypeStatistics = _lastFileTypeStatistics.ToList(),
            Progress = new ScanProgress { FilesScanned = TopFiles.Count, FoldersScanned = TopFolders.Count }
        };

        try
        {
            if (format == "csv")
                await _exportService.ExportToCsvAsync(results, filePath);
            else
                await _exportService.ExportToJsonAsync(results, filePath);

            var successBox = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Success"), string.Format(_localizationService.GetString("ExportSuccessMessage"), filePath), ButtonEnum.Ok, Icon.Success);
            await successBox.ShowAsync();
        }
        catch (Exception ex)
        {
            var errorBox = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), string.Format(_localizationService.GetString("ExportFailedMessage"), ex.Message));
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
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), string.Format(_localizationService.GetString("FailedToOpenLocation"), ex.Message));
            await box.ShowAsync();
        }
    }

    [RelayCommand]
    private async Task CopyFilePath(FileItem file)
    {
        try
        {
            await _shellService.CopyToClipboardAsync(file.FullPath);
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Success"), _localizationService.GetString("PathCopiedMessage"), ButtonEnum.Ok, Icon.Success);
            await box.ShowAsync();
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), string.Format(_localizationService.GetString("CopyFailedMessage"), ex.Message));
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
            var box = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), string.Format(_localizationService.GetString("FailedToOpenLocation"), ex.Message));
            await box.ShowAsync();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double size = bytes;

        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return $"{size:0.##} {suffixes[i]}";
    }

    private static string TruncatePath(string path, int maxLength)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
            return path;

        return "..." + path.Substring(path.Length - maxLength + 3);
    }

    /// <summary>
    /// Populates the treemap visualization data from scan results.
    /// </summary>
    /// <param name="results">The scan results to convert to treemap items.</param>
    private void PopulateTreemapData(ScanResults results)
    {
        TreemapItems.Clear();

        // Add top folders to treemap
        foreach (var folder in results.TopFolders.Take(20)) // Limit to top 20 for performance
        {
            var treemapItem = new TreemapItem
            {
                Name = folder.Name,
                Size = folder.RecursiveSize,
                FullPath = folder.FullPath,
                IsFile = false,
                Category = "Folders",
                Color = TreemapColors.GetColorForCategory("Folders")
            };
            TreemapItems.Add(treemapItem);
        }

        // Add top files to treemap if not in folders-only mode
        foreach (var file in results.TopFiles.Take(10)) // Limit to top 10 files
        {
            var category = _fileTypeAnalyzer.GetFileCategory(file.Type);
            var treemapItem = new TreemapItem
            {
                Name = file.Name,
                Size = file.Size,
                FullPath = file.FullPath,
                IsFile = true,
                Category = category,
                Color = TreemapColors.GetColorForCategory(category)
            };
            TreemapItems.Add(treemapItem);
        }
    }
}
