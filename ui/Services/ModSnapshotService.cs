using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace CloudRedirect.Services;

public record ModItem(
    string RelativePath,
    long SizeBytes,
    string Hash,
    DateTime LastModified
);

public record ModSnapshotManifest(
    string GameIdentifier,
    DateTime CapturedAt,
    int ModCount,
    List<ModItem> Mods,
    string? LoadOrderRaw
);

public record ModComparisonResult(
    bool IsIdentical,
    List<string> MissingMods,
    List<string> AddedMods,
    List<string> ModifiedMods
);

/// <summary>
/// Game Mod &amp; Load-Order Snapshot Service:
/// Automatically tracks installed mods, plugins, and load-orders alongside save files,
/// warning players if crucial mods are missing before loading a save.
/// </summary>
public static class ModSnapshotService
{
    private static readonly string[] CommonModSubdirs =
    {
        "mods",
        "Mods",
        "BepInEx\\plugins",
        "nativePC",
        "Content\\Paks\\~mods",
        "Game\\Mods",
        "plugins"
    };

    private static readonly string[] CommonLoadOrderFiles =
    {
        "loadorder.txt",
        "plugins.txt",
        "modlist.txt",
        "mods.json",
        "enabled_mods.json"
    };

    public static List<string> DetectModFolders(string gameInstallPath)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(gameInstallPath) || !Directory.Exists(gameInstallPath))
            return result;

        foreach (var sub in CommonModSubdirs)
        {
            var p = Path.Combine(gameInstallPath, sub);
            if (Directory.Exists(p))
                result.Add(p);
        }

        return result;
    }

    public static ModSnapshotManifest? CaptureModSnapshot(string gameIdentifier, string gameInstallPath, string targetDirectory)
    {
        try
        {
            var modFolders = DetectModFolders(gameInstallPath);
            var items = new List<ModItem>();

            foreach (var mf in modFolders)
            {
                var files = Directory.GetFiles(mf, "*", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    try
                    {
                        var rel = Path.GetRelativePath(gameInstallPath, f);
                        var fi = new FileInfo(f);
                        var hash = ComputeQuickHash(f);
                        items.Add(new ModItem(rel, fi.Length, hash, fi.LastWriteTime));
                    }
                    catch { }
                }
            }

            string? loadOrderContent = null;
            foreach (var lof in CommonLoadOrderFiles)
            {
                var p = Path.Combine(gameInstallPath, lof);
                if (File.Exists(p))
                {
                    try
                    {
                        loadOrderContent = File.ReadAllText(p);
                        break;
                    }
                    catch { }
                }
            }

            var manifest = new ModSnapshotManifest(
                gameIdentifier,
                DateTime.Now,
                items.Count,
                items,
                loadOrderContent
            );

            if (!Directory.Exists(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            var metaPath = Path.Combine(targetDirectory, "mod_snapshot.json");
            File.WriteAllText(metaPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            return manifest;
        }
        catch
        {
            return null;
        }
    }

    public static ModComparisonResult CompareModState(string gameInstallPath, string snapshotDirectory)
    {
        var manifestPath = Path.Combine(snapshotDirectory, "mod_snapshot.json");
        if (!File.Exists(manifestPath))
        {
            return new ModComparisonResult(true, new(), new(), new());
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var snap = JsonSerializer.Deserialize<ModSnapshotManifest>(json);
            if (snap == null || snap.Mods == null)
                return new ModComparisonResult(true, new(), new(), new());

            var snapDict = snap.Mods.ToDictionary(m => m.RelativePath, StringComparer.OrdinalIgnoreCase);

            var currentFolders = DetectModFolders(gameInstallPath);
            var currentFiles = new Dictionary<string, (long Size, string Hash)>(StringComparer.OrdinalIgnoreCase);

            foreach (var mf in currentFolders)
            {
                foreach (var f in Directory.GetFiles(mf, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(gameInstallPath, f);
                    var fi = new FileInfo(f);
                    currentFiles[rel] = (fi.Length, ComputeQuickHash(f));
                }
            }

            var missing = new List<string>();
            var modified = new List<string>();
            var added = new List<string>();

            foreach (var sm in snap.Mods)
            {
                if (!currentFiles.TryGetValue(sm.RelativePath, out var cur))
                {
                    missing.Add(sm.RelativePath);
                }
                else if (cur.Size != sm.SizeBytes || cur.Hash != sm.Hash)
                {
                    modified.Add(sm.RelativePath);
                }
            }

            foreach (var rel in currentFiles.Keys)
            {
                if (!snapDict.ContainsKey(rel))
                {
                    added.Add(rel);
                }
            }

            bool identical = missing.Count == 0 && modified.Count == 0 && added.Count == 0;
            return new ModComparisonResult(identical, missing, added, modified);
        }
        catch
        {
            return new ModComparisonResult(true, new(), new(), new());
        }
    }

    private static string ComputeQuickHash(string filePath)
    {
        try
        {
            using var md5 = MD5.Create();
            using var s = File.OpenRead(filePath);
            var h = md5.ComputeHash(s);
            return Convert.ToHexString(h).ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }
}
