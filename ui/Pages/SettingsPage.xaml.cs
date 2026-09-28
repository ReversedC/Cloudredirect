using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CloudRedirect.Resources;
using CloudRedirect.Services;

namespace CloudRedirect.Pages;

public partial class SettingsPage : Page
{
    private const string ReleasesUrl = "https://github.com/Selectively11/CloudRedirect/releases";

    private bool _syncLoading;

    public SettingsPage()
    {
        InitializeComponent();
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
                         snap.ShowNonSteamGame, snap.CustomCloudIcon);

        var steamPath = Services.SteamDetector.FindSteamPath();
        if (!string.IsNullOrEmpty(steamPath) && Services.SteamWebUiPatcher.IsMillenniumActive(steamPath))
        {
            MillenniumBadge.Visibility = Visibility.Visible;
        }
        else
        {
            MillenniumBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplySyncToggles(bool? achievements, bool? playtime, bool? luas, bool? autoUpdateDll,
                                   bool? showNonSteamGame, bool? customCloudIcon)
    {
        _syncLoading = true;
        try
        {
            if (achievements == true) SyncAchievementsToggle.IsChecked = true;
            if (playtime == true) SyncPlaytimeToggle.IsChecked = true;
            if (autoUpdateDll == true) AutoUpdateDllToggle.IsChecked = true;
            if (showNonSteamGame == true) ShowNonSteamGameToggle.IsChecked = true;
            if (customCloudIcon != false) SteamCloudIconToggle.IsChecked = true;

            StartWithWindowsToggle.IsChecked = AppSettings.StartWithWindows;
            MinimizeToTrayToggle.IsChecked = AppSettings.MinimizeToTrayOnClose;
            ShowNotificationsToggle.IsChecked = AppSettings.ShowSyncNotifications;
            GlobalHotkeyToggle.IsChecked = AppSettings.GlobalHotkeyEnabled;
            PopulateHotkeyPresets();
            AutoProtectNonCloudToggle.IsChecked = AppSettings.AutoProtectNonCloudGames;
            AutoFitZoomToggle.IsChecked = AppSettings.AutoFitZoom;

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
        AppSettings.StartWithWindows = StartWithWindowsToggle.IsChecked == true;
        AppSettings.MinimizeToTrayOnClose = MinimizeToTrayToggle.IsChecked == true;
        AppSettings.ShowSyncNotifications = ShowNotificationsToggle.IsChecked == true;
        AppSettings.AutoProtectNonCloudGames = AutoProtectNonCloudToggle.IsChecked == true;
        AppSettings.AutoSyncOnGameExit = AutoSyncExitToggle.IsChecked == true;
        AppSettings.AutoMidGameCheckpoint = AutoCheckpointToggle.IsChecked == true;
        AppSettings.AutoConflictHealing = AutoConflictHealingToggle.IsChecked == true;
        AppSettings.AutoStorageCompression = AutoCompressionToggle.IsChecked == true;
        AppSettings.AutoCommunityDatabase = AutoCommunityDbToggle.IsChecked == true;
    }

    private void AutoFitZoomToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncLoading) return;
        bool isEnabled = AutoFitZoomToggle.IsChecked == true;
        AppSettings.AutoFitZoom = isEnabled;
        UiZoomManager.Instance.SetAutoFit(isEnabled);
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

    private async void SteamCloudIconToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncLoading) return;

        bool isChecked = SteamCloudIconToggle.IsChecked == true;
        try
        {
            var steamPath = Services.SteamDetector.FindSteamPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                _syncLoading = true;
                try { SteamCloudIconToggle.IsChecked = !isChecked; }
                finally { _syncLoading = false; }

                await Services.Dialog.ShowErrorAsync(
                    S.Get("Common_Error"),
                    "Steam installation path could not be located.");
                return;
            }

            if (isChecked)
            {
                var (success, msg) = Services.SteamWebUiPatcher.ApplyPatch(steamPath);
                if (!success)
                {
                    _syncLoading = true;
                    try { SteamCloudIconToggle.IsChecked = false; }
                    finally { _syncLoading = false; }

                    await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), msg);
                    return;
                }

                await Services.Dialog.ShowInfoAsync(
                    S.Get("Settings_Done"),
                    S.Get("Settings_CustomCloudIconSuccess"));
            }
            else
            {
                var (success, msg) = Services.SteamWebUiPatcher.RemovePatch(steamPath);
                if (!success)
                {
                    _syncLoading = true;
                    try { SteamCloudIconToggle.IsChecked = true; }
                    finally { _syncLoading = false; }

                    await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), msg);
                    return;
                }

                await Services.Dialog.ShowInfoAsync(
                    S.Get("Settings_Done"),
                    S.Get("Settings_CustomCloudIconReverted"));
            }
        }
        catch (Exception ex)
        {
            _syncLoading = true;
            try { SteamCloudIconToggle.IsChecked = !isChecked; }
            finally { _syncLoading = false; }

            await Services.Dialog.ShowErrorAsync(
                S.Get("Common_Error"),
                ex.Message);
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
                writer.WriteBoolean("custom_cloud_icon", SteamCloudIconToggle.IsChecked == true);
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

    private async void RefreshSteamGui_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var steamPath = Services.SteamDetector.FindSteamPath();
            var (success, msg) = Services.SteamWebUiPatcher.ApplyPatch(steamPath);
            if (success)
            {
                await Services.Dialog.ShowInfoAsync(S.Get("Settings_Done"), msg);
            }
            else
            {
                await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), msg);
            }
        }
        catch (Exception ex)
        {
            await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), ex.Message);
        }
    }
}
