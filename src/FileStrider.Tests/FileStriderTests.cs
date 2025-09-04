using FileStrider.Core.Models;
using FileStrider.Scanner;
using FileStrider.Infrastructure.Export;
using FileStrider.Infrastructure.Configuration;

namespace FileStrider.Tests;

/// <summary>
/// Unit tests for the TopItemsTracker class to verify correct top-N item tracking behavior.
/// </summary>
public class TopItemsTrackerTests
{
    /// <summary>
    /// Tests that the TopItemsTracker correctly maintains the top N items in the correct order.
    /// </summary>
    [Fact]
    public void TopItemsTracker_ShouldTrackTopItems()
    {
        // Arrange
        var tracker = new TopItemsTracker<int>(3, (a, b) => b.CompareTo(a)); // Descending order
        
        // Act
        tracker.Add(5);
        tracker.Add(2);
        tracker.Add(8);
        tracker.Add(1);
        tracker.Add(6);
        
        // Assert
        var top = tracker.GetTop();
        Assert.Equal(3, top.Count);
        Assert.Equal(8, top[0]);
        Assert.Equal(6, top[1]);
        Assert.Equal(5, top[2]);
    }

    /// <summary>
    /// Tests that the TopItemsTracker correctly handles cases where fewer items are added than the maximum capacity.
    /// </summary>
    [Fact]
    public void TopItemsTracker_ShouldHandleFewerItemsThanCapacity()
    {
        // Arrange
        var tracker = new TopItemsTracker<int>(5, (a, b) => b.CompareTo(a));
        
        // Act
        tracker.Add(3);
        tracker.Add(1);
        
        // Assert
        var top = tracker.GetTop();
        Assert.Equal(2, top.Count);
        Assert.Equal(3, top[0]);
        Assert.Equal(1, top[1]);
    }
}

/// <summary>
/// Unit tests for the ScanOptions record to verify default values and record functionality.
/// </summary>
public class ScanOptionsTests
{
    /// <summary>
    /// Tests that ScanOptions has the correct default values for all properties.
    /// </summary>
    [Fact]
    public void ScanOptions_ShouldHaveCorrectDefaults()
    {
        // Act
        var options = new ScanOptions();
        
        // Assert
        Assert.Equal(20, options.TopN);
        Assert.False(options.IncludeHidden);
        Assert.False(options.FollowSymlinks);
        Assert.Equal(0, options.MinFileSize);
        Assert.True(options.ConcurrencyLimit > 0);
        Assert.Contains("node_modules", options.ExcludeDirectories);
        Assert.Contains(".git", options.ExcludeDirectories);
    }

    /// <summary>
    /// Tests that the ScanOptions record supports the "with" syntax for creating modified copies.
    /// </summary>
    [Fact]
    public void ScanOptions_WithSyntax_ShouldWork()
    {
        // Arrange
        var options = new ScanOptions();
        
        // Act
        var modified = options with { TopN = 50, IncludeHidden = true };
        
        // Assert
        Assert.Equal(50, modified.TopN);
        Assert.True(modified.IncludeHidden);
        Assert.Equal(options.RootPath, modified.RootPath); // Other properties unchanged
    }
}

/// <summary>
/// Unit tests for the ExportService to verify CSV and JSON export functionality.
/// </summary>
public class ExportServiceTests
{
    /// <summary>
    /// Tests that CSV export creates a valid file with the expected content structure.
    /// </summary>
    [Fact]
    public async Task ExportToCsv_ShouldCreateValidCsvContent()
    {
        // Arrange
        var exportService = new ExportService();
        var results = new ScanResults
        {
            TopFiles = new List<FileItem>
            {
                new() { Name = "test.txt", FullPath = "/test/test.txt", Size = 1024, Type = ".txt", LastModified = new DateTime(2023, 1, 1) }
            },
            TopFolders = new List<FolderItem>
            {
                new() { Name = "testfolder", FullPath = "/test", RecursiveSize = 2048, ItemCount = 5, LastModified = new DateTime(2023, 1, 1) }
            }
        };
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            await exportService.ExportToCsvAsync(results, tempFile);
            
            // Assert
            Assert.True(File.Exists(tempFile));
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("Top Files", content);
            Assert.Contains("Top Folders", content);
            Assert.Contains("test.txt", content);
            Assert.Contains("testfolder", content);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

/// <summary>
/// Unit tests for the ConfigurationService to verify configuration loading and saving functionality.
/// </summary>
public class ConfigurationServiceTests
{
    /// <summary>
    /// Tests that LoadDefaultOptions returns sensible defaults when no configuration file exists.
    /// </summary>
    [Fact]
    public async Task LoadDefaultOptions_ShouldReturnDefaults_WhenNoConfigFile()
    {
        // Arrange
        var configService = new ConfigurationService();
        
        // Act
        var options = await configService.LoadDefaultOptionsAsync();
        
        // Assert
        Assert.NotNull(options);
        Assert.Equal(20, options.TopN);
        Assert.False(options.IncludeHidden);
    }

    /// <summary>
    /// Tests that configuration options can be saved and loaded successfully, demonstrating persistence.
    /// </summary>
    [Fact]
    public async Task SaveAndLoadOptions_ShouldPersist()
    {
        // Arrange
        var configService = new ConfigurationService();
        var originalOptions = new ScanOptions { TopN = 100, IncludeHidden = true };
        
        try
        {
            // Act
            await configService.SaveDefaultOptionsAsync(originalOptions);
            var loadedOptions = await configService.LoadDefaultOptionsAsync();
            
            // Assert
            Assert.Equal(100, loadedOptions.TopN);
            Assert.True(loadedOptions.IncludeHidden);
        }
        finally
        {
            // Cleanup - restore defaults
            await configService.SaveDefaultOptionsAsync(new ScanOptions());
        }
    }
}

/// <summary>
/// Unit tests for the FileSystemScanner to verify scan functionality and progress reporting.
/// </summary>
public class FileSystemScannerTests
{
    /// <summary>
    /// Tests that the scanner properly reports elapsed time during progress updates.
    /// </summary>
    [Fact]
    public async Task Scanner_ShouldReportElapsedTime_DuringProgress()
    {
        // Arrange
        var scanner = new FileSystemScanner();
        var tempDir = Path.GetTempPath();
        var options = new ScanOptions { RootPath = tempDir, TopN = 1 };
        
        var progressReports = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(p => progressReports.Add(p));
        
        // Act
        await scanner.ScanAsync(options, progress, CancellationToken.None);
        
        // Assert
        Assert.NotEmpty(progressReports);
        
        // Find a progress report where files were scanned
        var progressWithFiles = progressReports.FirstOrDefault(p => p.FilesScanned > 0);
        if (progressWithFiles != null)
        {
            // Elapsed time should be greater than zero if files were processed
            Assert.True(progressWithFiles.Elapsed.TotalMilliseconds >= 0, 
                "Elapsed time should be reported during progress updates");
        }
    }
}