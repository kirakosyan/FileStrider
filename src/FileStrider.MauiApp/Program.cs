using FileStrider.Core.Contracts;
using FileStrider.Core.Models;
using FileStrider.Scanner;
using FileStrider.Platform.Services;
using FileStrider.Infrastructure.Export;
using FileStrider.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FileStrider.MauiApp;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🗂️  FileStrider - Big File and Folder Discovery Tool");
        Console.WriteLine("================================================");
        Console.WriteLine();

        // Setup dependency injection
        var host = CreateHostBuilder(args).Build();
        var scanner = host.Services.GetRequiredService<IFileSystemScanner>();
        var folderPicker = host.Services.GetRequiredService<IFolderPicker>();
        var exportService = host.Services.GetRequiredService<IExportService>();
        var configService = host.Services.GetRequiredService<IConfigurationService>();
        var shellService = host.Services.GetRequiredService<IShellService>();

        try
        {
            await RunApplicationAsync(scanner, folderPicker, exportService, configService, shellService);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private static async Task RunApplicationAsync(
        IFileSystemScanner scanner, 
        IFolderPicker folderPicker, 
        IExportService exportService,
        IConfigurationService configService,
        IShellService shellService)
    {
        // Load configuration
        var options = await configService.LoadDefaultOptionsAsync();
        
        while (true)
        {
            Console.WriteLine("Main Menu:");
            Console.WriteLine("1. Quick Scan (current directory)");
            Console.WriteLine("2. Custom Scan");
            Console.WriteLine("3. Configure Settings");
            Console.WriteLine("4. Exit");
            Console.Write("\nSelect option (1-4): ");

            var choice = Console.ReadLine();
            Console.WriteLine();

            switch (choice)
            {
                case "1":
                    await PerformQuickScanAsync(scanner, exportService, shellService, options);
                    break;
                case "2":
                    await PerformCustomScanAsync(scanner, folderPicker, exportService, shellService, options);
                    break;
                case "3":
                    options = await ConfigureSettingsAsync(configService, options);
                    break;
                case "4":
                    return;
                default:
                    Console.WriteLine("Invalid option. Please select 1-4.");
                    break;
            }
            
            Console.WriteLine("\n" + new string('=', 50));
        }
    }

    private static async Task PerformQuickScanAsync(
        IFileSystemScanner scanner, 
        IExportService exportService,
        IShellService shellService,
        ScanOptions baseOptions)
    {
        Console.WriteLine("🔍 Quick Scan - Current Directory");
        
        var options = baseOptions with { RootPath = Directory.GetCurrentDirectory() };
        await PerformScanAsync(scanner, exportService, shellService, options);
    }

    private static async Task PerformCustomScanAsync(
        IFileSystemScanner scanner, 
        IFolderPicker folderPicker,
        IExportService exportService,
        IShellService shellService,
        ScanOptions baseOptions)
    {
        Console.WriteLine("🎯 Custom Scan");
        
        var rootPath = await folderPicker.PickFolderAsync();
        if (string.IsNullOrEmpty(rootPath))
        {
            Console.WriteLine("❌ No valid directory selected.");
            return;
        }

        Console.Write($"Number of top items to find (current: {baseOptions.TopN}): ");
        var topNInput = Console.ReadLine();
        var topN = int.TryParse(topNInput, out var n) && n > 0 && n <= 200 ? n : baseOptions.TopN;

        Console.Write($"Include hidden files? (current: {baseOptions.IncludeHidden}) [y/N]: ");
        var includeHidden = Console.ReadLine()?.ToLower() == "y";

        Console.Write($"Minimum file size in bytes (current: {baseOptions.MinFileSize}): ");
        var minSizeInput = Console.ReadLine();
        var minSize = long.TryParse(minSizeInput, out var size) && size >= 0 ? size : baseOptions.MinFileSize;

        var options = baseOptions with 
        { 
            RootPath = rootPath, 
            TopN = topN, 
            IncludeHidden = includeHidden,
            MinFileSize = minSize
        };

        await PerformScanAsync(scanner, exportService, shellService, options);
    }

    private static async Task PerformScanAsync(
        IFileSystemScanner scanner,
        IExportService exportService,
        IShellService shellService,
        ScanOptions options)
    {
        Console.WriteLine($"📁 Scanning: {options.RootPath}");
        Console.WriteLine($"🔢 Finding top {options.TopN} files and folders");
        Console.WriteLine("⏳ Press 'c' + Enter to cancel during scan\n");

        var progress = new Progress<ScanProgress>(p =>
        {
            Console.Write($"\r📊 Files: {p.FilesScanned:N0} | Folders: {p.FoldersScanned:N0} | " +
                         $"Size: {FormatBytes(p.BytesProcessed)} | Time: {p.Elapsed:mm\\:ss} | " +
                         $"Path: {TruncatePath(p.CurrentPath, 40)}");
        });

        using var cts = new CancellationTokenSource();
        
        // Start scan task
        var scanTask = scanner.ScanAsync(options, progress, cts.Token);
        
        // Monitor for cancellation
        var cancelTask = Task.Run(() =>
        {
            while (!scanTask.IsCompleted)
            {
                var key = Console.ReadKey(true);
                if (key.KeyChar == 'c' || key.KeyChar == 'C')
                {
                    Console.WriteLine("\n🛑 Cancelling scan...");
                    cts.Cancel();
                    break;
                }
            }
        });

        var results = await scanTask;
        
        Console.WriteLine("\n");

        if (results.WasCancelled)
        {
            Console.WriteLine("⚠️  Scan was cancelled by user.");
        }
        else if (!string.IsNullOrEmpty(results.ErrorMessage))
        {
            Console.WriteLine($"❌ Scan failed: {results.ErrorMessage}");
        }
        else
        {
            Console.WriteLine("✅ Scan completed successfully!");
        }

        await DisplayResultsAsync(results, exportService, shellService);
    }

    private static async Task DisplayResultsAsync(
        ScanResults results,
        IExportService exportService,
        IShellService shellService)
    {
        Console.WriteLine($"\n📈 Scan Summary:");
        Console.WriteLine($"   Files scanned: {results.Progress.FilesScanned:N0}");
        Console.WriteLine($"   Folders scanned: {results.Progress.FoldersScanned:N0}");
        Console.WriteLine($"   Total size processed: {FormatBytes(results.Progress.BytesProcessed)}");
        Console.WriteLine($"   Time elapsed: {results.Progress.Elapsed:mm\\:ss}");

        if (results.TopFiles.Any())
        {
            Console.WriteLine($"\n🔥 Top {results.TopFiles.Count} Largest Files:");
            Console.WriteLine("   Rank | Size      | Name");
            Console.WriteLine("   -----|-----------|----");
            
            for (int i = 0; i < results.TopFiles.Count; i++)
            {
                var file = results.TopFiles[i];
                Console.WriteLine($"   {i + 1,3}  | {FormatBytes(file.Size),8} | {TruncatePath(file.Name, 60)}");
            }
        }

        if (results.TopFolders.Any())
        {
            Console.WriteLine($"\n📁 Top {results.TopFolders.Count} Largest Folders:");
            Console.WriteLine("   Rank | Size      | Name");
            Console.WriteLine("   -----|-----------|----");
            
            for (int i = 0; i < results.TopFolders.Count; i++)
            {
                var folder = results.TopFolders[i];
                Console.WriteLine($"   {i + 1,3}  | {FormatBytes(folder.RecursiveSize),8} | {TruncatePath(folder.Name, 60)}");
            }
        }

        Console.WriteLine("\nOptions:");
        Console.WriteLine("1. Export results to CSV");
        Console.WriteLine("2. Export results to JSON");
        Console.WriteLine("3. Open file location (enter file number)");
        Console.WriteLine("4. Copy file path (enter file number)");
        Console.WriteLine("5. Back to main menu");
        Console.Write("\nSelect option: ");

        var choice = Console.ReadLine();
        
        switch (choice)
        {
            case "1":
                await ExportResultsAsync(results, exportService, "csv");
                break;
            case "2":
                await ExportResultsAsync(results, exportService, "json");
                break;
            case "3":
                await OpenFileLocationAsync(results, shellService);
                break;
            case "4":
                await CopyFilePathAsync(results, shellService);
                break;
        }
    }

    private static async Task ExportResultsAsync(ScanResults results, IExportService exportService, string format)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"FileStrider_Results_{timestamp}.{format}";
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);

        try
        {
            if (format == "csv")
                await exportService.ExportToCsvAsync(results, filePath);
            else
                await exportService.ExportToJsonAsync(results, filePath);

            Console.WriteLine($"✅ Results exported to: {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Export failed: {ex.Message}");
        }
    }

    private static async Task OpenFileLocationAsync(ScanResults results, IShellService shellService)
    {
        if (!results.TopFiles.Any())
        {
            Console.WriteLine("No files to open.");
            return;
        }

        Console.Write($"Enter file number (1-{results.TopFiles.Count}): ");
        if (int.TryParse(Console.ReadLine(), out var index) && index > 0 && index <= results.TopFiles.Count)
        {
            try
            {
                var file = results.TopFiles[index - 1];
                await shellService.OpenFileLocationAsync(file.FullPath);
                Console.WriteLine($"📂 Opening location for: {file.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to open location: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Invalid file number.");
        }
    }

    private static async Task CopyFilePathAsync(ScanResults results, IShellService shellService)
    {
        if (!results.TopFiles.Any())
        {
            Console.WriteLine("No files to copy.");
            return;
        }

        Console.Write($"Enter file number (1-{results.TopFiles.Count}): ");
        if (int.TryParse(Console.ReadLine(), out var index) && index > 0 && index <= results.TopFiles.Count)
        {
            try
            {
                var file = results.TopFiles[index - 1];
                await shellService.CopyToClipboardAsync(file.FullPath);
                Console.WriteLine($"📋 Copied to clipboard: {file.FullPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to copy to clipboard: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Invalid file number.");
        }
    }

    private static async Task<ScanOptions> ConfigureSettingsAsync(IConfigurationService configService, ScanOptions currentOptions)
    {
        Console.WriteLine("⚙️  Settings Configuration");
        Console.WriteLine($"Current settings:");
        Console.WriteLine($"  Top N items: {currentOptions.TopN}");
        Console.WriteLine($"  Include hidden: {currentOptions.IncludeHidden}");
        Console.WriteLine($"  Follow symlinks: {currentOptions.FollowSymlinks}");
        Console.WriteLine($"  Min file size: {FormatBytes(currentOptions.MinFileSize)}");
        Console.WriteLine($"  Concurrency: {currentOptions.ConcurrencyLimit}");
        Console.WriteLine($"  Max depth: {currentOptions.MaxDepth?.ToString() ?? "Unlimited"}");

        Console.Write("\nDo you want to modify settings? [y/N]: ");
        if (Console.ReadLine()?.ToLower() != "y")
            return currentOptions;

        Console.Write($"Top N items (1-200, current: {currentOptions.TopN}): ");
        var topNInput = Console.ReadLine();
        var topN = int.TryParse(topNInput, out var n) && n > 0 && n <= 200 ? n : currentOptions.TopN;

        Console.Write($"Concurrency limit (1-16, current: {currentOptions.ConcurrencyLimit}): ");
        var concurrencyInput = Console.ReadLine();
        var concurrency = int.TryParse(concurrencyInput, out var c) && c > 0 && c <= 16 ? c : currentOptions.ConcurrencyLimit;

        var newOptions = currentOptions with { TopN = topN, ConcurrencyLimit = concurrency };
        
        await configService.SaveDefaultOptionsAsync(newOptions);
        Console.WriteLine("✅ Settings saved.");
        
        return newOptions;
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IFileSystemScanner, FileSystemScanner>();
                services.AddSingleton<IFolderPicker, ConsoleFolderPicker>();
                services.AddSingleton<IExportService, ExportService>();
                services.AddSingleton<IConfigurationService, ConfigurationService>();
                services.AddSingleton<IShellService, ShellService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
            });

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i;
        double dblSByte = bytes;
        
        for (i = 0; i < suffixes.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return $"{dblSByte:0.##} {suffixes[i]}";
    }

    private static string TruncatePath(string path, int maxLength)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
            return path;
        
        return "..." + path.Substring(path.Length - maxLength + 3);
    }
}
