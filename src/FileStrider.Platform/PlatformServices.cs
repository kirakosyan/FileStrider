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
    public Task OpenFileLocationAsync(string filePath)
    {
        filePath = Path.GetFullPath(filePath);
        if (!File.Exists(filePath) && !Directory.Exists(filePath))
            throw new FileNotFoundException("The selected item no longer exists.", filePath);
        var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
        if (OperatingSystem.IsWindows())
        {
            start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            start.Arguments = $"/select,\"{filePath}\"";
        }
        else if (OperatingSystem.IsMacOS())
        {
            start.FileName = "/usr/bin/open";
            start.ArgumentList.Add("-R");
            start.ArgumentList.Add(filePath);
        }
        else
        {
            start.FileName = "xdg-open";
            start.ArgumentList.Add(Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath)!);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the file manager.");
        return Task.CompletedTask;
    }

    public Task OpenUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ArgumentException("A valid web address is required.", nameof(url));
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })
            ?? throw new InvalidOperationException("Could not open the web browser.");
        return Task.CompletedTask;
    }

    public async Task CopyToClipboardAsync(string text)
    {
        var window = (Avalonia.Application.Current?.ApplicationLifetime as
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var clipboard = window?.Clipboard ?? throw new InvalidOperationException("The clipboard is unavailable.");
        await clipboard.SetTextAsync(text);
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
                    throw new InvalidOperationException("The selected folder does not have a usable local path.");
                }

                return null;
            }

            // Fallback to current directory if no dialog available
            throw new InvalidOperationException("The folder picker is unavailable.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not select a folder.", ex);
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
