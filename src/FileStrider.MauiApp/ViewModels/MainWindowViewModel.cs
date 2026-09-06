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

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private int _scanActive;
    private ScanOptions _loadedOptions = new();
    public Task Initialization { get; }
    public ScanResults? LastResults { get; private set; }
    public ObservableCollection<string> RecentPaths { get; } = new();
    public IReadOnlyList<SizeUnit> SizeUnits { get; } = new[] {
        new SizeUnit("B", 1), new SizeUnit("KiB", 1024), new SizeUnit("MiB", 1048576), new SizeUnit("GiB", 1073741824) };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MaximumMinFileSize))]
    private SizeUnit selectedSizeUnit = new("B", 1);
    public decimal MaximumMinFileSize => long.MaxValue / SelectedSizeUnit.Multiplier;
    public int MaximumConcurrency => Math.Max(1, Environment.ProcessorCount * 2);
    [ObservableProperty] private decimal? maxDepth;
    [ObservableProperty] private decimal? concurrencyLimit = Math.Min(8, Environment.ProcessorCount);
    [ObservableProperty] private bool followSymlinks;
    [ObservableProperty] private string excludePatternsText = "";
    [ObservableProperty] private string excludeDirectoriesText = "";
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string treemapPath = "";
    [ObservableProperty] private bool canNavigateUp;
    [ObservableProperty] private string? selectedRecentPath;
    private Dictionary<string, FolderItem> _folderIndex = new(PathComparer);
    private ILookup<string, FolderItem>? _children;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
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

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(StartScanCommand), nameof(QuickScanCommand), nameof(SelectFolderCommand), nameof(SaveSettingsCommand))]
    private bool canScan = false;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool canCancel = false;

    [ObservableProperty]
    private string currentPath = "";

    [ObservableProperty]
    private string scanStats = "";

    [ObservableProperty]
    private string selectedPath = "";

    [ObservableProperty]
    private decimal? topN = 20;

    [ObservableProperty]
    private bool includeHidden = false;

    [ObservableProperty]
    private decimal? minFileSize = 0;

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
        _localizationService.PropertyChanged += OnLanguageChanged;

        SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Initialization = InitializeAsync();
        UpdateLocalizedProperties();
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateLocalizedProperties();
    public void Dispose()
    {
        _localizationService.PropertyChanged -= OnLanguageChanged;
        _cancellationTokenSource?.Cancel();
    }
    partial void OnSelectedSizeUnitChanged(SizeUnit? oldValue, SizeUnit newValue)
    {
        if (oldValue is not null && newValue is not null)
            MinFileSize = (MinFileSize ?? 0) * oldValue.Multiplier / newValue.Multiplier;
    }
    partial void OnSelectedRecentPathChanged(string? value)
    {
        if (CanScan && !string.IsNullOrWhiteSpace(value)) SelectedPath = value;
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
        foreach (var property in new[] { nameof(RecentFoldersLabel), nameof(AdvancedOptionsLabel), nameof(DepthLabel),
            nameof(ConcurrencyLabel), nameof(FollowSymlinksLabel), nameof(ExcludePatternsLabel), nameof(ExcludeDirectoriesLabel),
            nameof(SaveSettingsLabel), nameof(UpLabel), nameof(CoverageSummary), nameof(TreemapHint) })
            OnPropertyChanged(property);
        if (LastResults is not null && !string.IsNullOrEmpty(TreemapPath)) NavigateTreemap(TreemapPath);
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
                _localizationService.GetString("ScanSummary"),
                _lastCompletedFilesCount,
                _lastCompletedFoldersCount,
                FormatBytes(LastResults?.Progress.BytesProcessed ?? 0),
                LastResults?.Progress.Elapsed ?? TimeSpan.Zero);
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
        OnPropertyChanged(nameof(CoverageSummary));
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

    public string RecentFoldersLabel => _localizationService.GetString("RecentFolders");
    public string AdvancedOptionsLabel => _localizationService.GetString("AdvancedOptions");
    public string DepthLabel => _localizationService.GetString("DepthLimit");
    public string ConcurrencyLabel => _localizationService.GetString("Concurrency");
    public string FollowSymlinksLabel => _localizationService.GetString("FollowSymlinks");
    public string ExcludePatternsLabel => _localizationService.GetString("ExcludePatterns");
    public string ExcludeDirectoriesLabel => _localizationService.GetString("ExcludeDirectories");
    public string SaveSettingsLabel => _localizationService.GetString("SaveSettings");
    public string UpLabel => _localizationService.GetString("Up");
    public string TreemapHint => _localizationService.GetString("TreemapHint");
    public string CoverageSummary
    {
        get
        {
            var p = IsScanning ? _lastProgressSnapshot : LastResults?.Progress;
            if (p is null) return "";
            return (p.HasIncompleteCoverage ? _localizationService.GetString("IncompleteCoverage") + " " : "") +
                string.Format(_localizationService.GetString("CoverageDetails"),
                    p.ExcludedItems, p.InaccessibleItems, p.OfflineItems, p.DepthLimitedDirectories);
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            _loadedOptions = await _configurationService.LoadDefaultOptionsAsync();
            TopN = _loadedOptions.TopN; IncludeHidden = _loadedOptions.IncludeHidden;
            MinFileSize = _loadedOptions.MinFileSize; FoldersOnly = _loadedOptions.FoldersOnly;
            MaxDepth = _loadedOptions.MaxDepth; ConcurrencyLimit = _loadedOptions.ConcurrencyLimit;
            FollowSymlinks = _loadedOptions.FollowSymlinks;
            ExcludePatternsText = string.Join(", ", _loadedOptions.ExcludePatterns ?? []);
            ExcludeDirectoriesText = string.Join(", ", _loadedOptions.ExcludeDirectories ?? []);
            if (Directory.Exists(_loadedOptions.RootPath)) SelectedPath = _loadedOptions.RootPath;
            foreach (var path in _loadedOptions.RecentPaths ?? []) RecentPaths.Add(path);
            _localizationService.ChangeLanguage(_loadedOptions.Language);
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { CanScan = true; }
    }

    public ScanOptions BuildScanOptions()
    {
        var count = TopN ?? 20;
        var amount = MinFileSize ?? 0;
        if (count < 1 || count > 200 || count != decimal.Truncate(count) ||
            amount < 0 || amount > MaximumMinFileSize ||
            MaxDepth < 0 || (MaxDepth.HasValue && MaxDepth != decimal.Truncate(MaxDepth.Value)) ||
            MaxDepth > int.MaxValue || ConcurrencyLimit is null || ConcurrencyLimit < 1 ||
            ConcurrencyLimit > MaximumConcurrency || ConcurrencyLimit != decimal.Truncate(ConcurrencyLimit.Value))
            throw new ArgumentException(_localizationService.GetString("InvalidScanOptions"));

        static HashSet<string> Parse(string value) => new(value.Split(new[] { ',', ';', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        return _loadedOptions with
        {
            RootPath = NormalizeSelectedPath(SelectedPath), TopN = (int)count, IncludeHidden = IncludeHidden,
            MinFileSize = (long)decimal.Ceiling(amount * SelectedSizeUnit.Multiplier), FoldersOnly = FoldersOnly,
            MaxDepth = MaxDepth.HasValue ? (int)MaxDepth.Value : null,
            ConcurrencyLimit = (int)ConcurrencyLimit.Value, FollowSymlinks = FollowSymlinks,
            ExcludePatterns = Parse(ExcludePatternsText), ExcludeDirectories = Parse(ExcludeDirectoriesText),
            Language = _localizationService.CurrentLanguage, RecentPaths = RecentPaths.ToList()
        };
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task SaveSettings()
    {
        try
        {
            _loadedOptions = BuildScanOptions();
            await _configurationService.SaveDefaultOptionsAsync(_loadedOptions);
            StatusMessage = _localizationService.GetString("SettingsSaved");
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task QuickScan()
    {
        if (!CanScan || IsScanning) return;
        SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await StartScan();
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task SelectFolder()
    {
        try
        {
            var path = await _folderPicker.PickFolderAsync();
            if (!string.IsNullOrWhiteSpace(path)) SelectedPath = NormalizeSelectedPath(path);
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
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
        if (Interlocked.CompareExchange(ref _scanActive, 1, 0) != 0) return;
        CancellationTokenSource? scanCancellation = null;
        try
        {
            await Initialization;
            CanScan = false;
            var options = BuildScanOptions();
            if (!Directory.Exists(options.RootPath))
                throw new DirectoryNotFoundException(_localizationService.GetString("DirectoryNotExist"));

            StatusMessage = "";
            TopFiles.Clear(); TopFolders.Clear(); FileTypeStatistics.Clear(); TreemapItems.Clear();
            _lastFileTypeStatistics.Clear(); _folderIndex.Clear(); _children = null;
            LastResults = null; HasResults = false; CanNavigateUp = false; TreemapPath = "";
            IsScanning = true;
            SetProgressScanStats(new ScanProgress());
            scanCancellation = new CancellationTokenSource();
            _cancellationTokenSource = scanCancellation;
            CanCancel = true;

            var recent = new[] { options.RootPath }.Concat(RecentPaths).Distinct(PathComparer).Take(8).ToList();
            RecentPaths.Clear();
            foreach (var path in recent) RecentPaths.Add(path);
            _loadedOptions = options with { RecentPaths = recent };
            try { await _configurationService.SaveDefaultOptionsAsync(_loadedOptions); }
            catch (Exception ex) { StatusMessage = string.Format(_localizationService.GetString("SettingsSaveFailed"), ex.Message); }

            var progress = new Progress<ScanProgress>(p =>
            {
                if (!IsScanning || !ReferenceEquals(_cancellationTokenSource, scanCancellation)) return;
                CurrentPath = p.CurrentPath;
                SetProgressScanStats(p);
            });
            var results = await _scanner.ScanAsync(options, progress, scanCancellation.Token);
            LastResults = results;
            foreach (var file in results.TopFiles) TopFiles.Add(file);
            foreach (var folder in results.TopFolders) TopFolders.Add(folder);
            _lastFileTypeStatistics.AddRange(results.FileTypeStatistics);
            RefreshLocalizedFileTypeStatistics();
            PopulateTreemapData(results);
            HasResults = true;
            SetCompletedScanStats(results.Progress.FilesScanned, results.Progress.FoldersScanned);
            if (results.ErrorMessage is not null)
                StatusMessage = string.Format(_localizationService.GetString("ScanFailedMessage"), results.ErrorMessage);
            else if (results.WasCancelled)
                StatusMessage = _localizationService.GetString("ScanCancelledMessage");
            else if (results.Progress.HasIncompleteCoverage)
                StatusMessage = _localizationService.GetString("IncompleteCoverage");
        }
        catch (OperationCanceledException) { StatusMessage = _localizationService.GetString("ScanCancelledMessage"); }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally
        {
            IsScanning = false; CanCancel = false; CurrentPath = "";
            _cancellationTokenSource = null;
            scanCancellation?.Dispose();
            Interlocked.Exchange(ref _scanActive, 0);
            CanScan = true;
            OnPropertyChanged(nameof(CoverageSummary));
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

        var results = LastResults;
        if (results is null) return;

        try
        {
            await ExportToPathAsync(format, filePath);

            var successBox = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Success"), string.Format(_localizationService.GetString("ExportSuccessMessage"), filePath), ButtonEnum.Ok, Icon.Success);
            await successBox.ShowAsync();
        }
        catch (Exception ex)
        {
            var errorBox = MessageBoxManager.GetMessageBoxStandard(_localizationService.GetString("Error"), string.Format(_localizationService.GetString("ExportFailedMessage"), ex.Message));
            await errorBox.ShowAsync();
        }
    }

    public Task ExportToPathAsync(string format, string filePath)
    {
        var results = LastResults ?? throw new InvalidOperationException(_localizationService.GetString("NoResultsMessage"));
        return format switch
        {
            "csv" => _exportService.ExportToCsvAsync(results, filePath),
            "json" => _exportService.ExportToJsonAsync(results, filePath),
            _ => throw new ArgumentException("Unsupported export format.", nameof(format))
        };
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

    private static string FormatBytes(long bytes) => FileStrider.Core.Models.ByteSize.Format(bytes);

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
        _folderIndex = results.Folders.ToDictionary(f => f.FullPath, PathComparer);
        _children = results.Folders.Where(f => !PathComparer.Equals(f.FullPath, results.RootPath))
            .ToLookup(f => Path.GetDirectoryName(f.FullPath) ?? "", PathComparer);
        NavigateTreemap(results.RootPath);
    }

    public void NavigateTreemap(string path)
    {
        if (LastResults is null || !_folderIndex.TryGetValue(path, out var folder)) return;
        TreemapPath = path;
        CanNavigateUp = !PathComparer.Equals(path, LastResults.RootPath);
        TreemapItems.Clear();
        var candidates = (_children?[path] ?? []).Where(f => f.RecursiveSize > 0)
            .Select(f => new TreemapItem { Name = f.Name, FullPath = f.FullPath, Size = f.RecursiveSize,
                Category = "Folders", Color = TreemapColors.GetColorForCategory("Folders") }).ToList();
        var directFiles = LastResults.TopFiles.Where(f => PathComparer.Equals(Path.GetDirectoryName(f.FullPath), path)).ToList();
        foreach (var file in directFiles)
        {
            var category = _fileTypeAnalyzer.GetFileCategory(file.Type);
            candidates.Add(new TreemapItem { Name = file.Name, FullPath = file.FullPath, Size = file.Size, IsFile = true,
                Category = category, Color = TreemapColors.GetColorForCategory(category) });
        }
        var otherFiles = Math.Max(0, folder.DirectSize - directFiles.Sum(f => f.Size));
        if (otherFiles > 0) candidates.Add(new TreemapItem {
            Name = _localizationService.GetString("OtherFiles"), Size = otherFiles, IsAggregate = true,
            Category = "Other", Color = TreemapColors.GetColorForCategory("Other") });
        var ordered = candidates.OrderByDescending(c => c.Size).ToList();
        foreach (var item in ordered.Take(29)) TreemapItems.Add(item);
        if (ordered.Count > 29)
            TreemapItems.Add(new TreemapItem { Name = _localizationService.GetString("OtherItems"),
                Size = ordered.Skip(29).Sum(i => i.Size), IsAggregate = true,
                Category = "Other", Color = TreemapColors.GetColorForCategory("Other") });
    }

    [RelayCommand]
    private void NavigateUp()
    {
        if (!CanNavigateUp) return;
        var parent = Path.GetDirectoryName(TreemapPath);
        if (parent is not null) NavigateTreemap(parent);
    }

    [RelayCommand]
    private async Task ActivateTreemapItem(TreemapItem? item)
    {
        if (item is null || item.IsAggregate) return;
        if (!item.IsFile) NavigateTreemap(item.FullPath);
        else await OpenFileLocation(new FileItem { FullPath = item.FullPath });
    }
}

public sealed record SizeUnit(string Name, decimal Multiplier)
{
    public override string ToString() => Name;
}
