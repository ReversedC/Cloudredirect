using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CloudRedirect.Services;
using Wpf.Ui.Controls;

namespace CloudRedirect.Dialogs;

public partial class MobileStreamingGuideDialog : FluentWindow
{
    private bool _isPasswordRevealed = true;
    private bool _qrModeApk = true;

    public MobileStreamingGuideDialog()
    {
        InitializeComponent();
        RefreshStatus();
        UpdateQrCode();
        _ = InitializeCredentialsAsync();

        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            if (Height > workArea.Height * 0.92)
            {
                Height = Math.Max(480, workArea.Height * 0.92);
            }
            if (Width > workArea.Width * 0.94)
            {
                Width = Math.Max(640, workArea.Width * 0.94);
            }
        };
    }

    private void RefreshStatus()
    {
        PcNameText.Text = Environment.MachineName;
        LanIpText.Text = SunshineSyncService.GetLocalLanIp() ?? "127.0.0.1";

        var tailscaleIp = SunshineSyncService.GetTailscaleIp();
        if (!string.IsNullOrEmpty(tailscaleIp))
        {
            TailscaleIpText.Text = tailscaleIp;
            TabTailscaleIpText.Text = tailscaleIp;
            TailscaleIpBorder.Visibility = Visibility.Visible;
            TailscaleStatusBadge.Text = "Active & Ready";
            TailscaleStatusBadge.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        else
        {
            TailscaleIpBorder.Visibility = Visibility.Collapsed;
            TabTailscaleIpText.Text = "Not Detected";
            TailscaleStatusBadge.Text = "Not Connected";
            TailscaleStatusBadge.Foreground = System.Windows.Media.Brushes.Orange;
        }

        if (SunshineSyncService.IsSunshineRunning)
        {
            SunshineStatusBadge.Text = "Active & Ready";
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

    private void UpdateQrCode()
    {
        try
        {
            var lanIp = SunshineSyncService.GetLocalLanIp() ?? "127.0.0.1";
            string payload = _qrModeApk
                ? "https://github.com/mirzaarsyad74-cmyk/Cloudredirect/releases/latest/download/CloudRedirect-Stream.apk"
                : $"https://{lanIp}:47990/pin";

            QrCodeImage.Source = QrCodeHelper.GenerateQrCode(payload, 5);
        }
        catch
        {
            QrCodeImage.Source = null;
        }
    }

    private void QrModeApk_Click(object sender, RoutedEventArgs e)
    {
        _qrModeApk = true;
        QrModeApkBtn.Appearance = ControlAppearance.Primary;
        QrModePinBtn.Appearance = ControlAppearance.Secondary;
        QrModeDescription.Text = "Scan with phone camera to download CloudRedirect-Stream.apk directly.";
        UpdateQrCode();
    }

    private void QrModePin_Click(object sender, RoutedEventArgs e)
    {
        _qrModeApk = false;
        QrModeApkBtn.Appearance = ControlAppearance.Secondary;
        QrModePinBtn.Appearance = ControlAppearance.Primary;
        QrModeDescription.Text = "Scan to open the Sunshine PIN pairing web page on your phone.";
        UpdateQrCode();
    }

    private void CopyIp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ip = LanIpText.Text;
            if (!string.IsNullOrEmpty(ip))
            {
                Clipboard.SetText(ip);
                CopyIpBtn.Content = "Copied!";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, ev) =>
                {
                    timer.Stop();
                    CopyIpBtn.Content = "Copy IP";
                };
                timer.Start();
            }
        }
        catch { }
    }

    private async Task InitializeCredentialsAsync()
    {
        try
        {
            var pass = await SunshineSyncService.EnsureCredentialsAsync();
            UpdatePasswordDisplay(pass);
        }
        catch { }
    }

    private void UpdatePasswordDisplay(string pass)
    {
        SunshineUsernameText.Text = AppSettings.SunshineUsername;
        SunshinePasswordText.Text = pass;
        SunshinePasswordBox.Password = pass;
    }

    private void CopyUsername_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SunshineUsernameText.Text);
        }
        catch { }
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pass = SunshinePasswordText.Text;
            if (!string.IsNullOrEmpty(pass))
            {
                Clipboard.SetText(pass);
            }
        }
        catch { }
    }

    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            SunshinePasswordText.Visibility = Visibility.Visible;
            SunshinePasswordBox.Visibility = Visibility.Collapsed;
            TogglePasswordBtn.Icon = new SymbolIcon(SymbolRegular.Eye24);
        }
        else
        {
            SunshinePasswordText.Visibility = Visibility.Collapsed;
            SunshinePasswordBox.Visibility = Visibility.Visible;
            TogglePasswordBtn.Icon = new SymbolIcon(SymbolRegular.EyeOff24);
        }
    }

    private async void AutoGeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var newPass = SunshineSyncService.GenerateStrongPassword();
            var res = await SunshineSyncService.SetCredentialsAsync("admin", newPass);
            if (res.Success)
            {
                UpdatePasswordDisplay(newPass);
                await Dialog.ShowInfoAsync("Password Generated", $"Sunshine password has been updated to:\n\n{newPass}\n\nYou can now log into Sunshine using username 'admin'.");
            }
            else
            {
                await Dialog.ShowWarningAsync("Password Update Warning", res.Message);
            }
        }
        catch (Exception ex)
        {
            await Dialog.ShowErrorAsync("Password Error", ex.Message);
        }
    }

    private void SetCustomPassword_Click(object sender, RoutedEventArgs e)
    {
        CustomPasswordPanel.Visibility = CustomPasswordPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (CustomPasswordPanel.Visibility == Visibility.Visible)
        {
            NewCustomPasswordBox.Focus();
        }
    }

    private async void SaveCustomPassword_Click(object sender, RoutedEventArgs e)
    {
        var custom = NewCustomPasswordBox.Text?.Trim();
        if (string.IsNullOrEmpty(custom) || custom.Length < 8)
        {
            await Dialog.ShowWarningAsync("Invalid Password", "Password must be at least 8 characters long.");
            return;
        }

        try
        {
            var res = await SunshineSyncService.SetCredentialsAsync("admin", custom);
            if (res.Success)
            {
                UpdatePasswordDisplay(custom);
                CustomPasswordPanel.Visibility = Visibility.Collapsed;
                NewCustomPasswordBox.Clear();
                await Dialog.ShowInfoAsync("Password Saved", "Sunshine credentials successfully updated.");
            }
            else
            {
                await Dialog.ShowWarningAsync("Password Update Warning", res.Message);
            }
        }
        catch (Exception ex)
        {
            await Dialog.ShowErrorAsync("Password Error", ex.Message);
        }
    }

    private void CancelCustomPassword_Click(object sender, RoutedEventArgs e)
    {
        CustomPasswordPanel.Visibility = Visibility.Collapsed;
        NewCustomPasswordBox.Clear();
    }

    private void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        AutoGeneratePassword_Click(sender, e);
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

    private void OpenMobileApk_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var localApk = Path.Combine(baseDir, "CloudRedirect-Stream.apk");
            if (!File.Exists(localApk))
            {
                var pubPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\ui\bin\publish\CloudRedirect-Stream.apk"));
                if (File.Exists(pubPath)) localApk = pubPath;
            }
            if (!File.Exists(localApk))
            {
                var resPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\resources\mobile\CloudRedirect-Stream.apk"));
                if (File.Exists(resPath)) localApk = resPath;
            }

            if (File.Exists(localApk))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{localApk}\"") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("https://github.com/mirzaarsyad74-cmyk/Cloudredirect/releases/latest") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Dialog.ShowErrorAsync("Open APK Error", ex.Message);
        }
    }

    private void CopyTailscaleIp_Click(object sender, RoutedEventArgs e)
    {
        var ip = SunshineSyncService.GetTailscaleIp();
        if (!string.IsNullOrEmpty(ip))
        {
            try
            {
                Clipboard.SetText(ip);
                _ = Dialog.ShowInfoAsync("Tailscale IP Copied", $"Host PC Remote IP ({ip}) copied to clipboard!\n\nUse this in Moonlight -> 'Add Computer Manually' when playing from work or mobile data.");
            }
            catch { }
        }
    }

    private async void StartTailscale_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "tailscale.exe",
                Arguments = "up --unattended",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            await Task.Delay(2000);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            await Dialog.ShowErrorAsync("Tailscale Error", $"Could not launch Tailscale: {ex.Message}");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
