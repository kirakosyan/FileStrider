using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;
using FileStrider.Infrastructure.Analysis;

namespace FileStrider.MauiApp.ViewModels;

public partial class MainWindowViewModel
{
    private readonly IFolderContentService? _folderContentService;
    private readonly IScanHistoryService? _historyService;
    private readonly SemaphoreSlim _historyRefresh = new(1, 1);
    private readonly HashSet<string> _currentSnapshotIds = [];
    private CancellationTokenSource? _explorerCancellation;
    private CancellationTokenSource? _filterCancellation;
    private string? _explorerPath;
    private ScanResults? _explorerResults;
    private (int Files, int Issues)? _explorerCounts;
    private List<ExplorerRow> _explorerRows = [];
    private List<ExplorerRow> _sortedExplorerRows = [];
    public Task ExplorerLoading { get; private set; } = Task.CompletedTask;
    public Task ExplorerFiltering { get; private set; } = Task.CompletedTask;
    public string this[string key] => _localizationService.GetString(key);
    public ResultCollection<ExplorerRow> ExplorerItems { get; } = [];
    public ObservableCollection<Breadcrumb> Breadcrumbs { get; } = [];
    public ObservableCollection<SavedScanInfo> SavedScans { get; } = [];
    public ResultCollection<FolderChange> FolderChanges { get; } = [];
    public ResultCollection<InsightRow> Insights { get; } = [];
    public ResultCollection<CoverageRow> CoverageItems { get; } = [];
    public ObservableCollection<Choice> SortChoices { get; } = [];
    [ObservableProperty] private int resultsTabIndex;
    [ObservableProperty] private string explorerSearch = "";
    [ObservableProperty] private string extensionFilter = "";
    [ObservableProperty] private Choice? explorerSort;
    [ObservableProperty] private ExplorerRow? selectedExplorerItem;
    [ObservableProperty] private FileItem? selectedTopFile;
    [ObservableProperty] private FolderItem? selectedTopFolder;
    [ObservableProperty] private SavedScanInfo? selectedSavedScan;
    [ObservableProperty] private CoverageRow? selectedCoverageItem;
    [ObservableProperty] private InsightRow? selectedInsight;
    [ObservableProperty] private string explorerStatus = "";
    [ObservableProperty] private string comparisonStatus = "";
    [ObservableProperty] private string insightsStatus = "";
    [ObservableProperty] private string coverageStatus = "";
    [ObservableProperty] private bool isExplorerLoading;
    public bool CanSaveSnapshot => CanScan && !IsScanning && LastResults?.Options is not null && LastResults.Folders.Count > 0 && _historyService is not null;
    public string ActiveFilters => string.Format(this["ActiveFilters"],
        string.IsNullOrWhiteSpace(ExcludePatternsText) ? this["None"] : ExcludePatternsText,
        string.IsNullOrWhiteSpace(ExcludeDirectoriesText) ? this["None"] : ExcludeDirectoriesText) + " · " +
        (IncludeHidden ? this["HiddenIncluded"] : this["HiddenExcluded"]) + " · " +
        (FollowSymlinks ? this["LinksFollowed"] : this["LinksSkipped"]);

    partial void OnExcludePatternsTextChanged(string value) => OnPropertyChanged(nameof(ActiveFilters));
    partial void OnExcludeDirectoriesTextChanged(string value) => OnPropertyChanged(nameof(ActiveFilters));
    partial void OnIncludeHiddenChanged(bool value) => OnPropertyChanged(nameof(ActiveFilters));
    partial void OnFollowSymlinksChanged(bool value) => OnPropertyChanged(nameof(ActiveFilters));
    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(CanSaveSnapshot));
    partial void OnCanScanChanged(bool value) => OnPropertyChanged(nameof(CanSaveSnapshot));
    partial void OnExplorerSearchChanged(string value) => QueueExplorerFilter();
    partial void OnExtensionFilterChanged(string value) => QueueExplorerFilter();
    partial void OnExplorerSortChanged(Choice? value) => SortExplorer();
    partial void OnResultsTabIndexChanged(int value)
    {
        if (value == 1) EnsureExplorerLoaded();
        else if (IsExplorerLoading) { _explorerCancellation?.Cancel(); _explorerPath = null; }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private void ApplyFullPreset() => ApplyPreset(ScanPresets.Full(_loadedOptions));
    [RelayCommand(CanExecute = nameof(CanScan))]
    private void ApplySourcePreset() => ApplyPreset(ScanPresets.SourceFiles(_loadedOptions));
    private void ApplyPreset(ScanOptions options)
    {
        IncludeHidden = options.IncludeHidden; FollowSymlinks = options.FollowSymlinks;
        FoldersOnly = options.FoldersOnly; MaxDepth = options.MaxDepth; MinFileSize = options.MinFileSize / SelectedSizeUnit.Multiplier;
        ExcludePatternsText = string.Join(", ", options.ExcludePatterns);
        ExcludeDirectoriesText = string.Join(", ", options.ExcludeDirectories);
    }

    private void RefreshFeatureLabels()
    {
        OnPropertyChanged("Item[]");
        OnPropertyChanged("Item");
        OnPropertyChanged(nameof(ActiveFilters));
        var sort = ExplorerSort?.Id ?? "SizeDescending";
        SortChoices.Clear();
        foreach (var key in new[] { "SizeDescending", "SizeAscending", "NameAscending", "NewestFirst" })
            SortChoices.Add(new(key, this[key]));
        ExplorerSort = SortChoices.First(c => c.Id == sort);
        var selected = SelectedSavedScan?.Id;
        for (var i = 0; i < SavedScans.Count; i++) SavedScans[i] = LocalizeSavedScan(SavedScans[i]);
        SelectedSavedScan = SavedScans.FirstOrDefault(s => s.Id == selected);
        if (_explorerCounts is { } counts) ExplorerStatus = string.Format(this["LiveFolderNotice"], counts.Files, counts.Issues);
        else if (IsExplorerLoading) ExplorerStatus = this["LoadingFolder"];
        if (LastResults is not null) PopulateAnalysis(LastResults);
    }

    private void ClearExploration()
    {
        _currentSnapshotIds.Clear();
        _explorerCancellation?.Cancel();
        _filterCancellation?.Cancel();
        _explorerPath = null; _explorerResults = null; _explorerCounts = null;
        _explorerRows.Clear(); _sortedExplorerRows.Clear(); ExplorerItems.Clear(); Breadcrumbs.Clear(); FolderChanges.Clear();
        Insights.Clear(); CoverageItems.Clear(); ExplorerStatus = ComparisonStatus = InsightsStatus = CoverageStatus = "";
        SelectedExplorerItem = null; SelectedCoverageItem = null; SelectedInsight = null;
        IsExplorerLoading = false;
        OnPropertyChanged(nameof(CanSaveSnapshot));
    }

    private void EnsureExplorerLoaded(bool refresh = false)
    {
        if (ResultsTabIndex != 1 || LastResults is null || string.IsNullOrEmpty(TreemapPath)) return;
        if (!refresh && PathComparer.Equals(_explorerPath, TreemapPath) && ReferenceEquals(_explorerResults, LastResults)) return;
        StartExplorerNavigation(TreemapPath);
    }

    private void StartExplorerNavigation(string path)
    {
        _explorerCancellation?.Cancel();
        _explorerPath = path; _explorerResults = LastResults;
        var cancellation = new CancellationTokenSource();
        _explorerCancellation = cancellation;
        ExplorerLoading = LoadExplorerAsync(path, cancellation);
    }

    private async Task LoadExplorerAsync(string path, CancellationTokenSource cancellation)
    {
        var results = LastResults;
        if (results is null) { cancellation.Dispose(); return; }
        IsExplorerLoading = true;
        _explorerCounts = null;
        SelectedExplorerItem = null;
        _explorerRows = (_children?[path] ?? []).Select(f => new ExplorerRow(f.Name, f.FullPath,
            f.RecursiveSize, "", f.LastModified, true)).ToList();
        SortExplorer();
        Breadcrumbs.Clear();
        var crumbs = new Stack<Breadcrumb>();
        for (string? current = path; current is not null && _folderIndex.ContainsKey(current); current = Path.GetDirectoryName(current))
        {
            crumbs.Push(new(new DirectoryInfo(current).Name, current));
            if (PathComparer.Equals(current, results.RootPath)) break;
        }
        foreach (var crumb in crumbs) Breadcrumbs.Add(crumb);
        ExplorerStatus = this["LoadingFolder"];
        try
        {
            var contents = _folderContentService is not null
                ? await _folderContentService.ReadFilesAsync(path, results.Options ?? _loadedOptions, cancellation.Token)
                : new FolderContents { Files = results.TopFiles.Where(f => PathComparer.Equals(Path.GetDirectoryName(f.FullPath), path)).ToList() };
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_explorerCancellation, cancellation) || !ReferenceEquals(LastResults, results)) return;
            _explorerRows.AddRange(contents.Files.Select(f => new ExplorerRow(f.Name, f.FullPath, f.Size, f.Type, f.LastModified, false)));
            SortExplorer();
            _explorerCounts = (contents.Files.Count, contents.Issues.Count + contents.OmittedIssues);
            ExplorerStatus = string.Format(this["LiveFolderNotice"], contents.Files.Count, contents.Issues.Count + contents.OmittedIssues);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (ReferenceEquals(_explorerCancellation, cancellation)) ExplorerStatus = ex.Message; }
        finally
        {
            if (ReferenceEquals(_explorerCancellation, cancellation)) { IsExplorerLoading = false; _explorerCancellation = null; }
            cancellation.Dispose();
        }
    }

    private void QueueExplorerFilter()
    {
        _filterCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _filterCancellation = cancellation;
        ExplorerFiltering = DebounceFilterAsync(cancellation);
    }

    private async Task DebounceFilterAsync(CancellationTokenSource cancellation)
    {
        try { await Task.Delay(175, cancellation.Token); FilterExplorer(); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_filterCancellation, cancellation)) _filterCancellation = null;
            cancellation.Dispose();
        }
    }

    private void SortExplorer()
    {
        IEnumerable<ExplorerRow> items = _explorerRows;
        _sortedExplorerRows = (ExplorerSort?.Id switch
        {
            "SizeAscending" => items.OrderBy(i => i.Size).ThenBy(i => i.Name),
            "NameAscending" => items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
            "NewestFirst" => items.OrderByDescending(i => i.LastModified).ThenBy(i => i.Name),
            _ => items.OrderByDescending(i => i.Size).ThenBy(i => i.Name)
        }).ToList();
        FilterExplorer();
    }

    private void FilterExplorer()
    {
        var query = ExplorerSearch.Trim();
        var extensions = ExtensionFilter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(e => "." + e.TrimStart('*', '.')).Where(e => e.Length > 1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = _sortedExplorerRows.Where(i => i.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) &&
            (extensions.Count == 0 || !i.IsDirectory && extensions.Contains(i.Extension)));
        ExplorerItems.ReplaceAll(items);
    }

    [RelayCommand]
    private async Task ExploreItem(ExplorerRow? row)
    {
        if (row is null) return;
        if (row.IsDirectory) { NavigateTreemap(row.FullPath); await ExplorerLoading; }
        else await OpenPath(row.FullPath);
    }

    [RelayCommand]
    private async Task RefreshExplorer()
    {
        if (LastResults is null || string.IsNullOrEmpty(TreemapPath)) return;
        EnsureExplorerLoaded(refresh: true);
        await ExplorerLoading;
    }

    [RelayCommand]
    private async Task OpenPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { await _shellService.OpenFileLocationAsync(path); }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task CopyPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { await _shellService.CopyToClipboardAsync(path); StatusMessage = this["PathCopiedMessage"]; }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private async Task InitializeHistoryAsync()
    {
        try { await RefreshHistoryAsync(); }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private SavedScanInfo LocalizeSavedScan(SavedScanInfo info) => info with { StatusLabel = this["ScanStatus" + info.Status] };

    private string ScanStatusNotice(ScanResults results) => SavedScanInfo.StatusOf(results) switch
    {
        ScanStatus.Failed => string.Format(this["ScanFailedMessage"], results.ErrorMessage),
        ScanStatus.Cancelled => this["ScanCancelledMessage"],
        ScanStatus.Partial => this["IncompleteCoverage"],
        _ => ""
    };

    private async Task RefreshHistoryAsync()
    {
        if (_historyService is null) return;
        await _historyRefresh.WaitAsync();
        try
        {
            var scans = await _historyService.ListAsync();
            var selected = SelectedSavedScan?.Id;
            SavedScans.Clear();
            foreach (var scan in scans) SavedScans.Add(LocalizeSavedScan(scan));
            SelectedSavedScan = SavedScans.FirstOrDefault(s => s.Id == selected) ?? SavedScans.FirstOrDefault();
        }
        finally { _historyRefresh.Release(); }
    }

    [RelayCommand]
    private async Task SaveSnapshot()
    {
        if (!CanSaveSnapshot || LastResults is null || _historyService is null) return;
        var results = LastResults;
        try
        {
            var info = await _historyService.SaveAsync(results);
            RememberSnapshot(results, info.Id);
            await RefreshHistoryAsync();
            SelectedSavedScan = SavedScans.First(s => s.Id == info.Id);
            StatusMessage = this["SnapshotSaved"];
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private void RememberSnapshot(ScanResults results, string id)
    {
        // A save can finish after another scan has replaced its results.
        if (ReferenceEquals(LastResults, results)) _currentSnapshotIds.Add(id);
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task LoadSnapshot()
    {
        if (_historyService is null || SelectedSavedScan is null) return;
        // Keep scans and snapshot loading mutually exclusive, including while reading from disk.
        if (Interlocked.CompareExchange(ref _scanActive, 1, 0) != 0) return;
        CanScan = false;
        try
        {
            var saved = await _historyService.LoadAsync(SelectedSavedScan.Id);
            ClearExploration();
            LastResults = saved.Results;
            RememberSnapshot(LastResults, saved.Id);
            SelectedPath = LastResults.RootPath;
            ApplyPreset(LastResults.Options!);
            TopN = Math.Clamp(LastResults.Options!.TopN, 1, 200);
            ConcurrencyLimit = Math.Clamp(LastResults.Options.ConcurrencyLimit, 1, MaximumConcurrency);
            TopFiles.Clear(); TopFolders.Clear(); _lastFileTypeStatistics.Clear();
            foreach (var file in LastResults.TopFiles) TopFiles.Add(file);
            foreach (var folder in LastResults.TopFolders) TopFolders.Add(folder);
            _lastFileTypeStatistics.AddRange(LastResults.FileTypeStatistics);
            RefreshLocalizedFileTypeStatistics();
            PopulateTreemapData(LastResults); PopulateAnalysis(LastResults);
            HasResults = true;
            SetCompletedScanStats(LastResults.Progress.FilesScanned, LastResults.Progress.FoldersScanned);
            StatusMessage = string.Format(this["SnapshotLoaded"], LastResults.ScannedAt.ToLocalTime());
            if (ScanStatusNotice(LastResults) is { Length: > 0 } notice) StatusMessage += " " + notice;
            OnPropertyChanged(nameof(CoverageSummary));
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { Interlocked.Exchange(ref _scanActive, 0); CanScan = true; OnPropertyChanged(nameof(CanSaveSnapshot)); }
    }

    [RelayCommand]
    private async Task CompareSnapshot()
    {
        if (_historyService is null || SelectedSavedScan is null || LastResults is null || IsScanning) return;
        var current = LastResults;
        try
        {
            var previous = (await _historyService.LoadAsync(SelectedSavedScan.Id)).Results;
            if (!ReferenceEquals(current, LastResults)) return;
            FolderChanges.Clear();
            if (ScanComparison.Validate(previous, current) is { } error) { ComparisonStatus = this[error]; return; }
            FolderChanges.ReplaceAll(ScanComparison.Compare(previous, current));
            ComparisonStatus = string.Format(this["ComparisonSummary"], previous.ScannedAt.ToLocalTime(), current.ScannedAt.ToLocalTime(), FolderChanges.Count);
            if (ScanComparison.CoverageWarning(previous, current) is { } warning) ComparisonStatus += " " + this[warning];
        }
        catch (Exception ex) { ComparisonStatus = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteSnapshot()
    {
        if (_historyService is null || SelectedSavedScan is null) return;
        var id = SelectedSavedScan.Id;
        try
        {
            await _historyService.DeleteAsync(id);
            _currentSnapshotIds.Remove(id);
            await RefreshHistoryAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private void PopulateAnalysis(ScanResults results)
    {
        Insights.ReplaceAll(DeveloperStorageAnalysis.Analyze(results).Select(insight =>
            new InsightRow(insight.FullPath, this[insight.Kind], insight.Size, insight.LastModified,
                insight.IsOld ? this["OlderArtifact"] : "")));
        InsightsStatus = string.Format(this["InsightsSummary"], Insights.Count, ByteSize.Format(Insights.Sum(i => i.Size)));
        CoverageItems.ReplaceAll(results.CoverageIssues.Select(issue => new CoverageRow(issue.FullPath, this["Coverage" + issue.Reason], issue)));
        CoverageStatus = string.Format(this["CoverageCount"], CoverageItems.Count, results.OmittedCoverageIssues);
        OnPropertyChanged(nameof(CanSaveSnapshot));
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task RetryCoverage()
    {
        if (SelectedCoverageItem is null || LastResults is null || !CanScan) return;
        var issue = SelectedCoverageItem.Issue;
        // Offline directories are retried through their parent so their Offline attribute is checked.
        var path = issue.IsDirectory && issue.Reason != CoverageReason.Offline ? issue.FullPath : Path.GetDirectoryName(issue.FullPath);
        if (path is null) return;
        var options = ScanPresets.Full(LastResults.Options ?? _loadedOptions) with { RootPath = path };
        ResultsTabIndex = 0;
        await RunScanAsync(options);
    }
}

public sealed record Choice(string Id, string Name);
public sealed record Breadcrumb(string Name, string FullPath);
public sealed record ExplorerRow(string Name, string FullPath, long Size, string Extension, DateTime LastModified, bool IsDirectory)
{
    public string DisplaySize => ByteSize.Format(Size);
    public string DisplayName => (IsDirectory ? "📁 " : "") + Name;
    public string DisplayModified => LastModified == DateTime.MinValue ? "—" : LastModified.ToString("g");
}
public sealed record CoverageRow(string FullPath, string Reason, CoverageIssue Issue);
public sealed record InsightRow(string FullPath, string Kind, long Size, DateTime LastModified, string AgeNote)
{
    public string DisplaySize => ByteSize.Format(Size);
}
