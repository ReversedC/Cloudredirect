using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CloudRedirect.Services;
using Microsoft.Win32;

namespace CloudRedirect.Pages;

public partial class RemotePlayPage : Page
{
    private bool _isRemoteMode = false;

    public RemotePlayPage()
    {
        InitializeComponent();
        Loaded += RemotePlayPage_Loaded;
        Unloaded += RemotePlayPage_Unloaded;
    }

    private void RemotePlayPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!RemotePlayServer.Instance.IsRunning)
        {
            RemotePlayServer.Instance.Start();
        }

        RemotePlayServer.Instance.OnStateChanged += OnServerStateChanged;
        RefreshUi();
    }

    private void RemotePlayPage_Unloaded(object sender, RoutedEventArgs e)
    {
        RemotePlayServer.Instance.OnStateChanged -= OnServerStateChanged;
    }

    private void OnServerStateChanged()
    {
        Dispatcher.Invoke(RefreshUi);
    }

    private void NetworkMode_Checked(object sender, RoutedEventArgs e)
    {
        _isRemoteMode = (RemoteModeRadio?.IsChecked == true);
        RefreshConnectionDisplay();
    }

    private void RefreshUi()
    {
        var server = RemotePlayServer.Instance;
        bool running = server.IsRunning;

        if (running)
        {
            ServerStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A4D007"));
            ServerStatusText.Text = "SERVER ONLINE";
            ServerStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A4D007"));
            ServerStatusPill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#183321"));
            ServerStatusPill.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B6B22"));
            ToggleServerBtn.Content = "Stop Host";

            RefreshConnectionDisplay();

            // Multi-Network telemetry labels
            WifiStatusLabel.Text = $"Active ({server.LanIp}:{server.Port})";
            WifiStatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A4D007"));

            bool upnp = RemoteTunnelService.Instance.IsUpnpMapped;
            UpnpStatusLabel.Text = upnp ? "Mapped (Port 8585)" : "Active (Direct)";
            UpnpStatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A4D007"));

            string? tunnel = server.TunnelUrl;
            if (!string.IsNullOrEmpty(tunnel))
            {
                TunnelStatusLabel.Text = "Online (4G/5G Ready)";
                TunnelStatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66C0F4"));
            }
            else
            {
                TunnelStatusLabel.Text = "Connecting...";
                TunnelStatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F98A0"));
            }

            int clients = server.ConnectedClientsCount;
            ConnectedDevicesText.Text = clients == 1 ? "1 Device Connected" : $"{clients} Devices Connected";
        }
        else
        {
            ServerStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            ServerStatusText.Text = "SERVER STOPPED";
            ServerStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            ServerStatusPill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D1616"));
            ServerStatusPill.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5C2020"));
            ToggleServerBtn.Content = "Start Host";

            ServerUrlBox.Text = "Server is stopped";
            QrCodeImage.Source = null;
            ConnectedDevicesText.Text = "0 Devices Connected";

            WifiStatusLabel.Text = "Stopped";
            WifiStatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F98A0"));
            UpnpStatusLabel.Text = "Inactive";
            UpnpStatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F98A0"));
            TunnelStatusLabel.Text = "Inactive";
            TunnelStatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F98A0"));
        }

        // Check if APK exists
        string apkPath = GetApkPath();
        if (File.Exists(apkPath))
        {
            var fi = new FileInfo(apkPath);
            ApkStatusText.Text = $"APK ready ({fi.Length / 1024} KB)";
            ApkStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A4D007"));
        }
        else
        {
            ApkStatusText.Text = "APK not found";
            ApkStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F98A0"));
        }
    }

    private void RefreshConnectionDisplay()
    {
        var server = RemotePlayServer.Instance;
        if (!server.IsRunning) return;

        string activeUrl;
        if (_isRemoteMode)
        {
            string? tunnel = server.TunnelUrl;
            activeUrl = !string.IsNullOrEmpty(tunnel) ? tunnel : server.ServerUrl;
            QrBadgeText.Text = "🌐 REMOTE (4G / 5G / ANY)";
            QrBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38EF7D"));
            ModeHintText.Text = "Connect from outside home via secure Cloudflare Tunnel (zero router setup needed):";
        }
        else
        {
            activeUrl = server.ServerUrl;
            QrBadgeText.Text = "📶 WI-FI (LOCAL LAN)";
            QrBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66C0F4"));
            ModeHintText.Text = "Connect from home on the same Wi-Fi network (<1ms ultra-low latency):";
        }

        ServerUrlBox.Text = activeUrl;

        try
        {
            var qrBmp = QrCodeHelper.GenerateQrCode(activeUrl, pixelsPerModule: 8);
            QrCodeImage.Source = qrBmp;
        }
        catch { }
    }

    private void ToggleServer_Click(object sender, RoutedEventArgs e)
    {
        if (RemotePlayServer.Instance.IsRunning)
        {
            RemotePlayServer.Instance.Stop();
        }
        else
        {
            RemotePlayServer.Instance.Start();
        }
        RefreshUi();
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        string url = ServerUrlBox.Text;
        if (!string.IsNullOrEmpty(url) && url.StartsWith("http"))
        {
            Clipboard.SetText(url);
            _ = Dialog.ShowInfoAsync("Link Copied", "Connection URL copied to clipboard!");
        }
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        string url = ServerUrlBox.Text;
        if (!string.IsNullOrEmpty(url) && url.StartsWith("http"))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private void DownloadApk_Click(object sender, RoutedEventArgs e)
    {
        string srcPath = GetApkPath();
        if (!File.Exists(srcPath))
        {
            _ = Dialog.ShowWarningAsync("APK Not Found", "SUO-Link.apk was not found. Please build the APK first.");
            return;
        }

        var dlg = new SaveFileDialog
        {
            FileName = "SUO-Link.apk",
            DefaultExt = ".apk",
            Filter = "Android Package (*.apk)|*.apk"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.Copy(srcPath, dlg.FileName, overwrite: true);
                _ = Dialog.ShowInfoAsync("APK Saved", $"Saved SUO Link APK to:\n{dlg.FileName}\n\nTransfer this file to your phone to install.");
            }
            catch (Exception ex)
            {
                _ = Dialog.ShowErrorAsync("Save Failed", ex.Message);
            }
        }
    }



    private static string GetApkPath()
    {
        string[] candidates = {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "SUO-Link.apk"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SUO-Link.apk"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "publish", "SUO-Link.apk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloudRedirect", "SUO-Link.apk")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private void Fps_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FpsComboBox?.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int fps))
        {
            ScreenCaptureService.Instance.TargetFps = fps;
        }
    }

    private void Resolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResolutionComboBox?.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int res))
        {
            ScreenCaptureService.Instance.TargetHeight = res;
        }
    }

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityPercentText != null)
        {
            int q = (int)e.NewValue;
            QualityPercentText.Text = $"{q}%";
            ScreenCaptureService.Instance.JpegQuality = q;
        }
    }
}
