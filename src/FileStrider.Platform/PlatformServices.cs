using System.Diagnostics;
using System.Runtime.InteropServices;
using FileStrider.Core.Contracts;
using Avalonia.Platform.Storage;

namespace FileStrider.Platform.Services;

/// <summary>
/// Cross-platform shell service that provides integration with the operating system's file manager and clipboard.
/// </summary>
public class ShellService : IShellService
{
    /// <summary>
    /// Opens the file or folder location in the platform's default file manager (Explorer, Finder, etc.).
    /// On Windows, uses Explorer with /select to highlight the specific file.
    /// </summary>
    /// <param name="filePath">The path of the file or folder to open in the file manager.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the operation fails.</exception>
    public async Task OpenFileLocationAsync(string filePath)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(filePath) ?? filePath;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use explorer to show the file
                if (File.Exists(filePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                }
                else
                {
                    Process.Start("explorer.exe", $"\"{directoryPath}\"");
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Use Finder to show the file
                if (File.Exists(filePath))
                {
                    Process.Start("open", $"-R \"{filePath}\"");
                }
                else
                {
                    Process.Start("open", $"\"{directoryPath}\"");
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Use default file manager
                Process.Start("xdg-open", $"\"{directoryPath}\"");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to open file location: {ex.Message}", ex);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Copies the specified text to the system clipboard using platform-specific commands.
    /// Uses clip.exe on Windows, pbcopy on macOS, and xclip on Linux.
    /// </summary>
    /// <param name="text">The text to copy to the clipboard.</param>
    /// <returns>A task that represents the asynchronous clipboard operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the clipboard operation fails.</exception>
    public async Task CopyToClipboardAsync(string text)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "clip",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                await process.StandardInput.WriteAsync(text);
                process.StandardInput.Close();
                await process.WaitForExitAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "pbcopy",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                await process.StandardInput.WriteAsync(text);
                process.StandardInput.Close();
                await process.WaitForExitAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "xclip",
                        Arguments = "-selection clipboard",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                await process.StandardInput.WriteAsync(text);
                process.StandardInput.Close();
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to copy to clipboard: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Console-based folder picker implementation that prompts the user via command line input.
/// </summary>
public class ConsoleFolderPicker : IFolderPicker
{
    /// <summary>
    /// Prompts the user via console input to select a folder for scanning.
    /// Returns the current directory if no input is provided.
    /// </summary>
    /// <returns>A task that represents the asynchronous folder selection operation, returning the selected folder path or null if invalid.</returns>
    public Task<string?> PickFolderAsync()
    {
        Console.Write("Enter folder path to scan (or press Enter for current directory): ");
        var input = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(input))
        {
            return Task.FromResult<string?>(Directory.GetCurrentDirectory());
        }
        
        if (Directory.Exists(input))
        {
            return Task.FromResult<string?>(Path.GetFullPath(input));
        }
        
        Console.WriteLine($"Directory '{input}' does not exist.");
        return Task.FromResult<string?>(null);
    }
}

/// <summary>
/// Avalonia-based folder picker implementation using the platform's native folder picker dialog.
/// </summary>
public class AvaloniaFolderPicker : IFolderPicker
{
    /// <summary>
    /// Opens the platform's native folder picker dialog to select a folder for scanning.
    /// </summary>
    /// <returns>A task that represents the asynchronous folder selection operation, returning the selected folder path or null if cancelled.</returns>
    public async Task<string?> PickFolderAsync()
    {
        try
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel?.StorageProvider is { } storageProvider)
            {
                var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Folder to Scan",
                    AllowMultiple = false
                });

                return folders.FirstOrDefault()?.Path.LocalPath;
            }

            // Fallback to current directory if no dialog available
            return Directory.GetCurrentDirectory();
        }
        catch (Exception)
        {
            // Fallback to current directory on error
            return Directory.GetCurrentDirectory();
        }
    }
}
