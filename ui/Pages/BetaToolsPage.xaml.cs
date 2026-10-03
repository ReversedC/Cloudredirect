using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CloudRedirect.Resources;
using CloudRedirect.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace CloudRedirect.Pages;

public partial class BetaToolsPage : Page
{
    private List<OrphanShaderCache> _scannedShaders = new();
    private bool _loading;

    public BetaToolsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                await LoadDataAsync();
            }
            catch { }
        };
    }

    private async Task LoadDataAsync()
    {
        _loading = true;
        try
        {
            // Auto-detect active game or universal saves for paths
            var active = ActiveGameTrackerService.CurrentActiveGame;
            if (active != null && !string.IsNullOrEmpty(active.SaveDirectory))
            {
                ExportSavePathBox.Text = active.SaveDirectory;
                ResignFolderBox.Text = active.SaveDirectory;
            }

            // Populate detected steam ID
            var currentSteamId = SteamIdResignerService.GetCurrentSteamId64();
            if (currentSteamId.HasValue && string.IsNullOrEmpty(NewSteamIdBox.Text))
            {
                NewSteamIdBox.Text = currentSteamId.Value.ToString();
            }

            // Populate games for character slots
            PopulateGamesForSlots();

            // Populate games and snapshots for pinning vault
            PopulateGamesForSnapshots();
            ReloadSnapshotsList();

            // Populate toggles
            BoosterToggle.IsChecked = AppSettings.EnableGamePerformanceBooster;
            SpacewarSpooferToggle.IsChecked = AppSettings.EnableSpacewarBroadcaster;
            ContextMenuToggle.IsChecked = SteamContextMenuService.IsShellContextMenuRegistered();

            // Setup Mobile Dashboard
            bool mobileRunning = MobileDashboardServer.IsRunning;
            MobileServerToggle.IsChecked = mobileRunning;
            UpdateMobileUi(mobileRunning);

            // Setup LAN P2P
            LanP2PSyncService.OnPeersChanged += () => Dispatcher.Invoke(UpdateLanPeersUi);
            UpdateLanPeersUi();

            // Refresh storage metrics in background
            await RefreshStorageAsync();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (SectionArchives == null || SectionSlots == null || SectionStorage == null || SectionLan == null || SectionPerformance == null)
            return;

        SectionArchives.Visibility = TabArchivesBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SectionSlots.Visibility = TabSlotsBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SectionStorage.Visibility = TabStorageBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SectionLan.Visibility = TabLanBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SectionPerformance.Visibility = TabPerformanceBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (TabStorageBtn.IsChecked == true)
        {
            _ = RefreshStorageAsync();
        }

        // Dynamically recalculate AutoFit scale for the newly visible tab content
        Dispatcher.InvokeAsync(() =>
        {
            UiZoomManager.Instance.RecalculateAutoFitImmediate();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ==========================================
    // 1-CLICK SAVE ARCHIVES (.ZIP EXPORT / IMPORT)
    // ==========================================

    private void BrowseSaveExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Save Directory to Export or Import" };
        if (dlg.ShowDialog() == true)
        {
            ExportSavePathBox.Text = dlg.FolderName;
        }
    }

    private async void ExportZip_Click(object sender, RoutedEventArgs e)
    {
        var saveDir = ExportSavePathBox.Text?.Trim();
        if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
        {
            await Dialog.ShowErrorAsync("Save Folder Not Found", "Please choose a valid save directory first.");
            return;
        }

        var sfd = new SaveFileDialog
        {
            Title = "Export Save Archive",
            Filter = "ZIP Save Archive (*.zip)|*.zip",
            FileName = $"{Path.GetFileName(saveDir.TrimEnd('\\', '/'))}_SaveBackup_{DateTime.Now:yyyyMMdd}.zip"
        };

        if (sfd.ShowDialog() == true)
        {
            ExportImportStatusText.Text = "Packaging save files into archive...";
            ExportImportStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
            ExportImportStatusText.Visibility = Visibility.Visible;

            var (ok, err, path) = await SaveArchiveService.ExportSaveArchiveAsync(
                Path.GetFileName(saveDir), saveDir, sfd.FileName);

            if (ok)
            {
                ExportImportStatusText.Text = $"Exported successfully to: {Path.GetFileName(path)}";
                ExportImportStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
                await Dialog.ShowInfoAsync("Export Complete", $"Save archive created successfully:\n\n{path}");
            }
            else
            {
                ExportImportStatusText.Text = $"Export failed: {err}";
                ExportImportStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4C, 0x4C));
                await Dialog.ShowErrorAsync("Export Failed", err ?? "Unknown error");
            }
        }
    }

    private async void ImportZip_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Select Save Archive (.zip)",
            Filter = "ZIP Save Archive (*.zip)|*.zip|All Files (*.*)|*.*"
        };

        if (ofd.ShowDialog() == true)
        {
            var meta = SaveArchiveService.InspectSaveArchive(ofd.FileName);
            var targetDir = ExportSavePathBox.Text?.Trim();

            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                var folderDlg = new OpenFolderDialog
                {
                    Title = $"Select Target Directory to Import '{meta?.GameName ?? "Save"}' Files"
                };
                if (folderDlg.ShowDialog() != true) return;
                targetDir = folderDlg.FolderName;
                ExportSavePathBox.Text = targetDir;
            }

            string prompt = $"Import {meta?.FileCount ?? 1} save file(s) into:\n{targetDir}\n\n(A safety snapshot of existing files will be created automatically before importing.)";
            bool confirm = await Dialog.ConfirmAsync("Confirm Save Import", prompt);
            if (!confirm) return;

            ExportImportStatusText.Text = "Importing save files...";
            ExportImportStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
            ExportImportStatusText.Visibility = Visibility.Visible;

            var (ok, err, count) = await SaveArchiveService.ImportSaveArchiveAsync(ofd.FileName, targetDir);
            if (ok)
            {
                ExportImportStatusText.Text = $"Successfully imported {count} file(s)!";
                ExportImportStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
                await Dialog.ShowInfoAsync("Import Successful", $"Imported {count} file(s) successfully into:\n{targetDir}");
            }
            else
            {
                ExportImportStatusText.Text = $"Import failed: {err}";
                ExportImportStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4C, 0x4C));
                await Dialog.ShowErrorAsync("Import Failed", err ?? "Unknown error");
            }
        }
    }

    // ==========================================
    // SAVE SNAPSHOT VAULT & PINNING (IMMUNE TO AUTO-PURGE)
    // ==========================================

    private void PopulateGamesForSnapshots()
    {
        var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var active = ActiveGameTrackerService.CurrentActiveGame;
        if (active != null && !string.IsNullOrWhiteSpace(active.GameName))
            games.Add(active.GameName);

        foreach (var p in UniversalSaveWatcherService.Profiles)
        {
            if (!string.IsNullOrWhiteSpace(p.GameName))
                games.Add(p.GameName);
        }

        var snapDir = SaveHistoryManager.GetSnapshotsBaseDir();
        if (Directory.Exists(snapDir))
        {
            foreach (var d in Directory.GetDirectories(snapDir))
            {
                var folderName = Path.GetFileName(d);
                if (!string.IsNullOrWhiteSpace(folderName))
                    games.Add(folderName);
            }
        }

        SnapshotGamesCombo.ItemsSource = games.OrderBy(g => g).ToList();

        if (active != null && games.Contains(active.GameName))
            SnapshotGamesCombo.SelectedItem = active.GameName;
        else if (games.Count > 0)
            SnapshotGamesCombo.SelectedIndex = 0;
    }

    private void SnapshotGamesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ReloadSnapshotsList();
    }

    private void RefreshSnapshots_Click(object sender, RoutedEventArgs e)
    {
        PopulateGamesForSnapshots();
        ReloadSnapshotsList();
    }

    private void ReloadSnapshotsList()
    {
        var selected = SnapshotGamesCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(selected))
        {
            SnapshotsListBox.ItemsSource = null;
            NoSnapshotsNotice.Visibility = Visibility.Visible;
            return;
        }

        var snapshots = SaveHistoryManager.GetSnapshots(selected);
        var viewModels = snapshots.Select(s => new SnapshotItemViewModel(s)).ToList();
        SnapshotsListBox.ItemsSource = viewModels;
        NoSnapshotsNotice.Visibility = viewModels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TakeInstantSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var selected = SnapshotGamesCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(selected))
        {
            selected = "Manual Backup";
        }

        string? targetDir = null;
        var active = ActiveGameTrackerService.CurrentActiveGame;
        if (active != null && string.Equals(active.GameName, selected, StringComparison.OrdinalIgnoreCase))
        {
            targetDir = active.SaveDirectory;
        }

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            var profile = UniversalSaveWatcherService.Profiles.FirstOrDefault(p => string.Equals(p.GameName, selected, StringComparison.OrdinalIgnoreCase));
            if (profile != null && Directory.Exists(profile.SaveDirectory))
            {
                targetDir = profile.SaveDirectory;
            }
        }

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            var dlg = new OpenFolderDialog { Title = $"Select Save Directory to Snapshot for '{selected}'" };
            if (dlg.ShowDialog() != true) return;
            targetDir = dlg.FolderName;
        }

        var snap = SaveHistoryManager.CreateSnapshot(selected, targetDir, "Manual Snapshot via Beta Tools");
        if (snap != null)
        {
            SnapshotStatusText.Text = $"Created snapshot '{snap.FolderName}' ({snap.FormattedSize})!";
            SnapshotStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
            SnapshotStatusText.Visibility = Visibility.Visible;
            ReloadSnapshotsList();
        }
        else
        {
            SnapshotStatusText.Text = "Failed to create snapshot. Please check save directory access.";
            SnapshotStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4C, 0x4C));
            SnapshotStatusText.Visibility = Visibility.Visible;
        }
    }

    private void TogglePinSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SnapshotItemViewModel vm })
        {
            bool newPinState = !vm.IsPinned;
            SaveHistoryManager.PinSnapshot(vm.Snapshot.DirectoryPath, newPinState);
            ReloadSnapshotsList();
        }
    }

    private async void RestoreSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SnapshotItemViewModel vm })
        {
            var gameName = SnapshotGamesCombo.SelectedItem as string ?? "Game";
            var active = ActiveGameTrackerService.CurrentActiveGame;
            string? targetDir = null;
            if (active != null && string.Equals(active.GameName, gameName, StringComparison.OrdinalIgnoreCase))
                targetDir = active.SaveDirectory;

            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                var profile = UniversalSaveWatcherService.Profiles.FirstOrDefault(p => string.Equals(p.GameName, gameName, StringComparison.OrdinalIgnoreCase));
                if (profile != null && Directory.Exists(profile.SaveDirectory))
                    targetDir = profile.SaveDirectory;
            }

            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                var dlg = new OpenFolderDialog { Title = $"Select Destination Save Directory to Restore Snapshot for '{gameName}'" };
                if (dlg.ShowDialog() != true) return;
                targetDir = dlg.FolderName;
            }

            bool confirm = await Dialog.ConfirmDangerAsync("Restore Save Snapshot",
                $"Are you sure you want to restore snapshot from {vm.Timestamp:yyyy-MM-dd HH:mm:ss}?\n\n(A safety backup of your current files will be created before restoring.)");

            if (!confirm) return;

            bool ok = SaveHistoryManager.RestoreSnapshot(gameName, targetDir, vm.Snapshot);
            if (ok)
            {
                await Dialog.ShowInfoAsync("Snapshot Restored", $"Successfully rolled back save to {vm.Timestamp:yyyy-MM-dd HH:mm:ss}!");
                ReloadSnapshotsList();
            }
            else
            {
                await Dialog.ShowErrorAsync("Restore Failed", "Could not restore the snapshot. Check directory permissions.");
            }
        }
    }

    private async void DeleteSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SnapshotItemViewModel vm })
        {
            if (vm.IsPinned)
            {
                bool unpinFirst = await Dialog.ConfirmAsync("Snapshot Is Pinned", "This snapshot is PINNED for protection. Are you sure you want to delete it permanently?");
                if (!unpinFirst) return;
            }
            else
            {
                bool confirm = await Dialog.ConfirmDangerAsync("Delete Snapshot", $"Permanently delete snapshot from {vm.Timestamp:yyyy-MM-dd HH:mm:ss}?");
                if (!confirm) return;
            }

            SaveHistoryManager.DeleteSnapshot(vm.Snapshot);
            ReloadSnapshotsList();
        }
    }

    // ==========================================
    // STEAMID64 RE-SIGNER / ACCOUNT TRANSFER
    // ==========================================

    private void PresetEmulatorId_Click(object sender, RoutedEventArgs e)
    {
        OldSteamIdBox.Text = SteamIdResignerService.DefaultEmulatorSteamId64.ToString();
    }

    private void DetectMySteamId_Click(object sender, RoutedEventArgs e)
    {
        var id = SteamIdResignerService.GetCurrentSteamId64();
        if (id.HasValue)
        {
            NewSteamIdBox.Text = id.Value.ToString();
        }
        else
        {
            _ = Dialog.ShowWarningAsync("Steam Account Not Found", "Could not automatically detect logged-in SteamID64 from config/loginusers.vdf.");
        }
    }

    private void BrowseResignFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Save Directory to Re-sign" };
        if (dlg.ShowDialog() == true)
        {
            ResignFolderBox.Text = dlg.FolderName;
        }
    }

    private async void RunResign_Click(object sender, RoutedEventArgs e)
    {
        var folder = ResignFolderBox.Text?.Trim();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            await Dialog.ShowErrorAsync("Folder Not Found", "Please choose a valid save directory to re-sign.");
            return;
        }

        if (!ulong.TryParse(OldSteamIdBox.Text?.Trim(), out var oldId) || oldId == 0)
        {
            await Dialog.ShowErrorAsync("Invalid SteamID", "Please specify a valid 17-digit Source SteamID64.");
            return;
        }

        if (!ulong.TryParse(NewSteamIdBox.Text?.Trim(), out var newId) || newId == 0)
        {
            await Dialog.ShowErrorAsync("Invalid SteamID", "Please specify a valid 17-digit Target SteamID64.");
            return;
        }

        bool confirm = await Dialog.ConfirmAsync(
            "Confirm Account Re-signing",
            $"Re-sign all save files and directories in:\n{folder}\n\nFrom: {oldId}\nTo: {newId}\n\n(A full safety backup snapshot will be saved automatically prior to modification.)");

        if (!confirm) return;

        ResignStatusText.Text = "Re-signing and transferring save files...";
        ResignStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
        ResignStatusText.Visibility = Visibility.Visible;

        var res = await SteamIdResignerService.ResignSaveDirectoryAsync(folder, oldId, newId);
        if (res.Success)
        {
            ResignStatusText.Text = $"Re-signed {res.FilesModified} file(s) ({res.OccurrencesReplaced} occurrences), renamed {res.FoldersRenamed} folder(s).";
            ResignStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
            await Dialog.ShowInfoAsync("Re-signing Complete",
                $"Successfully transferred save:\n\n• Files modified: {res.FilesModified}\n• ID occurrences patched: {res.OccurrencesReplaced}\n• Folders renamed: {res.FoldersRenamed}\n• Safety backup: {res.BackupDir ?? "Saved in Snapshots"}");
        }
        else
        {
            ResignStatusText.Text = $"Failed: {res.Error}";
            ResignStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4C, 0x4C));
            await Dialog.ShowErrorAsync("Re-signing Failed", res.Error ?? "Unknown error");
        }
    }

    // ==========================================
    // CHARACTER SWITCHER (MULTI-SLOT MANAGER)
    // ==========================================

    private void PopulateGamesForSlots()
    {
        SlotsGameCombo.Items.Clear();
        var active = ActiveGameTrackerService.CurrentActiveGame;
        if (active != null)
        {
            SlotsGameCombo.Items.Add(new ComboBoxItem { Content = active.GameName, Tag = active.GameName });
        }

        // Add games from Universal Saves
        var universalGames = UniversalSaveWatcherService.ConfiguredGames;
        foreach (var g in universalGames)
        {
            if (active == null || !g.Equals(active.GameName, StringComparison.OrdinalIgnoreCase))
            {
                SlotsGameCombo.Items.Add(new ComboBoxItem { Content = g, Tag = g });
            }
        }

        if (SlotsGameCombo.Items.Count > 0)
        {
            SlotsGameCombo.SelectedIndex = 0;
        }
    }

    private void SlotsGameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ReloadCharacterSlots();
    }

    private void ReloadCharacterSlots()
    {
        var gameName = (SlotsGameCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrEmpty(gameName))
        {
            CharacterSlotsList.ItemsSource = null;
            return;
        }

        var savePath = ExportSavePathBox.Text?.Trim();
        var slots = CharacterSlotManager.GetSlots(gameName, savePath);
        CharacterSlotsList.ItemsSource = slots;
    }

    private async void CreateSlotDialog_Click(object sender, RoutedEventArgs e)
    {
        var gameName = (SlotsGameCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrEmpty(gameName))
        {
            await Dialog.ShowWarningAsync("No Game Selected", "Please select a game first.");
            return;
        }

        var savePath = ExportSavePathBox.Text?.Trim();
        if (string.IsNullOrEmpty(savePath) || !Directory.Exists(savePath))
        {
            var dlg = new OpenFolderDialog { Title = "Select Save Directory to Clone for this New Slot" };
            if (dlg.ShowDialog() != true) return;
            savePath = dlg.FolderName;
            ExportSavePathBox.Text = savePath;
        }

        var slotName = $"Slot {DateTime.Now:MMdd_HHmm}";
        bool ok = CharacterSlotManager.CreateSlot(gameName, savePath, slotName, "Custom Character Playthrough");
        if (ok)
        {
            ReloadCharacterSlots();
            await Dialog.ShowInfoAsync("Slot Created", $"Created new character slot '{slotName}' successfully.");
        }
    }

    private async void SwitchSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string slotName })
        {
            var gameName = (SlotsGameCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var savePath = ExportSavePathBox.Text?.Trim();
            if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(savePath) || !Directory.Exists(savePath))
            {
                await Dialog.ShowErrorAsync("Save Path Missing", "Please specify the active game save path in Archives & Transfer tab first.");
                return;
            }

            bool confirm = await Dialog.ConfirmAsync("Switch Character Slot",
                $"Switch to slot '{slotName}'?\n\nYour current in-game progress will be saved to your active slot, and files from '{slotName}' will be loaded.");

            if (!confirm) return;

            var (ok, err) = CharacterSlotManager.SwitchSlot(gameName, savePath, slotName);
            if (ok)
            {
                ReloadCharacterSlots();
                await Dialog.ShowInfoAsync("Switched Successfully", $"Now playing on profile '{slotName}'!");
            }
            else
            {
                await Dialog.ShowErrorAsync("Switch Failed", err ?? "Unknown error");
            }
        }
    }

    private async void DeleteSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string slotName })
        {
            var gameName = (SlotsGameCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(gameName)) return;

            if (slotName.Equals("Default Profile", StringComparison.OrdinalIgnoreCase))
            {
                await Dialog.ShowWarningAsync("Cannot Delete", "The default profile slot cannot be deleted.");
                return;
            }

            bool confirm = await Dialog.ConfirmAsync("Delete Character Slot", $"Permanently delete character slot '{slotName}'?");
            if (confirm)
            {
                CharacterSlotManager.DeleteSlot(gameName, slotName);
                ReloadCharacterSlots();
            }
        }
    }

    // ==========================================
    // GAME MOD & LOAD-ORDER SNAPSHOT
    // ==========================================

    private void BrowseModGameDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Game Root Installation Directory (Containing mods/)" };
        if (dlg.ShowDialog() == true)
        {
            ModGameDirBox.Text = dlg.FolderName;
        }
    }

    private async void CaptureModSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var gameDir = ModGameDirBox.Text?.Trim();
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            await Dialog.ShowErrorAsync("Game Path Missing", "Please select a valid game installation directory.");
            return;
        }

        var savePath = ExportSavePathBox.Text?.Trim();
        var snapshotDir = Path.Combine(SteamDetector.GetConfigDir(), "mod_snapshots", Path.GetFileName(gameDir.TrimEnd('\\', '/')));

        var snap = ModSnapshotService.CaptureModSnapshot(Path.GetFileName(gameDir), gameDir, snapshotDir);
        if (snap != null)
        {
            ModSnapshotStatusText.Text = $"Mod snapshot captured: {snap.ModCount} mod/plugin files cataloged.";
            ModSnapshotStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
            ModSnapshotStatusText.Visibility = Visibility.Visible;
            await Dialog.ShowInfoAsync("Snapshot Saved", $"Cataloged {snap.ModCount} installed mods/load-order items.");
        }
        else
        {
            await Dialog.ShowWarningAsync("No Mods Found", "No standard mod folders (mods/, BepInEx/, nativePC/, plugins/) were detected in this game directory.");
        }
    }

    private async void CompareModSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var gameDir = ModGameDirBox.Text?.Trim();
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            await Dialog.ShowErrorAsync("Game Path Missing", "Please select a valid game installation directory.");
            return;
        }

        var snapshotDir = Path.Combine(SteamDetector.GetConfigDir(), "mod_snapshots", Path.GetFileName(gameDir.TrimEnd('\\', '/')));
        var res = ModSnapshotService.CompareModState(gameDir, snapshotDir);

        if (res.IsIdentical)
        {
            await Dialog.ShowInfoAsync("Mod State Clean", "All installed mods and checksums match the saved snapshot perfectly.");
        }
        else
        {
            string msg = $"Mod discrepancies detected:\n\n• Missing mods: {res.MissingMods.Count}\n• Added mods: {res.AddedMods.Count}\n• Modified mods: {res.ModifiedMods.Count}";
            await Dialog.ShowWarningAsync("Mod State Mismatch", msg);
        }
    }

    // ==========================================
    // STORAGE BREAKDOWN & SHADER CLEANER
    // ==========================================

    private async void RefreshStorage_Click(object sender, RoutedEventArgs e)
    {
        await RefreshStorageAsync();
    }

    private async Task RefreshStorageAsync()
    {
        try
        {
            var breakdown = await StorageBreakdownService.AnalyzeStorageAsync();
            MetricCloudSavesText.Text = breakdown.FormattedCloudStorage;
            MetricSnapshotsText.Text = breakdown.FormattedSnapshots;
            MetricSlotsText.Text = breakdown.FormattedCharacterSlots;
            MetricDiskFreeText.Text = breakdown.FormattedDiskFree;
        }
        catch { }
    }

    private async void ScanShaders_Click(object sender, RoutedEventArgs e)
    {
        ShaderScanSummaryText.Text = "Scanning Steam library folders for orphaned shader caches...";
        ShaderScanSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));

        _scannedShaders = await ShaderCacheCleanerService.ScanOrphanShaderCachesAsync();
        ShaderCachesListBox.ItemsSource = _scannedShaders;

        long totalBytes = _scannedShaders.Sum(s => s.SizeBytes);
        string formatted = totalBytes < 1024 * 1024 * 1024
            ? $"{totalBytes / (1024.0 * 1024.0):F1} MB"
            : $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";

        if (_scannedShaders.Count > 0)
        {
            ShaderScanSummaryText.Text = $"Found {_scannedShaders.Count} orphaned shader cache(s) consuming {formatted} of disk space.";
            ShaderScanSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x30));
            CleanShadersBtn.IsEnabled = true;
        }
        else
        {
            ShaderScanSummaryText.Text = "Clean! No orphaned shader caches were found across your Steam libraries.";
            ShaderScanSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
            CleanShadersBtn.IsEnabled = false;
        }
    }

    private async void CleanShaders_Click(object sender, RoutedEventArgs e)
    {
        if (_scannedShaders.Count == 0) return;

        bool confirm = await Dialog.ConfirmDangerAsync("Clean Orphan Shader Caches",
            $"Safely remove {_scannedShaders.Count} orphaned Steam shader cache folders from your drive?\n\nThese belong to games that are no longer installed.");

        if (!confirm) return;

        var (count, freed) = await ShaderCacheCleanerService.CleanOrphanShaderCachesAsync(_scannedShaders.Select(s => s.DirectoryPath));
        string formattedFreed = freed < 1024 * 1024 * 1024
            ? $"{freed / (1024.0 * 1024.0):F1} MB"
            : $"{freed / (1024.0 * 1024.0 * 1024.0):F2} GB";

        _scannedShaders.Clear();
        ShaderCachesListBox.ItemsSource = null;
        CleanShadersBtn.IsEnabled = false;

        ShaderScanSummaryText.Text = $"Cleaned {count} orphan caches! Freed {formattedFreed}.";
        ShaderScanSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07));
        await Dialog.ShowInfoAsync("Shader Cache Cleaned", $"Successfully deleted {count} orphaned shader caches and freed {formattedFreed}!");
    }

    // ==========================================
    // MOBILE DASHBOARD & LAN P2P
    // ==========================================

    private void MobileServerToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isEnabled = MobileServerToggle.IsChecked == true;
        AppSettings.EnableMobileDashboard = isEnabled;

        if (isEnabled)
        {
            MobileDashboardServer.Start();
        }
        else
        {
            MobileDashboardServer.Stop();
        }

        UpdateMobileUi(isEnabled);
    }

    private void UpdateMobileUi(bool isRunning)
    {
        MobileServerStatusText.Text = isRunning ? "Dashboard Active 🟢" : "Server Stopped ⚪";
        MobileServerStatusText.Foreground = new SolidColorBrush(isRunning ? Color.FromRgb(0xA4, 0xD0, 0x07) : Color.FromRgb(0x8F, 0x98, 0xA0));

        if (isRunning)
        {
            var url = MobileDashboardServer.GetDashboardUrl();
            MobileUrlBox.Text = url;
            try
            {
                MobileQrImage.Source = QrCodeHelper.GenerateQrCode(url);
            }
            catch { }
        }
        else
        {
            MobileUrlBox.Text = "Turn on the toggle switch above to start the mobile server";
            MobileQrImage.Source = null;
        }
    }

    private void OpenMobileDashboardBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = MobileDashboardServer.GetDashboardUrl();
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void UpdateLanPeersUi()
    {
        var peers = LanP2PSyncService.GetActivePeers();
        LanPeersListBox.ItemsSource = peers;
        NoPeersNotice.Visibility = peers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void P2PPush_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: LanPeer peer })
        {
            var active = ActiveGameTrackerService.CurrentActiveGame;
            var savePath = active?.SaveDirectory ?? ExportSavePathBox.Text?.Trim();

            if (string.IsNullOrEmpty(savePath) || !Directory.Exists(savePath))
            {
                await Dialog.ShowErrorAsync("Save Path Missing", "Please select a valid save folder in the Archives tab first.");
                return;
            }

            var gameId = active?.GameName ?? Path.GetFileName(savePath.TrimEnd('\\', '/'));
            var (ok, err) = await LanP2PSyncService.PushSaveToPeerAsync(peer, gameId, savePath);

            if (ok)
            {
                await Dialog.ShowInfoAsync("P2P Push Succeeded", $"Successfully sent save files for '{gameId}' to {peer.DeviceName} ({peer.IpAddress})!");
            }
            else
            {
                await Dialog.ShowErrorAsync("P2P Push Failed", err ?? "Transfer failed");
            }
        }
    }

    private async void P2PPull_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: LanPeer peer })
        {
            var active = ActiveGameTrackerService.CurrentActiveGame;
            var savePath = active?.SaveDirectory ?? ExportSavePathBox.Text?.Trim();

            if (string.IsNullOrEmpty(savePath))
            {
                await Dialog.ShowErrorAsync("Save Path Missing", "Please select a valid destination save folder in the Archives tab first.");
                return;
            }

            var gameId = active?.GameName ?? Path.GetFileName(savePath.TrimEnd('\\', '/'));
            var (ok, err) = await LanP2PSyncService.PullSaveFromPeerAsync(peer, gameId, savePath);

            if (ok)
            {
                await Dialog.ShowInfoAsync("P2P Pull Succeeded", $"Successfully received and applied save files for '{gameId}' from {peer.DeviceName}!");
            }
            else
            {
                await Dialog.ShowErrorAsync("P2P Pull Failed", err ?? "Transfer failed");
            }
        }
    }

    // ==========================================
    // BOOSTER, PRESENCE & CONTEXT MENU TOGGLES
    // ==========================================

    private void BoosterToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppSettings.EnableGamePerformanceBooster = BoosterToggle.IsChecked == true;
    }

    private void SpacewarSpooferToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppSettings.EnableSpacewarBroadcaster = SpacewarSpooferToggle.IsChecked == true;
    }

    private void ContextMenuToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool enable = ContextMenuToggle.IsChecked == true;
        AppSettings.EnableShellContextMenu = enable;
        SteamContextMenuService.RegisterShellContextMenu(enable);
    }
}

public class SnapshotItemViewModel
{
    public SnapshotInfo Snapshot { get; }
    public string FolderName => Snapshot.FolderName;
    public DateTime Timestamp => Snapshot.Timestamp;
    public string TriggerReason => Snapshot.TriggerReason;
    public bool IsPinned => Snapshot.IsPinned;
    public string FormattedDetails => $"{Snapshot.FileCount} files • {FormatBytes(Snapshot.TotalBytes)}";
    public string PinButtonText => IsPinned ? "Unpin 📌" : "Pin 📌";
    public ControlAppearance PinButtonAppearance => IsPinned ? ControlAppearance.Secondary : ControlAppearance.Primary;

    public SnapshotItemViewModel(SnapshotInfo snapshot)
    {
        Snapshot = snapshot;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / (1024.0 * 1024):F1} KB";
        return $"{bytes} B";
    }
}
