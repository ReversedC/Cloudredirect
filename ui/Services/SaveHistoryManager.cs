using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace CloudRedirect.Services;

public record SnapshotInfo(
    string Id,
    DateTime Timestamp,
    string Trigger,
    int FileCount,
    long TotalBytes,
    string DirectoryPath,
    bool IsPinned = false
)
{
    public string FolderName => Id;
    public string TriggerReason => Trigger;
    public string FormattedTime => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    public string FormattedSize
    {
        get
        {
            if (TotalBytes < 1024) return $"{TotalBytes} B";
            if (TotalBytes < 1024 * 1024) return $"{TotalBytes / 1024.0:F1} KB";
            return $"{TotalBytes / (1024.0 * 1024.0):F2} MB";
        }
    }
}

/// <summary>
/// Advanced Save Protection: creates immutable, timestamped save snapshots,
/// allows 1-click rollback, and safely protects against save corruption or accidental overwrites.
/// </summary>
public static class SaveHistoryManager
{
    private const int MaxSnapshotsPerGame = 15;

    public static string GetSnapshotsBaseDir()
    {
        var dir = Path.Combine(SteamDetector.GetConfigDir(), "save_snapshots");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    public static string SanitizeFolderName(string name)
    {
        var invalids = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalids.Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "unknown_game" : sanitized;
    }

    public static string? FindAppStorageDir(string? steamPath, uint appId, string? accountId = null)
    {
        if (string.IsNullOrEmpty(steamPath) || appId == 0) return null;

        var storageRoot = Path.Combine(steamPath, "cloud_redirect", "storage");
        if (!Directory.Exists(storageRoot)) return null;

        if (!string.IsNullOrEmpty(accountId) && accountId != "0")
        {
            var direct = Path.Combine(storageRoot, accountId, appId.ToString());
            if (Directory.Exists(direct)) return direct;
        }

        foreach (var accDir in Directory.GetDirectories(storageRoot))
        {
            var candidate = Path.Combine(accDir, appId.ToString());
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static SnapshotInfo? CreateSnapshot(string gameIdentifier, string sourceDirectory, string triggerDescription, string? appId = null)
    {
        try
        {
            if (!Directory.Exists(sourceDirectory)) return null;

            var safeName = SanitizeFolderName(gameIdentifier);
            var gameSnapshotDir = Path.Combine(GetSnapshotsBaseDir(), safeName);
            if (!Directory.Exists(gameSnapshotDir))
                Directory.CreateDirectory(gameSnapshotDir);

            var sourceFiles = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
            if (sourceFiles.Length == 0) return null;

            long totalBytes = 0;
            foreach (var f in sourceFiles)
            {
                totalBytes += new FileInfo(f).Length;
            }

            // Check the newest existing snapshot to prevent duplicate snapshots if nothing changed
            var existingSnapshots = GetSnapshots(gameIdentifier, appId);
            if (existingSnapshots.Count > 0)
            {
                var latest = existingSnapshots[0];
                if ((DateTime.Now - latest.Timestamp).TotalSeconds < 20 &&
                    latest.FileCount == sourceFiles.Length &&
                    latest.TotalBytes == totalBytes)
                {
                    return latest;
                }
            }

            var now = DateTime.Now;
            var timestampStr = now.ToString("yyyyMMdd_HHmmss");
            var targetDir = Path.Combine(gameSnapshotDir, timestampStr);

            Directory.CreateDirectory(targetDir);

            bool isCompressed = AppSettings.AutoStorageCompression;
            if (isCompressed)
            {
                var zipPath = Path.Combine(targetDir, "data.zip");
                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    foreach (var file in sourceFiles)
                    {
                        var relPath = Path.GetRelativePath(sourceDirectory, file);
                        archive.CreateEntryFromFile(file, relPath, CompressionLevel.Optimal);
                    }
                }
            }
            else
            {
                foreach (var file in sourceFiles)
                {
                    var relPath = Path.GetRelativePath(sourceDirectory, file);
                    var destPath = Path.Combine(targetDir, relPath);
                    var destDir = Path.GetDirectoryName(destPath)!;
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(file, destPath, overwrite: true);
                }
            }

            var meta = new
            {
                timestamp = now.ToString("o"),
                trigger = triggerDescription,
                fileCount = sourceFiles.Length,
                totalBytes,
                compressed = isCompressed,
                appId = appId ?? ""
            };

            var metaPath = Path.Combine(targetDir, "snapshot.json");
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

            // Cleanup oldest according to retention rules
            PurgeOldSnapshots(gameSnapshotDir);

            return new SnapshotInfo(timestampStr, now, triggerDescription, sourceFiles.Length, totalBytes, targetDir);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create snapshot for {gameIdentifier}: {ex}");
            return null;
        }
    }

    public static List<SnapshotInfo> GetSnapshots(string gameIdentifier, string? appId = null)
    {
        var result = new List<SnapshotInfo>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void ScanFolder(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var safe = SanitizeFolderName(name);
            var dir = Path.Combine(GetSnapshotsBaseDir(), safe);
            if (!Directory.Exists(dir)) return;

            var subDirs = Directory.GetDirectories(dir);
            foreach (var sub in subDirs)
            {
                if (seenDirs.Add(sub))
                {
                    var info = LoadSnapshotFromDir(sub);
                    if (info != null)
                        result.Add(info);
                }
            }
        }

        ScanFolder(gameIdentifier);

        if (!string.IsNullOrEmpty(appId) && !appId.Equals(gameIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            ScanFolder(appId);
        }

        // If gameIdentifier is an appId, try finding its game name
        if (uint.TryParse(gameIdentifier, out var parsedAppId))
        {
            var steamPath = SteamDetector.FindSteamPath();
            var name = SteamDetector.GetGameName(steamPath, parsedAppId);
            if (!string.IsNullOrEmpty(name))
            {
                ScanFolder(name);
            }
        }

        return result.OrderByDescending(s => s.Timestamp).ToList();
    }

    private static SnapshotInfo? LoadSnapshotFromDir(string dir)
    {
        try
        {
            var folderName = Path.GetFileName(dir);
            var metaPath = Path.Combine(dir, "snapshot.json");

            DateTime timestamp = Directory.GetCreationTime(dir);
            string trigger = "Save Backup";
            int fileCount = 0;
            long totalBytes = 0;
            bool isPinned = false;

            if (File.Exists(metaPath))
            {
                try
                {
                    var json = File.ReadAllText(metaPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("timestamp", out var tsProp) &&
                        DateTime.TryParse(tsProp.GetString(), out var parsedTs))
                    {
                        timestamp = parsedTs;
                    }
                    if (doc.RootElement.TryGetProperty("trigger", out var trigProp))
                        trigger = trigProp.GetString() ?? trigger;
                    if (doc.RootElement.TryGetProperty("fileCount", out var fcProp))
                        fileCount = fcProp.GetInt32();
                    if (doc.RootElement.TryGetProperty("totalBytes", out var tbProp))
                        totalBytes = tbProp.GetInt64();
                    if (doc.RootElement.TryGetProperty("pinned", out var pinProp))
                        isPinned = pinProp.GetBoolean();
                }
                catch { }
            }

            if (!isPinned && File.Exists(Path.Combine(dir, ".pinned")))
                isPinned = true;

            if (fileCount == 0)
            {
                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).Equals("snapshot.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                fileCount = files.Length;
                totalBytes = files.Sum(f => new FileInfo(f).Length);
            }

            return new SnapshotInfo(folderName, timestamp, trigger, fileCount, totalBytes, dir, isPinned);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Pins or unpins a snapshot. Pinned snapshots are immune to automatic purging/cleanup.
    /// </summary>
    public static bool PinSnapshot(string directoryPath, bool pinned)
    {
        try
        {
            if (!Directory.Exists(directoryPath)) return false;
            var marker = Path.Combine(directoryPath, ".pinned");
            if (pinned)
            {
                if (!File.Exists(marker)) File.WriteAllText(marker, "pinned");
            }
            else
            {
                if (File.Exists(marker)) File.Delete(marker);
            }

            var metaPath = Path.Combine(directoryPath, "snapshot.json");
            if (File.Exists(metaPath))
            {
                try
                {
                    var text = File.ReadAllText(metaPath);
                    var node = System.Text.Json.Nodes.JsonNode.Parse(text);
                    if (node is System.Text.Json.Nodes.JsonObject obj)
                    {
                        obj["pinned"] = pinned;
                        File.WriteAllText(metaPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
                catch { }
            }
            return true;
        }
        catch { return false; }
    }

    public static bool DeleteSnapshot(SnapshotInfo snapshot)
    {
        try
        {
            if (Directory.Exists(snapshot.DirectoryPath))
            {
                Directory.Delete(snapshot.DirectoryPath, true);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to delete snapshot: {ex}");
        }
        return false;
    }

    public static bool RestoreSnapshot(string gameIdentifier, string targetDirectory, SnapshotInfo snapshot)
    {
        try
        {
            if (!Directory.Exists(snapshot.DirectoryPath)) return false;

            // 1. Create a safety backup of targetDirectory first before restoring
            if (Directory.Exists(targetDirectory))
            {
                CreateSnapshot(gameIdentifier, targetDirectory, "Safety Backup before Rollback");
            }
            else
            {
                Directory.CreateDirectory(targetDirectory);
            }

            // 2. Extract from data.zip if compressed, or copy loose files
            var zipPath = Path.Combine(snapshot.DirectoryPath, "data.zip");
            if (File.Exists(zipPath))
            {
                ZipFile.ExtractToDirectory(zipPath, targetDirectory, overwriteFiles: true);
            }
            else
            {
                var snapshotFiles = Directory.GetFiles(snapshot.DirectoryPath, "*", SearchOption.AllDirectories);
                foreach (var file in snapshotFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("snapshot.json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var relPath = Path.GetRelativePath(snapshot.DirectoryPath, file);
                    var destPath = Path.Combine(targetDirectory, relPath);
                    var destDir = Path.GetDirectoryName(destPath)!;
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(file, destPath, overwrite: true);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to restore snapshot: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Checks whether the files in a save directory appear corrupted (e.g. all 0 bytes or completely empty after a crash).
    /// </summary>
    public static bool CheckSaveCorruption(string saveDirectory)
    {
        try
        {
            if (!Directory.Exists(saveDirectory)) return false;
            var files = Directory.GetFiles(saveDirectory, "*", SearchOption.AllDirectories);
            if (files.Length == 0) return false;

            long totalBytes = files.Sum(f => new FileInfo(f).Length);
            return totalBytes == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to auto-heal a corrupted save directory by safely restoring the latest valid snapshot.
    /// </summary>
    public static bool TryAutoHealFromLastSnapshot(string gameIdentifier, string saveDirectory, out string healMessage, string? appId = null)
    {
        healMessage = string.Empty;
        try
        {
            if (!CheckSaveCorruption(saveDirectory))
                return false;

            var snapshots = GetSnapshots(gameIdentifier, appId)
                .Where(s => s.TotalBytes > 0 && s.FileCount > 0)
                .ToList();

            if (snapshots.Count == 0)
            {
                healMessage = "Save is corrupted, but no valid prior snapshots were found.";
                return false;
            }

            var bestSnapshot = snapshots[0];
            // Take safety snapshot of the corrupted state first
            CreateSnapshot(gameIdentifier, saveDirectory, "Corrupted State Prior to Auto-Heal", appId);

            bool restored = RestoreSnapshot(gameIdentifier, saveDirectory, bestSnapshot);
            if (restored)
            {
                healMessage = $"Corrupted save auto-healed from healthy snapshot ({bestSnapshot.FormattedTime}).";
                return true;
            }
        }
        catch (Exception ex)
        {
            healMessage = $"Auto-heal error: {ex.Message}";
        }
        return false;
    }

    /// <summary>
    /// Creates an immutable backup in the Conflict Vault prior to any overwrite or cloud conflict sync.
    /// </summary>
    public static SnapshotInfo? CreateConflictBackup(string gameIdentifier, string sourceDirectory, string conflictReason, string? appId = null)
    {
        return CreateSnapshot(gameIdentifier, sourceDirectory, $"[Conflict Vault] {conflictReason}", appId);
    }

    private static void PurgeOldSnapshots(string gameSnapshotDir)
    {
        try
        {
            bool IsPinned(string d)
            {
                if (File.Exists(Path.Combine(d, ".pinned"))) return true;
                var meta = Path.Combine(d, "snapshot.json");
                if (File.Exists(meta))
                {
                    try
                    {
                        var json = File.ReadAllText(meta);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("pinned", out var p) && p.GetBoolean())
                            return true;
                    }
                    catch { }
                }
                return false;
            }

            var dirs = Directory.GetDirectories(gameSnapshotDir)
                .Where(d => !IsPinned(d))
                .OrderBy(Directory.GetCreationTime)
                .ToList();

            if (dirs.Count <= 5) return;

            if (AppSettings.AutoStorageCompression)
            {
                // Smart Time-Based Retention:
                // Snapshots within last 24 hours: keep all.
                // Snapshots within 1 to 7 days: keep 1 per day.
                // Snapshots within 7 to 30 days: keep 1 per week.
                // Snapshots older than 30 days: keep 1 per month.
                var now = DateTime.Now;
                var toKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var olderSnapshots = new List<(string Dir, DateTime Time)>();

                foreach (var d in dirs)
                {
                    var time = Directory.GetCreationTime(d);
                    var age = now - time;
                    if (age.TotalHours <= 24)
                    {
                        toKeep.Add(d);
                    }
                    else
                    {
                        olderSnapshots.Add((d, time));
                    }
                }

                // Group 1-7 days by date
                var dayGroups = olderSnapshots
                    .Where(x => (now - x.Time).TotalDays <= 7)
                    .GroupBy(x => x.Time.Date);
                foreach (var g in dayGroups)
                {
                    toKeep.Add(g.OrderByDescending(x => x.Time).First().Dir);
                }

                // Group 7-30 days by ISO week
                var weekGroups = olderSnapshots
                    .Where(x => (now - x.Time).TotalDays > 7 && (now - x.Time).TotalDays <= 30)
                    .GroupBy(x => x.Time.DayOfYear / 7);
                foreach (var g in weekGroups)
                {
                    toKeep.Add(g.OrderByDescending(x => x.Time).First().Dir);
                }

                // Group > 30 days by month
                var monthGroups = olderSnapshots
                    .Where(x => (now - x.Time).TotalDays > 30)
                    .GroupBy(x => new { x.Time.Year, x.Time.Month });
                foreach (var g in monthGroups)
                {
                    toKeep.Add(g.OrderByDescending(x => x.Time).First().Dir);
                }

                foreach (var d in dirs)
                {
                    if (!toKeep.Contains(d))
                    {
                        try { Directory.Delete(d, true); } catch { }
                    }
                }
            }
            else
            {
                while (dirs.Count > MaxSnapshotsPerGame)
                {
                    var toDelete = dirs[0];
                    dirs.RemoveAt(0);
                    try { Directory.Delete(toDelete, true); } catch { }
                }
            }
        }
        catch { }
    }
}
