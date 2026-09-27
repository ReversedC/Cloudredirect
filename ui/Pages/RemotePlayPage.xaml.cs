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
    private enum ConnectionMode
    {
        SmartAuto,
        RemoteOnly,
        LocalWifi
    }

    private ConnectionMode _mode = ConnectionMode.SmartAuto;

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
        if (RemoteModeRadio?.IsChecked == true)
        {
            _mode = ConnectionMode.RemoteOnly;
        }
        else if (WifiModeRadio?.IsChecked == true)
        {
            _mode = ConnectionMode.LocalWifi;
        }
        else
        {
            _mode = ConnectionMode.SmartAuto;
        }
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
        string? tunnel = server.TunnelUrl;

        switch (_mode)
        {
            case ConnectionMode.RemoteOnly:
                if (!string.IsNullOrEmpty(tunnel))
                {
                    activeUrl = tunnel;
                    QrBadgeText.Text = "🌐 REMOTE (4G / 5G / ANY NETWORK)";
                    QrBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38EF7D"));
                    ModeHintText.Text = "Direct Cloudflare Tunnel link. Works anywhere in the world on mobile data or any network:";
                    if (ModeExplainerText != null)
                    {
                        ModeExplainerText.Text = "🌐 Remote Mode: Connects across the Internet via Cloudflare Tunnel. Scan with SUO Link or open link in Chrome/Safari.";
                        ModeExplainerText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38EF7D"));
                    }
                }
                else
                {
                    activeUrl = "Establishing secure Cloudflare Tunnel... (please wait ~5s)";
                    QrBadgeText.Text = "🌐 CONNECTING TUNNEL...";
                    QrBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5A93C"));
                    ModeHintText.Text = "Initializing zero-config remote tunnel. QR code will appear automatically in ~5 seconds:";
                    if (ModeExplainerText != null)
                    {
                        ModeExplainerText.Text = "⏳ Generating Cloudflare Tunnel for remote network access... Please wait a few seconds.";
                        ModeExplainerText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5A93C"));
                    }
                }
                break;

            case ConnectionMode.LocalWifi:
                activeUrl = server.ServerUrl;
                QrBadgeText.Text = "📶 LOCAL WI-FI (OFFLINE LAN)";
                QrBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66C0F4"));
                ModeHintText.Text = "Connect from home on the same Wi-Fi network (<1ms ultra-low latency):";
                if (ModeExplainerText != null)
                {
                    ModeExplainerText.Text = "📶 Local Wi-Fi Mode: Requires both PC and phone to be connected to the exact same Wi-Fi router.";
                    ModeExplainerText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F98A0"));
                }
                break;

            case ConnectionMode.SmartAuto:
            default:
                activeUrl = server.SmartConnectUrl;
                if (!string.IsNullOrEmpty(tunnel))
                {
                    QrBadgeText.Text = "⚡ SMART AUTO (WI-FI + 4G/5G READY)";
                    QrBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D2FF"));
                    ModeHintText.Text = "Universal Smart QR: Connects via LAN when home (<1ms) or auto-routes via Cloudflare Tunnel on 4G/5G:";
                    if (ModeExplainerText != null)
                    {
                        ModeExplainerText.Text = "⚡ Smart Auto: Scan from phone on ANY network. Auto-connects via Wi-Fi (<1ms) or Cloudflare Tunnel (4G/5G)!";
                        ModeExplainerText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D2FF"));
                    }
                }
                else
                {
                    QrBadgeText.Text = "⚡ SMART AUTO (ESTABLISHING TUNNEL...)";
                    QrBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5A93C"));
                    ModeHintText.Text = "Universal Smart QR: Local Wi-Fi ready, establishing remote tunnel for 4G/5G failover...";
                    if (ModeExplainerText != null)
                    {
                        ModeExplainerText.Text = "⚡ Smart Auto: Local Wi-Fi is ready. Establishing remote tunnel for different networks in background...";
                        ModeExplainerText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5A93C"));
                    }
                }
                break;
        }

        ServerUrlBox.Text = activeUrl;

        try
        {
            if (activeUrl.StartsWith("http"))
            {
                var qrBmp = QrCodeHelper.GenerateQrCode(activeUrl, pixelsPerModule: 8);
                QrCodeImage.Source = qrBmp;
            }
            else
            {
                QrCodeImage.Source = null;
            }
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
