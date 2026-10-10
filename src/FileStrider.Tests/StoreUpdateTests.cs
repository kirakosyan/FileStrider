using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;
using FileStrider.Infrastructure.Analysis;
using FileStrider.Infrastructure.Export;
using FileStrider.Infrastructure.History;
using FileStrider.Infrastructure.Localization;
using FileStrider.MauiApp.Services;
using FileStrider.MauiApp.ViewModels;
using FileStrider.MauiApp.Views;

namespace FileStrider.Tests;

public class StoreUpdateTests
{
    [Fact]
    public async Task StartupChecksOnceAndManualCheckRefreshesAvailability()
    {
        using var fixture = new Fixture();
        await fixture.Model.CheckForUpdatesOnStartupAsync();
        Assert.True(fixture.Model.HasStoreUpdate);
        Assert.True(fixture.Model.HasUpdateNotice);
        Assert.True(fixture.Model.UpgradeCommand.CanExecute(null));
        await fixture.Model.CheckForUpdatesOnStartupAsync();
        Assert.Equal(1, fixture.Store.Checks);
        fixture.Store.Check = _ => Task.FromResult(false);
        await fixture.Model.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.False(fixture.Model.HasStoreUpdate);
        Assert.Equal(fixture.Model["UpdateCurrent"], fixture.Model.UpdateStatus);
        Assert.False(fixture.Model.UpgradeCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupWithoutAnUpdateIsQuietAndManualChecksReportResults(bool failure)
    {
        using var fixture = new Fixture();
        fixture.Store.Check = _ => failure ? Task.FromException<bool>(new IOException("offline")) : Task.FromResult(false);
        await fixture.Model.CheckForUpdatesOnStartupAsync();
        Assert.False(fixture.Model.HasUpdateNotice);
        Assert.False(fixture.Model.HasStoreUpdate);
        Assert.True(fixture.Model.CanScan);
        await fixture.Model.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.True(fixture.Model.HasUpdateNotice);
        Assert.Equal(fixture.Model[failure ? "UpdateCheckFailed" : "UpdateCurrent"], fixture.Model.UpdateStatus);
    }

    [Fact]
    public async Task FailedRecheckDoesNotForgetAKnownUpdate()
    {
        using var fixture = new Fixture();
        await fixture.Model.CheckForUpdatesOnStartupAsync();
        fixture.Store.Check = _ => Task.FromException<bool>(new IOException("offline"));
        await fixture.Model.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.True(fixture.Model.HasStoreUpdate);
        Assert.True(fixture.Model.UpgradeCommand.CanExecute(null));
    }

    [Fact]
    public async Task PendingCheckIsNotDuplicatedAndDisposalCancelsIt()
    {
        using var fixture = new Fixture();
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken requestToken = default;
        fixture.Store.Check = token => { requestToken = token; return pending.Task; };
        var check = fixture.Model.CheckForUpdatesOnStartupAsync();
        Assert.True(fixture.Model.IsCheckingForUpdates);
        Assert.False(fixture.Model.CheckForUpdatesCommand.CanExecute(null));
        await fixture.Model.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Store.Checks);
        fixture.Model.Dispose();
        await check.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(requestToken.IsCancellationRequested);
        Assert.False(fixture.Model.HasStoreUpdate);
        Assert.False(fixture.Model.CheckForUpdatesCommand.CanExecute(null));
        pending.SetResult(true);
    }

    [Fact]
    public async Task NonStoreBuildDoesNotCheckOrOfferUpgrade()
    {
        using var fixture = new Fixture(supported: false);
        await fixture.Model.CheckForUpdatesOnStartupAsync();
        await fixture.Model.CheckForUpdatesCommand.ExecuteAsync(null);
        await fixture.Model.UpgradeCommand.ExecuteAsync(null);
        Assert.Equal(0, fixture.Store.Checks);
        Assert.Empty(fixture.Events);
        Assert.False(fixture.Model.SupportsStoreUpdates);
        Assert.Equal(fixture.Model["UpdateUnsupported"], fixture.Model.UpdateStatus);
        Assert.False(new StoreUpdateService().IsSupported); // Test host is unpackaged, including on Windows.
    }

    [Fact]
    public async Task UpgradeSavesSettingsAndRestorableSnapshotBeforeStoreAndShutdown()
    {
        using var fixture = new Fixture();
        await fixture.Model.StartScanCommand.ExecuteAsync(null);
        fixture.Model.TopN = 37;
        await fixture.Model.CheckForUpdatesOnStartupAsync();
        fixture.Events.Clear();
        await fixture.Model.UpgradeCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "settings", "snapshot", "store", "shutdown" }, fixture.Events);
        Assert.Equal(37, fixture.Configuration.Saved.TopN);
        var info = Assert.Single(await fixture.History.Inner.ListAsync());
        var saved = await fixture.History.Inner.LoadAsync(info.Id);
        Assert.Equal(fixture.Model.LastResults!.RootPath, saved.Results.RootPath);
        Assert.Equal(123, saved.Results.Folders.Single().RecursiveSize);
    }

    [Theory]
    [InlineData("settings", "UpdateSaveFailed")]
    [InlineData("snapshot", "UpdateSaveFailed")]
    [InlineData("invalid-options", "UpdateSaveFailed")]
    [InlineData("store-false", "UpdateOpenFailed")]
    [InlineData("store-throw", "UpdateOpenFailed")]
    public async Task FailedUpgradeKeepsApplicationOpenAndCanBeRetried(string failure, string expectedStatus)
    {
        using var fixture = new Fixture();
        await fixture.Model.StartScanCommand.ExecuteAsync(null);
        await fixture.Model.CheckForUpdatesOnStartupAsync();
        fixture.Events.Clear();
        fixture.Configuration.Fail = failure == "settings";
        fixture.History.Fail = failure == "snapshot";
        if (failure == "invalid-options") fixture.Model.TopN = 0;
        if (failure == "store-false") fixture.Store.Open = _ => Task.FromResult(false);
        if (failure == "store-throw") fixture.Store.Open = _ => Task.FromException<bool>(new IOException("Store unavailable"));
        await fixture.Model.UpgradeCommand.ExecuteAsync(null);
        Assert.DoesNotContain("shutdown", fixture.Events);
        if (failure is "settings" or "snapshot" or "invalid-options") Assert.DoesNotContain("store", fixture.Events);
        Assert.True(fixture.Model.CanScan);
        Assert.False(fixture.Model.IsPreparingUpgrade);
        Assert.True(fixture.Model.UpgradeCommand.CanExecute(null));
        Assert.Equal(fixture.Model[expectedStatus], fixture.Model.UpdateStatus);
        fixture.Configuration.Fail = fixture.History.Fail = false;
        fixture.Model.TopN = 20;
        fixture.Store.Open = _ => Task.FromResult(true);
        await fixture.Model.UpgradeCommand.ExecuteAsync(null);
        Assert.Equal("shutdown", fixture.Events.Last());
    }

    [Fact]
    public async Task ActiveScanCannotBeClosedByUpgrade()
    {
        using var fixture = new Fixture();
        await fixture.Model.CheckForUpdatesOnStartupAsync();
        var pending = new TaskCompletionSource<ScanResults>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Scanner.Scan = (_, token) => pending.Task.WaitAsync(token);
        var scan = fixture.Model.StartScanCommand.ExecuteAsync(null);
        Assert.True(fixture.Model.IsScanning);
        Assert.False(fixture.Model.UpgradeCommand.CanExecute(null));
        Assert.Equal(fixture.Model["UpdateScanInProgress"], fixture.Model.UpgradeHint);
        await fixture.Model.UpgradeCommand.ExecuteAsync(null);
        Assert.DoesNotContain("store", fixture.Events);
        fixture.Model.CancelScanCommand.Execute(null);
        await scan;
        Assert.True(fixture.Model.UpgradeCommand.CanExecute(null));
    }

    [Fact]
    public async Task UpgradeWaitsForPendingSnapshotAndPreventsNewScansOrChecks()
    {
        using var fixture = new Fixture();
        await fixture.Model.StartScanCommand.ExecuteAsync(null);
        await fixture.Model.CheckForUpdatesOnStartupAsync();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.History.Pending = pending.Task;
        var saving = fixture.Model.SaveSnapshotCommand.ExecuteAsync(null);
        var upgrade = fixture.Model.UpgradeCommand.ExecuteAsync(null);
        Assert.True(fixture.Model.IsPreparingUpgrade);
        Assert.False(fixture.Model.CanScan);
        Assert.False(fixture.Model.CanCheckForUpdates);
        var scans = fixture.Scanner.Scans;
        await fixture.Model.StartScanCommand.ExecuteAsync(null); // Guard even direct command execution.
        Assert.Equal(scans, fixture.Scanner.Scans);
        Assert.DoesNotContain("store", fixture.Events);
        pending.SetResult();
        await Task.WhenAll(saving, upgrade);
        Assert.Equal("shutdown", fixture.Events.Last());
    }

    [AvaloniaTheory]
    [InlineData("en", false)]
    [InlineData("en", true)]
    [InlineData("fr", false)]
    [InlineData("fr", true)]
    [InlineData("es", false)]
    [InlineData("es", true)]
    [InlineData("sv", false)]
    [InlineData("sv", true)]
    public async Task UpdateControlsWorkAndFitInCompactWindowAndAbout(string language, bool dark)
    {
        using var fixture = new Fixture(language: language);
        await fixture.Model.Initialization;
        fixture.Model.SelectedPath = @"C:\Sample Storage";
        var window = new MainWindow { DataContext = fixture.Model, Width = 1040, Height = 700,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        Window? about = null;
        try
        {
            window.Show();
            await fixture.Model.UpdateChecking;
            Render();
            var upgrade = window.FindControl<Button>("StoreUpgradeButton")!;
            Assert.True(upgrade.IsEffectivelyVisible);
            Assert.True(upgrade.IsEnabled);
            Assert.Equal(fixture.Model["UpgradeAndClose"], upgrade.Content);
            AssertFits(window);
            SaveFrame(window, $"updates-{language}-{(dark ? "dark" : "light")}.png");
            var check = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == fixture.Model.CheckForUpdatesCommand);
            Click(window, check);
            await fixture.Model.CheckForUpdatesCommand.ExecutionTask!;
            Assert.Equal(2, fixture.Store.Checks);

            using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((opened, _) => about = opened);
            await fixture.Model.ShowAboutCommand.ExecuteAsync(null);
            Assert.NotNull(about);
            about.RequestedThemeVariant = window.RequestedThemeVariant;
            Render();
            AssertFits(about);
            Assert.Contains(about.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == fixture.Model.UpdateStatus);
            SaveFrame(about, $"updates-about-{language}-{(dark ? "dark" : "light")}.png");
            fixture.Shutdown.CloseWindows = () => { about.Close(); window.Close(); };
            var aboutUpgrade = about.GetVisualDescendants().OfType<Button>().Single(b => b.Command == fixture.Model.UpgradeCommand);
            Click(about, aboutUpgrade);
            await fixture.Model.UpgradeCommand.ExecutionTask!;
            Assert.Equal("shutdown", fixture.Events.Last());
            Assert.False(window.IsVisible);
            Assert.False(about.IsVisible);
        }
        finally { about?.Close(); window.Close(); }
    }

    private static void Render() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Render();
    }
    private static void AssertFits(Window window)
    {
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
        {
            var point = button.TranslatePoint(default, window)!.Value;
            Assert.True(point.X >= -1 && point.Y >= -1 && point.X + button.Bounds.Width <= window.Bounds.Width + 1
                && point.Y + button.Bounds.Height <= window.Bounds.Height + 1, $"Clipped button: {button.Content}");
        }
    }
    private static void SaveFrame(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("FILESTRIDER_SCREENSHOT_DIR") is not { Length: > 0 } output) return;
        Directory.CreateDirectory(output);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(output, name));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TestDirectory _directory = new();
        public List<string> Events { get; } = [];
        public FakeStore Store { get; }
        public FakeShutdown Shutdown { get; }
        public RecordingConfiguration Configuration { get; }
        public RecordingHistory History { get; }
        public FakeScanner Scanner { get; } = new();
        public MainWindowViewModel Model { get; }
        public Fixture(bool supported = true, string language = "en")
        {
            Store = new FakeStore(Events) { IsSupported = supported };
            Shutdown = new FakeShutdown(Events);
            Configuration = new RecordingConfiguration(new ScanOptions { RootPath = _directory.Path, Language = language }, Events);
            History = new RecordingHistory(Path.Combine(_directory.Path, "history"), Events);
            Model = new MainWindowViewModel(Scanner, new NullPicker(), new ExportService(), new RecordingShell(), Configuration,
                new LocalizationService(), new FileTypeAnalyzer(), historyService: History, storeUpdates: Store, applicationShutdown: Shutdown);
        }
        public void Dispose() { Model.Dispose(); _directory.Dispose(); }
    }
    private sealed class FakeStore(List<string> events) : IStoreUpdateService
    {
        public bool IsSupported { get; init; } = true;
        public int Checks { get; private set; }
        public Func<CancellationToken, Task<bool>> Check { get; set; } = _ => Task.FromResult(true);
        public Func<CancellationToken, Task<bool>> Open { get; set; } = _ => Task.FromResult(true);
        public Task<bool> CheckForUpdatesAsync(CancellationToken token) { Checks++; return Check(token); }
        public Task<bool> OpenStoreAsync(CancellationToken token) { events.Add("store"); return Open(token); }
    }
    private sealed class FakeShutdown(List<string> events) : IApplicationShutdown
    {
        public Action? CloseWindows { get; set; }
        public void Shutdown() { events.Add("shutdown"); CloseWindows?.Invoke(); }
    }
    private sealed class RecordingConfiguration(ScanOptions initial, List<string> events) : IConfigurationService
    {
        public ScanOptions Saved { get; private set; } = initial;
        public bool Fail { get; set; }
        public Task<ScanOptions> LoadDefaultOptionsAsync() => Task.FromResult(Saved);
        public Task SaveDefaultOptionsAsync(ScanOptions options)
        {
            events.Add("settings");
            if (Fail) throw new IOException("disk full");
            Saved = options; return Task.CompletedTask;
        }
    }
    private sealed class RecordingHistory(string path, List<string> events) : IScanHistoryService
    {
        public ScanHistoryService Inner { get; } = new(path);
        public bool Fail { get; set; }
        public Task Pending { get; set; } = Task.CompletedTask;
        public Task<IReadOnlyList<SavedScanInfo>> ListAsync() => Inner.ListAsync();
        public Task<SavedScan> LoadAsync(string id) => Inner.LoadAsync(id);
        public Task DeleteAsync(string id) => Inner.DeleteAsync(id);
        public async Task<SavedScanInfo> SaveAsync(ScanResults results)
        {
            events.Add("snapshot"); await Pending;
            if (Fail) throw new IOException("disk full");
            return await Inner.SaveAsync(results);
        }
    }
    private sealed class FakeScanner : IFileSystemScanner
    {
        public int Scans { get; private set; }
        public Func<ScanOptions, CancellationToken, Task<ScanResults>> Scan { get; set; } = (options, _) => Task.FromResult(new ScanResults
        {
            RootPath = options.RootPath, Options = options, IsCompleted = true,
            Folders = [new FolderItem { FullPath = options.RootPath, RecursiveSize = 123 }],
            Progress = new ScanProgress()
        });
        public Task<ScanResults> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
        { Scans++; return Scan(options, cancellationToken); }
    }
}
