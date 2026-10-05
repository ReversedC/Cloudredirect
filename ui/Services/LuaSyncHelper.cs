using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace CloudRedirect.Services;

public record LuaSyncResult(bool Success, int FileCount, string Message);

public static class LuaSyncHelper
{
    /// <summary>
    /// Performs a manual backup of all *.lua files in config/stplug-in to cloud storage (account 0).
    /// Creates or updates LuaArchive.zip, LuaManifest.json, and .sync_state.
    /// Also copies to the sync folder if folder provider is active.
    /// </summary>
    public static LuaSyncResult ManualBackup(string steamPath)
    {
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            return new LuaSyncResult(false, 0, "Steam directory not found.");

        var luaDir = Path.Combine(steamPath, "config", "stplug-in");
        if (!Directory.Exists(luaDir))
        {
            try { Directory.CreateDirectory(luaDir); } catch { }
            return new LuaSyncResult(false, 0, "No stplug-in directory found in Steam config.");
        }

        var luaFiles = Directory.GetFiles(luaDir, "*.lua");
        if (luaFiles.Length == 0)
            return new LuaSyncResult(false, 0, "No .lua scripts found in config/stplug-in to backup.");

        // Find active storage accounts in cloud_redirect/storage
        var storageBase = Path.Combine(steamPath, "cloud_redirect", "storage");
        var accountDirs = new List<string>();
        if (Directory.Exists(storageBase))
        {
            accountDirs.AddRange(Directory.GetDirectories(storageBase));
        }

        // If no account dir in storage, check userdata
        if (accountDirs.Count == 0)
        {
            var userdataDir = Path.Combine(steamPath, "userdata");
            if (Directory.Exists(userdataDir))
            {
                foreach (var u in Directory.GetDirectories(userdataDir))
                {
                    var id = Path.GetFileName(u);
                    if (id != "0" && uint.TryParse(id, out _))
                    {
                        var target = Path.Combine(storageBase, id);
                        Directory.CreateDirectory(target);
                        accountDirs.Add(target);
                    }
                }
            }
        }

        // If still none, create default 0
        if (accountDirs.Count == 0)
        {
            var fallback = Path.Combine(storageBase, "default");
            Directory.CreateDirectory(fallback);
            accountDirs.Add(fallback);
        }

        int totalBackedUp = 0;
        foreach (var acct in accountDirs)
        {
            var zeroDir = Path.Combine(acct, "0");
            Directory.CreateDirectory(zeroDir);

            var zipPath = Path.Combine(zeroDir, "LuaArchive.zip");
            var manifestPath = Path.Combine(zeroDir, "LuaManifest.json");

            // Build zip archive
            var tempZip = Path.Combine(zeroDir, $"LuaArchive_{Guid.NewGuid():N}.tmp");
            try
            {
                var manifestDict = new Dictionary<string, object>();
                using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
                {
                    foreach (var file in luaFiles)
                    {
                        var name = Path.GetFileName(file);
                        var fileInfo = new FileInfo(file);
                        zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);

                        manifestDict[name] = new
                        {
                            mod = ((DateTimeOffset)fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds(),
                            size = fileInfo.Length
                        };
                    }
                }

                if (File.Exists(zipPath)) File.Delete(zipPath);
                File.Move(tempZip, zipPath);

                // Write manifest
                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestDict, jsonOptions));

                // Write .sync_state in stplug-in
                var syncStatePath = Path.Combine(luaDir, ".sync_state");
                var lines = new List<string> { DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() };
                lines.AddRange(luaFiles.Select(Path.GetFileName).Where(f => !string.IsNullOrEmpty(f))!);
                File.WriteAllLines(syncStatePath, lines);

                totalBackedUp = luaFiles.Length;

            }
            catch (Exception ex)
            {
                if (File.Exists(tempZip))
                {
                    try { File.Delete(tempZip); } catch { }
                }
                return new LuaSyncResult(false, 0, $"Backup failed: {ex.Message}");
            }
        }

        return new LuaSyncResult(true, totalBackedUp, "OK");
    }

    /// <summary>
    /// Performs a manual restore of Lua scripts from cloud storage (account 0) to config/stplug-in.
    /// Extracts all files in LuaArchive.zip.
    /// </summary>
    public static LuaSyncResult ManualRestore(string steamPath)
    {
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            return new LuaSyncResult(false, 0, "Steam directory not found.");

        var luaDir = Path.Combine(steamPath, "config", "stplug-in");
        Directory.CreateDirectory(luaDir);

        var storageBase = Path.Combine(steamPath, "cloud_redirect", "storage");
        string? zipSource = null;


        // Check local cloud_redirect storage
        if (zipSource == null && Directory.Exists(storageBase))
        {
            var localZips = Directory.GetFiles(storageBase, "LuaArchive.zip", SearchOption.AllDirectories);
            if (localZips.Length > 0)
                zipSource = localZips[0];
        }

        if (zipSource == null || !File.Exists(zipSource))
            return new LuaSyncResult(false, 0, "No LuaArchive.zip cloud backup found to restore.");

        int extracted = 0;
        try
        {
            using var zip = ZipFile.OpenRead(zipSource);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || !entry.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                    continue;

                var dest = Path.Combine(luaDir, entry.Name);
                entry.ExtractToFile(dest, overwrite: true);
                extracted++;
            }
        }
        catch (Exception ex)
        {
            return new LuaSyncResult(false, 0, $"Restore failed: {ex.Message}");
        }

        return new LuaSyncResult(true, extracted, "OK");
    }
}
