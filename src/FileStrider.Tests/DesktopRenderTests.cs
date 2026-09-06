using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;
using FileStrider.MauiApp.Controls;
using FileStrider.MauiApp.Views;

[assembly: AvaloniaTestApplication(typeof(FileStrider.Tests.RenderTestApplication))]
namespace FileStrider.Tests;

public static class RenderTestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<FileStrider.MauiApp.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont();
}

public class DesktopRenderTests
{
    [AvaloniaTheory]
    [InlineData("en", false, 1600, 1000)]
    [InlineData("en", true, 1600, 1000)]
    [InlineData("fr", false, 1600, 1000)]
    [InlineData("es", false, 1600, 1000)]
    [InlineData("sv", false, 1600, 1000)]
    [InlineData("en", false, 1040, 700)]
    [InlineData("fr", true, 1040, 700)]
    [InlineData("es", false, 1040, 700)]
    [InlineData("sv", true, 1040, 700)]
    public async Task WindowRendersWithoutClippedPrimaryControls(string language, bool dark, int width, int height)
    {
        using var fixture = new TestDirectory();
        var result = DemoResults();
        using var vm = TestModels.Create(new DemoScanner(result), new ScanOptions { RootPath = fixture.Path, Language = language });
        await vm.Initialization;
        var window = new MainWindow { DataContext = vm, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            await vm.StartScanCommand.ExecuteAsync(null);
            // Screenshots show only the synthetic dataset, never a real profile or recent path.
            vm.RecentPaths.Clear();
            vm.RecentPaths.Add(result.RootPath);
            vm.SelectedRecentPath = result.RootPath;
            vm.SelectedPath = result.RootPath;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var map = window.GetVisualDescendants().OfType<TreemapControl>().Single();
            Assert.True(map.Bounds.Height >= 50, $"Treemap height: {map.Bounds.Height}");
            foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
            {
                var point = button.TranslatePoint(default, window);
                Assert.NotNull(point);
                Assert.True(point.Value.X >= -1 && point.Value.Y >= -1 &&
                    point.Value.X + button.Bounds.Width <= width + 1 &&
                    point.Value.Y + button.Bounds.Height <= height + 1, $"Button clipped: {button.Content}");
            }
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (Environment.GetEnvironmentVariable("FILESTRIDER_SCREENSHOT_DIR") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, $"{language}-{(dark ? "dark" : "light")}-{width}.png"));
            }
        }
        finally { window.Close(); }
    }

    private static ScanResults DemoResults()
    {
        var root = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "Sample Storage");
        var folders = new[] { ("Videos", 12884901888L), ("Photos", 6442450944L), ("Projects", 3221225472L), ("Documents", 1610612736L) };
        var results = new ScanResults { RootPath = root, IsCompleted = true, Progress = new ScanProgress {
            FilesScanned = 18426, FoldersScanned = 846, BytesProcessed = folders.Sum(f => f.Item2),
            Elapsed = TimeSpan.FromSeconds(12.4), ExcludedItems = 142 } };
        results.Folders.Add(new FolderItem { Name = "Sample Storage", FullPath = root, RecursiveSize = results.Progress.BytesProcessed, ItemCount = 18426 });
        foreach (var (name, size) in folders)
            results.Folders.Add(new FolderItem { Name = name, FullPath = Path.Combine(root, name), RecursiveSize = size, DirectSize = size, ItemCount = 1000 });
        results.TopFolders.AddRange(results.Folders.OrderByDescending(f => f.RecursiveSize));
        results.TopFiles.AddRange(new[] {
            new FileItem { Name = "Summer road trip.mp4", FullPath = Path.Combine(root,"Videos","Summer road trip.mp4"), Size = 4294967296, Type = ".mp4" },
            new FileItem { Name = "Project archive.zip", FullPath = Path.Combine(root,"Projects","Project archive.zip"), Size = 2147483648, Type = ".zip" },
            new FileItem { Name = "Family collection.zip", FullPath = Path.Combine(root,"Photos","Family collection.zip"), Size = 1610612736, Type = ".zip" },
            new FileItem { Name = "Design assets.psd", FullPath = Path.Combine(root,"Projects","Design assets.psd"), Size = 536870912, Type = ".psd" },
            new FileItem { Name = "Travel journal.pdf", FullPath = Path.Combine(root,"Documents","Travel journal.pdf"), Size = 104857600, Type = ".pdf" }
        });
        foreach (var (name, size) in folders)
            results.FileTypeStatistics.Add(new FileTypeStats { Category = name == "Photos" ? "Images" : name == "Projects" ? "Code" : name,
                TotalSize = size, FileCount = name == "Videos" ? 48 : 6126, Percentage = 100.0 * size / results.Progress.BytesProcessed });
        return results;
    }
    private sealed class DemoScanner(ScanResults result) : IFileSystemScanner
    {
        public Task<ScanResults> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
