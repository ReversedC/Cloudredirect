using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CloudRedirect.Resources;
using CloudRedirect.Services;
using Wpf.Ui.Controls;

namespace CloudRedirect.Dialogs;

public partial class CloudFolderBrowserDialog : FluentWindow
{
    private readonly string _gameName;
    private readonly string? _localSavePath;
    private readonly uint _appId;
    private readonly string? _accountId;
    private string? _webLink;

    public CloudFolderBrowserDialog(string gameName, string? localSavePath = null, uint appId = 0, string? accountId = null)
    {
        InitializeComponent();
        _gameName = gameName;
        _localSavePath = localSavePath;
        _appId = appId;
        _accountId = accountId;

        var sub = S.Get("CloudExplorer_Subtitle");
        if (string.IsNullOrWhiteSpace(sub) || sub == "CloudExplorer_Subtitle") sub = "Cloud Save Storage";
        GameTitleText.Text = $"{gameName} — {sub}";

        var titleRes = S.Get("CloudExplorer_Title");
        if (string.IsNullOrWhiteSpace(titleRes) || titleRes == "CloudExplorer_Title") titleRes = "Cloud Storage Explorer";
        Title = $"{gameName} — {titleRes}";
        AppTitleBar.Title = Title;

        Loaded += async (_, _) => await LoadFolderAsync();
    }

    private async Task LoadFolderAsync()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        EmptyStateBorder.Visibility = Visibility.Collapsed;
        FilesListBox.Visibility = Visibility.Collapsed;
        RefreshBtn.IsEnabled = false;

        try
        {
            var result = await UniversalCloudSyncService.GetCloudFolderViewAsync(_gameName, _appId, _accountId);
            if (!result.Success)
            {
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorMessageText.Text = result.ErrorMessage;
                FolderSummaryText.Text = "Connection failed";
                return;
            }

            _webLink = result.WebLink;
            FolderPathBreadcrumb.Text = result.FolderPathDisplay;

            if (result.Files.Count == 0)
            {
                EmptyStateBorder.Visibility = Visibility.Visible;
                FolderSummaryText.Text = "0 files in cloud folder (Empty)";
            }
            else
            {
                FilesListBox.ItemsSource = result.Files;
                FilesListBox.Visibility = Visibility.Visible;
                FolderSummaryText.Text = $"{result.Files.Count} file(s) in cloud • Total: {result.FormattedTotalSize}";
            }
        }
        catch (Exception ex)
        {
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorMessageText.Text = ex.Message;
            FolderSummaryText.Text = "Error";
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            RefreshBtn.IsEnabled = true;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadFolderAsync();
    }

    private void OpenLocal_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_localSavePath) && Directory.Exists(_localSavePath))
        {
            Process.Start(new ProcessStartInfo { FileName = _localSavePath, UseShellExecute = true })?.Dispose();
            return;
        }

        if (_appId > 0)
        {
            CloudLocationService.OpenLocalStorageFolder(_accountId ?? "0", _appId.ToString());
            return;
        }

        var profile = UniversalSaveWatcherService.GetProfiles()
            .Find(p => p.GameName.Equals(_gameName, StringComparison.OrdinalIgnoreCase));
        if (profile != null && Directory.Exists(profile.ExpandedSavePath))
        {
            Process.Start(new ProcessStartInfo { FileName = profile.ExpandedSavePath, UseShellExecute = true })?.Dispose();
            return;
        }

        var defaultSnapshots = Path.Combine(SaveHistoryManager.GetSnapshotsBaseDir(), SaveHistoryManager.SanitizeFolderName(_gameName));
        if (Directory.Exists(defaultSnapshots))
        {
            Process.Start(new ProcessStartInfo { FileName = defaultSnapshots, UseShellExecute = true })?.Dispose();
        }
        else
        {
            System.Windows.MessageBox.Show("Local save path is not configured or does not exist yet.", "Local Folder", System.Windows.MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenWeb_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_webLink))
        {
            try
            {
                Process.Start(new ProcessStartInfo(_webLink) { UseShellExecute = true })?.Dispose();
                return;
            }
            catch { }
        }

        var searchUrl = $"https://drive.google.com/drive/search?q={Uri.EscapeDataString(_gameName)}";
        try
        {
            Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true })?.Dispose();
        }
        catch { }
    }

    private async void DownloadFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not UniversalCloudSyncService.CloudDriveFileInfo file)
            return;

        var targetDir = _localSavePath;
        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            var profile = UniversalSaveWatcherService.GetProfiles()
                .Find(p => p.GameName.Equals(_gameName, StringComparison.OrdinalIgnoreCase));
            if (profile != null && Directory.Exists(profile.ExpandedSavePath))
            {
                targetDir = profile.ExpandedSavePath;
            }
        }

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                FileName = Path.GetFileName(file.Name),
                Title = $"Download {file.Name}"
            };
            if (sfd.ShowDialog() == true)
            {
                bool ok = await UniversalCloudSyncService.DownloadCloudFileAsync(file, sfd.FileName);
                if (ok)
                {
                    var msg = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = "Download Successful",
                        Content = $"Successfully downloaded {file.Name} ({file.FormattedSize}).",
                        CloseButtonText = "OK"
                    };
                    await msg.ShowDialogAsync();
                }
                else
                {
                    var msg = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = "Download Failed",
                        Content = $"Could not download {file.Name} from cloud.",
                        CloseButtonText = "OK"
                    };
                    await msg.ShowDialogAsync();
                }
            }
            return;
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Download Cloud Save",
            Content = $"Download and restore '{file.Name}' ({file.FormattedSize}) directly to your local save folder?\n\nDestination: {targetDir}",
            PrimaryButtonText = "Restore File",
            PrimaryButtonAppearance = ControlAppearance.Primary,
            CloseButtonText = "Cancel"
        };

        var res = await confirm.ShowDialogAsync();
        if (res == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            var destPath = Path.Combine(targetDir, file.Name);
            if (AppSettings.AutoConflictHealing && File.Exists(destPath))
            {
                SaveHistoryManager.CreateConflictBackup(_gameName, targetDir, $"Pre-Download Conflict Vault: {file.Name}", _appId > 0 ? _appId.ToString() : null);
            }
            bool ok = await UniversalCloudSyncService.DownloadCloudFileAsync(file, destPath);
            if (ok)
            {
                var okBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "File Restored",
                    Content = $"Successfully restored '{file.Name}' to:\n{destPath}",
                    CloseButtonText = "OK"
                };
                await okBox.ShowDialogAsync();
            }
            else
            {
                var failBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Download Error",
                    Content = $"Could not download '{file.Name}' from cloud storage.",
                    CloseButtonText = "OK"
                };
                await failBox.ShowDialogAsync();
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
