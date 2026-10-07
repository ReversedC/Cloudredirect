using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

            Services.SaveUploadWatcherService.Start();
            Services.SaveUploadWatcherService.OnSaveActivity += HandleSaveActivity;
            Services.ActiveGameTrackerService.OnActiveGameChanged += HandleActiveGameChanged;
            Services.GameBoostService.OnBoostStateChanged += HandleBoostStateChanged;
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
            Services.SaveUploadWatcherService.OnSaveActivity -= HandleSaveActivity;
            Services.ActiveGameTrackerService.OnActiveGameChanged -= HandleActiveGameChanged;
            Services.GameBoostService.OnBoostStateChanged -= HandleBoostStateChanged;
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer = null;
        };
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

                var configDir = Services.SteamDetector.GetConfigDir();
                var tokenPath = config?.TokenPath ?? Path.Combine(configDir, "gdrive_tokens.json");
                if (!File.Exists(tokenPath))
                {
                    var legacyGoogle = Path.Combine(configDir, "google_tokens.json");
                    if (File.Exists(legacyGoogle)) tokenPath = legacyGoogle;
                }
                tokenStatus = Services.OAuthService.CheckTokenStatus(tokenPath);

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

            var suo = await Services.SuoDetector.DetectAsync();
            SuoRemoteTopBtn.Visibility = (suo.IsInstalled || suo.IsOnline) ? Visibility.Visible : Visibility.Collapsed;
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
                ActiveGameBoostBadge.Visibility = Services.GameBoostService.IsBoostActive ? Visibility.Visible : Visibility.Collapsed;
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

                // Styling brushes
                var greenBrush = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
                var greenBg = new SolidColorBrush(Color.FromRgb(0x19, 0x2A, 0x1A));
                var greenBorder = new SolidColorBrush(Color.FromRgb(0x4C, 0x78, 0x15));
                var greenPillBg = new SolidColorBrush(Color.FromRgb(0x14, 0x2B, 0x1A));
                var greenPillBorder = new SolidColorBrush(Color.FromRgb(0x3D, 0x68, 0x1C));

                var cyanBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
                var cyanBg = new SolidColorBrush(Color.FromRgb(0x13, 0x24, 0x33));
                var cyanBorder = new SolidColorBrush(Color.FromRgb(0x1A, 0x5C, 0x88));
                var cyanPillBg = new SolidColorBrush(Color.FromRgb(0x11, 0x2B, 0x3D));
                var cyanPillBorder = new SolidColorBrush(Color.FromRgb(0x1E, 0x6B, 0x9E));

                var amberBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));
                var amberBg = new SolidColorBrush(Color.FromRgb(0x2B, 0x20, 0x0F));
                var amberBorder = new SolidColorBrush(Color.FromRgb(0x8A, 0x55, 0x12));
                var amberPillBg = new SolidColorBrush(Color.FromRgb(0x38, 0x24, 0x0A));
                var amberPillBorder = new SolidColorBrush(Color.FromRgb(0xA8, 0x6E, 0x18));

                ActiveGameFixSyncButton.Visibility = Visibility.Collapsed;

                if (game.IsCloudDenied || (!game.IsGenuineOwned && !game.IsFreeGame && !game.IsLuaGame && !game.IsZeroLuaIntercepted))
                {
                    // Blocked / Unlocked game without interception!
                    ActiveGameCard.Background = amberBg;
                    ActiveGameCard.BorderBrush = amberBorder;
                    ActiveGameArtworkBorder.Background = amberBg;
                    ActiveGameArtworkBorder.BorderBrush = amberBorder;
                    ActiveGameFallbackIcon.Foreground = amberBrush;
                    ActiveGameBadgeDot.Fill = amberBrush;
                    ActiveGameBadgeText.Foreground = amberBrush;
                    ActiveGameStatusBorder.Background = amberPillBg;
                    ActiveGameStatusBorder.BorderBrush = amberPillBorder;
                    ActiveGameStatusPill.Foreground = amberBrush;

                    ActiveGameBadgeText.Text = S.Get("Dashboard_ActiveGame_BadgeCloudDenied");
                    ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubCloudDenied"), game.AppId);
                    ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillCloudDenied");

                    ActiveGameFixSyncButton.Visibility = Visibility.Visible;
                }
                else if (game.IsZeroLuaIntercepted)
                {
                    // Zero-Lua Intercepted Game
                    ActiveGameCard.Background = cyanBg;
                    ActiveGameCard.BorderBrush = cyanBorder;
                    ActiveGameArtworkBorder.Background = cyanBg;
                    ActiveGameArtworkBorder.BorderBrush = cyanBorder;
                    ActiveGameFallbackIcon.Foreground = cyanBrush;
                    ActiveGameBadgeDot.Fill = cyanBrush;
                    ActiveGameBadgeText.Foreground = cyanBrush;
                    ActiveGameStatusBorder.Background = cyanPillBg;
                    ActiveGameStatusBorder.BorderBrush = cyanPillBorder;
                    ActiveGameStatusPill.Foreground = cyanBrush;

                    ActiveGameBadgeText.Text = S.Get("Dashboard_ActiveGame_BadgeZeroLua");
                    ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubZeroLua"), game.AppId);
                    ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillHooked");
                }
                else if (game.IsUniversal)
                {
                    ActiveGameCard.Background = greenBg;
                    ActiveGameCard.BorderBrush = greenBorder;
                    ActiveGameArtworkBorder.Background = greenBg;
                    ActiveGameArtworkBorder.BorderBrush = greenBorder;
                    ActiveGameFallbackIcon.Foreground = greenBrush;
                    ActiveGameBadgeDot.Fill = greenBrush;
                    ActiveGameBadgeText.Foreground = greenBrush;
                    ActiveGameStatusBorder.Background = greenPillBg;
                    ActiveGameStatusBorder.BorderBrush = greenPillBorder;
                    ActiveGameStatusPill.Foreground = greenBrush;

                    if (game.IsGenuineOwned || game.IsFreeGame)
                    {
                        ActiveGameBadgeText.Text = game.IsFreeGame
                            ? S.Get("Dashboard_ActiveGame_BadgeFreeNoCloud")
                            : S.Get("Dashboard_ActiveGame_BadgeGenuineNoCloud");
                        ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubGenuineNoCloud"), game.AppId);
                        ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillCRInactive");
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
                    ActiveGameCard.Background = greenBg;
                    ActiveGameCard.BorderBrush = greenBorder;
                    ActiveGameArtworkBorder.Background = greenBg;
                    ActiveGameArtworkBorder.BorderBrush = greenBorder;
                    ActiveGameFallbackIcon.Foreground = greenBrush;
                    ActiveGameBadgeDot.Fill = greenBrush;
                    ActiveGameBadgeText.Foreground = greenBrush;
                    ActiveGameStatusBorder.Background = greenPillBg;
                    ActiveGameStatusBorder.BorderBrush = greenPillBorder;
                    ActiveGameStatusPill.Foreground = greenBrush;

                    ActiveGameBadgeText.Text = S.Get("Dashboard_ActiveGame_BadgeLua");
                    ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubLua"), game.AppId);
                    ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillHooked");
                }
                else if (game.IsGenuineOwned || game.IsFreeGame)
                {
                    ActiveGameCard.Background = greenBg;
                    ActiveGameCard.BorderBrush = greenBorder;
                    ActiveGameArtworkBorder.Background = greenBg;
                    ActiveGameArtworkBorder.BorderBrush = greenBorder;
                    ActiveGameFallbackIcon.Foreground = greenBrush;
                    ActiveGameBadgeDot.Fill = greenBrush;
                    ActiveGameBadgeText.Foreground = greenBrush;
                    ActiveGameStatusBorder.Background = greenPillBg;
                    ActiveGameStatusBorder.BorderBrush = greenPillBorder;
                    ActiveGameStatusPill.Foreground = greenBrush;

                    if (game.HasSteamCloud)
                    {
                        ActiveGameBadgeText.Text = game.IsFreeGame
                            ? S.Get("Dashboard_ActiveGame_BadgeFreeGame")
                            : S.Get("Dashboard_ActiveGame_BadgeGenuine");
                        ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubGenuineSteam"), game.AppId);
                        ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillOriginalCloud");
                    }
                    else
                    {
                        ActiveGameBadgeText.Text = game.IsFreeGame
                            ? S.Get("Dashboard_ActiveGame_BadgeFreeNoCloud")
                            : S.Get("Dashboard_ActiveGame_BadgeGenuineNoCloud");
                        ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubGenuineNoCloud"), game.AppId);
                        ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillCRInactive");
                    }
                }
                else
                {
                    ActiveGameCard.Background = greenBg;
                    ActiveGameCard.BorderBrush = greenBorder;
                    ActiveGameArtworkBorder.Background = greenBg;
                    ActiveGameArtworkBorder.BorderBrush = greenBorder;
                    ActiveGameFallbackIcon.Foreground = greenBrush;
                    ActiveGameBadgeDot.Fill = greenBrush;
                    ActiveGameBadgeText.Foreground = greenBrush;
                    ActiveGameStatusBorder.Background = greenPillBg;
                    ActiveGameStatusBorder.BorderBrush = greenPillBorder;
                    ActiveGameStatusPill.Foreground = greenBrush;

                    ActiveGameBadgeText.Text = S.Get("Dashboard_ActiveGame_BadgeSteam");
                    ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubSteam"), game.AppId);
                    ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillRunning");
                }
            }
            else
            {
                ActiveGameCard.Visibility = Visibility.Collapsed;
                ActiveGameBoostBadge.Visibility = Visibility.Collapsed;
                ActiveGamePosterImage.Source = null;
                ActiveGameFallbackIcon.Visibility = Visibility.Visible;
            }

            // Trigger zoom recalculation when banner appears/disappears
            Services.UiZoomManager.Instance.TriggerAutoFitRecalculation();
        });
    }

    private void HandleBoostStateChanged(bool isBoosted, string? gameName)
    {
        Dispatcher.Invoke(() =>
        {
            ActiveGameBoostBadge.Visibility = (isBoosted && _currentActiveGame != null) ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private async void FixSync_Click(object sender, RoutedEventArgs e)
    {
        if (_currentActiveGame == null || _currentActiveGame.AppId == 0) return;
        var appId = _currentActiveGame.AppId;
        var gameName = _currentActiveGame.Name;

        try
        {
            // 1. Add to Zero-Lua intercept list (cloud_redirect/intercept_apps.txt and config.json)
            Services.SteamDetector.AddInterceptApp(appId);

            // 2. Fix remotecache.vdf syncstate (3 -> 1) to clear error
            Services.SteamDetector.FixRemoteCacheSyncState(appId);

            // 3. Auto-enroll in Universal Save Watcher if not already enrolled
            var steamPath = Services.SteamDetector.FindSteamPath();
            var installDir = steamPath != null ? Services.AppCloudConfig.FindGameInstallDir(steamPath, appId) : null;
            var saveFolder = Services.GameSaveAutoDetector.DetectSaveFolder(gameName, _currentActiveGame.ProcessName, appId);
            if (saveFolder != null)
            {
                Services.UniversalSaveWatcherService.AutoEnrollIfNeeded(
                    gameName, _currentActiveGame.ProcessName, appId, saveFolder, _currentActiveGame.HasAntiCheat, isGenuine: false);
            }

            // 4. Update UI immediately
            ActiveGameFixSyncButton.Visibility = Visibility.Collapsed;
            var cyanBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
            var cyanBg = new SolidColorBrush(Color.FromRgb(0x13, 0x24, 0x33));
            var cyanBorder = new SolidColorBrush(Color.FromRgb(0x1A, 0x5C, 0x88));
            var cyanPillBg = new SolidColorBrush(Color.FromRgb(0x11, 0x2B, 0x3D));
            var cyanPillBorder = new SolidColorBrush(Color.FromRgb(0x1E, 0x6B, 0x9E));

            ActiveGameCard.Background = cyanBg;
            ActiveGameCard.BorderBrush = cyanBorder;
            ActiveGameArtworkBorder.Background = cyanBg;
            ActiveGameArtworkBorder.BorderBrush = cyanBorder;
            ActiveGameFallbackIcon.Foreground = cyanBrush;
            ActiveGameBadgeDot.Fill = cyanBrush;
            ActiveGameBadgeText.Foreground = cyanBrush;
            ActiveGameStatusBorder.Background = cyanPillBg;
            ActiveGameStatusBorder.BorderBrush = cyanPillBorder;
            ActiveGameStatusPill.Foreground = cyanBrush;

            ActiveGameBadgeText.Text = S.Get("Dashboard_ActiveGame_BadgeZeroLua");
            ActiveGameSubtitle.Text = string.Format(S.Get("Dashboard_ActiveGame_SubZeroLua"), appId);
            ActiveGameStatusPill.Text = S.Get("Dashboard_ActiveGame_PillHooked");

            var restartPrompt = S.Format("Dashboard_ActiveGame_FixSuccessPrompt", gameName);
            var confirmRestart = await Services.Dialog.ConfirmAsync(S.Get("Dashboard_ActiveGame_FixSuccessTitle"), restartPrompt);
            if (confirmRestart)
            {
                await RestartSteamClientAsync();
            }
        }
        catch (Exception ex)
        {
            await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), ex.Message);
        }
    }

    private async Task RestartSteamClientAsync()
    {
        await Services.SteamDetector.RestartSteamAsync();
    }

    private void OpenGuide_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.InteractiveGuideDialog
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ShowDialog();
    }

    private void SuoRemote_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mw)
        {
            mw.NavigateTo(typeof(SuoRemotePage));
        }
    }

    private void SetupWizardCard_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mw)
        {
            mw.NavigateTo(typeof(SetupWizardPage));
        }
    }

    private void UniversalSavesCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mw)
        {
            mw.NavigateTo(typeof(UniversalSavesPage));
        }
    }

    private void UniversalSavesAction_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mw)
        {
            mw.NavigateTo(typeof(UniversalSavesPage));
        }
    }

    private void UpdateProviderAuthStatus(Services.CloudConfig config, Services.TokenStatus? preCheckedStatus)
    {
        if (preCheckedStatus != null && preCheckedStatus.IsAuthenticated)
        {
            ProviderStatus.Text = S.Format("Dashboard_Authenticated", "Google Drive");
            ProviderIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CloudCheckmark24;
            ProviderIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xA4, 0xD0, 0x07));
        }
        else
        {
            ProviderStatus.Text = preCheckedStatus != null ? preCheckedStatus.Message : S.Get("CloudProvider_NoTokensLoaded");
            ProviderIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CloudOff24;
            ProviderIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xDF, 0x56, 0x48));
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

        var confirmed = await Services.Dialog.ConfirmAsync(S.Get("Dashboard_RestartSteam"),
            S.Get("Dashboard_RestartSteamPrompt"));

        if (!confirmed) return;

        var button = (Wpf.Ui.Controls.Button)sender;
        button.IsEnabled = false;
        var originalContent = button.Content;

        try
        {
            if (Services.SteamDetector.IsSteamRunning())
            {
                button.Content = S.Get("Dashboard_ShuttingDownSteam");
                await Services.SteamDetector.StopSteamAsync(steamPath, timeoutSeconds: 6);
                await Task.Delay(1000);
            }

            button.Content = S.Get("Dashboard_StartingSteam");
            bool started = await Task.Run(() => Services.SteamDetector.StartSteam(steamPath));
            if (!started)
            {
                await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), S.Get("Dashboard_SteamExeNotFound"));
            }
            else
            {
                await Task.Delay(1500);
            }
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
            var steamRunning = Services.SteamDetector.IsSteamRunning();
            if (steamRunning)
            {
                DllStatus.Text = S.Get("Dashboard_ClosingSteam");
                await Services.SteamDetector.StopSteamAsync(_steamPath, timeoutSeconds: 6);
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
                        Services.SteamDetector.StartSteam(_steamPath);
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

    private void LogLuaTerminal(string message, int? percent = null)
    {
        Dispatcher.Invoke(() =>
        {
            LuaSyncProgressGrid.Visibility = Visibility.Visible;
            LuaMiniTerminalBorder.Visibility = Visibility.Visible;
            LuaToggleLogBtn.Content = S.Get("Dashboard_HideLog");

            var timeStamp = DateTime.Now.ToString("HH:mm:ss");
            LuaTerminalLogText.Text += $"[{timeStamp}] {message}\n";
            LuaTerminalScrollViewer.ScrollToEnd();

            LuaSyncStatusText.Text = message;
            if (percent.HasValue)
            {
                LuaSyncProgressBar.IsIndeterminate = false;
                LuaSyncProgressBar.Value = Math.Clamp(percent.Value, 0, 100);
                LuaSyncPercentText.Text = $"{LuaSyncProgressBar.Value}%";
            }
            else
            {
                LuaSyncProgressBar.IsIndeterminate = true;
                LuaSyncPercentText.Text = "";
            }
        });
    }

    private void SetLuaSyncButtonsEnabled(bool enabled)
    {
        LuaBackupBtn.IsEnabled = enabled;
        LuaFetchBtn.IsEnabled = enabled;
        LuaRestoreBtn.IsEnabled = enabled;
    }

    private void LuaToggleLog_Click(object sender, RoutedEventArgs e)
    {
        if (LuaMiniTerminalBorder.Visibility == Visibility.Visible)
        {
            LuaMiniTerminalBorder.Visibility = Visibility.Collapsed;
            LuaToggleLogBtn.Content = S.Get("Dashboard_TerminalLog");
        }
        else
        {
            LuaMiniTerminalBorder.Visibility = Visibility.Visible;
            LuaToggleLogBtn.Content = S.Get("Dashboard_HideLog");
        }
    }

    private async void LuaBackupManual_Click(object sender, RoutedEventArgs e)
    {
        if (_steamPath == null) return;
        SetLuaSyncButtonsEnabled(false);
        LuaTerminalLogText.Text = "";
        LogLuaTerminal("Starting manual Lua backup...", 0);

        try
        {
            var result = await Services.LuaSyncHelper.ManualBackupAsync(_steamPath, (msg, pct) => LogLuaTerminal(msg, pct));
            if (result.Success)
            {
                LogLuaTerminal($"Backup finished: {result.FileCount} script(s) saved.", 100);
            }
            else
            {
                LogLuaTerminal($"Backup warning: {result.Message}", 100);
            }
            await LoadStatusAsync();
        }
        catch (Exception ex)
        {
            LogLuaTerminal($"Backup error: {ex.Message}", 100);
        }
        finally
        {
            SetLuaSyncButtonsEnabled(true);
        }
    }

    private async void LuaFetchManual_Click(object sender, RoutedEventArgs e)
    {
        if (_steamPath == null) return;
        SetLuaSyncButtonsEnabled(false);
        LuaTerminalLogText.Text = "";
        LogLuaTerminal("Starting cloud fetch...", 0);

        try
        {
            var result = await Services.LuaSyncHelper.ManualFetchAsync(_steamPath, (msg, pct) => LogLuaTerminal(msg, pct));
            if (result.Success)
            {
                LogLuaTerminal($"Cloud fetch finished: {result.FileCount} script(s) available in cloud.", 100);
            }
            else
            {
                LogLuaTerminal($"Cloud fetch: {result.Message}", 100);
            }
            await LoadStatusAsync();
        }
        catch (Exception ex)
        {
            LogLuaTerminal($"Cloud fetch error: {ex.Message}", 100);
        }
        finally
        {
            SetLuaSyncButtonsEnabled(true);
        }
    }

    private async void LuaRestoreManual_Click(object sender, RoutedEventArgs e)
    {
        if (_steamPath == null) return;
        SetLuaSyncButtonsEnabled(false);
        LuaTerminalLogText.Text = "";
        LogLuaTerminal("Starting manual Lua restore...", 0);

        try
        {
            var result = await Services.LuaSyncHelper.ManualRestoreAsync(_steamPath, (msg, pct) => LogLuaTerminal(msg, pct));
            if (result.Success)
            {
                LogLuaTerminal($"Restore finished: {result.FileCount} script(s) placed into stplug-in.", 100);
            }
            else
            {
                LogLuaTerminal($"Restore warning: {result.Message}", 100);
            }
            await LoadStatusAsync();
        }
        catch (Exception ex)
        {
            LogLuaTerminal($"Restore error: {ex.Message}", 100);
        }
        finally
        {
            SetLuaSyncButtonsEnabled(true);
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

    private void ChangelogAction_Click(object sender, RoutedEventArgs e)
    {
        var win = Window.GetWindow(this) ?? Application.Current.MainWindow;
        var dialog = new Dialogs.UpdateChangelogDialog
        {
            Owner = win
        };
        dialog.ShowDialog();
    }

    private void SettingsAction_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(Pages.SettingsPage));
    }


    private void LuaFilesCard_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(Pages.LuaSyncPage));
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as Wpf.Ui.Controls.Button;
        if (btn != null) btn.IsEnabled = false;

        try
        {
            ActivityProgressBar.Visibility = Visibility.Visible;
            ActivityStatusText.Text = S.Get("Dashboard_Uploading");
            ActivityIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowSync24;

            var tasks = new List<Task>();
            if (_steamPath != null)
            {
                tasks.Add(Task.Run(() => Services.LuaSyncHelper.ManualBackup(_steamPath)));
            }

            // Sync all universal out-of-process game saves (Minecraft, Elden Ring, etc.)
            tasks.Add(Services.UniversalCloudSyncService.SyncAllProfilesAsync());

            await Task.WhenAll(tasks);
            await LoadStatusAsync();
        }
        catch { }
        finally
        {
            ActivityProgressBar.Visibility = Visibility.Collapsed;
            if (btn != null) btn.IsEnabled = true;
        }
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

                var cbi = new ComboBoxItem
                {
                    Content = itemText,
                    Tag = lang.Code,
                    FontSize = 12,
                    Padding = new Thickness(6, 4, 12, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };

                LanguageComboBox.Items.Add(cbi);
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

        if (LanguageComboBox.SelectedItem is ComboBoxItem cbi && cbi.Tag is string code)
        {
            Services.LanguageService.ApplyLanguage(code, save: true);
        }
        else
        {
            var idx = LanguageComboBox.SelectedIndex;
            var languages = Services.LanguageService.SupportedLanguages;
            if (idx >= 0 && idx < languages.Length)
            {
                Services.LanguageService.ApplyLanguage(languages[idx].Code, save: true);
            }
        }
    }
}
