using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CloudRedirect.Resources;
using CloudRedirect.Services;

namespace CloudRedirect.Pages;

public partial class LuaSyncPage : Page
{
    private List<LuaGameItem> _allGames = new();
    private List<LuaGameItem> _filteredGames = new();
    private CancellationTokenSource? _syncCts;
    private bool _allSelectedState;
    private bool _isInitialized;

    public LuaSyncPage()
    {
        InitializeComponent();
        _isInitialized = true;
        Loaded += async (_, _) => await LoadGamesAsync();
        Unloaded += (_, _) => _syncCts?.Cancel();
    }

    private async Task LoadGamesAsync()
    {
        if (LoadingPanel != null) LoadingPanel.Visibility = Visibility.Visible;
        if (GamesList != null) GamesList.Visibility = Visibility.Collapsed;
        if (EmptyStateBorder != null) EmptyStateBorder.Visibility = Visibility.Collapsed;

        try
        {
            _allGames = await Task.Run(() => LuaCloudSyncService.LoadLuaGamesAsync());
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                S.Format("LuaSync_ErrorLoading", ex.Message),
                S.Get("LuaSync_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (LoadingPanel != null) LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFilter()
    {
        if (!_isInitialized || SearchBox == null || GamesList == null || EmptyStateBorder == null) return;

        var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";

        var filtered = _allGames.AsEnumerable();

        // 1. Text filter (Game name or AppID)
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(g =>
                g.GameName.ToLowerInvariant().Contains(query) ||
                g.AppId.ToString().Contains(query));
        }

        // 2. Chip status filter
        if (FilterLocalRadio?.IsChecked == true)
        {
            filtered = filtered.Where(g => g.Status == LuaSyncStatus.LocalOnly);
        }
        else if (FilterCloudRadio?.IsChecked == true)
        {
            filtered = filtered.Where(g => g.Status == LuaSyncStatus.CloudOnly);
        }
        else if (FilterSyncedRadio?.IsChecked == true)
        {
            filtered = filtered.Where(g => g.Status == LuaSyncStatus.Synced);
        }

        _filteredGames = filtered.ToList();
        GamesList.ItemsSource = _filteredGames;

        bool hasItems = _filteredGames.Count > 0;
        GamesList.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateBorder.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        ApplyFilter();
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        ApplyFilter();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _allSelectedState = !_allSelectedState;
        foreach (var item in _filteredGames)
        {
            item.IsSelected = _allSelectedState;
        }

        SelectAllBtn.Content = _allSelectedState
            ? S.Get("LuaSync_DeselectAll")
            : S.Get("LuaSync_SelectAll");
    }

    private async void BackupSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _filteredGames.Where(x => x.IsSelected && x.IsLocal).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(
                S.Get("LuaSync_SelectLocalToBackup"),
                S.Get("LuaSync_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await ExecuteSyncOperationAsync(
            S.Get("LuaSync_BackingUp"),
            async (progress, ct) => await LuaCloudSyncService.BackupLuaFilesAsync(selected, progress, ct),
            count => S.Format("LuaSync_SuccessBackup", count));
    }

    private async void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _filteredGames.Where(x => x.IsSelected && x.IsCloud).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(
                S.Get("LuaSync_SelectCloudToRestore"),
                S.Get("LuaSync_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await ExecuteSyncOperationAsync(
            S.Get("LuaSync_Restoring"),
            async (progress, ct) => await LuaCloudSyncService.RestoreLuaFilesAsync(selected, progress, ct),
            count => S.Format("LuaSync_SuccessRestore", count));
    }

    private async void RestoreAll_Click(object sender, RoutedEventArgs e)
    {
        var allCloud = _allGames.Where(x => x.IsCloud).ToList();
        if (allCloud.Count == 0)
        {
            MessageBox.Show(
                S.Get("LuaSync_NoCloudItems"),
                S.Get("LuaSync_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            S.Format("LuaSync_ConfirmRestoreAll", allCloud.Count),
            S.Get("LuaSync_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        await ExecuteSyncOperationAsync(
            S.Get("LuaSync_Restoring"),
            async (progress, ct) => await LuaCloudSyncService.RestoreLuaFilesAsync(allCloud, progress, ct),
            count => S.Format("LuaSync_SuccessRestore", count));
    }

    private async void DeleteCloud_Click(object sender, RoutedEventArgs e)
    {
        var selected = _filteredGames.Where(x => x.IsSelected && x.IsCloud).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(
                S.Get("LuaSync_SelectCloudToDelete"),
                S.Get("LuaSync_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            S.Format("LuaSync_DeleteCloudConfirm", selected.Count),
            S.Get("LuaSync_DeleteCloudTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        await ExecuteSyncOperationAsync(
            S.Get("LuaSync_Deleting"),
            async (progress, ct) => await LuaCloudSyncService.DeleteCloudLuaFilesAsync(selected, progress, ct),
            count => S.Format("LuaSync_SuccessDelete", count));
    }

    private async Task ExecuteSyncOperationAsync(
        string initialStatus,
        Func<Action<int, int, string>, CancellationToken, Task<int>> operation,
        Func<int, string> successMessage)
    {
        SetBusy(true, initialStatus);
        _syncCts = new CancellationTokenSource();

        try
        {
            int result = await Task.Run(() => operation((current, total, gameName) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ProgressStatusText.Text = $"{initialStatus}: {gameName} ({current}/{total})";
                    SyncProgressBar.Maximum = total;
                    SyncProgressBar.Value = current;
                });
            }, _syncCts.Token));

            SetBusy(false);
            ApplyFilter();

            MessageBox.Show(
                successMessage(result),
                S.Get("LuaSync_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { SetBusy(false); }
        catch (Exception ex)
        {
            SetBusy(false);
            MessageBox.Show(
                ex.Message,
                S.Get("LuaSync_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SetBusy(bool isBusy, string status = "")
    {
        ProgressCard.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        ProgressStatusText.Text = status;
        SyncProgressBar.Value = 0;

        BackupBtn.IsEnabled = !isBusy;
        RestoreSelectedBtn.IsEnabled = !isBusy;
        RestoreAllBtn.IsEnabled = !isBusy;
        DeleteCloudBtn.IsEnabled = !isBusy;
        RefreshBtn.IsEnabled = !isBusy;
        SelectAllBtn.IsEnabled = !isBusy;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadGamesAsync();
    }
}
