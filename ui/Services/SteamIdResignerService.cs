using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

public record ResignResult(
    bool Success,
    string? Error,
    int FilesModified,
    int OccurrencesReplaced,
    int FoldersRenamed,
    string? BackupDir
);

/// <summary>
/// Tool for transferring and resigning save files between different Steam accounts or emulators.
/// Safely patches binary and text SteamID64 / AccountID instances and renames account directories.
/// </summary>
public static class SteamIdResignerService
{
    public const ulong DefaultEmulatorSteamId64 = 76561197960287930UL; // Standard Goldberg / Codex SteamID

    public static ulong? GetCurrentSteamId64()
    {
        try
        {
            var steamPath = SteamDetector.FindSteamPath();
            if (string.IsNullOrEmpty(steamPath)) return null;

            var loginUsers = Path.Combine(steamPath, "config", "loginusers.vdf");
            if (!File.Exists(loginUsers)) return null;

            var lines = File.ReadAllLines(loginUsers);
            foreach (var line in lines)
            {
                var trimmed = line.Trim().Trim('"');
                if (trimmed.Length == 17 && trimmed.StartsWith("7656119") && ulong.TryParse(trimmed, out var id))
                {
                    return id;
                }
            }
        }
        catch { }
        return null;
    }

    public static async Task<ResignResult> ResignSaveDirectoryAsync(
        string saveDirectory,
        ulong oldSteamId64,
        ulong newSteamId64,
        bool patchAccount32 = true)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(saveDirectory))
                    return new ResignResult(false, "Directory does not exist.", 0, 0, 0, null);

                if (oldSteamId64 == newSteamId64)
                    return new ResignResult(false, "Old and new SteamID64 are identical.", 0, 0, 0, null);

                // 1. Create a safety snapshot of the directory before any modification
                var backup = SaveHistoryManager.CreateSnapshot(
                    Path.GetFileName(saveDirectory),
                    saveDirectory,
                    $"Pre-Resign Backup (SteamID {oldSteamId64} -> {newSteamId64})"
                );

                var oldIdStr = oldSteamId64.ToString();
                var newIdStr = newSteamId64.ToString();

                uint oldAccId = (uint)(oldSteamId64 & 0xFFFFFFFF);
                uint newAccId = (uint)(newSteamId64 & 0xFFFFFFFF);
                var oldAccStr = oldAccId.ToString();
                var newAccStr = newAccId.ToString();

                byte[] old64Bytes = BitConverter.GetBytes(oldSteamId64);
                byte[] new64Bytes = BitConverter.GetBytes(newSteamId64);

                byte[] old32Bytes = BitConverter.GetBytes(oldAccId);
                byte[] new32Bytes = BitConverter.GetBytes(newAccId);

                int filesModified = 0;
                int occurrencesReplaced = 0;
                int foldersRenamed = 0;

                // 2. Scan and patch files
                var files = Directory.GetFiles(saveDirectory, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    bool modified = false;
                    int countInFile = 0;
                    try
                    {
                        var bytes = File.ReadAllBytes(file);
                        if (bytes.Length == 0) continue;

                        // Check if file is text-like (no null bytes in first 512 bytes)
                        bool isText = true;
                        int checkLen = Math.Min(bytes.Length, 512);
                        for (int i = 0; i < checkLen; i++)
                        {
                            if (bytes[i] == 0) { isText = false; break; }
                        }

                        if (isText)
                        {
                            var text = Encoding.UTF8.GetString(bytes);
                            if (text.Contains(oldIdStr))
                            {
                                int matches = (text.Length - text.Replace(oldIdStr, "").Length) / oldIdStr.Length;
                                text = text.Replace(oldIdStr, newIdStr);
                                countInFile += matches;
                                modified = true;
                            }
                            if (patchAccount32 && oldAccStr.Length >= 7 && text.Contains(oldAccStr))
                            {
                                int matches = (text.Length - text.Replace(oldAccStr, "").Length) / oldAccStr.Length;
                                text = text.Replace(oldAccStr, newAccStr);
                                countInFile += matches;
                                modified = true;
                            }
                            if (modified)
                            {
                                File.WriteAllText(file, text, Encoding.UTF8);
                            }
                        }
                        else
                        {
                            // Binary search and replace
                            var (replaced64, count64) = ReplaceBytes(bytes, old64Bytes, new64Bytes);
                            if (count64 > 0)
                            {
                                bytes = replaced64;
                                countInFile += count64;
                                modified = true;
                            }

                            if (patchAccount32)
                            {
                                var (replaced32, count32) = ReplaceBytes(bytes, old32Bytes, new32Bytes);
                                if (count32 > 0)
                                {
                                    bytes = replaced32;
                                    countInFile += count32;
                                    modified = true;
                                }
                            }

                            if (modified)
                            {
                                File.WriteAllBytes(file, bytes);
                            }
                        }

                        if (modified)
                        {
                            filesModified++;
                            occurrencesReplaced += countInFile;
                        }
                    }
                    catch { }
                }

                // 3. Rename any files matching old IDs
                foreach (var file in Directory.GetFiles(saveDirectory, "*", SearchOption.AllDirectories))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Contains(oldIdStr) || (patchAccount32 && oldAccStr.Length >= 7 && fileName.Contains(oldAccStr)))
                    {
                        var newName = fileName.Replace(oldIdStr, newIdStr);
                        if (patchAccount32 && oldAccStr.Length >= 7)
                            newName = newName.Replace(oldAccStr, newAccStr);

                        var dest = Path.Combine(Path.GetDirectoryName(file)!, newName);
                        if (!File.Exists(dest))
                        {
                            try { File.Move(file, dest); } catch { }
                        }
                    }
                }

                // 4. Rename any subdirectories matching old IDs (deepest first)
                var subDirs = Directory.GetDirectories(saveDirectory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length)
                    .ToList();

                foreach (var dir in subDirs)
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.Equals(oldIdStr, StringComparison.OrdinalIgnoreCase) ||
                        (patchAccount32 && dirName.Equals(oldAccStr, StringComparison.OrdinalIgnoreCase)))
                    {
                        var parent = Path.GetDirectoryName(dir)!;
                        var targetName = dirName.Equals(oldIdStr, StringComparison.OrdinalIgnoreCase) ? newIdStr : newAccStr;
                        var newDir = Path.Combine(parent, targetName);
                        if (!Directory.Exists(newDir))
                        {
                            try
                            {
                                Directory.Move(dir, newDir);
                                foldersRenamed++;
                            }
                            catch { }
                        }
                    }
                }

                return new ResignResult(true, null, filesModified, occurrencesReplaced, foldersRenamed, backup?.DirectoryPath);
            }
            catch (Exception ex)
            {
                return new ResignResult(false, ex.Message, 0, 0, 0, null);
            }
        });
    }

    private static (byte[] Result, int Count) ReplaceBytes(byte[] source, byte[] search, byte[] replacement)
    {
        if (search.Length != replacement.Length || source.Length < search.Length)
            return (source, 0);

        var copy = (byte[])source.Clone();
        int count = 0;
        int limit = source.Length - search.Length;

        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < search.Length; j++)
            {
                if (copy[i + j] != search[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                for (int j = 0; j < replacement.Length; j++)
                {
                    copy[i + j] = replacement[j];
                }
                count++;
                i += search.Length - 1;
            }
        }

        return (copy, count);
    }
}
