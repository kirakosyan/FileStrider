using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using FileStrider.MauiApp.ViewModels;
using FileStrider.MauiApp.Views;
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
            // Line below is needed to remove Avalonia data validation.
            // Without this line you will get duplicate validations from both Avalonia and CT
            BindingPlugins.DataValidators.RemoveAt(0);

            // Setup dependency injection
            _host = CreateHostBuilder().Build();
            
            var mainViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register application services
                services.AddSingleton<IFileSystemScanner, FileSystemScanner>();
                services.AddSingleton<IFolderPicker, AvaloniaFolderPicker>();
                services.AddSingleton<IExportService, ExportService>();
                services.AddSingleton<IConfigurationService, ConfigurationService>();
                services.AddSingleton<IShellService, ShellService>();
                services.AddSingleton<ILocalizationService, LocalizationService>();
                services.AddSingleton<IFileTypeAnalyzer, FileTypeAnalyzer>();

                // Register view models
                services.AddTransient<MainWindowViewModel>();
            });
}