using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileStrider.MauiApp.Services;

namespace FileStrider.MauiApp.ViewModels;

public partial class MainWindowViewModel
{
    private readonly IStoreUpdateService? _storeUpdates;
    private readonly IApplicationShutdown? _applicationShutdown;
    private readonly CancellationTokenSource _updateLifetime = new();
    private bool _disposed;
    private bool _startupUpdateCheckStarted;
    private int _updateCheckActive;
    private string _updateStatusKey = "UpdateNotChecked";
    public Task UpdateChecking { get; private set; } = Task.CompletedTask;
    public bool SupportsStoreUpdates { get; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UpgradeCommand))]
    private bool hasStoreUpdate;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(UpgradeCommand))]
    private bool isCheckingForUpdates;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(UpgradeCommand))]
    private bool isPreparingUpgrade;
    [ObservableProperty] private bool hasUpdateNotice;

    public string UpdateStatus => this[SupportsStoreUpdates ? _updateStatusKey : "UpdateUnsupported"];
    public string UpgradeHint => this[IsScanning ? "UpdateScanInProgress" : "UpdateCloseHint"];
    public bool CanCheckForUpdates => SupportsStoreUpdates && !IsCheckingForUpdates && !IsPreparingUpgrade && !_disposed;
    public bool CanUpgrade => SupportsStoreUpdates && HasStoreUpdate && CanScan && !IsScanning
        && !IsCheckingForUpdates && !IsPreparingUpgrade && !_disposed;

    // Called after the main window opens, so StoreContext has a valid owner HWND.
    public Task CheckForUpdatesOnStartupAsync()
    {
        if (_startupUpdateCheckStarted || !SupportsStoreUpdates || _disposed) return UpdateChecking;
        _startupUpdateCheckStarted = true;
        return UpdateChecking = CheckStoreAsync(false);
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private Task CheckForUpdates() => UpdateChecking = CheckStoreAsync(true);

    private async Task CheckStoreAsync(bool userRequested)
    {
        if (!CanCheckForUpdates || Interlocked.CompareExchange(ref _updateCheckActive, 1, 0) != 0) return;
        IsCheckingForUpdates = true;
        if (userRequested) HasUpdateNotice = true;
        SetUpdateStatus("UpdateChecking");
        try
        {
            await Initialization;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var available = await _storeUpdates!.CheckForUpdatesAsync(timeout.Token).WaitAsync(timeout.Token);
            _updateLifetime.Token.ThrowIfCancellationRequested();
            HasStoreUpdate = available;
            SetUpdateStatus(HasStoreUpdate ? "UpdateAvailable" : "UpdateCurrent");
            HasUpdateNotice = userRequested || HasStoreUpdate;
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Store update check failed: {ex.Message}");
            SetUpdateStatus("UpdateCheckFailed");
            HasUpdateNotice = userRequested || HasStoreUpdate;
        }
        finally
        {
            IsCheckingForUpdates = false;
            Interlocked.Exchange(ref _updateCheckActive, 0);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUpgrade))]
    private async Task Upgrade()
    {
        if (!CanUpgrade || Interlocked.CompareExchange(ref _scanActive, 1, 0) != 0) return;
        IsPreparingUpgrade = true;
        CanScan = false;
        HasUpdateNotice = true;
        SetUpdateStatus("UpdatePreparing");
        var failureKey = "UpdateSaveFailed";
        try
        {
            // Let already-started file writes finish before shutting down. The content
            // area is disabled during this handoff, and the scan gate prevents new scans.
            await Task.WhenAll(new[] { SaveSettingsCommand.ExecutionTask, SaveSnapshotCommand.ExecutionTask,
                DeleteSnapshotCommand.ExecutionTask, ExportToCsvCommand.ExecutionTask, ExportToJsonCommand.ExecutionTask }
                .OfType<Task>());
            _updateLifetime.Token.ThrowIfCancellationRequested();
            var options = BuildScanOptions();
            await _configurationService.SaveDefaultOptionsAsync(options);
            _loadedOptions = options;
            if (_historyService is not null && LastResults is { Options: not null, Folders.Count: > 0 })
                await _historyService.SaveAsync(LastResults);

            _updateLifetime.Token.ThrowIfCancellationRequested();
            failureKey = "UpdateOpenFailed";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            if (!await _storeUpdates!.OpenStoreAsync(timeout.Token).WaitAsync(timeout.Token))
            {
                SetUpdateStatus(failureKey);
                return;
            }
            _updateLifetime.Token.ThrowIfCancellationRequested();
            failureKey = "UpdateCloseFailed";
            _applicationShutdown!.Shutdown();
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Store update handoff failed: {ex.Message}");
            SetUpdateStatus(failureKey);
        }
        finally
        {
            Interlocked.Exchange(ref _scanActive, 0);
            IsPreparingUpgrade = false;
            CanScan = !_disposed;
        }
    }

    private void SetUpdateStatus(string key)
    {
        _updateStatusKey = key;
        OnPropertyChanged(nameof(UpdateStatus));
    }

    private void RefreshUpdateLabels()
    {
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(UpgradeHint));
    }
}
