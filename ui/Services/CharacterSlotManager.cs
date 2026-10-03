using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CloudRedirect.Services;

public record CharacterSlot(
    string Name,
    string Notes,
    DateTime CreatedAt,
    DateTime LastActiveAt,
    int FileCount,
    long TotalBytes,
    bool IsActive,
    string DirectoryPath
)
{
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
/// Character Switcher (Multi-Slot Manager):
/// Enables isolated save profiles / character slots for any game with seamless switching,
/// automatic auto-save preservation, and zero data loss.
/// </summary>
public static class CharacterSlotManager
{
    public static string GetSlotsBaseDir(string gameIdentifier)
    {
        var safe = SaveHistoryManager.SanitizeFolderName(gameIdentifier);
        var dir = Path.Combine(SteamDetector.GetConfigDir(), "character_slots", safe);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetActiveSlotName(string gameIdentifier)
    {
        var baseDir = GetSlotsBaseDir(gameIdentifier);
        var activeFile = Path.Combine(baseDir, "active_slot.txt");
        if (File.Exists(activeFile))
        {
            var name = File.ReadAllText(activeFile).Trim();
            if (!string.IsNullOrEmpty(name)) return name;
        }
        return "Default Profile";
    }

    public static void SetActiveSlotName(string gameIdentifier, string slotName)
    {
        var baseDir = GetSlotsBaseDir(gameIdentifier);
        var activeFile = Path.Combine(baseDir, "active_slot.txt");
        File.WriteAllText(activeFile, slotName);
    }

    public static List<CharacterSlot> GetSlots(string gameIdentifier, string? savePath = null)
    {
        var result = new List<CharacterSlot>();
        var baseDir = GetSlotsBaseDir(gameIdentifier);
        var activeSlot = GetActiveSlotName(gameIdentifier);

        // Ensure at least "Default Profile" exists if savePath exists
        var defaultDir = Path.Combine(baseDir, "Default Profile");
        if (!Directory.Exists(defaultDir) && !string.IsNullOrEmpty(savePath) && Directory.Exists(savePath))
        {
            CreateSlotInternal(gameIdentifier, savePath, "Default Profile", "Initial Game Save State");
        }

        foreach (var sub in Directory.GetDirectories(baseDir))
        {
            var slotName = Path.GetFileName(sub);
            var metaFile = Path.Combine(sub, "slot_meta.json");
            DateTime created = Directory.GetCreationTime(sub);
            DateTime lastActive = Directory.GetLastWriteTime(sub);
            string notes = "";

            if (File.Exists(metaFile))
            {
                try
                {
                    var json = File.ReadAllText(metaFile);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("notes", out var n))
                        notes = n.GetString() ?? "";
                    if (doc.RootElement.TryGetProperty("createdAt", out var ca) && DateTime.TryParse(ca.GetString(), out var dt))
                        created = dt;
                    if (doc.RootElement.TryGetProperty("lastActiveAt", out var la) && DateTime.TryParse(la.GetString(), out var laDt))
                        lastActive = laDt;
                }
                catch { }
            }

            var files = Directory.GetFiles(sub, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).Equals("slot_meta.json", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            long totalBytes = files.Sum(f => new FileInfo(f).Length);
            bool isActive = string.Equals(slotName, activeSlot, StringComparison.OrdinalIgnoreCase);

            result.Add(new CharacterSlot(
                slotName,
                notes,
                created,
                lastActive,
                files.Length,
                totalBytes,
                isActive,
                sub
            ));
        }

        return result.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.LastActiveAt).ToList();
    }

    private static void CreateSlotInternal(string gameIdentifier, string savePath, string slotName, string notes)
    {
        var baseDir = GetSlotsBaseDir(gameIdentifier);
        var slotDir = Path.Combine(baseDir, slotName);
        if (!Directory.Exists(slotDir))
            Directory.CreateDirectory(slotDir);

        if (Directory.Exists(savePath))
        {
            foreach (var file in Directory.GetFiles(savePath, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(savePath, file);
                var dest = Path.Combine(slotDir, rel);
                var dir = Path.GetDirectoryName(dest)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.Copy(file, dest, overwrite: true);
            }
        }

        var meta = new
        {
            slotName,
            notes,
            createdAt = DateTime.Now.ToString("o"),
            lastActiveAt = DateTime.Now.ToString("o")
        };
        File.WriteAllText(Path.Combine(slotDir, "slot_meta.json"),
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool CreateSlot(string gameIdentifier, string savePath, string slotName, string notes = "")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slotName)) return false;
            CreateSlotInternal(gameIdentifier, savePath, slotName.Trim(), notes);
            return true;
        }
        catch { return false; }
    }

    public static (bool Success, string? Error) SwitchSlot(string gameIdentifier, string savePath, string targetSlotName)
    {
        try
        {
            if (!Directory.Exists(savePath))
                return (false, "Game save folder not found.");

            var baseDir = GetSlotsBaseDir(gameIdentifier);
            var targetDir = Path.Combine(baseDir, targetSlotName);
            if (!Directory.Exists(targetDir))
                return (false, $"Target character slot '{targetSlotName}' not found.");

            // 1. Sync CURRENT active save files back into the CURRENT active slot
            var currentActive = GetActiveSlotName(gameIdentifier);
            if (!string.IsNullOrEmpty(currentActive))
            {
                var currentSlotDir = Path.Combine(baseDir, currentActive);
                if (!Directory.Exists(currentSlotDir)) Directory.CreateDirectory(currentSlotDir);

                foreach (var file in Directory.GetFiles(savePath, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(savePath, file);
                    var dest = Path.Combine(currentSlotDir, rel);
                    var dir = Path.GetDirectoryName(dest)!;
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.Copy(file, dest, overwrite: true);
                }

                // Update last active time
                var curMetaFile = Path.Combine(currentSlotDir, "slot_meta.json");
                try
                {
                    var curMeta = new { slotName = currentActive, lastActiveAt = DateTime.Now.ToString("o") };
                    File.WriteAllText(curMetaFile, JsonSerializer.Serialize(curMeta, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
            }

            // 2. Also take a safety snapshot of the save directory
            SaveHistoryManager.CreateSnapshot(gameIdentifier, savePath, $"Safety Snapshot before switching to slot '{targetSlotName}'");

            // 3. Deploy target slot files into savePath
            foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).Equals("slot_meta.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rel = Path.GetRelativePath(targetDir, file);
                var dest = Path.Combine(savePath, rel);
                var dir = Path.GetDirectoryName(dest)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.Copy(file, dest, overwrite: true);
            }

            SetActiveSlotName(gameIdentifier, targetSlotName);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static bool DeleteSlot(string gameIdentifier, string slotName)
    {
        try
        {
            var baseDir = GetSlotsBaseDir(gameIdentifier);
            var slotDir = Path.Combine(baseDir, slotName);
            if (Directory.Exists(slotDir))
            {
                Directory.Delete(slotDir, true);
                if (GetActiveSlotName(gameIdentifier).Equals(slotName, StringComparison.OrdinalIgnoreCase))
                {
                    SetActiveSlotName(gameIdentifier, "Default Profile");
                }
                return true;
            }
        }
        catch { }
        return false;
    }
}
