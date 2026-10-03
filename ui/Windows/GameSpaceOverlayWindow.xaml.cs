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
using CloudRedirect.Services;

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
    private readonly DispatcherTimer _notesDebounceTimer;
    private bool _isClosing;
    private string? _currentGameName;

    public GameSpaceOverlayWindow()
    {
        InitializeComponent();

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _updateTimer.Tick += UpdateTimer_Tick;

        _notesDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _notesDebounceTimer.Tick += (_, _) =>
        {
            _notesDebounceTimer.Stop();
            if (!string.IsNullOrEmpty(_currentGameName))
            {
                GameSpaceService.SaveGameNotes(_currentGameName, GameNotesTextBox.Text);
                NotesSavedIndicator.Visibility = Visibility.Visible;
                var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                fadeTimer.Tick += (_, _) =>
                {
                    fadeTimer.Stop();
                    NotesSavedIndicator.Visibility = Visibility.Collapsed;
                };
                fadeTimer.Start();
            }
        };
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
        Left = workArea.Right - Width;
        Top = workArea.Top;
        Height = workArea.Height;

        RefreshGameContext();
        RefreshMemoryStats();
        RefreshRecentSnapshots();

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

            // Load game notes
            GameNotesTextBox.Text = GameSpaceService.LoadGameNotes(game.Name);
        }
        else
        {
            _currentGameName = "Desktop";
            GameTitleText.Text = "Standby (No Game Running)";
            GameTimerText.Text = "⏱️ Ready for game launch";
            BoostActiveBadge.Visibility = Visibility.Collapsed;
            GamePosterFallback.Visibility = Visibility.Visible;
            GamePosterImage.Source = null;
            GameNotesTextBox.Text = GameSpaceService.LoadGameNotes("Desktop");
        }
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        var game = ActiveGameTrackerService.CurrentGame;
        if (game != null)
        {
            var elapsed = DateTime.Now - game.StartTime;
            GameTimerText.Text = $"⏱️ {elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            BoostActiveBadge.Visibility = GameBoostService.IsBoostActive ? Visibility.Visible : Visibility.Collapsed;
        }

        RefreshMemoryStats();
    }

    private void RefreshMemoryStats()
    {
        var mem = GameSpaceService.GetMemoryStats();
        RamUsageText.Text = $"RAM: {mem.UsedGb:F1} GB / {mem.TotalGb:F1} GB";
        RamPercentText.Text = $"{mem.LoadPercent}%";
        RamProgressBar.Value = mem.LoadPercent;

        if (mem.LoadPercent > 80)
        {
            RamProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x30)); // Steam Amber
        }
        else
        {
            RamProgressBar.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)); // Steam Cyan
        }

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
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x1A, 0x24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x34, 0x47)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 4)
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
                Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var timeDiff = DateTime.Now - snap.Timestamp;
            string timeStr = timeDiff.TotalMinutes < 1 ? "Just now" :
                             timeDiff.TotalHours < 1 ? $"{(int)timeDiff.TotalMinutes}m ago" :
                             timeDiff.TotalDays < 1 ? $"{(int)timeDiff.TotalHours}h ago" :
                             snap.Timestamp.ToString("MMM dd HH:mm");

            infoPanel.Children.Add(new TextBlock
            {
                Text = $"{timeStr} • {snap.FileCount} files ({FormatBytes(snap.TotalBytes)})",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0))
            });

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            var restoreBtn = new Button
            {
                Content = "↺ Restore",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x36, 0x4B)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                BorderThickness = new Thickness(0)
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
        RefreshMemoryStats();

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

    private void GameNotesTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _notesDebounceTimer.Stop();
        _notesDebounceTimer.Start();
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

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideOverlay();
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
