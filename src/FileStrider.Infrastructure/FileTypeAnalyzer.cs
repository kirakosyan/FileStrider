using FileStrider.Core.Contracts;
using FileStrider.Core.Models;

namespace FileStrider.Infrastructure.Analysis;

/// <summary>
/// Service for analyzing file types and generating statistics about disk usage by file category.
/// </summary>
public class FileTypeAnalyzer : IFileTypeAnalyzer
{
    private static readonly Dictionary<string, string> FileCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        [".jpg"] = "Images", [".jpeg"] = "Images", [".png"] = "Images", [".gif"] = "Images",
        [".bmp"] = "Images", [".tiff"] = "Images", [".tif"] = "Images", [".webp"] = "Images",
        [".svg"] = "Images", [".ico"] = "Images", [".raw"] = "Images", [".cr2"] = "Images",
        [".nef"] = "Images", [".orf"] = "Images", [".sr2"] = "Images", [".heic"] = "Images",
        
        // Videos
        [".mp4"] = "Videos", [".avi"] = "Videos", [".mkv"] = "Videos", [".mov"] = "Videos",
        [".wmv"] = "Videos", [".flv"] = "Videos", [".webm"] = "Videos", [".m4v"] = "Videos",
        [".3gp"] = "Videos", [".mts"] = "Videos", [".ts"] = "Videos", [".vob"] = "Videos",
        
        // Audio
        [".mp3"] = "Audio", [".wav"] = "Audio", [".flac"] = "Audio", [".aac"] = "Audio",
        [".ogg"] = "Audio", [".wma"] = "Audio", [".m4a"] = "Audio", [".opus"] = "Audio",
        [".ape"] = "Audio", [".alac"] = "Audio",
        
        // Documents
        [".pdf"] = "Documents", [".doc"] = "Documents", [".docx"] = "Documents", [".xls"] = "Documents",
        [".xlsx"] = "Documents", [".ppt"] = "Documents", [".pptx"] = "Documents", [".txt"] = "Documents",
        [".rtf"] = "Documents", [".odt"] = "Documents", [".ods"] = "Documents", [".odp"] = "Documents",
        
        // Archives
        [".zip"] = "Archives", [".rar"] = "Archives", [".7z"] = "Archives", [".tar"] = "Archives",
        [".gz"] = "Archives", [".bz2"] = "Archives", [".xz"] = "Archives", [".tar.gz"] = "Archives",
        [".tar.bz2"] = "Archives", [".tar.xz"] = "Archives",
        
        // Code
        [".cs"] = "Code", [".js"] = "Code", [".ts"] = "Code", [".py"] = "Code", [".java"] = "Code",
        [".cpp"] = "Code", [".c"] = "Code", [".h"] = "Code", [".hpp"] = "Code", [".php"] = "Code",
        [".rb"] = "Code", [".go"] = "Code", [".rs"] = "Code", [".swift"] = "Code", [".kt"] = "Code",
        [".html"] = "Code", [".css"] = "Code", [".scss"] = "Code", [".sass"] = "Code", [".less"] = "Code",
        [".xml"] = "Code", [".json"] = "Code", [".yaml"] = "Code", [".yml"] = "Code",
        
        // Executables
        [".exe"] = "Executables", [".msi"] = "Executables", [".deb"] = "Executables", [".rpm"] = "Executables",
        [".dmg"] = "Executables", [".pkg"] = "Executables", [".app"] = "Executables", [".appx"] = "Executables",
        [".dll"] = "Executables", [".so"] = "Executables", [".dylib"] = "Executables"
    };

    /// <summary>
    /// Analyzes file types from the provided files and generates statistics.
    /// </summary>
    /// <param name="files">The list of files to analyze.</param>
    /// <returns>A list of file type statistics sorted by total size descending.</returns>
    public IReadOnlyList<FileTypeStats> AnalyzeFileTypes(IEnumerable<FileItem> files)
    {
        var fileList = files.ToList();
        if (fileList.Count == 0)
            return Array.Empty<FileTypeStats>();

        var totalSize = fileList.Sum(f => f.Size);

        // Group files by extension and calculate statistics
        var extensionGroups = fileList
            .GroupBy(f => f.Type.ToLowerInvariant())
            .Select(g => new
            {
                Extension = g.Key,
                Category = GetFileCategory(g.Key),
                Files = g.ToList()
            })
            .ToList();

        // Group by category and create statistics
        var categoryStats = extensionGroups
            .GroupBy(g => g.Category)
            .Select(categoryGroup =>
            {
                var categoryFiles = categoryGroup.SelectMany(g => g.Files).ToList();
                var categorySize = categoryFiles.Sum(f => f.Size);
                
                return new FileTypeStats
                {
                    Extension = "", // Category level doesn't have a specific extension
                    Category = categoryGroup.Key,
                    FileCount = categoryFiles.Count,
                    TotalSize = categorySize,
                    Percentage = totalSize > 0 ? (double)categorySize / totalSize * 100 : 0
                };
            })
            .OrderByDescending(s => s.TotalSize)
            .ToList();

        // Return only category-level statistics to avoid duplication in the UI
        return categoryStats.AsReadOnly();
    }

    /// <summary>
    /// Gets the category name for a given file extension.
    /// </summary>
    /// <param name="extension">The file extension (e.g., ".jpg").</param>
    /// <returns>The category name (e.g., "Images").</returns>
    public string GetFileCategory(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return "Other";

        // Normalize extension (ensure it starts with a dot and is lowercase)
        var normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
        
        return FileCategories.TryGetValue(normalizedExtension, out var category) ? category : "Other";
    }
}