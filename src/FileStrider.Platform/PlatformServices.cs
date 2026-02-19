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
            await Task.Run(() =>
            {
                var directoryPath = Path.GetDirectoryName(filePath) ?? filePath;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (File.Exists(filePath))
                    {
                        using var process = Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                    }
                    else
                    {
                        using var process = Process.Start("explorer.exe", $"\"{directoryPath}\"");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    if (File.Exists(filePath))
                    {
                        using var process = Process.Start("open", $"-R \"{filePath}\"");
                    }
                    else
                    {
                        using var process = Process.Start("open", $"\"{directoryPath}\"");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    using var process = Process.Start("xdg-open", $"\"{directoryPath}\"");
                }
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to open file location: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Opens the specified URL in the platform's default web browser.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the operation fails.</exception>
    public async Task OpenUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));
        }

        try
        {
            await Task.Run(() =>
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    using var process = Process.Start("open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    using var process = Process.Start("xdg-open", url);
                }
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to open URL: {ex.Message}", ex);
        }
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
                using var process = new Process
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
                using var process = new Process
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
                using var process = new Process
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

                var folder = folders.FirstOrDefault();
                var resolvedPath = ResolveFolderPath(folder);

                if (!string.IsNullOrWhiteSpace(resolvedPath))
                {
                    return resolvedPath;
                }

                // Fall back to current directory if dialog produced an entry without a usable path (happens when selecting drives)
                if (folder is not null)
                {
                    return Directory.GetCurrentDirectory();
                }

                return null;
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

    private static string? ResolveFolderPath(IStorageFolder? folder)
    {
        if (folder is null)
        {
            return null;
        }

        var localPath = folder.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            return NormalizeDrivePath(localPath!);
        }

        var uriPath = folder.Path?.LocalPath;
        if (!string.IsNullOrWhiteSpace(uriPath))
        {
            return NormalizeDrivePath(uriPath!);
        }

        if (OperatingSystem.IsWindows())
        {
            var inferred = TryInferDriveFromName(folder.Name);
            if (!string.IsNullOrWhiteSpace(inferred))
            {
                return inferred;
            }
        }

        return null;
    }

    private static string NormalizeDrivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var trimmed = path.Trim();

        if (OperatingSystem.IsWindows())
        {
            trimmed = trimmed.Replace('/', Path.DirectorySeparatorChar);

            if (IsDriveRoot(trimmed))
            {
                return FormatDriveRoot(trimmed[0]);
            }
        }

        return trimmed;
    }

    private static string? TryInferDriveFromName(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        var start = folderName.LastIndexOf('(');
        var end = folderName.LastIndexOf(')');

        if (start >= 0 && end > start + 1)
        {
            var inner = folderName.Substring(start + 1, end - start - 1);
            if (IsDriveRoot(inner))
            {
                return FormatDriveRoot(inner[0]);
            }
        }

        return null;
    }

    private static bool IsDriveRoot(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim();
        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            return true;
        }

        return trimmed.Length == 3
            && char.IsLetter(trimmed[0])
            && trimmed[1] == ':'
            && (trimmed[2] == Path.DirectorySeparatorChar || trimmed[2] == Path.AltDirectorySeparatorChar || trimmed[2] == '/');
    }

    private static string FormatDriveRoot(char driveLetter)
    {
        return string.Create(3, driveLetter, static (span, letter) =>
        {
            span[0] = char.ToUpperInvariant(letter);
            span[1] = ':';
            span[2] = Path.DirectorySeparatorChar;
        });
    }
}
