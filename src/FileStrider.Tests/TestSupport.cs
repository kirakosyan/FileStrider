using FileStrider.Core.Contracts;
using FileStrider.Core.Models;
using FileStrider.Infrastructure.Analysis;
using FileStrider.Infrastructure.Export;
using FileStrider.Infrastructure.Localization;
using FileStrider.MauiApp.ViewModels;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FileStrider.Tests;

internal sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FileStrider-tests-" + Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public string File(string relative, int bytes = 100)
    {
        var file = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        System.IO.File.WriteAllBytes(file, new byte[bytes]);
        return file;
    }
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) { lock (report) report(value); }
}

internal sealed class MemoryConfiguration(ScanOptions options) : IConfigurationService
{
    public ScanOptions Saved { get; private set; } = options;
    public Task<ScanOptions> LoadDefaultOptionsAsync() => Task.FromResult(Saved);
    public Task SaveDefaultOptionsAsync(ScanOptions value) { Saved = value; return Task.CompletedTask; }
}

internal sealed class NullPicker : IFolderPicker { public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null); }
internal sealed class RecordingShell : IShellService
{
    public string? Opened { get; private set; }
    public Task OpenFileLocationAsync(string path) { Opened = path; return Task.CompletedTask; }
    public Task OpenUrlAsync(string url) => Task.CompletedTask;
    public Task CopyToClipboardAsync(string text) => Task.CompletedTask;
}

internal static class TestModels
{
    public static MainWindowViewModel Create(IFileSystemScanner scanner, ScanOptions options, MemoryConfiguration? config = null) =>
        new(scanner, new NullPicker(), new ExportService(), new RecordingShell(), config ?? new MemoryConfiguration(options),
            new LocalizationService(), new FileTypeAnalyzer());
}
