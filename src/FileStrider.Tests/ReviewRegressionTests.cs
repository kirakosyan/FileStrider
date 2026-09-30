using System.Diagnostics;
using System.Globalization;
using Avalonia;
using FileStrider.Core.Models;
using FileStrider.Infrastructure.Analysis;
using FileStrider.Infrastructure.Configuration;
using FileStrider.Infrastructure.Export;
using FileStrider.MauiApp.Models;
using FileStrider.Scanner;

namespace FileStrider.Tests;

public class ReviewRegressionTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task LinkedAncestorsAndLinkTargetsDoNotDoubleCountFiles(bool scanThroughAlias, bool targetThroughAlias)
    {
        using var fixture = new TestDirectory();
        fixture.File("real/child/file.txt", 5);
        var realRoot = Path.Combine(fixture.Path, "real", "child");
        using var alias = await CreateDirectoryLinkAsync(Path.Combine(fixture.Path, "alias"), Path.Combine(fixture.Path, "real"));
        var aliasRoot = Path.Combine(fixture.Path, "alias", "child");
        using var loop = await CreateDirectoryLinkAsync(Path.Combine(realRoot, "loop"), targetThroughAlias ? aliasRoot : realRoot);

        var root = scanThroughAlias ? aliasRoot : realRoot;
        var results = await Scanner().ScanAsync(new ScanOptions {
            RootPath = root, FollowSymlinks = true, ConcurrencyLimit = 1
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(results.IsCompleted);
        Assert.Null(results.ErrorMessage);
        Assert.Equal(1, results.Progress.FilesScanned);
        Assert.Equal(5, results.Progress.BytesProcessed);
        Assert.Equal(1, results.Progress.ExcludedItems);
        Assert.Equal(Path.Combine(root, "file.txt"), Assert.Single(results.TopFiles).FullPath);
        var folder = Assert.Single(results.Folders);
        Assert.Equal(root, folder.FullPath);
        Assert.Equal(5, folder.RecursiveSize);
        Assert.Equal(5, folder.DirectSize);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderDatesArePreservedWithConcurrentWorkers(bool concurrent)
    {
        using var fixture = new TestDirectory();
        var modified = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        for (var i = 0; i < 1000; i++)
        {
            fixture.File($"child{i:D4}/file.txt", 1);
            Directory.SetLastWriteTimeUtc(Path.Combine(fixture.Path, $"child{i:D4}"), modified);
        }
        Directory.SetLastWriteTimeUtc(fixture.Path, modified);
        for (var attempt = 0; attempt < (concurrent ? 10 : 1); attempt++)
        {
            var results = await Scanner().ScanAsync(new ScanOptions {
                RootPath = fixture.Path, ConcurrencyLimit = concurrent ? Environment.ProcessorCount * 2 : 1
            }).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(results.IsCompleted);
            Assert.Equal(1000, results.Progress.FilesScanned);
            Assert.Equal(1001, results.Folders.Count);
            Assert.All(results.Folders, folder => Assert.Equal(modified, folder.LastModified.ToUniversalTime()));
        }
    }

    [Theory]
    [InlineData(false, false, "")]
    [InlineData(false, true, "")]
    [InlineData(false, false, "=controlled failure, with details")]
    [InlineData(true, false, "")]
    public async Task CsvIncludesScanStatusTotalsAndCoverage(bool completed, bool cancelled, string error)
    {
        using var fixture = new TestDirectory();
        var results = new ScanResults {
            RootPath = fixture.Path,
            IsCompleted = completed,
            WasCancelled = cancelled,
            ErrorMessage = error,
            Progress = new ScanProgress {
                FilesScanned = 12, FoldersScanned = 3, BytesProcessed = 1572864,
                Elapsed = TimeSpan.FromMilliseconds(1234), InaccessibleItems = 2,
                OfflineItems = 4, ExcludedItems = 5, DepthLimitedDirectories = 6
            }
        };
        var path = Path.Combine(fixture.Path, "result.csv");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            await new ExportService().ExportToCsvAsync(results, path);
        }
        finally { CultureInfo.CurrentCulture = previous; }
        var lines = await File.ReadAllLinesAsync(path);

        Assert.Contains("Scan Metadata", lines);
        Assert.Contains("Field,Value", lines);
        Assert.Contains($"Root Path,{fixture.Path}", lines);
        Assert.Contains($"Scan Completed,{completed}", lines);
        Assert.Contains($"Scan Cancelled,{cancelled}", lines);
        Assert.Contains("Files Scanned,12", lines);
        Assert.Contains("Folders Scanned,3", lines);
        Assert.Contains("Bytes Processed,1572864", lines);
        Assert.Contains("Elapsed,00:00:01.2340000", lines);
        Assert.Contains("Skipped Items,17", lines);
        Assert.Contains("Inaccessible Items,2", lines);
        Assert.Contains("Offline Items,4", lines);
        Assert.Contains("Excluded Items,5", lines);
        Assert.Contains("Depth Limited Directories,6", lines);
        Assert.Contains("Incomplete Coverage,True", lines);
        Assert.Contains(string.IsNullOrEmpty(error) ? "Error Message," : $"Error Message,\"'{error}\"", lines);
        Assert.Contains(lines, line => line.StartsWith("Exported At (UTC),", StringComparison.Ordinal) && line.EndsWith('Z'));
    }

    [Fact]
    public async Task SettingsRemainUsableAfterTemporaryFileCleanupFails()
    {
        using var fixture = new TestDirectory();
        var path = Path.Combine(fixture.Path, "config.json");
        // A directory at the destination forces a save failure with a temporary file to clean up.
        Directory.CreateDirectory(path);
        var service = new FailingCleanupConfiguration(path);
        var failure = await Assert.ThrowsAsync<IOException>(() =>
            service.SaveDefaultOptionsAsync(new ScanOptions()).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("controlled cleanup failure", failure.Message);
        Assert.Single(Directory.GetFiles(fixture.Path, "*.tmp"));

        var defaults = await service.LoadDefaultOptionsAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(20, defaults.TopN);
        Directory.Delete(path);
        await service.SaveDefaultOptionsAsync(new ScanOptions { TopN = 27 }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(27, (await service.LoadDefaultOptionsAsync().WaitAsync(TimeSpan.FromSeconds(5))).TopN);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(1000, 20)]
    [InlineData(20, 1000)]
    public void TreemapExtremeSizeRatiosHaveFiniteBounds(double width, double height)
    {
        foreach (var sizes in new[] { new[] { 1L << 60, 1L, 1L }, new[] { long.MaxValue, long.MaxValue, 1L, 1L } })
        {
            var bounds = new Rect(10, 20, width, height);
            var layout = TreemapLayout.CalculateLayout(sizes.Select(size => new TreemapItem { Size = size }), bounds);
            foreach (var item in layout)
            {
                Assert.True(double.IsFinite(item.Bounds.X));
                Assert.True(double.IsFinite(item.Bounds.Y));
                Assert.True(double.IsFinite(item.Bounds.Width));
                Assert.True(double.IsFinite(item.Bounds.Height));
                Assert.InRange(item.Bounds.X, bounds.X, bounds.Right);
                Assert.InRange(item.Bounds.Y, bounds.Y, bounds.Bottom);
                Assert.True(item.Bounds.Width >= 0 && item.Bounds.Height >= 0);
                Assert.True(item.Bounds.Right <= bounds.Right && item.Bounds.Bottom <= bounds.Bottom);
            }
            Assert.Equal(width * height, layout.Sum(item => item.Bounds.Width * item.Bounds.Height), 7);
        }
    }

    private static FileSystemScanner Scanner() => new(new FileTypeAnalyzer());

    private static async Task<DirectoryLink> CreateDirectoryLinkAsync(string path, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(path, target);
            return new DirectoryLink(path);
        }
        // Junctions exercise Windows links without requiring administrator or developer-mode privileges.
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path '{path.Replace("'", "''")}' -Target '{target.Replace("'", "''")}' | Out-Null");
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
        return new DirectoryLink(path);
    }

    private sealed class DirectoryLink(string path) : IDisposable
    {
        // Remove links before their targets; Windows cannot always delete dangling junctions recursively.
        public void Dispose() => new DirectoryInfo(path).Delete();
    }

    private sealed class FailingCleanupConfiguration(string path) : ConfigurationService(path)
    {
        protected override void DeleteTemporaryFile(string path) => throw new IOException("controlled cleanup failure");
    }
}
