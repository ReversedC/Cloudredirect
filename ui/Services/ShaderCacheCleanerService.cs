using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

public record OrphanShaderCache(
    uint AppId,
    string GameName,
    string DirectoryPath,
    long SizeBytes,
    int FileCount,
    DateTime LastModified
)
{
    public string FormattedSize
    {
        get
        {
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F1} KB";
            if (SizeBytes < 1024 * 1024 * 1024) return $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
            return $"{SizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }
}

/// <summary>
/// Scans Steam shader caches (steamapps/shadercache) across all Steam library folders,
/// identifies orphaned shader caches for uninstalled games, and safely cleans them up.
/// </summary>
public static class ShaderCacheCleanerService
{
    public static List<string> GetAllSteamLibraryFolders()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamPath = SteamDetector.FindSteamPath();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            return result.ToList();

        result.Add(steamPath);

        try
        {
            var libVdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libVdf))
            {
                var lines = File.ReadAllLines(libVdf);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var path = parts[1].Trim('"').Replace(@"\\", @"\");
                            if (Directory.Exists(path))
                                result.Add(path);
                        }
                    }
                }
            }
        }
        catch { }

        return result.ToList();
    }

    public static HashSet<uint> GetInstalledAppIds(IEnumerable<string> libraryFolders)
    {
        var installed = new HashSet<uint>();
        foreach (var lib in libraryFolders)
        {
            var steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            try
            {
                var acfFiles = Directory.GetFiles(steamapps, "appmanifest_*.acf");
                foreach (var acf in acfFiles)
                {
                    var fn = Path.GetFileNameWithoutExtension(acf);
                    var parts = fn.Split('_');
                    if (parts.Length == 2 && uint.TryParse(parts[1], out var id))
                    {
                        installed.Add(id);
                    }
                }
            }
            catch { }
        }
        return installed;
    }

    public static async Task<List<OrphanShaderCache>> ScanOrphanShaderCachesAsync()
    {
        return await Task.Run(async () =>
        {
            var list = new List<OrphanShaderCache>();
            var libs = GetAllSteamLibraryFolders();
            var installedAppIds = GetInstalledAppIds(libs);

            var candidateAppIds = new List<uint>();
            var candidatePaths = new List<(uint AppId, string Path)>();

            foreach (var lib in libs)
            {
                var shaderDir = Path.Combine(lib, "steamapps", "shadercache");
                if (!Directory.Exists(shaderDir)) continue;

                try
                {
                    var appDirs = Directory.GetDirectories(shaderDir);
                    foreach (var appDir in appDirs)
                    {
                        var name = Path.GetFileName(appDir);
                        if (uint.TryParse(name, out var appId))
                        {
                            // If this app is NOT installed, it is an orphan!
                            if (!installedAppIds.Contains(appId))
                            {
                                candidateAppIds.Add(appId);
                                candidatePaths.Add((appId, appDir));
                            }
                        }
                    }
                }
                catch { }
            }

            if (candidatePaths.Count == 0) return list;

            // Fetch game titles in bulk from store client cache
            Dictionary<uint, StoreAppInfo> storeInfo = new();
            try
            {
                storeInfo = await SteamStoreClient.Shared.GetAppInfoAsync(candidateAppIds.Distinct().ToList());
            }
            catch { }

            foreach (var (appId, path) in candidatePaths)
            {
                long size = 0;
                int fileCount = 0;
                DateTime lastMod = DateTime.MinValue;

                try
                {
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    fileCount = files.Length;
                    foreach (var f in files)
                    {
                        var fi = new FileInfo(f);
                        size += fi.Length;
                        if (fi.LastWriteTime > lastMod)
                            lastMod = fi.LastWriteTime;
                    }
                }
                catch { }

                var gameName = storeInfo.TryGetValue(appId, out var s) && !string.IsNullOrEmpty(s.Name)
                    ? s.Name
                    : $"Steam App {appId}";

                list.Add(new OrphanShaderCache(
                    appId,
                    gameName,
                    path,
                    size,
                    fileCount,
                    lastMod == DateTime.MinValue ? Directory.GetLastWriteTime(path) : lastMod
                ));
            }

            return list.OrderByDescending(x => x.SizeBytes).ToList();
        });
    }

    public static async Task<(int FoldersDeleted, long TotalBytesFreed)> CleanOrphanShaderCachesAsync(IEnumerable<string> directoryPaths)
    {
        return await Task.Run(() =>
        {
            int deleted = 0;
            long freed = 0;

            foreach (var dir in directoryPaths)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                        long size = files.Sum(f => new FileInfo(f).Length);
                        Directory.Delete(dir, recursive: true);
                        freed += size;
                        deleted++;
                    }
                }
                catch { }
            }

            return (deleted, freed);
        });
    }
}
