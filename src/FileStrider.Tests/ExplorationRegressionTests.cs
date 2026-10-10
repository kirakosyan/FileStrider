using System.Text.Json;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;
using FileStrider.Infrastructure.Analysis;
using FileStrider.Infrastructure.Configuration;
using FileStrider.Infrastructure.Export;
using FileStrider.Infrastructure.History;
using FileStrider.Infrastructure.Localization;
using FileStrider.MauiApp.ViewModels;
using FileStrider.Scanner;

namespace FileStrider.Tests;

public class ExplorationRegressionTests
{
    private static FileSystemScanner Scanner() => new(new FileTypeAnalyzer());
    private static ScanOptions Options(TestDirectory directory) => ScanPresets.Full(new ScanOptions { RootPath = directory.Path });

    [Fact]
    public async Task FullPresetFindsLogsDependenciesAndBuildOutputsWhileSourcePresetExcludesThem()
    {
        using var directory = new TestDirectory();
        directory.File("app.log", 11); directory.File("node_modules/package.js", 13);
        directory.File("bin/app.exe", 17); directory.File("app.cs", 19);
        var full = await Scanner().ScanAsync(Options(directory));
        var source = await Scanner().ScanAsync(ScanPresets.SourceFiles(Options(directory)));
        Assert.Equal(60, full.Progress.BytesProcessed);
        Assert.Equal(19, source.Progress.BytesProcessed);
        Assert.Equal(3, source.CoverageIssues.Count);
        Assert.All(source.CoverageIssues, issue => Assert.Equal(CoverageReason.Filtered, issue.Reason));
    }

    [Fact]
    public async Task ExistingSavedFiltersArePreserved()
    {
        using var directory = new TestDirectory();
        var configuration = new ConfigurationService(Path.Combine(directory.Path, "settings.json"));
        await configuration.SaveDefaultOptionsAsync(ScanPresets.SourceFiles(Options(directory)));
        var loaded = await configuration.LoadDefaultOptionsAsync();
        Assert.Contains("*.log", loaded.ExcludePatterns);
        Assert.Contains("node_modules", loaded.ExcludeDirectories);
        Assert.False(loaded.IncludeHidden);
    }

    [Fact]
    public async Task FolderBrowserReturnsEveryDirectFileBeyondTheGlobalTopLimit()
    {
        using var directory = new TestDirectory();
        for (var i = 0; i < 205; i++) directory.File($"small/{i}.txt", i);
        directory.File("large.bin", 10000);
        var scanner = Scanner();
        var options = Options(directory) with { TopN = 1 };
        var results = await scanner.ScanAsync(options);
        Assert.Single(results.TopFiles);
        var contents = await scanner.ReadFilesAsync(Path.Combine(directory.Path, "small"), options);
        Assert.Equal(205, contents.Files.Count);
        Assert.Equal(results.Folders.Single(f => f.Name == "small").DirectSize, contents.Files.Sum(f => f.Size));
    }

    [Fact]
    public async Task FolderBrowserHonorsExclusionsAndCancellation()
    {
        using var directory = new TestDirectory();
        directory.File("keep.txt"); directory.File("skip.log");
        var options = Options(directory) with { ExcludePatterns = ["*.log"] };
        var contents = await Scanner().ReadFilesAsync(directory.Path, options);
        Assert.Single(contents.Files);
        Assert.Equal("keep.txt", contents.Files[0].Name);
        Assert.Contains(contents.Issues, i => i.FullPath.EndsWith("skip.log"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scanner().ReadFilesAsync(directory.Path, options, cancel.Token));
    }

    [Fact]
    public async Task TimerReportsProgressWhileAWorkerHasNotCompletedEvenOneFile()
    {
        using var directory = new TestDirectory();
        directory.File("blocked.txt");
        using var release = new ManualResetEventSlim();
        var report = new TaskCompletionSource<ScanProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanning = new FileSystemScanner(new BlockedAnalyzer(release)).ScanAsync(Options(directory),
            new InlineProgress<ScanProgress>(p => report.TrySetResult(p)));
        try
        {
            var progress = await report.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(scanning.IsCompleted);
            Assert.Equal(0, progress.FilesScanned);
            Assert.True(progress.Elapsed >= TimeSpan.FromMilliseconds(150));
            Assert.NotEmpty(progress.CurrentPath);
        }
        finally { release.Set(); await scanning; }
    }

    [Fact]
    public async Task CoverageRecordsExcludedAndDepthLimitedPathsAndBoundsItsMemory()
    {
        using var directory = new TestDirectory();
        directory.File("nested/deep.txt");
        for (var i = 0; i < 5002; i++) directory.File($"{i}.log", 0);
        var result = await Scanner().ScanAsync(Options(directory) with { MaxDepth = 0, ExcludePatterns = ["*.log"] });
        Assert.Equal(5000, result.CoverageIssues.Count);
        Assert.Equal(3, result.OmittedCoverageIssues);
        Assert.Equal(5002, result.Progress.ExcludedItems);
        Assert.Equal(1, result.Progress.DepthLimitedDirectories);
        Assert.Equal(5003, result.Progress.SkippedItems);
    }

    [Fact]
    public async Task SnapshotsRoundTripTheEntireTreeOptionsTimeAndCoverage()
    {
        using var directory = new TestDirectory();
        using var storage = new TestDirectory();
        directory.File("one/a.txt", 21); directory.File("two/b.txt", 34); directory.File("skip.log");
        var result = await Scanner().ScanAsync(Options(directory) with { TopN = 1, ExcludePatterns = ["*.log"] });
        var service = new ScanHistoryService(storage.Path);
        var saved = await service.SaveAsync(result);
        var restored = (await service.LoadAsync(saved.Id)).Results;
        Assert.Equal(3, restored.Folders.Count);
        Assert.Single(restored.TopFolders);
        Assert.Equal(result.ScannedAt, restored.ScannedAt);
        Assert.Equal(55, restored.Progress.BytesProcessed);
        Assert.Equal(result.Folders.Sum(f => f.DirectSize), restored.Folders.Sum(f => f.DirectSize));
        Assert.Contains("*.log", restored.Options!.ExcludePatterns);
        Assert.Equal(result.CoverageIssues, restored.CoverageIssues);
        Assert.Single(await service.ListAsync());
        await service.DeleteAsync(saved.Id);
        Assert.Empty(await service.ListAsync());
    }

    [Fact]
    public async Task HistoryRejectsPathTraversalAndUnsupportedSchemaAndSkipsCorruptFiles()
    {
        using var directory = new TestDirectory();
        var service = new ScanHistoryService(directory.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => service.LoadAsync("../settings"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync("../settings"));
        var id = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, id + ".json"), "{broken");
        Assert.Empty(await service.ListAsync());
        await File.WriteAllTextAsync(Path.Combine(directory.Path, id + ".json"), JsonSerializer.Serialize(new { schemaVersion = 42 }));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync(id));
    }

    [Fact]
    public async Task SavedDriveRootsAndTheirChildrenAreValid()
    {
        using var storage = new TestDirectory();
        var root = Path.GetPathRoot(storage.Path)!;
        var result = new ScanResults { RootPath = root, Options = new ScanOptions { RootPath = root },
            Folders = [new FolderItem { FullPath = root }, new FolderItem { FullPath = Path.Combine(root, "synthetic") }] };
        var service = new ScanHistoryService(storage.Path);
        var info = await service.SaveAsync(result);
        Assert.Equal(2, (await service.LoadAsync(info.Id)).Results.Folders.Count);
    }

    [Fact]
    public async Task ReportsIncludeCoveragePathsAndScanTimeWithoutUnrelatedRecentFolders()
    {
        using var directory = new TestDirectory();
        using var output = new TestDirectory();
        directory.File("skip.log");
        var result = await Scanner().ScanAsync(Options(directory) with { ExcludePatterns = ["*.log"], RecentPaths = ["unrelated-private-path"] });
        var service = new ExportService();
        await service.ExportToJsonAsync(result, Path.Combine(output.Path, "scan.json"));
        await service.ExportToCsvAsync(result, Path.Combine(output.Path, "scan.csv"));
        var json = await File.ReadAllTextAsync(Path.Combine(output.Path, "scan.json"));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(result.ScannedAt, document.RootElement.GetProperty("scannedAt").GetDateTimeOffset());
        Assert.Equal("Filtered", document.RootElement.GetProperty("coverageIssues")[0].GetProperty("reason").GetString());
        Assert.DoesNotContain("unrelated-private-path", json);
        Assert.Empty(result.Options!.RecentPaths);
        var csv = await File.ReadAllTextAsync(Path.Combine(output.Path, "scan.csv"));
        Assert.Contains("Coverage Issues", csv);
        Assert.Contains(Path.Combine(directory.Path, "skip.log"), csv);
    }

    [Fact]
    public async Task ComparisonDetectsGrowthNewAndRemovedFolders()
    {
        using var directory = new TestDirectory();
        directory.File("grow/a.txt", 10); var removed = directory.File("gone/a.txt", 20);
        var before = await Scanner().ScanAsync(Options(directory));
        directory.File("grow/a.txt", 30);
        File.Delete(removed); Directory.Delete(Path.GetDirectoryName(removed)!);
        directory.File("new/a.txt", 40);
        var after = await Scanner().ScanAsync(Options(directory));
        var changes = ScanComparison.Compare(before, after);
        Assert.Equal(20, changes.Single(c => c.FullPath.EndsWith("grow")).Delta);
        Assert.Equal(-20, changes.Single(c => c.FullPath.EndsWith("gone")).Delta);
        Assert.Equal(40, changes.Single(c => c.FullPath.EndsWith("new")).Delta);
        Assert.Equal(40, changes.Single(c => c.FullPath == directory.Path).Delta);
    }

    [Fact]
    public async Task ComparisonRefusesDifferentRootsFiltersAndIncompleteScans()
    {
        using var directory = new TestDirectory();
        using var other = new TestDirectory();
        directory.File("a.txt");
        var before = await Scanner().ScanAsync(Options(directory));
        Assert.Equal("CompareDifferentRoot", ScanComparison.Validate(before, await Scanner().ScanAsync(Options(other))));
        Assert.Equal("CompareDifferentFilters", ScanComparison.Validate(before,
            await Scanner().ScanAsync(Options(directory) with { ExcludePatterns = ["*.log"] })));
        var after = await Scanner().ScanAsync(Options(directory));
        after.WasCancelled = true;
        Assert.Equal("CompareIncomplete", ScanComparison.Validate(before, after));
    }

    [Fact]
    public async Task DeveloperInsightsAvoidNestedDoubleCountingAndMarkOldFolderTimestamps()
    {
        using var directory = new TestDirectory();
        directory.File("project/package.json", 0);
        directory.File("project/node_modules/package/package.json", 0);
        directory.File("project/node_modules/package/build/file.txt", 101);
        directory.File("project/bin/app.exe", 37);
        directory.File("project/.cache/entry", 11);
        Directory.SetLastWriteTime(Path.Combine(directory.Path, "project", "bin"), DateTime.Now.AddDays(-200));
        var results = await Scanner().ScanAsync(Options(directory));
        var insights = DeveloperStorageAnalysis.Analyze(results);
        Assert.Equal(3, insights.Count);
        Assert.Equal(149, insights.Sum(i => i.Size));
        Assert.True(insights.Single(i => i.Kind == "InsightBuilds").IsOld);
    }

    [Fact]
    public async Task AggregateTilesOpenAnExplorerWithSearchExtensionsAndSorting()
    {
        using var directory = new TestDirectory();
        directory.File("largest.bin", 1000); directory.File("detail/alpha.log", 12); directory.File("detail/beta.zip", 30);
        using var model = TestModels.Create(Scanner(), Options(directory) with { TopN = 1 });
        await model.Initialization;
        await model.StartScanCommand.ExecuteAsync(null);
        model.NavigateTreemap(Path.Combine(directory.Path, "detail"));
        await model.ExplorerLoading;
        await model.ActivateTreemapItemCommand.ExecuteAsync(model.TreemapItems.Single(i => i.IsAggregate));
        Assert.Equal(1, model.ResultsTabIndex);
        Assert.Equal(2, model.ExplorerItems.Count);
        Assert.Equal(2, model.Breadcrumbs.Count);
        model.ExtensionFilter = "log";
        await model.ExplorerFiltering;
        Assert.Equal("alpha.log", Assert.Single(model.ExplorerItems).Name);
        model.ExtensionFilter = ""; model.ExplorerSearch = "BETA";
        await model.ExplorerFiltering;
        Assert.Equal("beta.zip", Assert.Single(model.ExplorerItems).Name);
        model.ExplorerSearch = ""; model.ExplorerSort = model.SortChoices.Single(c => c.Id == "SizeAscending");
        Assert.Equal("alpha.log", model.ExplorerItems[0].Name);
    }

    [Fact]
    public async Task OverflowFolderTilesRemainReachableInExplorer()
    {
        using var directory = new TestDirectory();
        for (var i = 0; i < 35; i++) directory.File($"folder{i}/file", i + 1);
        using var model = TestModels.Create(Scanner(), Options(directory));
        await model.Initialization; await model.StartScanCommand.ExecuteAsync(null); await model.ExplorerLoading;
        Assert.Equal(30, model.TreemapItems.Count);
        await model.ActivateTreemapItemCommand.ExecuteAsync(model.TreemapItems.Single(i => i.IsAggregate));
        Assert.Equal(35, model.ExplorerItems.Count);
        await model.ExploreItemCommand.ExecuteAsync(model.ExplorerItems.Last());
        Assert.EndsWith("folder0", model.TreemapPath);
        Assert.Single(model.ExplorerItems);
    }

    [Fact]
    public async Task RetryStartsATargetedFullScanAndSnapshotCommandsUseIsolatedHistory()
    {
        using var directory = new TestDirectory();
        using var storage = new TestDirectory();
        directory.File("bin/app.exe", 200);
        var configuration = new MemoryConfiguration(ScanPresets.SourceFiles(Options(directory)));
        using var model = new MainWindowViewModel(Scanner(), new NullPicker(), new ExportService(), new RecordingShell(),
            configuration, new LocalizationService(), new FileTypeAnalyzer(),
            historyService: new ScanHistoryService(storage.Path));
        await model.Initialization; await model.StartScanCommand.ExecuteAsync(null); await model.ExplorerLoading;
        await model.SaveSnapshotCommand.ExecuteAsync(null);
        Assert.Single(model.SavedScans);
        var savedSettings = configuration.Saved;
        var controls = JsonSerializer.Serialize(model.BuildScanOptions());
        var recentPaths = model.RecentPaths.ToArray();
        model.SelectedCoverageItem = Assert.Single(model.CoverageItems);
        await model.RetryCoverageCommand.ExecuteAsync(null); await model.ExplorerLoading;
        Assert.Equal(Path.Combine(directory.Path, "bin"), model.LastResults!.RootPath);
        Assert.Equal(200, model.LastResults.Progress.BytesProcessed);
        Assert.Same(savedSettings, configuration.Saved);
        Assert.Equal(controls, JsonSerializer.Serialize(model.BuildScanOptions()));
        Assert.Equal(recentPaths, model.RecentPaths);
        await model.LoadSnapshotCommand.ExecuteAsync(null); await model.ExplorerLoading;
        Assert.Equal(directory.Path, model.LastResults.RootPath);
        Assert.Equal(directory.Path, model.SelectedPath);
        Assert.Contains("bin", model.BuildScanOptions().ExcludeDirectories);
        Assert.Equal(0, model.LastResults.Progress.BytesProcessed);
        await model.CompareSnapshotCommand.ExecuteAsync(null);
        Assert.Empty(model.FolderChanges);
        await model.DeleteSnapshotCommand.ExecuteAsync(null);
        Assert.Empty(model.SavedScans);
    }

    private sealed class BlockedAnalyzer(ManualResetEventSlim release) : IFileTypeAnalyzer
    {
        public string GetFileCategory(string extension) { release.Wait(TimeSpan.FromSeconds(5)); return "Other"; }
        public IReadOnlyList<FileTypeStats> AnalyzeFileTypes(IEnumerable<FileItem> files) => [];
    }
}
