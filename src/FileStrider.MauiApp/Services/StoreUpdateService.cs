using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
#if WINDOWS
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.System;
#endif

namespace FileStrider.MauiApp.Services;

public interface IStoreUpdateService
{
    bool IsSupported { get; }
    Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken);
    Task<bool> OpenStoreAsync(CancellationToken cancellationToken);
}

public interface IApplicationShutdown
{
    void Shutdown();
}

public sealed class ApplicationShutdown : IApplicationShutdown
{
    public void Shutdown()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            throw new InvalidOperationException("The desktop application lifetime is unavailable.");

        // Shutdown closes every window (including About), not just the window with the button.
        desktop.Shutdown();
    }
}

public sealed class StoreUpdateService : IStoreUpdateService
{
    public const string ProductId = "9PD3M0MHZ8TC";
    public static readonly Uri ProductUri = new($"ms-windows-store://pdp/?ProductId={ProductId}");

    public bool IsSupported
    {
        get
        {
#if WINDOWS
            try { return Package.Current.SignatureKind == PackageSignatureKind.Store; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; } // Unpackaged builds have no Package.Current.
#else
            return false;
#endif
        }
    }

    public async Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if WINDOWS
        Dispatcher.UIThread.VerifyAccess();
        if (!IsSupported) throw new NotSupportedException("Store installation required.");
        var context = StoreContext.GetDefault();
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var handle = window?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) throw new InvalidOperationException("The main window is not ready.");
        WinRT.Interop.InitializeWithWindow.Initialize(context, handle);
        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken);
        var family = Package.Current.Id.FamilyName;
        // An optional package update alone is not a new version of File Strider.
        return updates.Any(update => update.Package.Id.FamilyName == family);
#else
        await Task.CompletedTask;
        throw new NotSupportedException("Store installation required.");
#endif
    }

    public async Task<bool> OpenStoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if WINDOWS
        Dispatcher.UIThread.VerifyAccess();
        if (!IsSupported) return false;
        // Hand off to the independent Store process before exiting. Do not start an
        // in-process installation and then cancel its WinRT operation by terminating.
        return await Launcher.LaunchUriAsync(ProductUri).AsTask(cancellationToken);
#else
        await Task.CompletedTask;
        return false;
#endif
    }
}
