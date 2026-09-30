using System.Diagnostics;
using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
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
    [InlineData("a", "b")]
    [InlineData("b", "a")]
    public async Task RealDirectoriesWinOverSiblingAliases(string realName, string aliasName)
    {
        using var fixture = new TestDirectory();
        var file = Path.GetFullPath(fixture.File($"{realName}/child/file.txt", 5));
        var real = Path.Combine(fixture.Path, realName);
        using var alias = await CreateDirectoryLinkAsync(Path.Combine(fixture.Path, aliasName), real);
        var results = await ScanLinksAsync(fixture.Path);

        Assert.Equal(file, Assert.Single(results.TopFiles).FullPath);
        Assert.Equal(3, results.Folders.Count);
        Assert.Contains(results.Folders, f => f.FullPath == real && f.RecursiveSize == 5);
        Assert.DoesNotContain(results.Folders, f => f.FullPath.StartsWith(alias.Path, StringComparison.Ordinal));
        Assert.Equal(1, results.Progress.ExcludedItems);
    }

    [Fact]
    public async Task ChainedLinksOutsideTheRootAreScannedOnce()
    {
        using var fixture = new TestDirectory();
        fixture.File("target/file.txt", 5);
        var root = Directory.CreateDirectory(Path.Combine(fixture.Path, "scan")).FullName;
        using var intermediate = await CreateDirectoryLinkAsync(Path.Combine(fixture.Path, "intermediate"), Path.Combine(fixture.Path, "target"));
        using var first = await CreateDirectoryLinkAsync(Path.Combine(root, "first"), intermediate.Path);
        using var second = await CreateDirectoryLinkAsync(Path.Combine(root, "second"), intermediate.Path);
        var results = await ScanLinksAsync(root);

        Assert.Equal(1, results.Progress.FilesScanned);
        Assert.Equal(5, results.Progress.BytesProcessed);
        Assert.Equal(2, results.Folders.Count);
        Assert.Equal(1, results.Progress.ExcludedItems);
    }

    [Fact]
    public async Task DanglingLinksAndClosedCyclesDoNotPreventScanningReadableFiles()
    {
        using var fixture = new TestDirectory();
        var file = fixture.File("file.txt", 5);
        using var dangling = await CreateDirectoryLinkAsync(Path.Combine(fixture.Path, "dangling"), Path.Combine(fixture.Path, "missing"));
        using var cycleA = await CreateDirectoryLinkAsync(Path.Combine(fixture.Path, "cycle-a"), Path.Combine(fixture.Path, "cycle-b"));
        using var cycleB = await CreateDirectoryLinkAsync(Path.Combine(fixture.Path, "cycle-b"), cycleA.Path);
        var results = await ScanLinksAsync(fixture.Path);

        Assert.Equal(file, Assert.Single(results.TopFiles).FullPath);
        Assert.Equal(1, results.Progress.FilesScanned);
        Assert.Equal(5, results.Progress.BytesProcessed);
        Assert.True(results.Progress.InaccessibleItems >= 3);
        Assert.True(results.Progress.HasIncompleteCoverage);
    }

    [Fact]
    public async Task SelectedRootThroughALinkedAncestorWorksWithoutFollowingChildLinks()
    {
        using var fixture = new TestDirectory();
        fixture.File("real/child/file.txt", 5);
        using var alias = await CreateDirectoryLinkAsync(Path.Combine(fixture.Path, "alias"), Path.Combine(fixture.Path, "real"));
        using var loop = await CreateDirectoryLinkAsync(Path.Combine(fixture.Path, "real", "child", "loop"), Path.Combine(alias.Path, "child"));
        var root = Path.Combine(alias.Path, "child");
        var results = await ScanLinksAsync(root, follow: false);

        Assert.Equal(Path.Combine(root, "file.txt"), Assert.Single(results.TopFiles).FullPath);
        Assert.Equal(root, Assert.Single(results.Folders).FullPath);
        Assert.Equal(1, results.Progress.ExcludedItems);
        Assert.Equal(5, results.Progress.BytesProcessed);
    }

    [Fact]
    public async Task CaseAliasesOnCaseInsensitiveFilesystemsDoNotDoubleCount()
    {
        using var fixture = new TestDirectory();
        fixture.File("MixedCase/child/file.txt", 5);
        var root = Path.Combine(fixture.Path, "MixedCase", "child");
        var caseAlias = Path.Combine(fixture.Path, "MIXEDCASE", "CHILD");
        // Case-sensitive volumes do not have this alias; the link cases still run there.
        if (!Directory.Exists(caseAlias)) return;
        using var loop = await CreateDirectoryLinkAsync(Path.Combine(root, "loop"), caseAlias);
        var results = await ScanLinksAsync(root);

        Assert.Equal(1, results.Progress.FilesScanned);
        Assert.Equal(5, results.Progress.BytesProcessed);
        Assert.Single(results.Folders);
        Assert.Equal(1, results.Progress.ExcludedItems);
    }

    [WindowsFact]
    public async Task ReadableRootDoesNotRequireAncestorReadAttributes()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new TestDirectory();
        var file = Path.GetFullPath(fixture.File("ancestor/root/file.txt", 5));
        var ancestor = new DirectoryInfo(Path.Combine(fixture.Path, "ancestor"));
        var original = ancestor.GetAccessControl();
        var restricted = ancestor.GetAccessControl();
        using var user = WindowsIdentity.GetCurrent();
        restricted.AddAccessRule(new FileSystemAccessRule(user.User!, FileSystemRights.ReadAttributes,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
        try
        {
            ancestor.SetAccessControl(restricted);
            var hasReadAttributesDeny = false;
            foreach (FileSystemAccessRule rule in ancestor.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)))
                hasReadAttributesDeny |= rule.AccessControlType == AccessControlType.Deny &&
                    (rule.FileSystemRights & FileSystemRights.ReadAttributes) != 0;
            Assert.True(hasReadAttributesDeny);
            var results = await ScanLinksAsync(Path.GetDirectoryName(file)!);
            Assert.Equal(file, Assert.Single(results.TopFiles).FullPath);
            Assert.Equal(5, results.Progress.BytesProcessed);
        }
        finally { ancestor.SetAccessControl(original); }
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
            await new ExportService().ExportToJsonAsync(results, path + ".json");
        }
        finally { CultureInfo.CurrentCulture = previous; }
        var lines = await File.ReadAllLinesAsync(path);

        Assert.Equal("Scan Metadata", lines[0]);
        Assert.Equal("Field,Value", lines[1]);
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
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path + ".json"));
        var metadata = json.RootElement;
        Assert.Equal(results.RootPath, metadata.GetProperty("rootPath").GetString());
        Assert.Equal(completed, metadata.GetProperty("scanCompleted").GetBoolean());
        Assert.Equal(cancelled, metadata.GetProperty("scanCancelled").GetBoolean());
        Assert.Equal(error, metadata.GetProperty("errorMessage").GetString());
        Assert.Equal(DateTimeKind.Utc, metadata.GetProperty("exportedAt").GetDateTime().Kind);
        var progress = metadata.GetProperty("progress");
        Assert.Equal(12, progress.GetProperty("filesScanned").GetInt32());
        Assert.Equal(3, progress.GetProperty("foldersScanned").GetInt32());
        Assert.Equal(1572864, progress.GetProperty("bytesProcessed").GetInt64());
        Assert.Equal("00:00:01.2340000", progress.GetProperty("elapsed").GetString());
        Assert.Equal(17, progress.GetProperty("skippedItems").GetInt32());
        Assert.Equal(2, progress.GetProperty("inaccessibleItems").GetInt32());
        Assert.Equal(4, progress.GetProperty("offlineItems").GetInt32());
        Assert.Equal(5, progress.GetProperty("excludedItems").GetInt32());
        Assert.Equal(6, progress.GetProperty("depthLimitedDirectories").GetInt32());
        Assert.True(progress.GetProperty("hasIncompleteCoverage").GetBoolean());
    }

    [Fact]
    public async Task SettingsPreserveSaveErrorsAndRemainUsableAfterFailure()
    {
        using var fixture = new TestDirectory();
        var path = Path.Combine(fixture.Path, "config.json");
        // A directory at the destination forces a save failure with a temporary file to clean up.
        Directory.CreateDirectory(path);
        var service = new ConfigurationService(path);
        var failure = await Record.ExceptionAsync(() =>
            service.SaveDefaultOptionsAsync(new ScanOptions()).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Contains("MoveFile", failure.StackTrace);
        Assert.Empty(Directory.GetFiles(fixture.Path, "*.tmp"));

        var defaults = await service.LoadDefaultOptionsAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(20, defaults.TopN);
        Directory.Delete(path);
        await service.SaveDefaultOptionsAsync(new ScanOptions { TopN = 27 }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(27, (await service.LoadDefaultOptionsAsync().WaitAsync(TimeSpan.FromSeconds(5))).TopN);
    }

    [WindowsFact]
    public void TemporaryFileCleanupToleratesLockedAndReadOnlyFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new TestDirectory();
        var path = fixture.File("settings.tmp", 1);
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ConfigurationService.DeleteTemporaryFile(path);
            Assert.True(File.Exists(path));
        }
        try
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
            ConfigurationService.DeleteTemporaryFile(path);
            Assert.True(File.Exists(path));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        ConfigurationService.DeleteTemporaryFile(path);
        Assert.False(File.Exists(path));
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

    [Fact]
    public void TreemapSkewedRangesPreservePercentagesAndTotalArea()
    {
        var items = Enumerable.Range(0, 61).Select(i => new TreemapItem { Size = 1L << i }).ToList();
        var bounds = new Rect(0, 0, 800, 600);
        var layout = TreemapLayout.CalculateLayout(items, bounds);
        Assert.Equal(100, layout.Sum(item => item.Percentage), 10);
        Assert.All(layout, item => Assert.True(double.IsFinite(item.Bounds.Width) && double.IsFinite(item.Bounds.Height)));
        Assert.Equal(bounds.Width * bounds.Height, layout.Sum(item => item.Bounds.Width * item.Bounds.Height), 7);
        Assert.All(layout, item => Assert.Equal(100.0 * item.Size / ((1L << 61) - 1), item.Percentage, 10));
    }

    private static FileSystemScanner Scanner() => new(new FileTypeAnalyzer());

    private static async Task<ScanResults> ScanLinksAsync(string root, bool follow = true)
    {
        var results = await Scanner().ScanAsync(new ScanOptions {
            RootPath = root, FollowSymlinks = follow, ConcurrencyLimit = 1
        }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(results.IsCompleted, results.ErrorMessage);
        Assert.Null(results.ErrorMessage);
        return results;
    }

    private static async Task<DirectoryLink> CreateDirectoryLinkAsync(string path, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(path, target);
            return new DirectoryLink(path);
        }
        // Junctions exercise Windows links without requiring administrator or developer-mode privileges.
        var start = new ProcessStartInfo("cmd.exe") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            // cmd parses its own command line; ArgumentList applies incompatible CRT quote escaping.
            Arguments = $"/d /c mklink /J \"{path}\" \"{target}\""
        };
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
        return new DirectoryLink(path);
    }

    private sealed class DirectoryLink(string path) : IDisposable
    {
        public string Path { get; } = path;
        // Remove links before their targets; Windows cannot always delete dangling junctions recursively.
        public void Dispose() => new DirectoryInfo(Path).Delete();
    }

}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires Windows filesystem semantics; exercised by the Windows CI job."; }
}
