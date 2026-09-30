using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using CloudRedirect.Resources;

namespace CloudRedirect;

public partial class MainWindow : FluentWindow
{
    private Services.AppUpdater.CheckResult? _pendingUpdate;
    private System.Windows.Threading.DispatcherTimer? _autoUpdateTimer;
    public bool AppUpdateAvailable { get; private set; }

    public MainWindow()
    {
        App.LogStartup("MainWindow constructor started");
        Closing += (_, e) => App.LogStartup("MainWindow Closing event fired. Cancel=" + e.Cancel + " isExplicitExit=" + _isExplicitExit);
        Closed += (_, _) =>
        {
            App.LogStartup("MainWindow Closed event fired.");
        };

        InitializeComponent();
        App.LogStartup("MainWindow InitializeComponent finished");

        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (ver != null)
        {
            var title = $"CloudRedirect v{ver.Major}.{ver.Minor}.{ver.Build}";
            Title = title;
            if (AppTitleBar != null)
                AppTitleBar.Title = title;
        }

        var workArea = SystemParameters.WorkArea;
        if (Height > workArea.Height - 30)
            Height = Math.Max(MinHeight, workArea.Height - 40);
        if (Width > workArea.Width - 30)
            Width = Math.Max(MinWidth, workArea.Width - 40);

        Loaded += async (_, _) =>
        {
            App.LogStartup("MainWindow Loaded event fired");
            try
            {
                var stagedExe = Path.Combine(Services.SteamDetector.GetConfigDir(), "staged_update.exe");
                if (File.Exists(stagedExe) && !Services.AppUpdater.IsAnyGameRunning())
                {
                    App.LogStartup("stagedExe found! Calling ApplyStagedAndRelaunch");
                    Services.AppUpdater.ApplyStagedAndRelaunch(stagedExe);
                    return;
                }

                App.LogStartup("Loaded: Initializing TrayIconService");
                Services.TrayIconService.Instance.Initialize(this);

                App.LogStartup("Loaded: CheckForAutoUpdateAsync");
                _ = CheckForAutoUpdateAsync();

                // Periodic check for new releases every 3 minutes
                _autoUpdateTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(3)
                };
                _autoUpdateTimer.Tick += async (_, _) => await CheckForAutoUpdateAsync();
                _autoUpdateTimer.Start();

                // Auto-setup compatible unlock tools (OST, HubcapTools), deploy DLL and ensure default config
                App.LogStartup("Loaded: AutoSetupService.RunAutoSetupAsync");
                await Services.AutoSetupService.RunAutoSetupAsync();

                _ = Task.Run(() =>
                {
                    Services.SteamWebUiPatcher.EnsureStockIconRestored();
                });

                App.LogStartup("Loaded: ActiveGameTrackerService.Start");
                Services.ActiveGameTrackerService.Start();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Services.SteamGameScannerService.ScanInstalledSteamGamesAsync(autoEnroll: true);
                    }
                    catch { }
                });

                App.LogStartup("Loaded: UiZoomManager.Instance.Initialize");
                Services.UiZoomManager.Instance.Initialize(this, ContentAreaHost, RootFrame, GuiScaleTransform);
                Services.UiZoomManager.Instance.OnZoomChanged += (scale, isAuto) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        var autoLabel = S.Get("Zoom_Auto");
                        ZoomPercentText.Text = isAuto ? $"{autoLabel} ({Math.Round(scale * 100)}%)" : $"{Math.Round(scale * 100)}%";
                        ZoomPercentText.Foreground = new System.Windows.Media.SolidColorBrush(
                            isAuto ? System.Windows.Media.Color.FromRgb(0x66, 0xC0, 0xF4) : System.Windows.Media.Color.FromRgb(0xC6, 0xD4, 0xDF));
                        AutoFitBtn.Background = new System.Windows.Media.SolidColorBrush(
                            isAuto ? System.Windows.Media.Color.FromRgb(0x1C, 0x35, 0x4A) : System.Windows.Media.Colors.Transparent);
                        AutoFitBtn.Foreground = new System.Windows.Media.SolidColorBrush(
                            isAuto ? System.Windows.Media.Color.FromRgb(0xA4, 0xD0, 0x07) : System.Windows.Media.Color.FromRgb(0x8F, 0x98, 0xA0));
                    });
                };

                App.LogStartup("Loaded: SaveUploadWatcherService.Start");
                Services.SaveUploadWatcherService.Start();

                App.LogStartup("Loaded: GlobalHotkeyService.Instance.Initialize");
                Services.GlobalHotkeyService.Instance.Initialize(this);

                App.LogStartup("Loaded: MigrateLegacyMode");
                var mode = await Task.Run(() => MigrateLegacyMode());
                ApplyMode(mode);


                App.LogStartup("Loaded: NavigateTo DashboardPage");
                NavigateTo(typeof(Pages.DashboardPage));
                App.LogStartup("Loaded: DashboardPage navigation succeeded");

                if (App.StartMinimized)
                {
                    App.LogStartup("Loaded: MinimizeToTray because StartMinimized=true");
                    Services.TrayIconService.Instance.MinimizeToTray();
                }
            }
            catch (Exception ex)
            {
                App.LogStartup("MainWindow Loaded EXCEPTION: " + ex);
            }
        };

        Services.LanguageService.OnLanguageChanged += OnLanguageChanged;
    }

    private bool _isExplicitExit;
    private bool _isPromptingExit;

    public void ForceExit()
    {
        _isExplicitExit = true;
        Services.GlobalHotkeyService.Instance.Dispose();
        Services.TrayIconService.Instance.Dispose();
        Close();
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isExplicitExit)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (Services.AppSettings.MinimizeToTrayOnClose)
        {
            Services.TrayIconService.Instance.MinimizeToTray();
            return;
        }

        if (_isPromptingExit) return;
        _isPromptingExit = true;
        try
        {
            await PromptExitOrMinimizeAsync();
        }
        finally
        {
            _isPromptingExit = false;
        }
    }

    private async Task PromptExitOrMinimizeAsync()
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "CloudRedirect",
            Content = new System.Windows.Controls.StackPanel
            {
                Children =
                {
                    new System.Windows.Controls.TextBlock
                    {
                        Text = "Choose an option upon closing:",
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextFillColorPrimaryBrush"),
                        Margin = new Thickness(0, 0, 0, 8),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new System.Windows.Controls.TextBlock
                    {
                        Text = "• Minimize to Tray: CloudRedirect keeps running in the system tray to sync saves in the background.\n• Exit: Close and quit the application completely.",
                        FontSize = 12,
                        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextFillColorSecondaryBrush"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            },
            PrimaryButtonText = "Minimize to Tray",
            PrimaryButtonAppearance = ControlAppearance.Primary,
            SecondaryButtonText = "Exit",
            SecondaryButtonAppearance = ControlAppearance.Danger,
            CloseButtonText = "Cancel"
        };

        var result = await box.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            Services.TrayIconService.Instance.MinimizeToTray();
        }
        else if (result == Wpf.Ui.Controls.MessageBoxResult.Secondary)
        {
            ForceExit();
        }
    }

    private static string? MigrateLegacyMode()
    {
        var mode = Services.SteamDetector.ReadModeSetting();

        if (mode != "cloud_redirect")
        {
            try
            {
                Services.ModeService.PersistMode("cloud_redirect", cloudRedirectEnabled: true);
                mode = "cloud_redirect";
            }
            catch { }
        }

        Services.ModeService.SaveClientType("thirdparty");

        return mode;
    }

    public void ApplyMode(string? mode, string? clientType = null)
    {
    }

    /// <summary>True on first run of a new release version; writes a .news-seen marker.</summary>
    private static bool ShouldShowNews()
    {
        try
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(informational)) return false;

            var plus = informational.IndexOf('+');
            var version = plus >= 0 ? informational.Substring(0, plus) : informational;

            if (version.Contains("-TEST", StringComparison.OrdinalIgnoreCase)) return false;

            var markerPath = Path.Combine(Services.SteamDetector.GetConfigDir(), ".news-seen");

            if (File.Exists(markerPath))
            {
                var seen = File.ReadAllText(markerPath).Trim();
                if (seen == version) return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            File.WriteAllText(markerPath, version);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if the DLL isn't deployed or config.json doesn't exist yet.
    /// </summary>
    private static bool NeedsSetup()
    {
        var steamPath = Services.SteamDetector.FindSteamPath();
        if (steamPath == null) return true;

        if (!File.Exists(Path.Combine(steamPath, "cloud_redirect.dll")))
            return true;

        var configPath = Services.SteamDetector.GetConfigFilePath();
        if (!File.Exists(configPath))
            return true;

        return false;
    }

    /// <summary>
    /// Checks GitHub for a newer version. If found, automatically downloads
    /// and installs the update.
    /// </summary>
    private async Task CheckForAutoUpdateAsync()
    {
        try
        {
            var result = await Services.AppUpdater.CheckAsync();
            if (result == null || !result.UpdateAvailable || result.DownloadUrl == null)
                return;

            _pendingUpdate = result;
            var versionStr = result.TagName?.TrimStart('v') ?? result.TagName ?? "unknown";

            // Automatically download and install new update
            UpdateBannerTitle.Text = $"Updating CloudRedirect to v{versionStr}...";
            UpdateBannerStatus.Text = "Downloading update from GitHub...";
            UpdateNowButton.Visibility = Visibility.Collapsed;
            UpdateSkipButton.Visibility = Visibility.Collapsed;
            UpdateReleaseNotesButton.Visibility = Visibility.Collapsed;
            UpdateChangelogScroll.Visibility = Visibility.Collapsed;
            UpdateProgressBar.Visibility = Visibility.Visible;
            UpdateProgressBar.IsIndeterminate = true;

            AppUpdateAvailable = true;
            UpdateBanner.Visibility = Visibility.Visible;

            var error = await Services.AppUpdater.DownloadAndApplyAsync(
                result.DownloadUrl,
                (pct, status) => Dispatcher.Invoke(() =>
                {
                    UpdateBannerStatus.Text = status;
                    if (pct >= 0)
                    {
                        UpdateProgressBar.IsIndeterminate = false;
                        UpdateProgressBar.Value = pct;
                    }
                    else
                    {
                        UpdateProgressBar.IsIndeterminate = true;
                    }
                }));

            if (error != null)
            {
                UpdateBannerTitle.Text = $"Update to v{versionStr} failed";
                UpdateBannerStatus.Text = error;
                UpdateProgressBar.Visibility = Visibility.Collapsed;
                UpdateNowButton.Content = "Retry";
                UpdateNowButton.Visibility = Visibility.Visible;
                UpdateSkipButton.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            // Auto-update check failures are non-fatal
        }
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate?.DownloadUrl == null) return;

        var versionStr = _pendingUpdate.TagName?.TrimStart('v') ?? "unknown";

        // Switch banner to download mode
        UpdateNowButton.Visibility = Visibility.Collapsed;
        UpdateSkipButton.Visibility = Visibility.Collapsed;
        UpdateReleaseNotesButton.Visibility = Visibility.Collapsed;
        UpdateChangelogScroll.Visibility = Visibility.Collapsed;
        UpdateBannerStatus.Text = $"Downloading v{versionStr}...";
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressBar.IsIndeterminate = true;

        var error = await Services.AppUpdater.DownloadAndApplyAsync(
            _pendingUpdate.DownloadUrl,
            (pct, status) => Dispatcher.Invoke(() =>
            {
                UpdateBannerStatus.Text = status;
                if (pct >= 0)
                {
                    UpdateProgressBar.IsIndeterminate = false;
                    UpdateProgressBar.Value = pct;
                }
                else
                {
                    UpdateProgressBar.IsIndeterminate = true;
                }
            }));

        if (error != null)
        {
            UpdateBannerTitle.Text = "Update failed";
            UpdateBannerStatus.Text = error;
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateBanner.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x33, 0xC4, 0x2B, 0x1C));
            UpdateBanner.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C));
            UpdateNowButton.Visibility = Visibility.Visible;
            UpdateSkipButton.Visibility = Visibility.Visible;
        }
        // If successful, the process will have exited already
    }

    private void UpdateReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate?.HtmlUrl != null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _pendingUpdate.HtmlUrl,
                UseShellExecute = true
            });
        }
    }

    private void UpdateSkip_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
        _pendingUpdate = null;
    }

    public void NavigateTo(Type pageType)
    {
        if (RootFrame.Content?.GetType() == pageType) return;

        try
        {
            var page = Activator.CreateInstance(pageType);
            RootFrame.Navigate(page);

            bool isDashboard = pageType == typeof(Pages.DashboardPage);
            TopNavHeader.Visibility = isDashboard ? Visibility.Collapsed : Visibility.Visible;
            if (!isDashboard)
            {
                BackToDashboardBtn.Content = S.Get("Nav_BackToDashboard");
                CurrentPageTitle.Text = pageType.Name switch
                {
                    nameof(Pages.CloudProviderPage) => S.Get("Nav_CloudProvider"),
                    nameof(Pages.AppsPage) => S.Get("Nav_Apps"),
                    nameof(Pages.CleanupPage) => S.Get("Nav_Cleanup"),
                    nameof(Pages.StatsPage) => S.Get("Nav_Stats"),
                    nameof(Pages.MigrationPage) => S.Get("Nav_Migration"),
                    nameof(Pages.SettingsPage) => S.Get("Nav_Settings"),
                    nameof(Pages.UniversalSavesPage) => S.Get("Nav_UniversalSaves"),
                    nameof(Pages.LuaSyncPage) => S.Get("Nav_LuaSync"),
                    nameof(Pages.SuoRemotePage) => "SUO Remote",
                    _ => ""
                };
            }
        }
        catch (Exception ex)
        {
            App.LogStartup($"NavigateTo({pageType.Name}) failed: {ex}");
            _ = Services.Dialog.ShowErrorAsync("Navigation Error", $"Failed to open {pageType.Name}:\n{ex.Message}");
        }
    }

    private void BackToDashboard_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(typeof(Pages.DashboardPage));
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            AppTitleBar.Title = S.Get("MainWindow_TitleBar");
            Title = S.Get("MainWindow_Title");
            UpdateSkipButton.Content = S.Get("AppUpdate_Skip");
            UpdateNowButton.Content = S.Get("AppUpdate_UpdateNow");
            UpdateReleaseNotesButton.Content = S.Get("AppUpdate_ReleaseNotes");
            BackToDashboardBtn.Content = S.Get("Nav_BackToDashboard");
            Services.UiZoomManager.Instance.RefreshDisplay();

            RefreshCurrentPage();
        });
    }

    public void RefreshCurrentPage()
    {
        if (RootFrame.Content is Page currentPage)
        {
            var pageType = currentPage.GetType();
            var newPage = Activator.CreateInstance(pageType);
            RootFrame.Navigate(newPage);

            bool isDashboard = pageType == typeof(Pages.DashboardPage);
            TopNavHeader.Visibility = isDashboard ? Visibility.Collapsed : Visibility.Visible;
            if (!isDashboard)
            {
                BackToDashboardBtn.Content = S.Get("Nav_BackToDashboard");
                CurrentPageTitle.Text = pageType.Name switch
                {
                    nameof(Pages.CloudProviderPage) => S.Get("Nav_CloudProvider"),
                    nameof(Pages.AppsPage) => S.Get("Nav_Apps"),
                    nameof(Pages.CleanupPage) => S.Get("Nav_Cleanup"),
                    nameof(Pages.StatsPage) => S.Get("Nav_Stats"),
                    nameof(Pages.MigrationPage) => S.Get("Nav_Migration"),
                    nameof(Pages.SettingsPage) => S.Get("Nav_Settings"),
                    nameof(Pages.UniversalSavesPage) => S.Get("Nav_UniversalSaves"),
                    _ => ""
                };
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Services.GlobalHotkeyService.Instance.Dispose();
        base.OnClosed(e);
    }

    public void ShowRestartSteam()
    {
        // Button is always visible now; kept for callers.
    }

    private async void RestartSteamItem_Click(object sender, RoutedEventArgs e)
    {
        var steamPath = Services.SteamDetector.FindSteamPath();
        if (steamPath == null) return;

        var steamExe = Path.Combine(steamPath, "steam.exe");
        if (!File.Exists(steamExe)) return;

        // Graceful shutdown first
        var procs = Process.GetProcessesByName("steam");
        bool wasRunning = procs.Length > 0;
        foreach (var p in procs) p.Dispose();

        if (wasRunning)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                Arguments = "-shutdown",
                UseShellExecute = true
            })?.Dispose();

            // Wait up to 15s for Steam to close
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(500);
                var check = Process.GetProcessesByName("steam");
                bool still = check.Length > 0;
                foreach (var p in check) p.Dispose();
                if (!still) break;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                UseShellExecute = true
            })?.Dispose();
        }
        catch { }
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        Services.UiZoomManager.Instance.ZoomOut();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        Services.UiZoomManager.Instance.ZoomIn();
    }

    private void AutoFit_Click(object sender, RoutedEventArgs e)
    {
        Services.UiZoomManager.Instance.ToggleAutoFit();
    }

    private void ZoomPercent_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Services.UiZoomManager.Instance.ToggleAutoFit();
    }
}
