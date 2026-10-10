using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using FileStrider.MauiApp.ViewModels;
using FileStrider.MauiApp.Views;
using FileStrider.MauiApp.Services;
using FileStrider.Core.Contracts;
using FileStrider.Scanner;
using FileStrider.Platform.Services;
using FileStrider.Infrastructure.Export;
using FileStrider.Infrastructure.Configuration;
using FileStrider.Infrastructure.Localization;
using FileStrider.Infrastructure.Analysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FileStrider.MauiApp;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Remove Avalonia data validation to avoid duplicates with CommunityToolkit
            if (BindingPlugins.DataValidators.Count > 0)
                BindingPlugins.DataValidators.RemoveAt(0);

            // Setup dependency injection
            _host = CreateHostBuilder().Build();

            var mainViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            desktop.Exit += (_, _) =>
            {
                mainViewModel.Dispose();
                (_host as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register application services
                services.AddSingleton<FileSystemScanner>();
                services.AddSingleton<IFileSystemScanner>(s => s.GetRequiredService<FileSystemScanner>());
                services.AddSingleton<IFolderContentService>(s => s.GetRequiredService<FileSystemScanner>());
                services.AddSingleton<IScanHistoryService, FileStrider.Infrastructure.History.ScanHistoryService>();
                services.AddSingleton<IFolderPicker, AvaloniaFolderPicker>();
                services.AddSingleton<IExportService, ExportService>();
                services.AddSingleton<IConfigurationService, ConfigurationService>();
                services.AddSingleton<IShellService, ShellService>();
                services.AddSingleton<ILocalizationService, LocalizationService>();
                services.AddSingleton<IFileTypeAnalyzer, FileTypeAnalyzer>();
                services.AddSingleton<IStoreUpdateService, StoreUpdateService>();
                services.AddSingleton<IApplicationShutdown, ApplicationShutdown>();

                // Register view models
                services.AddTransient<MainWindowViewModel>();
            });
}
