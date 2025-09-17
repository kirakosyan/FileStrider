using System.Globalization;
using Avalonia.Media;

namespace FileStrider.MauiApp.ViewModels;

public class StorageMapSliceViewModel
{
    public StorageMapSliceViewModel(string name, string fullPath, long size, double share, string displaySize, IBrush brush)
    {
        Name = name;
        FullPath = fullPath;
        Size = size;
        Share = share;
        DisplaySize = displaySize;
        Brush = brush;
    }

    public string Name { get; }

    public string FullPath { get; }

    public long Size { get; }

    public double Share { get; }

    public string DisplaySize { get; }

    public IBrush Brush { get; }

    public string ShareDisplay => Share.ToString("P1", CultureInfo.CurrentCulture);

    public string Tooltip => $"{FullPath}\n{DisplaySize} • {ShareDisplay}";
}
