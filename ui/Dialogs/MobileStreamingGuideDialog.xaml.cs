using System;
using System.Windows;
using CloudRedirect.Services;
using Wpf.Ui.Controls;

namespace CloudRedirect.Dialogs;

public partial class MobileStreamingGuideDialog : FluentWindow
{
    public MobileStreamingGuideDialog()
    {
        InitializeComponent();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        PcNameText.Text = Environment.MachineName;
        LanIpText.Text = SunshineSyncService.GetLocalLanIp() ?? "127.0.0.1";

        if (SunshineSyncService.IsSunshineRunning)
        {
            SunshineStatusBadge.Text = "Active & Running";
            SunshineStatusBadge.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        else if (SunshineSyncService.IsSunshineInstalled)
        {
            SunshineStatusBadge.Text = "Installed (Stopped)";
            SunshineStatusBadge.Foreground = System.Windows.Media.Brushes.Orange;
        }
        else
        {
            SunshineStatusBadge.Text = "Not Installed";
            SunshineStatusBadge.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private void EnterPin_Click(object sender, RoutedEventArgs e)
    {
        SunshineSyncService.OpenSunshinePinPage();
    }

    private async void SyncGames_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var res = await SunshineSyncService.SyncGamesToSunshineAsync();
            if (res.Success)
            {
                await Dialog.ShowInfoAsync("Sunshine Synced", res.Message);
            }
            else
            {
                await Dialog.ShowWarningAsync("Sync Warning", res.Message);
            }
        }
        catch (Exception ex)
        {
            await Dialog.ShowErrorAsync("Sync Error", ex.Message);
        }
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e)
    {
        SunshineSyncService.OpenSunshineDashboard();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
