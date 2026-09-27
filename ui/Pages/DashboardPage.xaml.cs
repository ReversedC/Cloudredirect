using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CloudRedirect.Resources;

namespace CloudRedirect.Pages;

public partial class DashboardPage : Page
{
    private string? _steamPath;
    private System.Windows.Threading.DispatcherTimer? _autoRefreshTimer;
    private bool _isLoadingStatus;
    private bool _languageLoading;

    public void HideDllUpdateBanner()
    {
        Dispatcher.Invoke(() => UpdateBanner.Visibility = Visibility.Collapsed);
    }

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            InitializeLanguageSelector();
            UpdateSuoLinkVisibility();
            Services.AppSettings.OnSuoLinkToggled -= HandleSuoLinkToggled;
            Services.AppSettings.OnSuoLinkToggled += HandleSuoLinkToggled;

            Services.SaveUploadWatcherService.Start();
            Services.SaveUploadWatcherService.OnSaveActivity += HandleSaveActivity;
            Services.ActiveGameTrackerService.OnActiveGameChanged += HandleActiveGameChanged;
            HandleActiveGameChanged(Services.ActiveGameTrackerService.CurrentGame);

            try { await LoadStatusAsync(); }
            catch { }

            _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _autoRefreshTimer.Tick += async (_, _) =>
            {
                try { await LoadStatusAsync(); }
                catch { }
            };
            _autoRefreshTimer.Start();
        };

        Unloaded += (_, _) =>
        {
            Services.AppSettings.OnSuoLinkToggled -= HandleSuoLinkToggled;
            Services.SaveUploadWatcherService.OnSaveActivity -= HandleSaveActivity;
            Services.ActiveGameTrackerService.OnActiveGameChanged -= HandleActiveGameChanged;
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer = null;
        };
    }

    private void UpdateSuoLinkVisibility()
    {
        if (SuoLinkQuickActionBtn != null)
        {
            SuoLinkQuickActionBtn.Visibility = Services.AppSettings.EnableSuoLink ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void HandleSuoLinkToggled(bool enabled)
    {
        Dispatcher.Invoke(UpdateSuoLinkVisibility);
    }

    private void HandleSaveActivity(Services.SaveUploadEvent ev)
    {
        Dispatcher.Invoke(() =>
        {
            string gameDisplay = ev.AppId > 0
                ? $"{ev.GameName} (AppID: {ev.AppId})"
                : ev.GameName;

            if (ev.IsUploading)
            {
                ActivityTitle.Text = S.Format("Dashboard_AutoSaving", gameDisplay);
                ActivityDetail.Text = ev.Bytes > 0
                    ? S.Format("Dashboard_UploadingFileFormat", ev.FileName, ev.Bytes)
                    : S.Get("Dashboard_UploadingSaveData");
                ActivityProgressBar.Visibility = Visibility.Visible;
                ActivityStatusBadge.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x1B, 0x33, 0x47));
                ActivityStatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x1A, 0x9F, 0xFF));
                ActivityStatusText.Text = S.Get("Dashboard_Uploading");
                ActivityStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x66, 0xC0, 0xF4));
                ActivityIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowSync24;
                ActivityIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x1A, 0x9F, 0xFF));
            }
            else
            {
                ActivityTitle.Text = S.Format("Dashboard_LastCloudBackupFormat", gameDisplay);
                ActivityDetail.Text = ev.Bytes > 0
                    ? S.Format("Dashboard_SaveDataFileBackedUpFormat", ev.FileName, ev.Bytes, ev.Timestamp.ToString("t"))
                    : S.Format("Dashboard_SaveDataBackedUpFormat", ev.Timestamp.ToString("t"));
                ActivityProgressBar.Visibility = Visibility.Collapsed;
                ActivityStatusBadge.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x18, 0x33, 0x21));
                ActivityStatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4C, 0x75, 0x15));
                ActivityStatusText.Text = S.Get("Dashboard_Synchronized");
                ActivityStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xA4, 0xD0, 0xA4));
                ActivityIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Checkmark24;
                ActivityIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xA4, 0xD0, 0x07));
            }
        });
    }

    // M16: Gather data off the UI thread, update controls on dispatcher
    private async Task LoadStatusAsync()
    {
        if (_isLoadingStatus) return;
        _isLoadingStatus = true;
        try
        {
        var data = await Task.Run(() =>
        {
            var steamPath = Services.SteamDetector.FindSteamPath();
            bool dllExists = false;
            bool? dllCurrent = null;
            Services.CloudConfig config = null;
            int appCount = 0;
            Services.TokenStatus tokenStatus = null;
            int localLuas = 0;
            int cloudLuas = 0;
            Services.LastBackupInfo? lastBackup = null;

            if (steamPath != null)
            {
                var dllPath = Path.Combine(steamPath, "cloud_redirect.dll");
                dllExists = File.Exists(dllPath);
                if (dllExists)
                    dllCurrent = Services.EmbeddedDll.IsDeployedCurrent(dllPath);
                config = Services.SteamDetector.ReadConfig();

                var storagePath = Path.Combine(steamPath, "cloud_redirect", "storage");
                if (Directory.Exists(storagePath))
                {
                    foreach (var accountDir in Directory.GetDirectories(storagePath))
                        appCount += Directory.GetDirectories(accountDir).Length;
                }

                // M9: Check OAuth token status off the UI thread (DPAPI + file I/O).
                if (config?.TokenPath != null && config.Provider is not "r2" and not "s3")
                    tokenStatus = Services.OAuthService.CheckTokenStatus(config.TokenPath);

                var luaCounts = Services.SteamDetector.CountLuaFiles(steamPath);
                localLuas = luaCounts.LocalCount;
                cloudLuas = luaCounts.CloudCount;

                lastBackup = Services.SteamDetector.GetLastBackupInfo(steamPath);
            }

            return (steamPath, dllExists, dllCurrent, config, appCount, tokenStatus, localLuas, cloudLuas, lastBackup);
        });

        _steamPath = data.steamPath;

        // Update UI on dispatcher thread
        SteamStatus.Text = data.steamPath ?? S.Get("Dashboard_NotFound");

        if (data.steamPath != null)
        {
            if (!data.dllExists || data.dllCurrent == false)
            {
                // Auto-apply update immediately
                var destPath = Path.Combine(data.steamPath, "cloud_redirect.dll");
                Services.EmbeddedDll.DeployTo(destPath);
            }

            DllStatus.Text = S.Get("Dashboard_DllInstalled");
            DllIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.PlugConnected24;
            UpdateBanner.Visibility = Visibility.Collapsed;

            if (data.config != null)
                UpdateProviderAuthStatus(data.config, data.tokenStatus);

            AppCount.Text = S.Format("Dashboard_AppCountFormat", data.appCount);

            LuaFilesCount.Text = S.Format("Dashboard_LuaCountFormat", data.localLuas, data.cloudLuas);
            LuaFilesDetail.Text = data.localLuas > 0
                ? S.Format("Dashboard_LuaAddonsFoundFormat", data.localLuas)
                : S.Get("Dashboard_NoLuaAddonsFound");

            // Update top activity banner with last backed up game & AppID
            if (data.lastBackup != null)
            {
                ActivityTitle.Text = S.Format("Dashboard_LastCloudBackupFormat", $"{data.lastBackup.GameName} (AppID: {data.lastBackup.AppId})");
                var timeStr = data.lastBackup.BackupTime.Date == DateTime.Today
                    ? data.lastBackup.BackupTime.ToString("t")
                    : data.lastBackup.BackupTime.ToString("g");
                ActivityDetail.Text = data.lastBackup.TotalBytes > 0
                    ? S.Format("Dashboard_SaveFilesTotalBackedUpFormat", data.lastBackup.FileCount, Services.FileUtils.FormatSize(data.lastBackup.TotalBytes), timeStr)
                    : S.Format("Dashboard_SaveFilesBackedUpFormat", data.lastBackup.FileCount, timeStr);
                ActivityStatusBadge.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x18, 0x33, 0x21));
                ActivityStatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4C, 0x75, 0x15));
                ActivityStatusText.Text = S.Get("Dashboard_Synchronized");
                ActivityStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xA4, 0xD0, 0x07));
                ActivityIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Checkmark24;
                ActivityIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xA4, 0xD0, 0x07));
                ActivityProgressBar.Visibility = Visibility.Collapsed;
            }

            var uniCount = Services.UniversalSaveWatcherService.GetProfiles().Count;
            UniversalSavesCountText.Text = S.Format("Dashboard_UniversalSavesConfiguredFormat", uniCount);
        }
        }
        finally
        {
            _isLoadingStatus = false;
        }
    }

    private Services.ActiveGameInfo? _currentActiveGame;

    private void HandleActiveGameChanged(Services.ActiveGameInfo? game)
    {
        _currentActiveGame = game;
        Dispatcher.Invoke(() =>
        {
            if (game != null)
            {
                ActiveGameCard.Visibility = Visibility.Visible;
                ActiveGameTitle.Text = game.Name;
                ActiveGamePosterImage.Source = null;
                ActiveGameFallbackIcon.Visibility = Visibility.Visible;

                // Load artwork from profile if available
                if (game.UniversalProfile != null && !string.IsNullOrEmpty(game.UniversalProfile.HeaderUrl))
                {
                    try
                    {
                        ActiveGamePosterImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(game.UniversalProfile.HeaderUrl));
                        ActiveGameFallbackIcon.Visibility = Visibility.Collapsed;
                    }
                    catch { }
                }

                // If AppID > 0, fetch full official Steam artwork asynchronously
                if (game.AppId > 0)
                {
                    var targetAppId = game.AppId;
                    _ = Task.Run(async () =>
                    {
                        var store = await Services.SteamStoreClient.Shared.GetAppInfoAsync(new[] { targetAppId });
                        if (store.TryGetValue(targetAppId, out var info) && !string.IsNullOrEmpty(info.HeaderUrl))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (_currentActiveGame != null && _currentActiveGame.AppId == targetAppId)
                                {
                                    try
                                    {
                                        ActiveGamePosterImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(info.HeaderUrl));
                                        ActiveGameFallbackIcon.Visibility = Visibility.Collapsed;
                                    }
                                    catch { }
                                }
                            });
                        }
                    });
                }

                if (game.IsUniversal)
                {
                    if (game.IsGenuineOwned)
                    {
                        ActiveGameBadgeText.Text = S.Get("Dashboard_ActiveGame_BadgeGenuineNoCloud");
                        ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubGenuineNoCloud"), game.AppId);
                        ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillUniversal");
                    }
                    else
                    {
                        ActiveGameBadgeText.Text = S.Get("Dashboard_ActiveGame_BadgeUniversal");
                        ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubUniversal"), game.ProcessName);
                        ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillSafeMode");
                    }
                }
                else if (game.IsLuaGame)
                {
                    ActiveGameBadgeText.Text = S.Get("Dashboard_ActiveGame_BadgeLua");
                    ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubLua"), game.AppId);
                    ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillHooked");
                }
                else if (game.IsGenuineOwned && game.HasSteamCloud)
                {
                    ActiveGameBadgeText.Text = S.Get("Dashboard_ActiveGame_BadgeGenuine");
                    ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubGenuineSteam"), game.AppId);
                    ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillOriginalCloud");
                }
                else
                {
                    ActiveGameBadgeText.Text = S.Get("Dashboard_ActiveGame_BadgeSteam");
                    ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubSteam"), game.AppId);
                    ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillRunning");
                }
            }
            else
            {
                ActiveGameCard.Visibility = Visibility.Collapsed;
                ActiveGamePosterImage.Source = null;
                ActiveGameFallbackIcon.Visibility = Visibility.Visible;
            }

            // Trigger zoom recalculation when banner appears/disappears
            Services.UiZoomManager.Instance.TriggerAutoFitRecalculation();
        });
    }

    private void OpenGuide_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.InteractiveGuideDialog
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ShowDialog();
    }

    private void UniversalSavesCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mw)
        {
            mw.NavigateTo(typeof(UniversalSavesPage));
        }
    }

    private void UpdateProviderAuthStatus(Services.CloudConfig config, Services.TokenStatus preCheckedStatus)
    {
        if (config.IsLocal)
        {
            ProviderStatus.Text = config.DisplayName;
            ProviderIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CloudCheckmark24;
            return;
        }

        if (config.IsFolder)
        {
            if (config.SyncPath != null)
            {
                if (Directory.Exists(config.SyncPath))
                {
                    ProviderStatus.Text = S.Format("Dashboard_FolderAccessible", config.DisplayName);
                    ProviderIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CloudCheckmark24;
                }
                else
                {
                    ProviderStatus.Text = S.Format("Dashboard_FolderNotFound", config.DisplayName);
                    ProviderIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CloudDismiss24;
                }
            }
            else
            {
                ProviderStatus.Text = S.Format("Dashboard_NoSyncFolder", config.DisplayName);
                ProviderIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CloudOff24;
            }
            return;
        }

        // R2/S3: static credentials (no OAuth)
        if (config.Provider is "r2" or "s3")
        {
            bool hasCreds = config.TokenPath != null && File.Exists(config.TokenPath);
            ProviderStatus.Text = hasCreds
                ? S.Format("Dashboard_Authenticated", config.DisplayName)
                : S.Format("Dashboard_AuthStatus", config.DisplayName,
                    S.Get(config.Provider == "s3" ? "CloudProvider_S3CredMissing" : "CloudProvider_R2CredMissing"));
            ProviderIcon.Symbol = hasCreds
                ? Wpf.Ui.Controls.SymbolRegular.CloudCheckmark24
                : Wpf.Ui.Controls.SymbolRegular.CloudOff24;
            return;
        }

        // OAuth providers (gdrive, onedrive)
        if (config.TokenPath != null && preCheckedStatus != null)
        {
            ProviderStatus.Text = preCheckedStatus.IsAuthenticated
                ? S.Format("Dashboard_Authenticated", config.DisplayName)
                : S.Format("Dashboard_AuthStatus", config.DisplayName, preCheckedStatus.Message);
            ProviderIcon.Symbol = preCheckedStatus.IsAuthenticated
                ? Wpf.Ui.Controls.SymbolRegular.CloudCheckmark24
                : Wpf.Ui.Controls.SymbolRegular.CloudOff24;
        }
        else
        {
            ProviderStatus.Text = S.Format("Dashboard_NoTokenPath", config.DisplayName);
            ProviderIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CloudOff24;
        }
    }

    private async void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var logPath = Services.SteamDetector.GetLogPath();
        if (logPath != null && File.Exists(logPath))
        {
            // Open the containing folder with the log file highlighted, rather than
            // opening the (large) log in a text editor. /select takes the file path.
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{logPath}\"",
                UseShellExecute = true
            })?.Dispose();
        }
        else
        {
            await Services.Dialog.ShowInfoAsync(S.Get("Common_Info"),
                S.Get("Dashboard_LogNotFound"));
        }
    }

    private async void RestartSteam_Click(object sender, RoutedEventArgs e)
    {
        var steamPath = Services.SteamDetector.FindSteamPath();
        if (steamPath == null)
        {
            await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), S.Get("Dashboard_CouldNotFindSteam"));
            return;
        }

        var steamExe = Path.Combine(steamPath, "steam.exe");
        if (!File.Exists(steamExe))
        {
            await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), S.Get("Dashboard_SteamExeNotFound"));
            return;
        }

        if (!Services.SteamDetector.IsSteamRunning())
        {
            return;
        }

        var confirmed = await Services.Dialog.ConfirmAsync(S.Get("Dashboard_RestartSteam"),
            S.Get("Dashboard_RestartSteamPrompt"));

        if (!confirmed) return;

        var button = (Wpf.Ui.Controls.Button)sender;
        button.IsEnabled = false;
        var originalContent = button.Content;
        button.Content = S.Get("Dashboard_ShuttingDownSteam");

        try
        {
            // Ask Steam to shut down correctly
            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                Arguments = "-shutdown",
                UseShellExecute = true
            })?.Dispose();

            // Poll until Steam processes exit (up to 15 seconds)
            bool exited = await Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++) // 30 x 500ms = 15s
                {
                    await Task.Delay(500);
                    var procs = Process.GetProcessesByName("steam");
                    bool any = procs.Length > 0;
                    foreach (var p in procs) p.Dispose();
                    if (!any) return true;
                }
                return false;
            });

            if (!exited)
            {
                // Graceful shutdown didn't work -- offer force-kill
                var forceKill = await Services.Dialog.ConfirmAsync(S.Get("Dashboard_SteamStillRunning"),
                    S.Get("Dashboard_SteamStillRunningPrompt"));

                if (forceKill)
                {
                    button.Content = S.Get("Dashboard_ForceKilling");
                    await Task.Run(() =>
                    {
                        foreach (var proc in Process.GetProcessesByName("steam"))
                        {
                            try { proc.Kill(); }
                            catch { /* already exited */ }
                            finally { proc.Dispose(); }
                        }
                    });

                    // Brief wait for process table cleanup
                    await Task.Delay(1000);
                }
                else
                {
                    return; // User cancelled
                }
            }

            // Start Steam
            button.Content = S.Get("Dashboard_StartingSteam");
            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                UseShellExecute = true
            })?.Dispose();
        }
        catch (Exception ex)
        {
            await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), S.Format("Dashboard_FailedRestartSteam", ex.Message));
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
    }

    private async void UpdateDll_Click(object sender, RoutedEventArgs e)
    {
        if (_steamPath == null) return;

        UpdateBanner.Visibility = Visibility.Collapsed;

        try
        {
            // Shut down Steam if it's running
            var steamRunning = await Task.Run(() =>
            {
                var procs = Process.GetProcessesByName("steam");
                bool running = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                return running;
            });

            if (steamRunning)
            {
                DllStatus.Text = S.Get("Dashboard_ClosingSteam");

                await Task.Run(() =>
                {
                    var steamExe = Path.Combine(_steamPath, "steam.exe");
                    if (File.Exists(steamExe))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = steamExe,
                            Arguments = "-shutdown",
                            UseShellExecute = true
                        })?.Dispose();
                    }

                    // Wait up to 15s for graceful exit
                    for (int i = 0; i < 30; i++)
                    {
                        System.Threading.Thread.Sleep(500);
                        var check = Process.GetProcessesByName("steam");
                        bool any = check.Length > 0;
                        foreach (var p in check) p.Dispose();
                        if (!any) return;
                    }

                    // Force-kill stragglers
                    foreach (var p in Process.GetProcessesByName("steam"))
                    {
                        try { p.Kill(); } catch { }
                        finally { p.Dispose(); }
                    }
                });
            }

            DllStatus.Text = S.Get("Dashboard_Updating");

            var destPath = Path.Combine(_steamPath, "cloud_redirect.dll");
            var error = await Task.Run(() => Services.EmbeddedDll.DeployTo(destPath));

            if (error != null)
            {
                DllStatus.Text = S.Get("Dashboard_UpdateFailed");
                await Services.Dialog.ShowErrorAsync(S.Get("Common_UpdateFailed"), error);
                UpdateBanner.Visibility = Visibility.Visible;
            }
            else
            {
                DllStatus.Text = S.Get("Dashboard_DllInstalledUpdated");
                DllIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.PlugConnected24;

                if (steamRunning)
                {
                    var restart = await Services.Dialog.ConfirmAsync(S.Get("Dashboard_DllUpdatedTitle"),
                        S.Get("Dashboard_DllUpdatedRestartPrompt"));
                    if (restart)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = Path.Combine(_steamPath, "steam.exe"),
                            UseShellExecute = true
                        })?.Dispose();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), S.Format("Dashboard_FailedUpdateDll", ex.Message));
            UpdateBanner.Visibility = Visibility.Visible;
        }
    }

    private async void LuaBackupManual_Click(object sender, RoutedEventArgs e)
    {
        if (_steamPath == null) return;
        var btn = sender as Wpf.Ui.Controls.Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            var result = await Task.Run(() => Services.LuaSyncHelper.ManualBackup(_steamPath));
            if (result.Success)
            {
                await Services.Dialog.ShowInfoAsync("Lua Backup Successful",
                    $"Successfully backed up {result.FileCount} Lua script(s) to cloud storage.\nTimestamp: {DateTime.Now:t}");
            }
            else
            {
                await Services.Dialog.ShowWarningAsync("Lua Backup", result.Message);
            }
            await LoadStatusAsync();
        }
        catch (Exception ex)
        {
            await Services.Dialog.ShowErrorAsync("Lua Backup Failed", ex.Message);
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private async void LuaRestoreManual_Click(object sender, RoutedEventArgs e)
    {
        if (_steamPath == null) return;
        var btn = sender as Wpf.Ui.Controls.Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            var result = await Task.Run(() => Services.LuaSyncHelper.ManualRestore(_steamPath));
            if (result.Success)
            {
                await Services.Dialog.ShowInfoAsync("Lua Restore Successful",
                    $"Successfully restored {result.FileCount} Lua script(s) to config/stplug-in.\nTimestamp: {DateTime.Now:t}");
            }
            else
            {
                await Services.Dialog.ShowWarningAsync("Lua Restore", result.Message);
            }
            await LoadStatusAsync();
        }
        catch (Exception ex)
        {
            await Services.Dialog.ShowErrorAsync("Lua Restore Failed", ex.Message);
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private void CloudProviderCard_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(Pages.CloudProviderPage));
    }

    private void AppsSyncingCard_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(Pages.AppsPage));
    }

    private void CleanUpAction_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(Pages.CleanupPage));
    }

    private void StatsAction_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(Pages.StatsPage));
    }

    private void MigrationAction_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(Pages.MigrationPage));
    }

    private void SettingsAction_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(Pages.SettingsPage));
    }

    private void SuoLinkAction_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(Pages.RemotePlayPage));
    }

    private void LuaFilesCard_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(Pages.LuaSyncPage));
    }

    private void InitializeLanguageSelector()
    {
        _languageLoading = true;
        try
        {
            LanguageComboBox.Items.Clear();
            var currentCode = Services.LanguageService.ReadLanguagePreference();

            int selectedIndex = 0;
            var languages = Services.LanguageService.SupportedLanguages;
            for (int i = 0; i < languages.Length; i++)
            {
                var lang = languages[i];
                var itemText = lang.Code == "system"
                    ? S.Get(lang.ResourceKey)
                    : lang.DisplayName;

                LanguageComboBox.Items.Add(itemText);
                if (string.Equals(lang.Code, currentCode, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                }
            }

            LanguageComboBox.SelectedIndex = selectedIndex;
        }
        finally
        {
            _languageLoading = false;
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_languageLoading) return;

        var idx = LanguageComboBox.SelectedIndex;
        var languages = Services.LanguageService.SupportedLanguages;
        if (idx < 0 || idx >= languages.Length) return;

        var selectedCode = languages[idx].Code;
        Services.LanguageService.ApplyLanguage(selectedCode, save: true);
    }
}
