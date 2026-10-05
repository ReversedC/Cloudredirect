using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudRedirect.Models;
using CloudRedirect.Services;
using Microsoft.Web.WebView2.Core;

namespace CloudRedirect.Windows;

public partial class GameSpaceOverlayWindow : Window
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly DispatcherTimer _updateTimer;
    private readonly DispatcherTimer _forceKillResetTimer;

    private bool _isClosing;
    private string? _currentGameName;
    private bool _forceKillPending;

    public GameSpaceOverlayWindow()
    {
        InitializeComponent();

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _updateTimer.Tick += UpdateTimer_Tick;

        StickyNotesService.OnNotesChanged += () =>
        {
            Dispatcher.Invoke(RefreshStickyNotesList);
        };

        _forceKillResetTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _forceKillResetTimer.Tick += (_, _) =>
        {
            _forceKillResetTimer.Stop();
            ResetForceKillButton();
        };

        BackgroundAppFreezer.FreezeStateChanged += OnFreezeStateChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int currentStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, currentStyle | WS_EX_TOOLWINDOW);
        }
    }

    public void ShowOverlay()
    {
        _isClosing = false;
        var workArea = SystemParameters.WorkArea;
        Width = 390;
        DrawerBorder.Width = 390;
        Left = workArea.Right - 390;
        Top = workArea.Top;
        Height = workArea.Height;

        RefreshGameContext();
        RefreshTelemetryStats();
        RefreshRecentSnapshots();
        UpdateFreezeButtonState();
        UpdateAntiLagButtonState();

        Show();
        Activate();
        Topmost = true;

        _updateTimer.Start();

        // Animate slide-in from right
        DrawerBorder.Opacity = 0.0;
        DrawerTransform.X = 80;

        var slideIn = new DoubleAnimation(80, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180));

        DrawerTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
        DrawerBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    public void HideOverlay()
    {
        if (_isClosing) return;
        _isClosing = true;
        _updateTimer.Stop();

        var slideOut = new DoubleAnimation(0, 80, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
        };
        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(140));

        slideOut.Completed += (_, _) =>
        {
            Hide();
            _isClosing = false;
        };

        DrawerTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        DrawerBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void RefreshGameContext()
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game != null)
        {
            _currentGameName = game.Name;
            GameTitleText.Text = game.Name;
            BoostActiveBadge.Visibility = GameBoostService.IsBoostActive ? Visibility.Visible : Visibility.Collapsed;
            ForceKillContainer.Visibility = Visibility.Visible;

            // Load header poster if available
            if (!string.IsNullOrEmpty(game.HeaderUrl))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(game.HeaderUrl);
                    bi.EndInit();
                    GamePosterImage.Source = bi;
                    GamePosterFallback.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    GamePosterFallback.Visibility = Visibility.Visible;
                }
            }
            else
            {
                GamePosterFallback.Visibility = Visibility.Visible;
            }

            // Load game sticky notes
            RefreshStickyNotesList();
        }
        else
        {
            _currentGameName = "Desktop";
            GameTitleText.Text = "Standby (No Game Running)";
            GameTimerText.Text = "⏱️ Ready for game launch";
            BoostActiveBadge.Visibility = Visibility.Collapsed;
            ForceKillContainer.Visibility = Visibility.Collapsed;
            GamePosterFallback.Visibility = Visibility.Visible;
            GamePosterImage.Source = null;
            RefreshStickyNotesList();
        }

        ResetForceKillButton();
        UpdateAntiLagButtonState();
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game != null)
        {
            var elapsed = DateTime.Now - game.StartTime;
            GameTimerText.Text = $"⏱️ {elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            BoostActiveBadge.Visibility = GameBoostService.IsBoostActive ? Visibility.Visible : Visibility.Collapsed;
            ForceKillContainer.Visibility = Visibility.Visible;
        }
        else
        {
            ForceKillContainer.Visibility = Visibility.Collapsed;
        }

        RefreshTelemetryStats();
    }

    private void RefreshTelemetryStats()
    {
        var data = SystemTelemetryService.ReadTelemetry();

        // 1. CPU Telemetry (Auto-fitted by Viewbox)
        CpuPercentText.Text = $"{data.CpuPercent:0}%";
        CpuTempText.Text = $"🌡️ {data.CpuTempC}°C";

        // Color thermal indicator based on safety limits
        if (data.CpuTempC >= 85)
            CpuTempText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55)); // Hot Red
        else if (data.CpuTempC >= 75)
            CpuTempText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x30)); // Amber
        else
            CpuTempText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)); // Normal Green

        // 2. GPU Telemetry (Auto-fitted by Viewbox)
        GpuPercentText.Text = $"{data.GpuPercent:0}%";
        GpuTempText.Text = $"🌡️ {data.GpuTempC}°C";

        if (data.GpuTempC >= 83)
            GpuTempText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55));
        else if (data.GpuTempC >= 75)
            GpuTempText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x30));
        else
            GpuTempText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));

        // 3. RAM Telemetry (Auto-fitted by Viewbox)
        RamPercentValueText.Text = $"{data.RamPercent:0}%";
        RamUsageDetailedText.Text = $"{data.RamUsedGb:F1} / {data.RamTotalGb:0} GB";

        RamProgressBar.Value = data.RamPercent;
        if (data.RamPercent > 85)
            RamProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x30)); // Steam Amber
        else
            RamProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)); // Steam Cyan

        PowerPlanText.Text = GameBoostService.IsBoostActive ? "🚀 Ultimate Performance" : "High Performance";
    }

    private void RefreshRecentSnapshots()
    {
        RecentSnapshotsPanel.Children.Clear();
        RecentSnapshotsPanel.Children.Add(new TextBlock
        {
            Text = "Recent Checkpoints:",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0)),
            Margin = new Thickness(0, 2, 0, 4)
        });

        var game = ActiveGameTrackerService.CurrentGame;
        if (game == null)
        {
            RecentSnapshotsPanel.Children.Add(new TextBlock
            {
                Text = "No active game running.",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x82)),
                FontStyle = FontStyles.Italic
            });
            return;
        }

        var appIdStr = game.AppId > 0 ? game.AppId.ToString() : null;
        var snapshots = SaveHistoryManager.GetSnapshots(game.Name, appIdStr);

        if (snapshots.Count == 0)
        {
            RecentSnapshotsPanel.Children.Add(new TextBlock
            {
                Text = "No save snapshots recorded yet.",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x82)),
                FontStyle = FontStyles.Italic
            });
            return;
        }

        int count = Math.Min(3, snapshots.Count);
        for (int i = 0; i < count; i++)
        {
            var snap = snapshots[i];
            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x16, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1D, 0x2E, 0x40)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(snap.Trigger) ? "Checkpoint" : snap.Trigger,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var timeDiff = DateTime.Now - snap.Timestamp;
            string timeStr = timeDiff.TotalMinutes < 1 ? "Just now" :
                             timeDiff.TotalHours < 1 ? $"{(int)timeDiff.TotalMinutes}m ago" :
                             timeDiff.TotalDays < 1 ? $"{(int)timeDiff.TotalHours}h ago" :
                             snap.Timestamp.ToString("MMM dd HH:mm");

            infoPanel.Children.Add(new TextBlock
            {
                Text = $"{timeStr} • {snap.FileCount} file(s) ({FormatBytes(snap.TotalBytes)})",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0)),
                Margin = new Thickness(0, 2, 0, 0)
            });

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            var restoreBtn = new Button
            {
                Content = "↺ Restore",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x36, 0x50)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x54, 0x7E)),
                BorderThickness = new Thickness(1)
            };
            restoreBtn.Click += (_, _) =>
            {
                var confirm = MessageBox.Show(
                    $"Roll back {game.Name} save to this checkpoint?\n({snap.Trigger} - {timeStr})",
                    "Game Space Restore",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    if (GameSpaceService.RestoreCheckpoint(game.Name, snap, out var err))
                    {
                        SteamToastService.ShowAuto(
                            "Save Restored ↺",
                            $"{game.Name} rolled back to {timeStr} checkpoint successfully!");
                    }
                    else
                    {
                        SteamToastService.ShowAuto(
                            "Restore Failed ⚠️",
                            err ?? "Could not restore checkpoint.");
                    }
                }
            };

            Grid.SetColumn(restoreBtn, 1);
            grid.Children.Add(restoreBtn);

            row.Child = grid;
            RecentSnapshotsPanel.Children.Add(row);
        }
    }

    private void OptimizePingBt_Click(object sender, RoutedEventArgs e)
    {
        bool currentState = AppSettings.GameBoostNetworkBoost || AppSettings.GameBoostBluetoothBoost;
        bool newState = !currentState;

        AppSettings.GameBoostNetworkBoost = newState;
        AppSettings.GameBoostBluetoothBoost = newState;

        if (newState)
        {
            GameBoostService.ApplyNetworkBoost();
            GameBoostService.ApplyBluetoothBoost();
            SteamToastService.ShowAuto(
                "Anti-Lag Active 🚀",
                "Network throttling disabled (0ms) & Bluetooth low-latency active!");
        }
        else
        {
            GameBoostService.RevertNetworkBoost();
            GameBoostService.RevertBluetoothBoost();
            SteamToastService.ShowAuto(
                "Anti-Lag Off ⏸️",
                "Restored default Windows network and Bluetooth settings.");
        }

        UpdateAntiLagButtonState();
    }

    private async void PurgeRam_Click(object sender, RoutedEventArgs e)
    {
        PurgeRamBtn.IsEnabled = false;

        var game = ActiveGameTrackerService.CurrentGame;
        int gamePid = 0;
        if (game != null && !string.IsNullOrEmpty(game.ProcessName))
        {
            var p = Process.GetProcessesByName(game.ProcessName);
            if (p.Length > 0) gamePid = p[0].Id;
        }

        long freed = await Task.Run(() => GameBoostService.PurgeMemory(gamePid));
        RefreshTelemetryStats();

        double freedMb = Math.Round((double)freed / (1024 * 1024), 0);
        if (freedMb > 0)
        {
            RamReclaimedText.Text = $"✓ Reclaimed +{freedMb:N0} MB Memory!";
        }
        else
        {
            RamReclaimedText.Text = "✓ Standby Cache Trimmed Clean!";
        }

        RamReclaimedPill.Visibility = Visibility.Visible;
        PurgeRamBtn.IsEnabled = true;

        var pillTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        pillTimer.Tick += (_, _) =>
        {
            pillTimer.Stop();
            RamReclaimedPill.Visibility = Visibility.Collapsed;
        };
        pillTimer.Start();
    }

    private void FreezeBgApps_Click(object sender, RoutedEventArgs e)
    {
        FreezeBgAppsBtn.IsEnabled = false;

        try
        {
            if (BackgroundAppFreezer.IsFrozen)
            {
                int thawed = BackgroundAppFreezer.ThawBackgroundApps();
                FreezeFeedbackText.Text = $"✓ Thawed {thawed} apps (Normal Priority)";
                FreezeFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
            }
            else
            {
                (int frozenCount, long reclaimedMb) = BackgroundAppFreezer.FreezeBackgroundApps();
                FreezeFeedbackText.Text = $"✓ Frozen {frozenCount} apps (+{reclaimedMb} MB freed)";
                FreezeFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
            }

            FreezeFeedbackPill.Visibility = Visibility.Visible;
            var pillTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            pillTimer.Tick += (_, _) =>
            {
                pillTimer.Stop();
                FreezeFeedbackPill.Visibility = Visibility.Collapsed;
            };
            pillTimer.Start();
        }
        finally
        {
            FreezeBgAppsBtn.IsEnabled = true;
            UpdateFreezeButtonState();
        }
    }

    private void OnFreezeStateChanged(bool isFrozen, int count)
    {
        Dispatcher.Invoke(UpdateFreezeButtonState);
    }

    private void UpdateFreezeButtonState()
    {
        if (BackgroundAppFreezer.IsFrozen)
        {
            FreezeBtnText.Text = "Thaw Apps";
            FreezeIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
            FreezeBgAppsBtn.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x33, 0x22));
            FreezeBgAppsBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x6D, 0x24));
        }
        else
        {
            FreezeBtnText.Text = "Freeze Apps";
            FreezeIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
            FreezeBgAppsBtn.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x36, 0x4B));
            FreezeBgAppsBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x52, 0x72));
        }
    }

    private void UpdateAntiLagButtonState()
    {
        bool isAntiLagOn = AppSettings.GameBoostNetworkBoost || AppSettings.GameBoostBluetoothBoost;
        if (isAntiLagOn)
        {
            AntiLagBtnText.Text = "Anti-Lag: ON";
            AntiLagBtnText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            AntiLagIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
            AntiLagBtn.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x33, 0x22));
            AntiLagBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x6D, 0x24));

            NetBoostChip.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x1C, 0x27));
            NetBoostChip.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x3D, 0x56));
            NetBoostChipText.Text = "Net Boost: 0ms Throttling";
            NetBoostChipText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));

            BtBoostChip.Background = new SolidColorBrush(Color.FromRgb(0x15, 0x29, 0x1C));
            BtBoostChip.BorderBrush = new SolidColorBrush(Color.FromRgb(0x27, 0x55, 0x2E));
            BtBoostChipText.Text = "BT Boost: Anti-Lag";
            BtBoostChipText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
        }
        else
        {
            AntiLagBtnText.Text = "Anti-Lag: OFF";
            AntiLagBtnText.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0));
            AntiLagIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0));
            AntiLagBtn.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x22, 0x2F));
            AntiLagBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x38, 0x4C));

            NetBoostChip.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x17, 0x20));
            NetBoostChip.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1D, 0x26, 0x33));
            NetBoostChipText.Text = "Net Boost: Default";
            NetBoostChipText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x82));

            BtBoostChip.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x17, 0x20));
            BtBoostChip.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1D, 0x26, 0x33));
            BtBoostChipText.Text = "BT Boost: Default";
            BtBoostChipText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x82));
        }
    }

    private void ForceKill_Click(object sender, RoutedEventArgs e)
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game == null) return;

        if (!_forceKillPending)
        {
            _forceKillPending = true;
            ForceKillBtnText.Text = "⚠️ Click again to confirm Force Kill!";
            ForceKillBtn.Background = new SolidColorBrush(Color.FromRgb(0x4D, 0x16, 0x1C));
            ForceKillBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x24, 0x2F));
            _forceKillResetTimer.Stop();
            _forceKillResetTimer.Start();
            return;
        }

        // Confirmed force kill
        _forceKillResetTimer.Stop();
        _forceKillPending = false;
        ResetForceKillButton();

        try
        {
            int killed = 0;
            int targetPid = game.ProcessId;
            string? targetProcName = game.ProcessName;

            // If PID is 0 or ProcName is null, actively scan running processes to find the game
            if (targetPid <= 0 || string.IsNullOrEmpty(targetProcName))
            {
                var steamPath = SteamDetector.FindSteamPath();
                string? installDir = steamPath != null && game.AppId > 0 
                    ? AppCloudConfig.FindGameInstallDir(steamPath, game.AppId) 
                    : null;

                string normalizedGameName = !string.IsNullOrEmpty(game.Name)
                    ? game.Name.Replace(" ", "").Replace(":", "").Replace("-", "")
                    : string.Empty;

                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id <= 4) continue;

                        if (!string.IsNullOrEmpty(installDir))
                        {
                            var path = ActiveGameTrackerService.GetProcessFilePath(p.Id);
                            if (!string.IsNullOrEmpty(path) && path.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
                            {
                                targetPid = p.Id;
                                targetProcName = p.ProcessName;
                                break;
                            }
                        }

                        if (!string.IsNullOrEmpty(normalizedGameName) && p.ProcessName.Equals(normalizedGameName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetPid = p.Id;
                            targetProcName = p.ProcessName;
                            break;
                        }
                    }
                    catch { }
                }
            }

            // 1. Terminate by PID with full process tree kill
            if (targetPid > 0)
            {
                try
                {
                    using var p = Process.GetProcessById(targetPid);
                    p.Kill(true);
                    killed++;
                }
                catch { }

                try
                {
                    using var kp = Process.Start(new ProcessStartInfo("taskkill", $"/F /PID {targetPid} /T")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    kp?.WaitForExit(1500);
                    killed++;
                }
                catch { }
            }

            // 2. Terminate by process name with full process tree kill
            if (!string.IsNullOrEmpty(targetProcName))
            {
                var procs = Process.GetProcessesByName(targetProcName);
                foreach (var p in procs)
                {
                    try
                    {
                        p.Kill(true);
                        killed++;
                    }
                    catch { }
                }

                try
                {
                    using var kp = Process.Start(new ProcessStartInfo("taskkill", $"/F /IM \"{targetProcName}.exe\" /T")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    kp?.WaitForExit(1500);
                    killed++;
                }
                catch { }
            }

            if (killed > 0)
            {
                SteamToastService.ShowAuto(
                    "Game Force Killed ⚠️",
                    $"Terminated frozen process '{game.Name}'. System resources freed!");
            }
            else
            {
                SteamToastService.ShowAuto(
                    "Kill Notice ⚠️",
                    $"No active process found for '{game.Name}'.");
            }

            ActiveGameTrackerService.ClearActiveGame();
            RefreshGameContext();

            // Auto-close Game Space overlay and Game Booster
            HideOverlay();
            GameBoostToastService.Dismiss();
            MiniBrowserWindow.CloseBrowser();
        }
        catch (Exception ex)
        {
            SteamToastService.ShowAuto("Kill Failed ⚠️", ex.Message);
        }
    }

    private void ResetForceKillButton()
    {
        _forceKillPending = false;
        ForceKillBtnText.Text = "⚠️ Force Kill Frozen Game";
        ForceKillBtn.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x16, 0x19));
        ForceKillBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x5C, 0x25, 0x2B));
    }

    private void CreateCheckpoint_Click(object sender, RoutedEventArgs e)
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game == null)
        {
            SnapshotFeedbackText.Text = "⚠️ Launch a game first to create checkpoints.";
            SnapshotFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x30));
            SnapshotFeedbackPill.Visibility = Visibility.Visible;
            return;
        }

        string label = CheckpointNameInput.Text.Trim();
        if (string.IsNullOrEmpty(label)) label = "Manual Checkpoint";

        if (GameSpaceService.CreateCheckpoint(game.Name, label, out var err))
        {
            SnapshotFeedbackText.Text = $"✓ Saved: \"{label}\"";
            SnapshotFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
            SnapshotFeedbackPill.Visibility = Visibility.Visible;
            RefreshRecentSnapshots();

            SteamToastService.ShowAuto(
                "Checkpoint Created 💾",
                $"Snapshot \"{label}\" saved safely for {game.Name}!");
        }
        else
        {
            SnapshotFeedbackText.Text = $"⚠️ {err ?? "Snapshot failed"}";
            SnapshotFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x30));
            SnapshotFeedbackPill.Visibility = Visibility.Visible;
        }

        var pillTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        pillTimer.Tick += (_, _) =>
        {
            pillTimer.Stop();
            SnapshotFeedbackPill.Visibility = Visibility.Collapsed;
        };
        pillTimer.Start();
    }

    private void NewNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        var gameName = _currentGameName == "Desktop" ? "Global" : (_currentGameName ?? "Global");
        var note = StickyNotesService.CreateNote(gameName, "New Note", "");
        StickyNotesService.ShowNoteWindow(note);
        RefreshStickyNotesList();
    }

    private void FloatAllPinned_Click(object sender, RoutedEventArgs e)
    {
        var gameName = _currentGameName == "Desktop" ? "Global" : (_currentGameName ?? "Global");
        StickyNotesService.ShowAllPinnedNotes(gameName);
        RefreshStickyNotesList();
    }

    private void HideAllFloating_Click(object sender, RoutedEventArgs e)
    {
        StickyNotesService.HideAllNotes();
        RefreshStickyNotesList();
    }

    private void RefreshStickyNotesList()
    {
        StickyNotesListPanel.Children.Clear();
        var notes = StickyNotesService.GetNotesForGame(_currentGameName);
        NotesCountText.Text = $"{notes.Count} note(s)";

        if (notes.Count == 0)
        {
            StickyNotesListPanel.Children.Add(new TextBlock
            {
                Text = "No notes yet. Click \"+ New Note\" to create an in-game HUD sticky note.",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x82)),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var note in notes)
        {
            var noteCard = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x1A, 0x24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1D, 0x2E, 0x40)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var stack = new StackPanel();

            // Header line: Color accent bar, Title, Game tag, and Pin chip
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            Color accentColor = Color.FromRgb(0x66, 0xC0, 0xF4);
            try { accentColor = (Color)ColorConverter.ConvertFromString(note.AccentColor); } catch { }

            titlePanel.Children.Add(new Border
            {
                Width = 4,
                Height = 12,
                Background = new SolidColorBrush(accentColor),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            titlePanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(note.Title) ? "Untitled Note" : note.Title,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 180
            });

            Grid.SetColumn(titlePanel, 0);
            headerGrid.Children.Add(titlePanel);

            // Right side: Pinned indicator
            var pinBadge = new Border
            {
                Background = note.IsPinned ? new SolidColorBrush(Color.FromRgb(0x14, 0x2E, 0x18)) : new SolidColorBrush(Color.FromRgb(0x14, 0x1E, 0x28)),
                BorderBrush = note.IsPinned ? new SolidColorBrush(Color.FromRgb(0x2E, 0x66, 0x36)) : new SolidColorBrush(Color.FromRgb(0x23, 0x35, 0x48)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            pinBadge.Child = new TextBlock
            {
                Text = note.IsPinned ? "📌 Pinned" : "Unpinned",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = note.IsPinned ? new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)) : new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0))
            };
            Grid.SetColumn(pinBadge, 1);
            headerGrid.Children.Add(pinBadge);

            stack.Children.Add(headerGrid);

            // Snippet preview (first line)
            var previewText = string.IsNullOrWhiteSpace(note.Content) ? "(Empty note)" : note.Content.Replace("\r\n", " ").Replace("\n", " ");
            stack.Children.Add(new TextBlock
            {
                Text = previewText,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(10, 3, 0, 6)
            });

            // Action buttons row: Float / Edit, Pin Toggle, Delete
            var actionDock = new DockPanel { LastChildFill = false };

            var floatBtn = new Button
            {
                Content = StickyNotesService.IsWindowOpen(note.Id) ? "👁️ Floating" : "↗ Pop Out",
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 3, 8, 3),
                Cursor = Cursors.Hand,
                Style = (Style)FindResource("SteamCyanButtonStyle"),
                Margin = new Thickness(0, 0, 6, 0)
            };
            var capturedNote = note;
            floatBtn.Click += (_, _) =>
            {
                StickyNotesService.ShowNoteWindow(capturedNote);
                RefreshStickyNotesList();
            };
            DockPanel.SetDock(floatBtn, Dock.Left);
            actionDock.Children.Add(floatBtn);

            var togglePinBtn = new Button
            {
                Content = capturedNote.IsPinned ? "Unpin" : "Pin 📌",
                FontSize = 9.5,
                Padding = new Thickness(7, 3, 7, 3),
                Cursor = Cursors.Hand,
                Style = (Style)FindResource("SteamMiniActionButtonStyle"),
                Margin = new Thickness(0, 0, 6, 0)
            };
            togglePinBtn.Click += (_, _) =>
            {
                capturedNote.IsPinned = !capturedNote.IsPinned;
                StickyNotesService.SaveNotes();
                RefreshStickyNotesList();
            };
            DockPanel.SetDock(togglePinBtn, Dock.Left);
            actionDock.Children.Add(togglePinBtn);

            var deleteBtn = new Button
            {
                Content = "🗑️",
                FontSize = 10,
                Padding = new Thickness(6, 2, 6, 2),
                Cursor = Cursors.Hand,
                Style = (Style)FindResource("SteamMiniActionButtonStyle"),
                ToolTip = "Delete note"
            };
            deleteBtn.Click += (_, _) =>
            {
                var res = MessageBox.Show($"Delete note \"{capturedNote.Title}\"?", "Delete Sticky Note", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    StickyNotesService.DeleteNote(capturedNote.Id);
                    RefreshStickyNotesList();
                }
            };
            DockPanel.SetDock(deleteBtn, Dock.Right);
            actionDock.Children.Add(deleteBtn);

            stack.Children.Add(actionDock);
            noteCard.Child = stack;
            StickyNotesListPanel.Children.Add(noteCard);
        }
    }

    private void OpenMiniBrowser_Click(object sender, RoutedEventArgs e)
    {
        MiniBrowserWindow.Open();
    }

    private void BookmarkMapGenie_Click(object sender, RoutedEventArgs e)
    {
        MiniBrowserWindow.Open("https://mapgenie.io");
    }

    private void BookmarkSteamGuides_Click(object sender, RoutedEventArgs e)
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game != null && game.AppId > 0)
        {
            MiniBrowserWindow.Open($"https://steamcommunity.com/app/{game.AppId}/guides/");
        }
        else
        {
            MiniBrowserWindow.Open("https://steamcommunity.com/?subsection=guides");
        }
    }

    private void BookmarkWiki_Click(object sender, RoutedEventArgs e)
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game != null && !string.IsNullOrWhiteSpace(game.Name))
        {
            MiniBrowserWindow.Open($"https://www.google.com/search?q={Uri.EscapeDataString(game.Name + " wiki guide")}");
        }
        else
        {
            MiniBrowserWindow.Open("https://www.fandom.com");
        }
    }

    private void BookmarkGoogle_Click(object sender, RoutedEventArgs e)
    {
        MiniBrowserWindow.Open("https://www.google.com");
    }

    private void OpenMainApp_Click(object sender, RoutedEventArgs e)
    {
        HideOverlay();
        App.BringToForeground();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        HideOverlay();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideOverlay();
            return;
        }

        // Alt+N or Ctrl+N: Create new note
        if ((Keyboard.Modifiers == ModifierKeys.Alt && (e.SystemKey == Key.N || e.Key == Key.N)) ||
            (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N))
        {
            e.Handled = true;
            NewNoteBtn_Click(this, new RoutedEventArgs());
            return;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
