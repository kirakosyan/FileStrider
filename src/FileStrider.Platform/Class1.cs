using System.Diagnostics;
using System.Runtime.InteropServices;
using FileStrider.Core.Contracts;

namespace FileStrider.Platform.Services;

public class ShellService : IShellService
{
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

public class ConsoleFolderPicker : IFolderPicker
{
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
