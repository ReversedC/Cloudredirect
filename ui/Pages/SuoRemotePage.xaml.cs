using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CloudRedirect.Services;
using Microsoft.Web.WebView2.Core;

namespace CloudRedirect.Pages;

public partial class SuoRemotePage : Page
{
    private const string DashboardUrl = "https://onennabe.duckdns.org/dashboard";
    private bool _isWebViewInitialized;

    public SuoRemotePage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializePageAsync();
    }

    private async Task InitializePageAsync()
    {
        try
        {
            var suo = await SuoDetector.DetectAsync();
            UpdateSuoStatusUi(suo);

            if (suo.IsInstalled || suo.IsOnline)
            {
                ActivationCard.Visibility = Visibility.Collapsed;
                LoadingOverlay.Visibility = Visibility.Visible;
                await InitWebViewAsync();
            }
            else
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ActivationCard.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            LoadingStatusText.Text = "Error loading dashboard: " + ex.Message;
        }
    }

    private void UpdateSuoStatusUi(SuoDetector.SuoStatus suo)
    {
        if (suo.IsInstalled || suo.IsOnline)
        {
            SuoVersionDetailText.Text = $"{suo.Version} • {(suo.IsOnline ? "Daemon active" : "Daemon offline")}";
            if (suo.IsOnline)
            {
                SuoStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x32, 0x24));
                SuoStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0x2B));
                SuoStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
                SuoStatusText.Text = "Online";
                SuoStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
            }
            else
            {
                SuoStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x32, 0x24, 0x14));
                SuoStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xA3, 0x70, 0x2B));
                SuoStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C));
                SuoStatusText.Text = "Standby";
                SuoStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C));
            }
        }
        else
        {
            SuoVersionDetailText.Text = "Not detected or installed";
            SuoStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x28));
            SuoStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
            SuoStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            SuoStatusText.Text = "Not Detected";
            SuoStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
        }
    }

    public void ZoomIn()
    {
        try
        {
            if (_isWebViewInitialized && DashboardWebView?.CoreWebView2 != null)
            {
                DashboardWebView.ZoomFactor = Math.Min(2.0, Math.Round(DashboardWebView.ZoomFactor + 0.1, 2));
            }
        }
        catch { }
    }

    public void ZoomOut()
    {
        try
        {
            if (_isWebViewInitialized && DashboardWebView?.CoreWebView2 != null)
            {
                DashboardWebView.ZoomFactor = Math.Max(0.5, Math.Round(DashboardWebView.ZoomFactor - 0.1, 2));
            }
        }
        catch { }
    }

    private async Task InitWebViewAsync()
    {
        if (_isWebViewInitialized) return;

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userDataFolder = Path.Combine(appData, "CloudRedirect", "webview2_profile");
            Directory.CreateDirectory(userDataFolder);

            var options = new CoreWebView2EnvironmentOptions(
                "--enable-features=DnsOverHttps --dns-over-https-mode=automatic");

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await DashboardWebView.EnsureCoreWebView2Async(env);

            DashboardWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x10, 0x18, 0x22);
            DashboardWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            DashboardWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            DashboardWebView.NavigationStarting += (_, _) =>
            {
                ErrorCard.Visibility = Visibility.Collapsed;
            };

            DashboardWebView.NavigationCompleted += (_, args) =>
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                if (!args.IsSuccess)
                {
                    ShowError($"Could not connect to SUO Remote Dashboard ({args.WebErrorStatus}). Please check your connection or click 'Open in Browser'.");
                }
            };

            DashboardWebView.CoreWebView2.Navigate(DashboardUrl);
            _isWebViewInitialized = true;
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorCardText.Text = message;
        ErrorCard.Visibility = Visibility.Visible;
    }

    private async void UpdateSuo_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await Services.Dialog.ConfirmAsync(
            "Activate / Update SUO",
            "This will run the following PowerShell command in the background to activate/update SUO:\n\nirm onennabe.duckdns.org | iex\n\nProceed?");

        if (!confirmed) return;

        try
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingStatusText.Text = "Running SUO setup script (irm onennabe.duckdns.org | iex)...";
            ActivationCard.Visibility = Visibility.Collapsed;

            await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"irm onennabe.duckdns.org | iex\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(60000);
            });

            // Re-detect and refresh
            var suo = await SuoDetector.DetectAsync();
            UpdateSuoStatusUi(suo);

            if (suo.IsInstalled || suo.IsOnline)
            {
                await InitWebViewAsync();
                DashboardWebView.Reload();
            }
            else
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ActivationCard.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            await Services.Dialog.ShowErrorAsync("Setup Error", "Failed to run setup: " + ex.Message);
        }
    }

    private async void RecheckSuo_Click(object sender, RoutedEventArgs e)
    {
        await InitializePageAsync();
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        ErrorCard.Visibility = Visibility.Collapsed;
        if (_isWebViewInitialized)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingStatusText.Text = "Reloading SUO Remote Dashboard...";
            DashboardWebView.Reload();
        }
        else
        {
            _ = InitWebViewAsync();
        }
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(DashboardUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = Services.Dialog.ShowErrorAsync("Error", ex.Message);
        }
    }
}
