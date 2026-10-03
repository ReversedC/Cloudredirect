using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

public record SaveArchiveMetadata(
    string GameIdentifier,
    string GameName,
    uint AppId,
    DateTime ExportedAt,
    int FileCount,
    long TotalBytes,
    List<string> RelativeFilePaths
);

/// <summary>
/// Handles 1-click Save Export & Import (.zip Archive / Backup / Sharing).
/// Packages game saves into portable zip archives with embedded metadata,
/// and securely unpacks them with automatic pre-import safety snapshots.
/// </summary>
public static class SaveArchiveService
{
    private const string ManifestFileName = "cloudredirect_archive.json";

    public static async Task<(bool Success, string? Error, string? ExportPath)> ExportSaveArchiveAsync(
        string gameIdentifier,
        string saveDirectory,
        string destinationZipPath,
        string? gameName = null,
        uint appId = 0)
    {
        return await Task.Run<(bool Success, string? Error, string? ExportPath)>(() =>
        {
            try
            {
                if (!Directory.Exists(saveDirectory))
                    return (false, "Source save directory does not exist.", null);

                var files = Directory.GetFiles(saveDirectory, "*", SearchOption.AllDirectories);
                if (files.Length == 0)
                    return (false, "No save files found in the source directory.", null);

                var destDir = Path.GetDirectoryName(destinationZipPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                if (File.Exists(destinationZipPath))
                    File.Delete(destinationZipPath);

                long totalBytes = 0;
                var relPaths = new List<string>();

                using (var zip = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create))
                {
                    foreach (var file in files)
                    {
                        var rel = Path.GetRelativePath(saveDirectory, file);
                        relPaths.Add(rel);
                        totalBytes += new FileInfo(file).Length;
                        zip.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
                    }

                    var meta = new SaveArchiveMetadata(
                        gameIdentifier,
                        gameName ?? gameIdentifier,
                        appId,
                        DateTime.Now,
                        files.Length,
                        totalBytes,
                        relPaths
                    );

                    var entry = zip.CreateEntry(ManifestFileName, CompressionLevel.Fastest);
                    using var stream = entry.Open();
                    using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                    JsonSerializer.Serialize(writer, meta);
                }

                return (true, null, destinationZipPath);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        });
    }

    public static SaveArchiveMetadata? InspectSaveArchive(string zipPath)
    {
        try
        {
            if (!File.Exists(zipPath)) return null;

            using var zip = ZipFile.OpenRead(zipPath);
            var manifestEntry = zip.GetEntry(ManifestFileName);
            if (manifestEntry != null)
            {
                using var stream = manifestEntry.Open();
                return JsonSerializer.Deserialize<SaveArchiveMetadata>(stream);
            }

            // Fallback for generic zips: infer metadata from entries
            var entries = zip.Entries.Where(e => !e.FullName.EndsWith("/")).ToList();
            long bytes = entries.Sum(e => e.Length);
            var rels = entries.Select(e => e.FullName).ToList();
            var name = Path.GetFileNameWithoutExtension(zipPath);

            return new SaveArchiveMetadata(
                name,
                name,
                0,
                File.GetLastWriteTime(zipPath),
                entries.Count,
                bytes,
                rels
            );
        }
        catch
        {
            return null;
        }
    }

    public static async Task<(bool Success, string? Error, int FilesImported)> ImportSaveArchiveAsync(
        string zipPath,
        string targetDirectory,
        string? gameIdentifier = null)
    {
        return await Task.Run<(bool Success, string? Error, int FilesImported)>(() =>
        {
            try
            {
                if (!File.Exists(zipPath))
                    return (false, "Selected ZIP archive was not found.", 0);

                var meta = InspectSaveArchive(zipPath);
                var id = gameIdentifier ?? meta?.GameIdentifier ?? Path.GetFileNameWithoutExtension(zipPath);

                // 1. Automatic pre-import safety snapshot so existing saves are never lost
                if (Directory.Exists(targetDirectory))
                {
                    SaveHistoryManager.CreateSnapshot(id, targetDirectory, "Pre-Import Archive Safety Snapshot");
                }
                else
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                // 2. Extract files, skipping the internal manifest
                int imported = 0;
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (entry.FullName.EndsWith("/"))
                            continue;

                        var dest = Path.Combine(targetDirectory, entry.FullName);
                        var dir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        entry.ExtractToFile(dest, overwrite: true);
                        imported++;
                    }
                }

                return (true, null, imported);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0);
            }
        });
    }
}
