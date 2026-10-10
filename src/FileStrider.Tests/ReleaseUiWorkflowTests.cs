using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using FileStrider.Core.Models;
using FileStrider.Infrastructure.Analysis;
using FileStrider.Infrastructure.Export;
using FileStrider.Infrastructure.History;
using FileStrider.Infrastructure.Localization;
using FileStrider.MauiApp.ViewModels;
using FileStrider.MauiApp.Views;
using FileStrider.Scanner;

namespace FileStrider.Tests;

public class ReleaseUiWorkflowTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("sv")]
    public async Task NewFeaturesWorkThroughBoundControlsAndKeyboard(string language)
    {
        using var files = new TestDirectory(); using var storage = new TestDirectory();
        files.File("project/package.json", 0);
        files.File("project/alpha.log", 30); files.File("project/beta.txt", 20);
        files.File("project/node_modules/dependency.js", 50);
        var options = ScanPresets.Full(new ScanOptions { RootPath = files.Path, Language = language });
        var config = new MemoryConfiguration(options);
        var shell = new RecordingShell();
        using var vm = new MainWindowViewModel(new FileSystemScanner(new FileTypeAnalyzer()), new NullPicker(),
            new ExportService(), shell, config, new LocalizationService(), new FileTypeAnalyzer(),
            historyService: new ScanHistoryService(storage.Path));
        await vm.Initialization; await vm.HistoryLoading;
        var window = new MainWindow { DataContext = vm, Width = 1040, Height = 700 };
        void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        void Click(Control control)
        {
            Layout();
            Assert.True(control.IsEffectivelyVisible);
            Assert.True(control.IsEffectivelyEnabled);
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout();
        }
        async Task Command(ICommand command)
        {
            Click(window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && ReferenceEquals(b.Command, command)));
            if (command is IAsyncRelayCommand asyncCommand && asyncCommand.ExecutionTask is { } execution) await execution;
            Layout();
        }
        void Tab(string key) => Click(window.GetVisualDescendants().OfType<TabItem>().Single(t => Equals(t.Header, vm[key])));
        ListBox List(object source) => window.GetVisualDescendants().OfType<ListBox>().Single(l => ReferenceEquals(l.ItemsSource, source));
        try
        {
            window.Show(); Layout();
            await Command(vm.StartScanCommand);
            Assert.Equal(100, vm.LastResults!.Progress.BytesProcessed);
            Tab("Explorer"); await vm.ExplorerLoading; Layout();
            Assert.Equal(1, vm.ResultsTabIndex);
            var explorer = List(vm.ExplorerItems);
            explorer.SelectedIndex = 0; Layout();
            Assert.True(explorer.ContainerFromIndex(0)!.Focus());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await vm.ExplorerLoading; Layout();
            Assert.Equal(Path.Combine(files.Path, "project"), vm.TreemapPath);
            var filter = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == vm["ExtensionFilter"]);
            Click(filter); window.KeyTextInput("*.log");
            await vm.ExplorerFiltering; Layout();
            Assert.Equal("alpha.log", Assert.Single(vm.ExplorerItems).Name);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
            await vm.ExplorerFiltering; Layout();
            Assert.Equal(4, vm.ExplorerItems.Count);
            Click(window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, files.Path)));
            await vm.ExplorerLoading; Assert.Equal(files.Path, vm.TreemapPath);

            Tab("Insights");
            var insight = Assert.Single(vm.Insights);
            List(vm.Insights).SelectedIndex = 0; Layout();
            await Command(vm.CopyPathCommand);
            Assert.Equal(insight.FullPath, shell.Copied);

            Tab("History"); await Command(vm.SaveSnapshotCommand);
            Assert.Single(vm.SavedScans);
            files.File("project/growth.txt", 200);
            await Command(vm.StartScanCommand);
            await Command(vm.CompareSnapshotCommand);
            Assert.Equal(200, vm.FolderChanges.Single(c => c.FullPath == files.Path).Delta);
            await Command(vm.LoadSnapshotCommand);
            Assert.Equal(100, vm.LastResults!.Progress.BytesProcessed);
            await Command(vm.DeleteSnapshotCommand);
            Assert.Empty(vm.SavedScans);

            await Command(vm.ApplySourcePresetCommand);
            await Command(vm.StartScanCommand);
            Tab("Coverage");
            var coverage = List(vm.CoverageItems);
            coverage.SelectedItem = vm.CoverageItems.Single(c => c.FullPath.EndsWith("node_modules")); Layout();
            var savedOptions = config.Saved;
            await Command(vm.RetryCoverageCommand);
            Assert.Equal(50, vm.LastResults!.Progress.BytesProcessed);
            Assert.Equal(0, vm.ResultsTabIndex);
            Assert.Equal(files.Path, vm.SelectedPath);
            Assert.Same(savedOptions, config.Saved);
        }
        finally { window.Close(); }
    }
}
