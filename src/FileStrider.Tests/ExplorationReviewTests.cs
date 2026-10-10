using System.Collections.Specialized;
using System.Security.AccessControl;
using System.Security.Principal;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;
using FileStrider.Infrastructure.Analysis;
using FileStrider.Infrastructure.Export;
using FileStrider.Infrastructure.History;
using FileStrider.Infrastructure.Localization;
using FileStrider.MauiApp.ViewModels;
using FileStrider.Scanner;

namespace FileStrider.Tests;

public class ExplorationReviewTests
{
    private static ScanResults Result(string root) => new()
    {
        RootPath = root, Options = ScanPresets.Full(new ScanOptions { RootPath = root }), IsCompleted = true,
        Folders = [new FolderItem { FullPath = root, Name = "root", RecursiveSize = 100 }]
    };

    private static MainWindowViewModel Model(ScanResults result, IScanHistoryService? history = null,
        IFolderContentService? contents = null) => new(new FixedScanner(result), new NullPicker(), new ExportService(),
        new RecordingShell(), new MemoryConfiguration(result.Options!), new LocalizationService(), new FileTypeAnalyzer(), contents, history);

    [Theory]
    [InlineData("same", "CompareCoveragePartial")]
    [InlineData("path", "CompareCoverageChanged")]
    [InlineData("reason", "CompareCoverageChanged")]
    [InlineData("truncated", "CompareCoverageUnknown")]
    [InlineData("missing", "CompareCoverageUnknown")]
    public void CompletedScansWithSkippedItemsCompareAndExplainCoverage(string difference, string warning)
    {
        using var directory = new TestDirectory();
        var before = Result(directory.Path);
        var after = Result(directory.Path);
        before.Progress.IncrementInaccessibleItems(); after.Progress.IncrementInaccessibleItems();
        before.CoverageIssues.Add(new(Path.Combine(directory.Path, "restricted"), CoverageReason.Inaccessible, true));
        after.CoverageIssues.Add(new(Path.Combine(directory.Path, difference == "path" ? "other" : "restricted"),
            difference == "reason" ? CoverageReason.Offline : CoverageReason.Inaccessible, true));
        if (difference == "truncated") after.OmittedCoverageIssues = 1;
        if (difference == "missing") after.CoverageIssues.Clear();
        Assert.Null(ScanComparison.Validate(before, after));
        Assert.Equal(warning, ScanComparison.CoverageWarning(before, after));
        Assert.Empty(ScanComparison.Compare(before, after));
    }

    [Theory]
    [InlineData("unfinished")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    public void ComparisonStillRejectsUnfinishedCancelledAndFailedScans(string state)
    {
        using var directory = new TestDirectory();
        var before = Result(directory.Path); var after = Result(directory.Path);
        if (state == "unfinished") after.IsCompleted = false;
        if (state == "cancelled") after.WasCancelled = true;
        if (state == "failed") after.ErrorMessage = "read failure";
        Assert.Equal("CompareIncomplete", ScanComparison.Validate(before, after));
    }

    [Theory]
    [InlineData(ScanStatus.Complete)]
    [InlineData(ScanStatus.Partial)]
    [InlineData(ScanStatus.Cancelled)]
    [InlineData(ScanStatus.Failed)]
    public async Task SavedScansKeepLocalizedStatusAndWarningsWhenReopened(ScanStatus state)
    {
        using var directory = new TestDirectory(); using var storage = new TestDirectory();
        var result = Result(directory.Path);
        if (state == ScanStatus.Partial) result.Progress.IncrementInaccessibleItems();
        if (state == ScanStatus.Cancelled) { result.WasCancelled = true; result.IsCompleted = false; }
        if (state == ScanStatus.Failed) { result.ErrorMessage = "fixture failure"; result.IsCompleted = false; }
        var history = new ScanHistoryService(storage.Path);
        var saved = await history.SaveAsync(result);
        Assert.Equal(state, saved.Status);
        using var model = Model(Result(directory.Path), history);
        await model.Initialization; await model.HistoryLoading;
        foreach (var language in model.AvailableLanguages)
        {
            model.SelectedLanguage = language;
            Assert.Contains(model["ScanStatus" + state], Assert.Single(model.SavedScans).DisplayName);
            await model.LoadSnapshotCommand.ExecuteAsync(null);
            var warning = state switch
            {
                ScanStatus.Partial => model["IncompleteCoverage"],
                ScanStatus.Cancelled => model["ScanCancelledMessage"],
                ScanStatus.Failed => "fixture failure",
                _ => string.Format(model["SnapshotLoaded"], result.ScannedAt.ToLocalTime())
            };
            Assert.Contains(warning, model.StatusMessage);
        }
    }

    [Fact]
    public async Task HistoryUsesSmallSummariesAndValidatesTheFullSnapshotOnlyWhenOpened()
    {
        using var storage = new TestDirectory();
        var history = new ScanHistoryService(storage.Path);
        var saved = await history.SaveAsync(Result(storage.Path));
        var path = Path.Combine(storage.Path, saved.Id + ".json");
        var metadata = Path.Combine(storage.Path, saved.Id + ".meta.json");
        Assert.True(new FileInfo(metadata).Length < 1024);
        File.Delete(metadata); // Older histories acquire a summary on their first listing.
        Assert.Equal(saved, Assert.Single(await history.ListAsync()));
        Assert.True(File.Exists(metadata));
        var contents = await File.ReadAllBytesAsync(path);
        var modified = File.GetLastWriteTimeUtc(path);
        contents[0] = (byte)'!';
        await File.WriteAllBytesAsync(path, contents);
        File.SetLastWriteTimeUtc(path, modified);
        Assert.Equal(saved, Assert.Single(await history.ListAsync()));
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => history.LoadAsync(saved.Id));
        File.SetLastWriteTimeUtc(path, modified.AddSeconds(10));
        Assert.Empty(await history.ListAsync()); // A changed payload invalidates its cached summary.
        await history.DeleteAsync(saved.Id);
        Assert.False(File.Exists(metadata));
    }

    [WindowsFact]
    public async Task HistorySkipsAccessDeniedSnapshotsAndKeepsReadableEntries()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var storage = new TestDirectory();
        var history = new ScanHistoryService(storage.Path);
        var denied = await history.SaveAsync(Result(storage.Path));
        var readable = await history.SaveAsync(Result(storage.Path));
        var file = new FileInfo(Path.Combine(storage.Path, denied.Id + ".json"));
        var original = file.GetAccessControl();
        var restricted = file.GetAccessControl();
        restricted.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            file.SetAccessControl(restricted);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => history.LoadAsync(denied.Id));
            Assert.Equal(readable.Id, Assert.Single(await history.ListAsync()).Id);
            File.Delete(Path.ChangeExtension(file.FullName, ".meta.json"));
            Assert.Equal(readable.Id, Assert.Single(await history.ListAsync()).Id);
        }
        finally { file.SetAccessControl(original); }
    }

    [Fact]
    public async Task SlowHistoryDoesNotBlockScanInitialization()
    {
        using var directory = new TestDirectory();
        var history = new DelayedHistory();
        using var model = Model(Result(directory.Path), history);
        try
        {
            await model.Initialization.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(model.CanScan);
            Assert.False(model.HistoryLoading.IsCompleted);
            await model.StartScanCommand.ExecuteAsync(null);
            Assert.True(model.HasResults);
        }
        finally { history.Ready.TrySetResult([]); await model.HistoryLoading; }
    }

    [Fact]
    public async Task InsightsRequireProjectEvidenceAndRetainItInSnapshots()
    {
        using var directory = new TestDirectory(); using var storage = new TestDirectory();
        foreach (var path in new[] { "bin/tool", "usr/bin/tool", "home/.local/bin/tool", "AppData/Local/Packages/app",
                     "unmarked/build/output", "project/bin/app", "project/node_modules/dependency", "env/.venv/lib/site.py" })
            directory.File(path);
        directory.File("project/App.csproj", 0); directory.File("env/.venv/pyvenv.cfg", 0);
        var result = await new FileSystemScanner(new FileTypeAnalyzer()).ScanAsync(Result(directory.Path).Options!);
        var insights = DeveloperStorageAnalysis.Analyze(result);
        Assert.Equal(3, insights.Count);
        Assert.Contains(insights, i => i.FullPath == Path.Combine(directory.Path, "project", "bin"));
        Assert.Contains(insights, i => i.FullPath == Path.Combine(directory.Path, "project", "node_modules"));
        Assert.Contains(insights, i => i.FullPath == Path.Combine(directory.Path, "env", ".venv"));
        var history = new ScanHistoryService(storage.Path);
        var saved = await history.SaveAsync(result);
        Assert.Equal(insights, DeveloperStorageAnalysis.Analyze((await history.LoadAsync(saved.Id)).Results));
    }

    [Fact]
    public async Task ReadableDirectoriesWithoutIdentitiesDoNotCreateFalseCoverageWarnings()
    {
        using var directory = new TestDirectory();
        directory.File("nested/file.txt", 25);
        var scanner = new FileSystemScanner(new FileTypeAnalyzer(), NoIdentity);
        var result = await scanner.ScanAsync(Result(directory.Path).Options!);
        Assert.True(result.IsCompleted);
        Assert.Equal(25, result.Progress.BytesProcessed);
        Assert.Equal(2, result.Folders.Count);
        Assert.Empty(result.CoverageIssues);
        Assert.False(result.Progress.HasIncompleteCoverage);
    }

    [UnixFact]
    public async Task SkippedLinksWithoutIdentitiesStillReportIncompleteCoverage()
    {
        using var directory = new TestDirectory();
        directory.File("real/file.txt", 25);
        var link = Path.Combine(directory.Path, "link");
        Directory.CreateSymbolicLink(link, Path.Combine(directory.Path, "real"));
        var scanner = new FileSystemScanner(new FileTypeAnalyzer(), NoIdentity);
        var result = await scanner.ScanAsync(Result(directory.Path).Options! with { FollowSymlinks = true });
        Assert.Equal(25, result.Progress.BytesProcessed);
        Assert.Equal(new CoverageIssue(link, CoverageReason.IdentityUnavailable, true), Assert.Single(result.CoverageIssues));
        Assert.True(result.Progress.HasIncompleteCoverage);
    }

    [Fact]
    public async Task ExplorerReadsOnlyWhenOpenedAndReusesLoadedFolderAcrossTabsAndLanguages()
    {
        using var directory = new TestDirectory(); using var storage = new TestDirectory();
        var result = Result(directory.Path);
        var child = Path.Combine(directory.Path, "child");
        result.Folders.Add(new FolderItem { FullPath = child, Name = "child", RecursiveSize = 100 });
        var history = new ScanHistoryService(storage.Path);
        await history.SaveAsync(result);
        var contents = new CountingContents();
        using var model = Model(result, history, contents);
        await model.Initialization; await model.HistoryLoading;
        await model.StartScanCommand.ExecuteAsync(null);
        model.NavigateTreemap(child);
        model.SelectedLanguage = model.AvailableLanguages.Single(l => l.Code == "fr");
        await model.LoadSnapshotCommand.ExecuteAsync(null);
        Assert.Equal(0, contents.Reads);
        model.ResultsTabIndex = 1; await model.ExplorerLoading;
        Assert.Equal(1, contents.Reads);
        model.ResultsTabIndex = 0; model.ResultsTabIndex = 1; await model.ExplorerLoading;
        model.SelectedLanguage = model.AvailableLanguages.Single(l => l.Code == "en");
        await model.ExplorerLoading;
        Assert.Equal(1, contents.Reads);
        Assert.Equal(string.Format(model["LiveFolderNotice"], 0, 0), model.ExplorerStatus);
        await model.RefreshExplorerCommand.ExecuteAsync(null);
        Assert.Equal(2, contents.Reads);
        model.ResultsTabIndex = 0; model.NavigateTreemap(child);
        Assert.Equal(2, contents.Reads);
        model.ResultsTabIndex = 1; await model.ExplorerLoading;
        Assert.Equal(3, contents.Reads);
        await model.StartScanCommand.ExecuteAsync(null); await model.ExplorerLoading;
        Assert.Equal(4, contents.Reads);
    }

    [Theory]
    [InlineData("log", 1)]
    [InlineData(".log", 1)]
    [InlineData("*.LOG", 1)]
    [InlineData("*.log; .zip", 2)]
    [InlineData("*", 2)]
    public async Task ExplorerAcceptsCommonExtensionFilterForms(string filter, int count)
    {
        using var directory = new TestDirectory();
        var contents = new CountingContents(new FolderContents { Files =
        [new FileItem { Name = "a.log", Type = ".log" }, new FileItem { Name = "b.zip", Type = ".zip" }] });
        using var model = Model(Result(directory.Path), contents: contents);
        await model.Initialization; await model.StartScanCommand.ExecuteAsync(null);
        model.ResultsTabIndex = 1; await model.ExplorerLoading;
        model.ExtensionFilter = filter; await model.ExplorerFiltering;
        Assert.Equal(count, model.ExplorerItems.Count);
    }

    [Fact]
    public async Task RapidSearchChangesPublishOneBatchWithTheLatestFilter()
    {
        using var directory = new TestDirectory();
        var contents = new CountingContents(new FolderContents { Files = Enumerable.Range(0, 20000)
            .Select(i => new FileItem { Name = $"file{i}.log", Type = ".log", Size = i }).ToList() });
        using var model = Model(Result(directory.Path), contents: contents);
        await model.Initialization; await model.StartScanCommand.ExecuteAsync(null);
        model.ResultsTabIndex = 1; await model.ExplorerLoading;
        var notifications = new List<NotifyCollectionChangedAction>();
        model.ExplorerItems.CollectionChanged += (_, e) => notifications.Add(e.Action);
        model.ExplorerSearch = "file"; model.ExplorerSearch = "file12"; model.ExplorerSearch = "file123.log";
        await model.ExplorerFiltering;
        Assert.Equal("file123.log", Assert.Single(model.ExplorerItems).Name);
        Assert.Equal(NotifyCollectionChangedAction.Reset, Assert.Single(notifications));
    }

    [Fact]
    public void LargeResultReplacementPublishesOneCollectionNotification()
    {
        var rows = new ResultCollection<int>();
        var notifications = 0;
        rows.CollectionChanged += (_, _) => notifications++;
        rows.ReplaceAll(Enumerable.Range(0, 100000));
        Assert.Equal(100000, rows.Count);
        Assert.Equal(1, notifications);
    }

    private static bool NoIdentity(string path, out DirectoryIdentity identity) { identity = default; return false; }
    private sealed class FixedScanner(ScanResults result) : IFileSystemScanner
    {
        public Task<ScanResults> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
    private sealed class CountingContents(FolderContents? contents = null) : IFolderContentService
    {
        public int Reads { get; private set; }
        public Task<FolderContents> ReadFilesAsync(string path, ScanOptions options, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult(contents ?? new FolderContents()); }
    }
    private sealed class DelayedHistory : IScanHistoryService
    {
        public TaskCompletionSource<IReadOnlyList<SavedScanInfo>> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<SavedScanInfo>> ListAsync() => Ready.Task;
        public Task<SavedScanInfo> SaveAsync(ScanResults results) => throw new NotSupportedException();
        public Task<SavedScan> LoadAsync(string id) => throw new NotSupportedException();
        public Task DeleteAsync(string id) => throw new NotSupportedException();
    }
}
