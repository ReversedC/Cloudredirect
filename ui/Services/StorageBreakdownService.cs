using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

public record GameStorageUsage(
    string GameIdentifier,
    string GameName,
    uint AppId,
    long SaveSizeBytes,
    long SnapshotSizeBytes,
    long TotalBytes,
    double PercentOfTotal
)
{
    public string FormattedTotal => FormatBytes(TotalBytes);
    public string FormattedSaveSize => FormatBytes(SaveSizeBytes);
    public string FormattedSnapshotSize => FormatBytes(SnapshotSizeBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

public record StorageBreakdown(
    long CloudStorageBytes,
    long SnapshotsBytes,
    long CharacterSlotsBytes,
    long TotalUsageBytes,
    long TotalDiskFreeBytes,
    long TotalDiskCapacityBytes,
    List<GameStorageUsage> TopGames
)
{
    public string FormattedTotalUsage => FormatBytes(TotalUsageBytes);
    public string FormattedCloudStorage => FormatBytes(CloudStorageBytes);
    public string FormattedSnapshots => FormatBytes(SnapshotsBytes);
    public string FormattedCharacterSlots => FormatBytes(CharacterSlotsBytes);
    public string FormattedDiskFree => FormatBytes(TotalDiskFreeBytes);
    public string FormattedDiskCapacity => FormatBytes(TotalDiskCapacityBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

/// <summary>
/// Cloud Storage Quota & Breakdown Visualizer Service:
/// Computes deep breakdown across cloud saves, snapshot vaults, character slots,
/// and available disk capacity.
/// </summary>
public static class StorageBreakdownService
{
    public static async Task<StorageBreakdown> AnalyzeStorageAsync()
    {
        return await Task.Run(async () =>
        {
            long cloudBytes = 0;
            long snapshotBytes = 0;
            long slotBytes = 0;

            var gameMap = new Dictionary<string, (string Name, uint AppId, long Save, long Snap)>(StringComparer.OrdinalIgnoreCase);

            var steamPath = SteamDetector.FindSteamPath();

            // 1. Cloud storage directory
            if (!string.IsNullOrEmpty(steamPath))
            {
                var storageRoot = Path.Combine(steamPath, "cloud_redirect", "storage");
                if (Directory.Exists(storageRoot))
                {
                    foreach (var accDir in Directory.GetDirectories(storageRoot))
                    {
                        foreach (var appDir in Directory.GetDirectories(accDir))
                        {
                            var appIdStr = Path.GetFileName(appDir);
                            uint.TryParse(appIdStr, out var appId);
                            var files = Directory.GetFiles(appDir, "*", SearchOption.AllDirectories);
                            long sz = files.Sum(f => new FileInfo(f).Length);
                            cloudBytes += sz;

                            if (!gameMap.TryGetValue(appIdStr, out var val))
                                gameMap[appIdStr] = (appIdStr, appId, sz, 0);
                            else
                                gameMap[appIdStr] = (val.Name, val.AppId, val.Save + sz, val.Snap);
                        }
                    }
                }
            }

            // 2. Snapshot directory
            var snapshotBase = SaveHistoryManager.GetSnapshotsBaseDir();
            if (Directory.Exists(snapshotBase))
            {
                foreach (var gameSnapDir in Directory.GetDirectories(snapshotBase))
                {
                    var id = Path.GetFileName(gameSnapDir);
                    var files = Directory.GetFiles(gameSnapDir, "*", SearchOption.AllDirectories);
                    long sz = files.Sum(f => new FileInfo(f).Length);
                    snapshotBytes += sz;

                    uint.TryParse(id, out var parsedAppId);
                    if (!gameMap.TryGetValue(id, out var val))
                        gameMap[id] = (id, parsedAppId, 0, sz);
                    else
                        gameMap[id] = (val.Name, val.AppId != 0 ? val.AppId : parsedAppId, val.Save, val.Snap + sz);
                }
            }

            // 3. Character slots directory
            var slotsRoot = Path.Combine(SteamDetector.GetConfigDir(), "character_slots");
            if (Directory.Exists(slotsRoot))
            {
                var files = Directory.GetFiles(slotsRoot, "*", SearchOption.AllDirectories);
                slotBytes = files.Sum(f => new FileInfo(f).Length);
            }

            long totalUsage = cloudBytes + snapshotBytes + slotBytes;

            // Resolve game names for top entries
            var appIdsToFetch = gameMap.Values.Where(v => v.AppId != 0).Select(v => v.AppId).Distinct().ToList();
            Dictionary<uint, StoreAppInfo> storeInfo = new();
            if (appIdsToFetch.Count > 0)
            {
                try
                {
                    storeInfo = await SteamStoreClient.Shared.GetAppInfoAsync(appIdsToFetch);
                }
                catch { }
            }

            var gameList = new List<GameStorageUsage>();
            foreach (var kvp in gameMap)
            {
                var id = kvp.Key;
                var (rawName, appId, saveSz, snapSz) = kvp.Value;
                var total = saveSz + snapSz;
                double pct = totalUsage > 0 ? (double)total / totalUsage * 100.0 : 0.0;

                string resolvedName = rawName;
                if (appId != 0 && storeInfo.TryGetValue(appId, out var si) && !string.IsNullOrEmpty(si.Name))
                {
                    resolvedName = si.Name;
                }
                else if (uint.TryParse(id, out var pId) && storeInfo.TryGetValue(pId, out var si2) && !string.IsNullOrEmpty(si2.Name))
                {
                    resolvedName = si2.Name;
                }

                gameList.Add(new GameStorageUsage(
                    id,
                    resolvedName,
                    appId,
                    saveSz,
                    snapSz,
                    total,
                    pct
                ));
            }

            // Disk info
            long diskFree = 0;
            long diskTotal = 0;
            try
            {
                var driveLetter = Path.GetPathRoot(SteamDetector.GetConfigDir());
                if (!string.IsNullOrEmpty(driveLetter))
                {
                    var drive = new DriveInfo(driveLetter);
                    if (drive.IsReady)
                    {
                        diskFree = drive.AvailableFreeSpace;
                        diskTotal = drive.TotalSize;
                    }
                }
            }
            catch { }

            return new StorageBreakdown(
                cloudBytes,
                snapshotBytes,
                slotBytes,
                totalUsage,
                diskFree,
                diskTotal,
                gameList.OrderByDescending(g => g.TotalBytes).Take(15).ToList()
            );
        });
    }
}
