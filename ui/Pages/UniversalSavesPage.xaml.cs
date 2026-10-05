using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CloudRedirect.Dialogs;
using CloudRedirect.Services;

namespace CloudRedirect.Pages;

public partial class UniversalSavesPage : Page
{
    public UniversalSavesPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshList();
            UniversalSaveWatcherService.OnProfilesChanged += OnProfilesChangedHandler;
            UniversalSaveWatcherService.OnProfileStatusChanged += OnProfileStatusChangedHandler;
        };
        Unloaded += (_, _) =>
        {
            UniversalSaveWatcherService.OnProfilesChanged -= OnProfilesChangedHandler;
            UniversalSaveWatcherService.OnProfileStatusChanged -= OnProfileStatusChangedHandler;
        };
    }

    private void OnProfilesChangedHandler()
    {
        Dispatcher.Invoke(RefreshList);
    }

    private void OnProfileStatusChangedHandler(UniversalGameProfile profile)
    {
        Dispatcher.Invoke(RefreshList);
    }

    private string _currentFilterChip = "all";
    private System.Windows.Data.ListCollectionView? _profilesView;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyProfileFilter();
    }

    private void FilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            _currentFilterChip = tag;
            ApplyProfileFilter();
        }
    }

    private void ApplyProfileFilter()
    {
        _profilesView?.Refresh();
    }

    private bool ProfileFilter(object item)
    {
        if (item is not UniversalGameProfile p) return false;

        if (_currentFilterChip == "monitoring" && p.Status != "Monitoring" && p.Status != "Up to Date" && !p.Status.Contains("Running"))
            return false;

        if (_currentFilterChip == "anticheat" && !p.HasAntiCheat)
            return false;

        var query = SearchBox?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(query)) return true;

        return p.GameName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || p.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (p.SteamAppId > 0 && p.SteamAppId.ToString().Contains(query));
    }

    private void RefreshList()
    {
        var list = UniversalSaveWatcherService.GetProfiles().ToList();
        _profilesView = (System.Windows.Data.ListCollectionView)System.Windows.Data.CollectionViewSource.GetDefaultView(list);
        _profilesView.Filter = ProfileFilter;
        GamesItemsControl.ItemsSource = _profilesView;
        EmptyStateBorder.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GamesItemsControl.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _ = Task.Run(async () =>
        {
            var appIds = list.Where(p => p.SteamAppId > 0).Select(p => p.SteamAppId).Distinct().ToList();
            if (appIds.Count > 0)
            {
                var store = await SteamStoreClient.Shared.GetAppInfoAsync(appIds);
                Dispatcher.Invoke(() =>
                {
                    foreach (var p in list)
                    {
                        if (p.SteamAppId > 0 && store.TryGetValue(p.SteamAppId, out var info) && !string.IsNullOrEmpty(info.HeaderUrl))
                        {
                            p.HeaderUrl = info.HeaderUrl;
                        }
                    }
                });
            }
        });
    }

    private async void AddPreset_Click(object sender, RoutedEventArgs e)
    {
        var existing = UniversalSaveWatcherService.GetProfiles();
        var availablePresets = UniversalSaveWatcherService.PopularPresets
            .Where(p => !existing.Any(ex => ex.GameName.Equals(p.GameName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (availablePresets.Count == 0)
        {
            await Services.Dialog.ShowInfoAsync("Popular Presets", "All built-in presets have already been added to your library!");
            return;
        }

        var stack = new StackPanel { MinWidth = 320 };
        var primaryBrush = (System.Windows.Media.Brush)FindResource("SteamTextPrimaryBrush");
        stack.Children.Add(new TextBlock
        {
            Text = "Select a game preset to add to Universal Cloud Saves:",
            FontSize = 13,
            Foreground = primaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var combo = new ComboBox
        {
            ItemsSource = availablePresets.Select(p => p.GameName).ToList(),
            SelectedIndex = 0,
            Height = 32,
            Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(combo);

        bool confirmed = await Services.Dialog.ShowCustomAsync(
            "Add Game Preset",
            stack,
            primaryText: "Add Game",
            secondaryText: "Cancel",
            theme: Dialogs.SteamMessageBoxDialog.ButtonTheme.PlayGreen);

        if (confirmed && combo.SelectedIndex >= 0)
        {
            var selectedPreset = availablePresets[combo.SelectedIndex];
            UniversalSaveWatcherService.AddProfile(new UniversalGameProfile
            {
                GameName = selectedPreset.GameName,
                ProcessName = selectedPreset.ProcessName,
                SaveFolderPath = selectedPreset.SaveFolderPath
            });
            RefreshList();
        }
    }

    private async void ScanSteamLibrary_Click(object sender, RoutedEventArgs e)
    {
        var summary = await SteamGameScannerService.ScanInstalledSteamGamesAsync(autoEnroll: true);
        RefreshList();

        string details = $"Scanned {summary.TotalInstalledGames} installed Steam games across all libraries:\n\n" +
                         $"• Lua / CloudRedirect Games: {summary.LuaGamesCount} (Synced to your cloud)\n" +
                         $"• Genuine Steam Cloud Games: {summary.GenuineCloudGamesCount} (Using native Steam Cloud)\n" +
                         $"• Games without Steam Cloud: {summary.NonCloudGamesCount} (Protected by Universal Saves)\n\n";

        if (summary.NewlyEnrolledCount > 0)
        {
            details += $"Successfully auto-enrolled {summary.NewlyEnrolledCount} new game(s) into Universal Cloud Saves!";
        }
        else if (summary.TotalInstalledGames == 0)
        {
            details = "No installed Steam games were found in your Steam libraries.";
        }
        else
        {
            details += "All eligible games are already monitored and up to date!";
        }

        await Services.Dialog.ShowInfoAsync("Steam Library Scan Results", details);
    }

    private async void AutoScan_Click(object sender, RoutedEventArgs e)
    {
        var detected = await System.Threading.Tasks.Task.Run(() => GameSaveAutoDetector.ScanInstalledGameSaves());
        var existing = UniversalSaveWatcherService.GetProfiles();
        var newlyFound = detected.Where(d => !existing.Any(ex =>
            (d.AppId > 0 && ex.SteamAppId == d.AppId) ||
            ex.GameName.Equals(d.GameName, StringComparison.OrdinalIgnoreCase) ||
            ex.ExpandedSavePath.Equals(d.SaveFolderPath, StringComparison.OrdinalIgnoreCase)
        )).ToList();

        if (newlyFound.Count == 0)
        {
            await Services.Dialog.ShowInfoAsync("Auto-Scan Game Saves", "No new unmonitored game saves were detected on this PC. All existing saves are already added or none were found.");
            return;
        }

        var stack = new StackPanel { MinWidth = 420 };
        stack.Children.Add(new TextBlock
        {
            Text = $"Found {newlyFound.Count} game save location(s) on your PC! Select games to add:",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("SteamTextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        var checkBoxes = new System.Collections.Generic.List<(CheckBox Check, DetectedGameSave Save)>();
        var scroll = new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var listStack = new StackPanel();

        foreach (var game in newlyFound)
        {
            var itemBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1a, 0x27, 0x36)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var itemGrid = new Grid();
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cb = new CheckBox
            {
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(cb, 0);

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = $"{game.GameName} ({game.FormattedSize}, {game.FileCount} files)",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0xc0, 0xf4))
            });
            textStack.Children.Add(new TextBlock
            {
                Text = game.SaveFolderPath,
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8f, 0x98, 0xa0)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(textStack, 1);

            itemGrid.Children.Add(cb);
            itemGrid.Children.Add(textStack);
            itemBorder.Child = itemGrid;
            listStack.Children.Add(itemBorder);

            checkBoxes.Add((cb, game));
        }

        scroll.Content = listStack;
        stack.Children.Add(scroll);

        bool confirmed = await Services.Dialog.ShowCustomAsync(
            "Auto-Detected Game Saves",
            stack,
            primaryText: $"Add Selected ({newlyFound.Count})",
            secondaryText: "Cancel",
            theme: Dialogs.SteamMessageBoxDialog.ButtonTheme.PlayGreen);

        if (confirmed)
        {
            int added = 0;
            var steamPath = SteamDetector.FindSteamPath();
            foreach (var (cb, game) in checkBoxes)
            {
                if (cb.IsChecked == true)
                {
                    var proc = game.ProcessName;
                    if (!string.IsNullOrEmpty(proc) && !proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        proc += ".exe";
                    }

                    UniversalSaveWatcherService.AddProfile(new UniversalGameProfile
                    {
                        GameName = game.GameName,
                        ProcessName = proc,
                        SaveFolderPath = game.SaveFolderPath,
                        SteamAppId = game.AppId,
                        IsGenuineSteamGame = game.AppId > 0 && !SteamDetector.IsLuaGame(game.AppId, steamPath)
                    });
                    added++;
                }
            }

            RefreshList();
            if (added > 0)
            {
                TrayIconService.Instance.ShowNotification(
                    "Game Saves Configured",
                    $"Successfully added {added} games to Universal Cloud Saves!");
            }
        }
    }

    private async void AddCustomGame_Click(object sender, RoutedEventArgs e)
    {
        var stack = new StackPanel { MinWidth = 380 };

        var labelBrush = (System.Windows.Media.Brush)FindResource("SteamTextSecondaryBrush");

        // 1-Click Auto-Detect from Running Game
        var autoDetectBtn = new System.Windows.Controls.Button
        {
            Content = "⚡ Auto-Detect from Currently Running Game",
            Style = (Style)FindResource("SteamBlueButtonStyle"),
            Margin = new Thickness(0, 0, 0, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(14, 8, 14, 8)
        };
        stack.Children.Add(autoDetectBtn);

        stack.Children.Add(new TextBlock { Text = "Game Name:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = labelBrush, Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new Wpf.Ui.Controls.TextBox { PlaceholderText = "e.g. My Custom Game", Margin = new Thickness(0, 0, 0, 10) };
        stack.Children.Add(nameBox);

        stack.Children.Add(new TextBlock { Text = "Process Executable Name:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = labelBrush, Margin = new Thickness(0, 0, 0, 4) });
        var procBox = new Wpf.Ui.Controls.TextBox { PlaceholderText = "e.g. game.exe (without path)", Margin = new Thickness(0, 0, 0, 10) };
        stack.Children.Add(procBox);

        stack.Children.Add(new TextBlock { Text = "Save Folder Path (supports %APPDATA%, %USERPROFILE%):", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = labelBrush, Margin = new Thickness(0, 0, 0, 4) });

        var pathGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pathBox = new Wpf.Ui.Controls.TextBox { PlaceholderText = @"e.g. %APPDATA%\MyGame\Saves" };
        Grid.SetColumn(pathBox, 0);

        var browseBtn = new System.Windows.Controls.Button
        {
            Content = "Browse...",
            Style = (Style)FindResource("SteamSecondaryButtonStyle"),
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 6, 14, 6)
        };
        Grid.SetColumn(browseBtn, 1);
        browseBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Game Save Folder"
            };
            if (dlg.ShowDialog() == true)
            {
                pathBox.Text = dlg.FolderName;
            }
        };

        pathGrid.Children.Add(pathBox);
        pathGrid.Children.Add(browseBtn);
        stack.Children.Add(pathGrid);

        // Auto-detect button logic
        autoDetectBtn.Click += async (_, _) =>
        {
            // 1. Check verified running game saves
            var runningGames = GameSaveAutoDetector.DetectFromRunningProcesses();
            if (runningGames.Count > 0)
            {
                var first = runningGames[0];
                nameBox.Text = first.GameName;
                procBox.Text = first.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? first.ProcessName : first.ProcessName + ".exe";
                pathBox.Text = first.SaveFolderPath;
                return;
            }

            // 2. Search running processes for verified game candidates (strictly excluding system and Windows apps)
            var candidates = new List<(string Name, string Exe, string? Path)>();
            try
            {
                var procs = System.Diagnostics.Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                        if (GameSaveAutoDetector.IsSystemProcess(p.ProcessName, p)) continue;
                        if (p.MainWindowHandle == IntPtr.Zero) continue;

                        var detected = GameSaveAutoDetector.DetectSaveFolder(p.MainWindowTitle, p.ProcessName);
                        if (!string.IsNullOrEmpty(detected) && Directory.Exists(detected))
                        {
                            candidates.Add((p.MainWindowTitle, p.ProcessName + ".exe", detected));
                        }
                    }
                    catch { }
                }
            }
            catch { }

            if (candidates.Count > 0)
            {
                var match = candidates[0];
                nameBox.Text = match.Name;
                procBox.Text = match.Exe;
                pathBox.Text = match.Path;
            }
            else
            {
                await Services.Dialog.ShowWarningAsync(
                    "No Running Game Detected",
                    "No active game processes were detected on your system.\n\nPlease start your game first before clicking Auto-Detect, or manually fill in the Game Name and Save Folder Path.");
            }
        };

        bool confirmed = await Services.Dialog.ShowCustomAsync(
            "Add Custom Game",
            stack,
            primaryText: "Add Game",
            secondaryText: "Cancel",
            theme: Dialogs.SteamMessageBoxDialog.ButtonTheme.PlayGreen);

        if (confirmed)
        {
            var name = nameBox.Text.Trim();
            var proc = procBox.Text.Trim();
            var path = pathBox.Text.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
            {
                await Services.Dialog.ShowWarningAsync(
                    "Invalid Input",
                    "Please provide at least a Game Name and Save Folder Path.");
                return;
            }

            UniversalSaveWatcherService.AddProfile(new UniversalGameProfile
            {
                GameName = name,
                ProcessName = proc,
                SaveFolderPath = path
            });
            RefreshList();
        }
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is UniversalGameProfile profile)
        {
            if (fe is Wpf.Ui.Controls.Button btn) btn.IsEnabled = false;
            try
            {
                var saveDir = profile.ExpandedSavePath;
                if (!Directory.Exists(saveDir) || Directory.GetFiles(saveDir, "*", SearchOption.AllDirectories).Length == 0)
                {
                    var alert = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = "No Save Files Yet",
                        Content = $"No save files were found in:\n{saveDir}\n\nPlease play the game and create a save inside the game first, then click Sync.",
                        CloseButtonText = "OK"
                    };
                    await alert.ShowDialogAsync();
                    UniversalSaveWatcherService.UpdateProfileStatus(profile, "No Saves Yet");
                    return;
                }

                await UniversalSaveWatcherService.SyncProfileNowAsync(profile, "Manual Sync");
            }
            finally
            {
                if (fe is Wpf.Ui.Controls.Button actionBtn) actionBtn.IsEnabled = true;
                RefreshList();
            }
        }
    }

    private void OpenDrive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is UniversalGameProfile profile)
        {
            var dlg = new Dialogs.CloudFolderBrowserDialog(profile.GameName, profile.ExpandedSavePath, profile.SteamAppId)
            {
                Owner = Window.GetWindow(this)
            };
            dlg.ShowDialog();
        }
    }

    private void OpenGuide_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.InteractiveGuideDialog
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ShowDialog();
    }

    private void SaveHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is UniversalGameProfile profile)
        {
            var appId = profile.SteamAppId > 0 ? profile.SteamAppId.ToString() : null;
            var dialog = new SaveHistoryDialog(profile.GameName, profile.ExpandedSavePath, appId)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is UniversalGameProfile profile)
        {
            bool confirmed = await Services.Dialog.ConfirmDangerAsync(
                "Remove Game",
                $"Stop monitoring '{profile.GameName}'? Existing backups will not be deleted.");

            if (confirmed)
            {
                UniversalSaveWatcherService.RemoveProfile(profile.Id);
                RefreshList();
            }
        }
    }

    private void GameCard_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Don't trigger if clicked on child button or interactive control
        if (e.OriginalSource is DependencyObject dep)
        {
            var btn = FindVisualParent<System.Windows.Controls.Button>(dep) ?? (DependencyObject?)FindVisualParent<Wpf.Ui.Controls.Button>(dep);
            if (btn != null) return;
        }

        if (sender is FrameworkElement fe && fe.DataContext is UniversalGameProfile profile)
        {
            var appId = profile.SteamAppId > 0 ? profile.SteamAppId.ToString() : null;
            var dialog = new SaveHistoryDialog(profile.GameName, profile.ExpandedSavePath, appId)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is UniversalGameProfile profile)
        {
            OpenFolder(profile.ExpandedSavePath);
        }
    }

    private void SavePath_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string path)
        {
            e.Handled = true;
            OpenFolder(path);
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true });
                }
            }
        }
        catch { }
    }
}
