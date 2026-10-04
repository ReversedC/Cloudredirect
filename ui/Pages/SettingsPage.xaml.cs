using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CloudRedirect.Resources;
using CloudRedirect.Services;

namespace CloudRedirect.Pages;

public partial class SettingsPage : Page
{
    private const string ReleasesUrl = "https://github.com/Selectively11/CloudRedirect/releases";

    private bool _syncLoading;

    public SettingsPage()
    {
        _syncLoading = true;
        try
        {
            InitializeComponent();
        }
        finally
        {
            _syncLoading = false;
        }

        Loaded += async (_, _) =>
        {
            try { await LoadSettingsAsync(); }
            catch { }
        };
    }

    /// <summary>Off-thread snapshot of config.json state.</summary>
    private sealed record SettingsSnapshot(
        bool? SyncAchievements,
        bool? SyncPlaytime,
        bool? SyncLuas,
        bool? AutoUpdateDll,
        bool? ShowNonSteamGame,
        bool? CustomCloudIcon);

    // M15: Read config off UI thread to avoid slow-disk stall.
    private async Task LoadSettingsAsync()
    {
        var snapshot = await Task.Run(() =>
        {
            bool? a = null, p = null, l = null, u = null, nsg = null, cci = null;
            ReadSyncTogglesInto(ref a, ref p, ref l, ref u, ref nsg, ref cci);

            return new SettingsSnapshot(a, p, l, u, nsg, cci);
        });

        ApplySettingsSnapshot(snapshot);
    }

    private void ApplySettingsSnapshot(SettingsSnapshot snap)
    {
        ShowNonSteamGameCard.Visibility = Visibility.Collapsed;
        ExtraSection.Visibility = Visibility.Visible;

        ApplySyncToggles(snap.SyncAchievements, snap.SyncPlaytime, snap.SyncLuas, snap.AutoUpdateDll,
                         snap.ShowNonSteamGame);

        _ = LoadPlatformStatusAsync();
    }

    private void ApplySyncToggles(bool? achievements, bool? playtime, bool? luas, bool? autoUpdateDll,
                                   bool? showNonSteamGame)
    {
        _syncLoading = true;
        try
        {
            if (achievements == true) SyncAchievementsToggle.IsChecked = true;
            if (playtime == true) SyncPlaytimeToggle.IsChecked = true;
            if (autoUpdateDll == true) AutoUpdateDllToggle.IsChecked = true;
            if (showNonSteamGame == true) ShowNonSteamGameToggle.IsChecked = true;

            StartWithWindowsToggle.IsChecked = AppSettings.StartWithWindows;
            MinimizeToTrayToggle.IsChecked = AppSettings.MinimizeToTrayOnClose;
            ShowNotificationsToggle.IsChecked = AppSettings.ShowSyncNotifications;
            GlobalHotkeyToggle.IsChecked = AppSettings.GlobalHotkeyEnabled;
            PopulateHotkeyPresets();
            GameBoostToggle.IsChecked = AppSettings.GameBoostEnabled;
            if (GameBoostGpuToggle != null) GameBoostGpuToggle.IsChecked = AppSettings.GameBoostHighPerformanceGpu;
            if (GameBoostAutoFreezeToggle != null) GameBoostAutoFreezeToggle.IsChecked = AppSettings.GameBoostAutoFreezeBackground;
            if (GameBoostPeriodicRamToggle != null) GameBoostPeriodicRamToggle.IsChecked = AppSettings.GameBoostPeriodicRamPurge;
            if (GameBoostSubOptionsPanel != null) GameBoostSubOptionsPanel.IsEnabled = AppSettings.GameBoostEnabled;
            GameSpaceToggle.IsChecked = AppSettings.GameSpaceEnabled;
            AutoProtectNonCloudToggle.IsChecked = AppSettings.AutoProtectNonCloudGames;
            MillenniumPluginToggle.IsChecked = AppSettings.EnableMillenniumPlugin;
            bool millInstalled = MillenniumPluginService.IsMillenniumInstalled();
            MillenniumStatusText.Text = millInstalled ? S.Get("Settings_MillenniumInstalled") : S.Get("Settings_MillenniumNotFound");
            MillenniumStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                millInstalled ? System.Windows.Media.Color.FromRgb(0x66, 0xC0, 0xF4) : System.Windows.Media.Color.FromRgb(0x8F, 0x98, 0xA0));
            MillenniumStatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                millInstalled ? System.Windows.Media.Color.FromRgb(0x25, 0x42, 0x5F) : System.Windows.Media.Color.FromRgb(0x36, 0x3E, 0x45));
            AutoFitZoomToggle.IsChecked = AppSettings.AutoFitZoom;
            if (AutoRestartSteamOnLaunchToggle != null)
                AutoRestartSteamOnLaunchToggle.IsChecked = AppSettings.AutoRestartSteamOnLaunch;

            AutoSyncExitToggle.IsChecked = AppSettings.AutoSyncOnGameExit;
            AutoCheckpointToggle.IsChecked = AppSettings.AutoMidGameCheckpoint;
            AutoConflictHealingToggle.IsChecked = AppSettings.AutoConflictHealing;
            AutoCompressionToggle.IsChecked = AppSettings.AutoStorageCompression;
            AutoCommunityDbToggle.IsChecked = AppSettings.AutoCommunityDatabase;
        }
        finally
        {
            _syncLoading = false;
        }
    }

    private void PopulateHotkeyPresets()
    {
        var current = AppSettings.GlobalHotkey;
        var presets = new[] { "Ctrl+Shift+C", "Ctrl+Alt+C", "Ctrl+Shift+R", "Ctrl+Shift+S", "Alt+Shift+C", "Ctrl+~" };
        HotkeyPresetCombo.Items.Clear();
        foreach (var p in presets)
        {
            HotkeyPresetCombo.Items.Add(p);
        }

        if (!HotkeyPresetCombo.Items.Contains(current))
        {
            HotkeyPresetCombo.Items.Add(current);
        }

        HotkeyPresetCombo.SelectedItem = current;
        HotkeyBadgeText.Text = current;
    }

    private void HotkeyPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncLoading) return;
        if (HotkeyPresetCombo.SelectedItem is string selected && !string.IsNullOrEmpty(selected))
        {
            HotkeyBadgeText.Text = selected;
            if (GlobalHotkeyToggle.IsChecked == true)
            {
                bool ok = GlobalHotkeyService.Instance.RegisterWithFallback(selected, out var actual);
                if (ok && !string.Equals(actual, selected, StringComparison.OrdinalIgnoreCase))
                {
                    _syncLoading = true;
                    try
                    {
                        HotkeyPresetCombo.SelectedItem = actual;
                        HotkeyBadgeText.Text = actual;
                    }
                    finally { _syncLoading = false; }
                }
            }
            else
            {
                AppSettings.GlobalHotkey = selected;
            }
        }
    }

    private void GlobalHotkeyToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncLoading) return;
        bool isEnabled = GlobalHotkeyToggle.IsChecked == true;
        AppSettings.GlobalHotkeyEnabled = isEnabled;
        if (isEnabled)
        {
            var shortcut = HotkeyPresetCombo.SelectedItem as string ?? AppSettings.GlobalHotkey;
            bool ok = GlobalHotkeyService.Instance.RegisterWithFallback(shortcut, out var actual);
            if (ok && !string.Equals(actual, shortcut, StringComparison.OrdinalIgnoreCase))
            {
                _syncLoading = true;
                try
                {
                    HotkeyPresetCombo.SelectedItem = actual;
                    HotkeyBadgeText.Text = actual;
                }
                finally { _syncLoading = false; }
            }
        }
        else
        {
            GlobalHotkeyService.Instance.Unregister();
        }
    }

    private void AppSettingsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncLoading) return;

        if (StartWithWindowsToggle != null)
            AppSettings.StartWithWindows = StartWithWindowsToggle.IsChecked == true;
        if (MinimizeToTrayToggle != null)
            AppSettings.MinimizeToTrayOnClose = MinimizeToTrayToggle.IsChecked == true;
        if (ShowNotificationsToggle != null)
            AppSettings.ShowSyncNotifications = ShowNotificationsToggle.IsChecked == true;

        if (GameBoostToggle != null)
        {
            bool prevBoost = AppSettings.GameBoostEnabled;
            bool newBoost = GameBoostToggle.IsChecked == true;
            AppSettings.GameBoostEnabled = newBoost;
            if (GameBoostSubOptionsPanel != null)
            {
                GameBoostSubOptionsPanel.IsEnabled = newBoost;
            }
            if (prevBoost && !newBoost && GameBoostService.IsBoostActive)
            {
                GameBoostService.RevertBoost();
            }
        }

        if (GameBoostGpuToggle != null)
            AppSettings.GameBoostHighPerformanceGpu = GameBoostGpuToggle.IsChecked == true;
        if (GameBoostAutoFreezeToggle != null)
            AppSettings.GameBoostAutoFreezeBackground = GameBoostAutoFreezeToggle.IsChecked == true;
        if (GameBoostPeriodicRamToggle != null)
            AppSettings.GameBoostPeriodicRamPurge = GameBoostPeriodicRamToggle.IsChecked == true;

        if (GameSpaceToggle != null)
        {
            bool prevGameSpace = AppSettings.GameSpaceEnabled;
            bool newGameSpace = GameSpaceToggle.IsChecked == true;
            AppSettings.GameSpaceEnabled = newGameSpace;
            if (prevGameSpace != newGameSpace)
            {
                GlobalHotkeyService.Instance.RegisterGameSpaceHotkey();
            }
        }

        if (AutoProtectNonCloudToggle != null)
            AppSettings.AutoProtectNonCloudGames = AutoProtectNonCloudToggle.IsChecked == true;

        if (MillenniumPluginToggle != null)
        {
            bool prevMillennium = AppSettings.EnableMillenniumPlugin;
            bool newMillennium = MillenniumPluginToggle.IsChecked == true;
            AppSettings.EnableMillenniumPlugin = newMillennium;
            if (prevMillennium != newMillennium)
            {
                Task.Run(() => MillenniumPluginService.SyncWithSettings());
            }
        }

        if (AutoRestartSteamOnLaunchToggle != null)
            AppSettings.AutoRestartSteamOnLaunch = AutoRestartSteamOnLaunchToggle.IsChecked == true;

        if (AutoSyncExitToggle != null)
            AppSettings.AutoSyncOnGameExit = AutoSyncExitToggle.IsChecked == true;
        if (AutoCheckpointToggle != null)
            AppSettings.AutoMidGameCheckpoint = AutoCheckpointToggle.IsChecked == true;
        if (AutoConflictHealingToggle != null)
            AppSettings.AutoConflictHealing = AutoConflictHealingToggle.IsChecked == true;
        if (AutoCompressionToggle != null)
            AppSettings.AutoStorageCompression = AutoCompressionToggle.IsChecked == true;
        if (AutoCommunityDbToggle != null)
            AppSettings.AutoCommunityDatabase = AutoCommunityDbToggle.IsChecked == true;
    }

    private void AutoFitZoomToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncLoading || AutoFitZoomToggle == null) return;
        bool isEnabled = AutoFitZoomToggle.IsChecked == true;
        AppSettings.AutoFitZoom = isEnabled;
        UiZoomManager.Instance.SetAutoFit(isEnabled);
    }

    private int _testNotificationCycle = 0;
    private void NotificationTestButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_testNotificationCycle % 5)
        {
            case 0:
                SteamToastService.ShowAuto("CloudRedirect", "☁️ Factory Town 2: Paradise: Saves synchronized to cloud (0.8s)");
                break;
            case 1:
                SteamToastService.ShowAuto("CloudRedirect", "↺ Factory Town 2: Paradise: Save snapshot restored successfully");
                break;
            case 2:
                SteamToastService.ShowAuto("CloudRedirect Auto-Heal", "🩹 Factory Town 2: Paradise: Repaired 0-byte corrupted save file");
                break;
            case 3:
                SteamToastService.ShowAuto("CloudRedirect", "⚠️ Factory Town 2: Paradise: Cloud sync encountered an issue upon exit.");
                break;
            case 4:
                SteamToastService.Show("CloudRedirect", "Running in background • Cloud save synchronization remains active", Windows.ToastNotificationType.Tray);
                break;
        }
        _testNotificationCycle++;
    }

    private void GameBoostTestButton_Click(object sender, RoutedEventArgs e)
    {
        GameBoostToastService.Show("Cyberpunk 2077", "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/1091500/header.jpg", "⚡ 1,240 MB Standby RAM Optimized");
    }

    private void GameSpaceTestButton_Click(object sender, RoutedEventArgs e)
    {
        GameSpaceService.Instance.Toggle(isPreview: true);
    }


    /// <summary>Reads sync toggles from config.json (called inside Task.Run).</summary>
    private static void ReadSyncTogglesInto(ref bool? achievements, ref bool? playtime, ref bool? luas, ref bool? autoUpdateDll,
                                              ref bool? showNonSteamGame, ref bool? customCloudIcon)
    {
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("sync_achievements", out var a) && a.ValueKind == JsonValueKind.True)
                achievements = true;
            if (root.TryGetProperty("sync_playtime", out var p) && p.ValueKind == JsonValueKind.True)
                playtime = true;
            if (root.TryGetProperty("sync_luas", out var l) && l.ValueKind == JsonValueKind.True)
                luas = true;
            if (root.TryGetProperty("auto_update_dll", out var u))
                autoUpdateDll = u.ValueKind == JsonValueKind.True;
            else
                autoUpdateDll = true; // default on when key absent
            if (root.TryGetProperty("show_non_steam_game", out var nsg))
                showNonSteamGame = nsg.ValueKind == JsonValueKind.True;
            else
                showNonSteamGame = true; // default on when key absent
            if (root.TryGetProperty("custom_cloud_icon", out var cci))
                customCloudIcon = cci.ValueKind == JsonValueKind.True;
            else
                customCloudIcon = true; // default on when key absent
        }
        catch { }
    }

    private static string GetConfigPath()
    {
        return Services.SteamDetector.GetConfigFilePath();
    }

    private async void SyncToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncLoading) return;

        try
        {
            SaveSyncToggles();
        }
        catch (Exception ex)
        {
            // Only the toggle that just fired diverges from the on-disk
            // value — flip it back and surface the error. The
            // _syncLoading guard suppresses the recursive Changed event
            // that the programmatic IsChecked set will trigger.
            if (sender is Wpf.Ui.Controls.ToggleSwitch toggle)
            {
                _syncLoading = true;
                try { toggle.IsChecked = !(toggle.IsChecked == true); }
                finally { _syncLoading = false; }
            }

            await Services.Dialog.ShowErrorAsync(
                S.Get("Common_Error"),
                S.Format("Settings_FailedSaveSync", ex.Message));
        }
    }

    /// <summary>Persists sync toggles to config.json; throws on I/O failure for caller to revert.</summary>
    private void SaveSyncToggles()
    {
        var path = GetConfigPath();

        // schema_fetch / experimental_schema_fetch are retired: stay in the strip list so a
        // saved config drops the stale keys, but no longer written back.
        Services.ConfigHelper.SaveConfig(path,
            new[] { "sync_achievements", "sync_playtime", "sync_luas", "sync_luas_backup", "sync_luas_restore", "auto_update_dll",
                    "show_non_steam_game", "custom_cloud_icon", "parental_ignore_playtime", "parental_bypass_playtime",
                    "schema_fetch", "experimental_schema_fetch" },
            writer =>
            {
                writer.WriteBoolean("sync_achievements", SyncAchievementsToggle.IsChecked == true);
                writer.WriteBoolean("sync_playtime", SyncPlaytimeToggle.IsChecked == true);
                writer.WriteBoolean("sync_luas", true);
                writer.WriteBoolean("sync_luas_backup", true);
                writer.WriteBoolean("sync_luas_restore", false);
                writer.WriteBoolean("auto_update_dll", AutoUpdateDllToggle.IsChecked == true);
                writer.WriteBoolean("show_non_steam_game", ShowNonSteamGameToggle.IsChecked == true);
            });
    }

    private async void ResetData_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await Services.Dialog.ConfirmDangerAsync(S.Get("Settings_ConfirmResetTitle"),
            S.Get("Settings_ConfirmResetMessage"));

        if (!confirmed) return;

        var steamPath = Services.SteamDetector.FindSteamPath();
        if (steamPath == null) return;

        try
        {
            var dataRoot = Path.Combine(steamPath, "cloud_redirect");
            var storagePath = Path.Combine(dataRoot, "storage");

            // Legacy/unused folders from older versions
            var blobsPath = Path.Combine(dataRoot, "blobs");
            var savesPath = Path.Combine(dataRoot, "saves");

            if (Directory.Exists(storagePath))
                Directory.Delete(storagePath, true);
            if (Directory.Exists(blobsPath))
                Directory.Delete(blobsPath, true);
            if (Directory.Exists(savesPath))
                Directory.Delete(savesPath, true);

            await Services.Dialog.ShowInfoAsync(S.Get("Settings_Done"), S.Get("Settings_ResetDoneMessage"));
        }
        catch (Exception ex)
        {
            await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), S.Format("Settings_FailedReset", ex.Message));
        }
    }

    private void OpenSuoRemote_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mw)
        {
            mw.NavigateTo(typeof(SuoRemotePage));
        }
    }

    private async Task LoadPlatformStatusAsync()
    {
        try
        {
            var steamPath = SteamDetector.FindSteamPath();
            var steamDetails = SteamDetector.GetSteamVersionDetails(steamPath);

            SteamVersionText.Text = steamDetails.DisplayVersion;
            SteamBranchText.Text = steamDetails.Branch;
            SteamManifestVersionText.Text = steamDetails.PackageVersion ?? "--";
            SteamClientBuildDateText.Text = steamDetails.ClientBuildDateStr ?? "--";
            SteamWebBuildDateText.Text = steamDetails.WebBuildDateStr ?? "--";
            SteamApiVersionText.Text = steamDetails.ApiVersion ?? "--";

            if (steamDetails.IsBeta)
            {
                SteamBranchBadge.Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x2B, 0x16));
                SteamBranchBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x00));
                SteamBranchText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x00));
            }
            else
            {
                SteamBranchBadge.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x2B, 0x3D));
                SteamBranchBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
                SteamBranchText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
            }

            var suoInfo = await SuoDetector.DetectAsync();
            OpenSuoRemoteSettingsBtn.Visibility = (suoInfo.IsInstalled || suoInfo.IsOnline) ? Visibility.Visible : Visibility.Collapsed;

            if (suoInfo.IsInstalled)
            {
                SuoVersionText.Text = $"{suoInfo.Version} • {(suoInfo.IsOnline ? "Local daemon active" : "Daemon offline")}";
                if (suoInfo.IsOnline)
                {
                    SuoStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x32, 0x24));
                    SuoStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0x2B));
                    SuoStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
                    SuoStatusText.Text = "Online";
                    SuoStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
                }
                else
                {
                    SuoStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x32, 0x14, 0x14));
                    SuoStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xA3, 0x3B, 0x3B));
                    SuoStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0x50, 0x50));
                    SuoStatusText.Text = "Offline";
                    SuoStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x50, 0x50));
                }
            }
            else
            {
                SuoVersionText.Text = "Not detected or installed";
                SuoStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x28));
                SuoStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
                SuoStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
                SuoStatusText.Text = "Not Detected";
                SuoStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            }
        }
        catch { }
    }
}
