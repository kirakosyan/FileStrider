using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileStrider.Core.Models;
using FileStrider.MauiApp.ViewModels;

namespace FileStrider.MauiApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void FileItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is FileItem fileItem)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.OpenFileLocationCommand.ExecuteAsync(fileItem);
            }
        }
    }

    private async void FolderItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is FolderItem folderItem)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.OpenFolderLocationCommand.ExecuteAsync(folderItem);
            }
        }
    }

    private async void StorageMapItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is StorageMapSliceViewModel slice)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.OpenStorageMapItemCommand.ExecuteAsync(slice);
            }
        }
    }
}