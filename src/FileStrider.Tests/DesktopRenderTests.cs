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
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("sv")]
    public async Task AboutShowsTheRunningAssemblyVersionWithoutClipping(string language)
    {
        using var fixture = new TestDirectory();
        using var vm = TestModels.Create(new DemoScanner(new ScanResults()),
            new ScanOptions { RootPath = fixture.Path, Language = language });
        await vm.Initialization;
        Window? about = null;
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => about = window);
        try
        {
            await vm.ShowAboutCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.NotNull(about);
            var version = typeof(FileStrider.MauiApp.App).Assembly.GetName().Version!.ToString();
            Assert.Contains(about.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == vm.VersionText && text.Text.Contains(version));
            foreach (var button in about.GetVisualDescendants().OfType<Button>())
            {
                var point = button.TranslatePoint(default, about)!.Value;
                Assert.True(point.Y >= 0 && point.Y + button.Bounds.Height <= about.Bounds.Height);
            }
        }
        finally { about?.Close(); }
    }

    [AvaloniaFact]
    public async Task ScanFailureIsVisibleBesideScanControls()
    {
        using var fixture = new TestDirectory();
        using var vm = TestModels.Create(new DemoScanner(new ScanResults { ErrorMessage = "controlled scan failure" }),
            new ScanOptions { RootPath = fixture.Path });
        await vm.Initialization;
        var window = new MainWindow { DataContext = vm, Width = 1040, Height = 700 };
        try
        {
            window.Show();
            await vm.StartScanCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var status = window.FindControl<TextBlock>("ScanStatusMessage")!;
            Assert.True(status.IsEffectivelyVisible);
            Assert.Contains("controlled scan failure", status.Text);
            Assert.InRange(status.TranslatePoint(default, window)!.Value.Y, 0, 180);
            Assert.True(vm.CanScan);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", false, 1600, 1000)]
    [InlineData("en", true, 1600, 1000)]
    [InlineData("fr", false, 1600, 1000)]
    [InlineData("fr", true, 1600, 1000)]
    [InlineData("es", false, 1600, 1000)]
    [InlineData("es", true, 1600, 1000)]
    [InlineData("sv", false, 1600, 1000)]
    [InlineData("sv", true, 1600, 1000)]
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
                if (width == 1600)
                {
                    vm.NavigateTreemap(Path.Combine(result.RootPath, "Videos"));
                    SaveStoreFrame(window, Path.Combine(output, $"{language}-{(dark ? "dark" : "light")}-detail.png"));
                    vm.NavigateTreemap(result.RootPath);
                    vm.MaxDepth = 4;
                    vm.ConcurrencyLimit = Math.Min(4, Environment.ProcessorCount);
                    vm.SelectedSizeUnit = vm.SizeUnits.Single(unit => unit.Name == "MiB");
                    vm.MinFileSize = 100;
                    vm.ExcludePatternsText = "*.tmp, *.log";
                    window.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
                    SaveStoreFrame(window, Path.Combine(output, $"{language}-{(dark ? "dark" : "light")}-options.png"));
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", false, 1280, 860)]
    [InlineData("fr", true, 1040, 700)]
    [InlineData("es", false, 1040, 700)]
    [InlineData("sv", true, 1040, 700)]
    public async Task FeatureTabsRenderAndRelocalizeWithoutClipping(string language, bool dark, int width, int height)
    {
        using var fixture = new TestDirectory();
        var result = DemoResults();
        result.CoverageIssues.Add(new(Path.Combine(result.RootPath, "Restricted"), FileStrider.Core.Models.CoverageReason.Inaccessible, true));
        result.ProjectDirectories.Add(result.RootPath);
        result.Folders.Add(new FileStrider.Core.Models.FolderItem { Name = "node_modules", FullPath = Path.Combine(result.RootPath, "node_modules"), RecursiveSize = 1000000 });
        using var vm = TestModels.Create(new DemoScanner(result), new ScanOptions { RootPath = fixture.Path });
        await vm.Initialization;
        var window = new MainWindow { DataContext = vm, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            await vm.StartScanCommand.ExecuteAsync(null);
            vm.SelectedLanguage = vm.AvailableLanguages.Single(l => l.Code == language);
            vm.SelectedPath = result.RootPath; vm.RecentPaths.Clear();
            vm.NavigateTreemap(Path.Combine(result.RootPath, "Videos"));
            await vm.ExplorerLoading;
            vm.SavedScans.Add(new("sample", result.ScannedAt, result.RootPath, result.Progress.BytesProcessed));
            vm.SelectedSavedScan = vm.SavedScans[0];
            vm.FolderChanges.Add(new(Path.Combine(result.RootPath, "Videos"), 1000000, 3000000));
            for (var tab = 0; tab < 5; tab++)
            {
                vm.ResultsTabIndex = tab;
                await vm.ExplorerLoading;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Equal(vm[new[] { "Overview", "Explorer", "History", "Insights", "Coverage" }[tab]],
                    window.GetVisualDescendants().OfType<TabControl>().Single().SelectedItem is TabItem selected ? selected.Header : null);
                foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                {
                    var point = button.TranslatePoint(default, window)!.Value;
                    Assert.True(point.X >= -1 && point.Y >= -1 && point.X + button.Bounds.Width <= width + 1 &&
                        point.Y + button.Bounds.Height <= height + 1, $"Clipped button on tab {tab}: {button.Content}");
                }
                if (Environment.GetEnvironmentVariable("FILESTRIDER_SCREENSHOT_DIR") is { Length: > 0 } output)
                {
                    Directory.CreateDirectory(output);
                    SaveStoreFrame(window, Path.Combine(output, $"feature-{language}-{tab}.png"));
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TreemapSupportsKeyboardSelectionActivationAndUpNavigation()
    {
        var first = new FileStrider.MauiApp.Models.TreemapItem { Name = "folder", Size = 50 };
        var second = new FileStrider.MauiApp.Models.TreemapItem { Name = "aggregate", Size = 50, IsAggregate = true };
        var map = new TreemapControl { Items = [first, second] };
        var window = new Window { Content = map };
        FileStrider.MauiApp.Models.TreemapItem? activated = null;
        var up = false;
        map.ItemClicked += (_, item) => activated = item;
        map.NavigateUpRequested += (_, _) => up = true;
        try
        {
            window.Show(); map.Focus();
            Assert.Same(first, map.SelectedItem);
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowRight, Avalonia.Input.RawInputModifiers.None);
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
            Assert.Same(second, activated);
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Backspace, Avalonia.Input.RawInputModifiers.None);
            Assert.True(up);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TreemapKeepsHintsAndRepaintsWhenFocusChanges()
    {
        var first = new FileStrider.MauiApp.Models.TreemapItem { Name = "folder", Size = 50 };
        var map = new TreemapControl { Width = 300, Height = 200, Items = [first], HintText = "Arrow keys select tiles" };
        var button = new Button { Content = "Other control" };
        var window = new Window { Content = new StackPanel { Children = { map, button } } };
        byte[] Frame()
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            using var stream = new MemoryStream();
            frame!.Save(stream);
            return stream.ToArray();
        }
        try
        {
            window.Show(); map.Focus();
            Assert.Same(first, map.SelectedItem);
            Frame();
            var position = map.TranslatePoint(new Point(100, 100), window)!.Value;
            window.MouseMove(position);
            Assert.Contains(map.HintText, ToolTip.GetTip(map)!.ToString());
            Assert.Contains("folder", ToolTip.GetTip(map)!.ToString());
            window.MouseMove(new Point(400, 400));
            Assert.Equal(map.HintText, ToolTip.GetTip(map));
            map.Items.Clear(); map.Items.Add(first);
            map.HintText = "Les flèches sélectionnent";
            Assert.Equal(map.HintText, ToolTip.GetTip(map));
            button.Focus();
            var unfocused = Frame();
            map.Focus();
            var focused = Frame();
            Assert.False(unfocused.SequenceEqual(focused));
            button.Focus();
            Assert.True(unfocused.SequenceEqual(Frame()));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ResultKeyboardAndContextMenuActionsUseSelectedPaths()
    {
        using var fixture = new TestDirectory();
        var result = DemoResults();
        var shell = new RecordingShell();
        using var vm = new FileStrider.MauiApp.ViewModels.MainWindowViewModel(new DemoScanner(result), new NullPicker(),
            new FileStrider.Infrastructure.Export.ExportService(), shell, new MemoryConfiguration(new ScanOptions { RootPath = fixture.Path }),
            new FileStrider.Infrastructure.Localization.LocalizationService(), new FileStrider.Infrastructure.Analysis.FileTypeAnalyzer());
        await vm.Initialization;
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show(); await vm.StartScanCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => ReferenceEquals(l.ItemsSource, vm.TopFiles));
            list.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.True(list.ContainerFromIndex(1)!.Focus());
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.C, Avalonia.Input.RawInputModifiers.Control);
            Assert.Equal(result.TopFiles[1].FullPath, shell.Copied);
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
            Assert.Equal(result.TopFiles[1].FullPath, shell.Opened);
            list.ContextMenu!.Open(list);
            Dispatcher.UIThread.RunJobs();
            var copy = list.ContextMenu.Items.OfType<MenuItem>().Last();
            Assert.Equal(vm["CopyPath"], copy.Header);
            Assert.Equal(result.TopFiles[1].FullPath, copy.CommandParameter);
            Assert.NotNull(copy.Command);
            copy.Command.Execute(copy.CommandParameter);
            Assert.Equal(result.TopFiles[1].FullPath, shell.Copied);
            list.ContextMenu.Close();
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
            new FileItem { Name = "Training video.mp4", FullPath = Path.Combine(root,"Videos","Training video.mp4"), Size = 4294967296, Type = ".mp4" },
            new FileItem { Name = "Product demo.mov", FullPath = Path.Combine(root,"Videos","Product demo.mov"), Size = 3221225472, Type = ".mov" },
            new FileItem { Name = "Workshop recording.mp4", FullPath = Path.Combine(root,"Videos","Workshop recording.mp4"), Size = 2147483648, Type = ".mp4" },
            new FileItem { Name = "Feature walkthrough.mp4", FullPath = Path.Combine(root,"Videos","Feature walkthrough.mp4"), Size = 2147483648, Type = ".mp4" },
            new FileItem { Name = "Quick start.mp4", FullPath = Path.Combine(root,"Videos","Quick start.mp4"), Size = 1073741824, Type = ".mp4" },
            new FileItem { Name = "Project archive.zip", FullPath = Path.Combine(root,"Projects","Project archive.zip"), Size = 2147483648, Type = ".zip" },
            new FileItem { Name = "Reference images.zip", FullPath = Path.Combine(root,"Photos","Reference images.zip"), Size = 1610612736, Type = ".zip" },
            new FileItem { Name = "Design assets.psd", FullPath = Path.Combine(root,"Projects","Design assets.psd"), Size = 536870912, Type = ".psd" },
            new FileItem { Name = "Reference guide.pdf", FullPath = Path.Combine(root,"Documents","Reference guide.pdf"), Size = 104857600, Type = ".pdf" }
        });
        results.TopFiles.Sort((a, b) => b.Size.CompareTo(a.Size));
        foreach (var (name, size) in folders)
            results.FileTypeStatistics.Add(new FileTypeStats { Category = name == "Photos" ? "Images" : name == "Projects" ? "Code" : name,
                TotalSize = size, FileCount = name == "Videos" ? 48 : 6126, Percentage = 100.0 * size / results.Progress.BytesProcessed });
        return results;
    }

    private static void SaveStoreFrame(MainWindow window, string path)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(path);
    }
    private sealed class DemoScanner(ScanResults result) : IFileSystemScanner
    {
        public Task<ScanResults> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
