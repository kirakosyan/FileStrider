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

    private async void TopFilesList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is FileItem fileItem)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.OpenFileLocationCommand.ExecuteAsync(fileItem);
                e.Handled = true;
            }
        }
    }

    private async void TopFoldersList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is FolderItem folderItem)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.OpenFolderLocationCommand.ExecuteAsync(folderItem);
                e.Handled = true;
            }
        }
    }
}