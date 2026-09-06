using System.Globalization;
using System.Text.Json;
using Avalonia;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;
using FileStrider.Infrastructure.Analysis;
using FileStrider.Infrastructure.Configuration;
using FileStrider.Infrastructure.Export;
using FileStrider.MauiApp.Models;
using FileStrider.Scanner;

namespace FileStrider.Tests;

public class ReleaseRegressionTests
{
    [Fact]
    public async Task FileSystemRootCanBeScannedWithoutResolvingItAsALink()
    {
        var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!;
        var result = await Scanner().ScanAsync(new ScanOptions
        {
            RootPath = root,
            MaxDepth = 0,
            ConcurrencyLimit = 1
        }).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(result.ErrorMessage);
        Assert.True(result.IsCompleted);
        Assert.Contains(result.Folders, folder => folder.FullPath == root);
    }

    [Fact]
    public async Task PreCancelledScanReturnsInsteadOfWaitingForChannelCompletion()
    {
        using var fixture = new TestDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await Scanner().ScanAsync(Options(fixture), cancellationToken: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.WasCancelled);
        Assert.False(result.IsCompleted);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancellationReturnsStablePartialResults(int workers)
    {
        using var fixture = new TestDirectory();
        for (var i = 0; i < 500; i++) fixture.File($"child/{i}.txt", 10);
        using var cancellation = new CancellationTokenSource();
        var reports = new InlineProgress<ScanProgress>(p => { if (p.FilesScanned >= 100) cancellation.Cancel(); });
        var result = await Scanner().ScanAsync(Options(fixture) with { ConcurrencyLimit = workers }, reports, cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.WasCancelled);
        Assert.False(result.IsCompleted);
        Assert.InRange(result.Progress.FilesScanned, 100, 500);
        Assert.Equal(result.Progress.FilesScanned * 10L, result.Progress.BytesProcessed);
        var size = result.Progress.BytesProcessed;
        await Task.Delay(30);
        Assert.Equal(size, result.Progress.BytesProcessed);
        Assert.Equal(size, result.Folders.Single(f => f.FullPath == fixture.Path).RecursiveSize);
    }

    [Fact]
    public async Task ConsumerFailureWithQueuedEntriesReturnsTheOriginalError()
    {
        using var fixture = new TestDirectory();
        for (var i = 0; i < 30; i++) fixture.File($"{i}.txt");
        var result = await new FileSystemScanner(new FailingAnalyzer()).ScanAsync(Options(fixture))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("controlled failure", result.ErrorMessage);
        Assert.False(result.IsCompleted);
    }

    [Fact]
    public async Task EmptyDirectoriesAreIncludedInResults()
    {
        using var fixture = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(fixture.Path, "empty"));
        var result = await Scanner().ScanAsync(Options(fixture));
        Assert.True(result.IsCompleted);
        Assert.Equal(2, result.Folders.Count);
        Assert.Equal(1, result.Progress.FoldersScanned);
        Assert.All(result.Folders, f => Assert.Equal(0, f.RecursiveSize));
    }

    [Fact]
    public async Task CoverageDistinguishesFiltersAndDepthLimits()
    {
        using var fixture = new TestDirectory();
        fixture.File("keep.txt", 50); fixture.File("skip.tmp"); fixture.File("nested/deeper.txt");
        var result = await Scanner().ScanAsync(Options(fixture) with { MaxDepth = 0, ExcludePatterns = ["*.tmp"] });
        Assert.Equal(1, result.Progress.ExcludedItems);
        Assert.Equal(1, result.Progress.DepthLimitedDirectories);
        Assert.True(result.Progress.HasIncompleteCoverage);
        Assert.Equal(50, result.Progress.BytesProcessed);
    }

    [Fact]
    public async Task MultiSegmentDirectoryExclusionsWork()
    {
        using var fixture = new TestDirectory();
        fixture.File("Library/Caches/skipped.txt");
        fixture.File("Library/Documents/kept.txt", 50);
        var result = await Scanner().ScanAsync(Options(fixture) with { ExcludeDirectories = ["Library/Caches"] });
        Assert.Equal(50, result.Progress.BytesProcessed);
        Assert.Equal(1, result.Progress.ExcludedItems);
    }

    [LinuxFact]
    public async Task LinuxPathsRemainCaseSensitive()
    {
        using var fixture = new TestDirectory();
        fixture.File("a/file.txt", 40); fixture.File("A/file.txt", 60);
        var result = await Scanner().ScanAsync(Options(fixture));
        Assert.Equal(3, result.Folders.Count);
        Assert.Equal(100, result.Progress.BytesProcessed);
    }

    [LinuxFact]
    public async Task FollowingSymlinkCyclesTerminatesWithoutCountingTwice()
    {
        using var fixture = new TestDirectory();
        fixture.File("child/file.txt", 50);
        Directory.CreateSymbolicLink(Path.Combine(fixture.Path, "child", "loop"), fixture.Path);
        var result = await Scanner().ScanAsync(Options(fixture) with { FollowSymlinks = true })
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.IsCompleted);
        Assert.Equal(50, result.Progress.BytesProcessed);
        Assert.Equal(1, result.Progress.ExcludedItems);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("es-ES")]
    [InlineData("sv-SE")]
    public async Task CsvUsesInvariantColumnsAndNeutralizesFormulas(string culture)
    {
        using var fixture = new TestDirectory();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var data = new ScanResults {
                TopFiles = [new FileItem { Name = "=1+1", FullPath = "/example/file", Size = 1572864 }],
                FileTypeStatistics = [new FileTypeStats { Category = "@formula", TotalSize = 1572864, FileCount = 1, Percentage = 100 }]
            };
            var path = Path.Combine(fixture.Path, "export.csv");
            await new ExportService().ExportToCsvAsync(data, path);
            var lines = await File.ReadAllLinesAsync(path);
            Assert.Equal(6, lines[2].Split(',').Length);
            Assert.Contains(",1.50,", lines[2]);
            Assert.StartsWith("'=1+1,", lines[2]);
            Assert.Contains(lines, line => line.StartsWith(",'@formula,"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task ConfigurationSavesAllOptionsAndRecoversPreviousValidCopy()
    {
        using var fixture = new TestDirectory();
        var path = Path.Combine(fixture.Path, "settings", "config.json");
        var service = new ConfigurationService(path);
        var options = Options(fixture) with { FoldersOnly = true, FollowSymlinks = true, MaxDepth = 2,
            ExcludePatterns = ["*.bin"], RecentPaths = [fixture.Path], Language = "fr", MinFileSize = 4096 };
        await service.SaveDefaultOptionsAsync(options);
        var loaded = await service.LoadDefaultOptionsAsync();
        Assert.True(loaded.FoldersOnly);
        Assert.True(loaded.FollowSymlinks);
        Assert.Equal(2, loaded.MaxDepth);
        Assert.Equal(["*.bin"], loaded.ExcludePatterns);
        Assert.Equal("fr", loaded.Language);
        await service.SaveDefaultOptionsAsync(options with { TopN = 99 });
        await File.WriteAllTextAsync(path, "{broken json");
        Assert.Equal(options.TopN, (await service.LoadDefaultOptionsAsync()).TopN);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task ConfigurationNormalizesMalformedButReadableValues()
    {
        using var fixture = new TestDirectory();
        var path = Path.Combine(fixture.Path, "config.json");
        await File.WriteAllTextAsync(path, """{"topN":0,"minFileSize":-1,"maxDepth":-3,"concurrencyLimit":999999,"excludePatterns":null,"excludeDirectories":null,"recentPaths":null}""");
        var options = await new ConfigurationService(path).LoadDefaultOptionsAsync();
        Assert.Equal(1, options.TopN);
        Assert.Equal(0, options.MinFileSize);
        Assert.Null(options.MaxDepth);
        Assert.InRange(options.ConcurrencyLimit, 1, Environment.ProcessorCount * 2);
        Assert.NotNull(options.ExcludePatterns);
        Assert.NotNull(options.RecentPaths);
    }

    [Fact]
    public async Task ScanCommandsShareReentryAndCancellationState()
    {
        using var fixture = new TestDirectory();
        var scanner = new ControlledScanner();
        using var vm = TestModels.Create(scanner, Options(fixture));
        await vm.Initialization;
        var first = vm.StartScanCommand.ExecuteAsync(null);
        await scanner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(vm.StartScanCommand.CanExecute(null));
        Assert.False(vm.QuickScanCommand.CanExecute(null));
        Assert.True(vm.CancelScanCommand.CanExecute(null));
        await vm.QuickScanCommand.ExecuteAsync(null);
        Assert.Equal(1, scanner.Calls);
        vm.CancelScanCommand.Execute(null);
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(scanner.Token.IsCancellationRequested);
        Assert.True(vm.StartScanCommand.CanExecute(null));
        Assert.True(vm.QuickScanCommand.CanExecute(null));
        Assert.False(vm.CancelScanCommand.CanExecute(null));
    }

    [Fact]
    public async Task SettingsUnitsAndValidationPreserveAdvancedOptions()
    {
        using var fixture = new TestDirectory();
        var options = Options(fixture) with { MinFileSize = 1048576, MaxDepth = 2, FoldersOnly = true,
            FollowSymlinks = true, ExcludePatterns = ["*.tmp"], ExcludeDirectories = ["cache"] };
        var config = new MemoryConfiguration(options);
        using var vm = TestModels.Create(Scanner(), options, config);
        await vm.Initialization;
        vm.SelectedSizeUnit = vm.SizeUnits.Single(u => u.Name == "MiB");
        Assert.Equal(1, vm.MinFileSize);
        var actual = vm.BuildScanOptions();
        Assert.Equal(options.MinFileSize, actual.MinFileSize);
        Assert.Equal(2, actual.MaxDepth);
        Assert.True(actual.FoldersOnly);
        Assert.True(actual.FollowSymlinks);
        Assert.Equal(options.ExcludePatterns, actual.ExcludePatterns);
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Equal(2, config.Saved.MaxDepth);
        vm.MinFileSize = decimal.MaxValue;
        await vm.StartScanCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.StatusMessage);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task TreemapDrilldownAndExportUseFullScanInsteadOfTopNCounts()
    {
        using var fixture = new TestDirectory();
        fixture.File("a/large.bin", 100); fixture.File("a/small.txt", 20); fixture.File("root.txt", 30);
        var options = Options(fixture) with { TopN = 1 };
        using var vm = TestModels.Create(Scanner(), options);
        await vm.Initialization;
        await vm.StartScanCommand.ExecuteAsync(null);
        Assert.NotNull(vm.LastResults);
        Assert.Equal(3, vm.LastResults.Progress.FilesScanned);
        Assert.Equal(150, vm.TreemapItems.Sum(i => i.Size));
        Assert.Equal(fixture.Path, vm.TreemapPath);
        var child = vm.TreemapItems.Single(i => i.Name == "a");
        await vm.ActivateTreemapItemCommand.ExecuteAsync(child);
        Assert.Equal(Path.Combine(fixture.Path, "a"), vm.TreemapPath);
        Assert.Equal(120, vm.TreemapItems.Sum(i => i.Size));
        Assert.True(vm.CanNavigateUp);
        vm.NavigateUpCommand.Execute(null);
        Assert.Equal(fixture.Path, vm.TreemapPath);
        Assert.False(vm.CanNavigateUp);

        var path = Path.Combine(fixture.Path, "result.json");
        await vm.ExportToPathAsync("json", path);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.True(json.RootElement.GetProperty("scanCompleted").GetBoolean());
        var progress = json.RootElement.GetProperty("progress");
        Assert.Equal(3, progress.GetProperty("filesScanned").GetInt32());
        Assert.Equal(150, progress.GetProperty("bytesProcessed").GetInt64());
        Assert.Equal(fixture.Path, json.RootElement.GetProperty("rootPath").GetString());
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(1000, 20)]
    [InlineData(20, 1000)]
    [InlineData(1, 1)]
    public void TreemapPreservesAreaAndStaysWithinBounds(double width, double height)
    {
        var items = new[] { 900L, 100, 1, 0 }.Select(size => new TreemapItem { Size = size }).ToArray();
        var bounds = new Rect(10, 20, width, height);
        var layout = TreemapLayout.CalculateLayout(items, bounds);
        foreach (var item in layout.Where(i => i.Size > 0))
        {
            Assert.True(item.Bounds.X >= bounds.X - 1e-8 && item.Bounds.Y >= bounds.Y - 1e-8);
            Assert.True(item.Bounds.Right <= bounds.Right + 1e-8 && item.Bounds.Bottom <= bounds.Bottom + 1e-8);
            Assert.Equal(item.Size / 1001.0 * width * height, item.Bounds.Width * item.Bounds.Height, 7);
        }
        for (var i = 0; i < layout.Count; i++)
            for (var j = i + 1; j < layout.Count; j++)
            {
                var overlap = layout[i].Bounds.Intersect(layout[j].Bounds);
                Assert.True(overlap.Width * overlap.Height < 1e-8);
            }
        Assert.Equal(0, items[^1].Percentage);
    }

    private static FileSystemScanner Scanner() => new(new FileTypeAnalyzer());
    private static ScanOptions Options(TestDirectory fixture) => new() { RootPath = fixture.Path, ConcurrencyLimit = 1 };
    private sealed class FailingAnalyzer : IFileTypeAnalyzer
    {
        public string GetFileCategory(string extension) { Thread.Sleep(50); throw new InvalidOperationException("controlled failure"); }
        public IReadOnlyList<FileTypeStats> AnalyzeFileTypes(IEnumerable<FileItem> files) => [];
    }
    private sealed class ControlledScanner : IFileSystemScanner
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ScanResults> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++; Token = cancellationToken; Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ScanResults();
        }
    }
}

public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires a Linux filesystem; exercised by the Linux CI job."; }
}
