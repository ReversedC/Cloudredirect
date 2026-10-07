using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

public record LuaSyncResult(bool Success, int FileCount, string Message);

public static class LuaSyncHelper
{
    private const string CloudFolderName = "EncryptedLua";

    public static LuaSyncResult ManualBackup(string steamPath, Action<string, int?>? progress = null)
        => ManualBackupAsync(steamPath, progress).GetAwaiter().GetResult();

    public static LuaSyncResult ManualFetch(string steamPath, Action<string, int?>? progress = null)
        => ManualFetchAsync(steamPath, progress).GetAwaiter().GetResult();

    public static LuaSyncResult ManualRestore(string steamPath, Action<string, int?>? progress = null)
        => ManualRestoreAsync(steamPath, progress).GetAwaiter().GetResult();

    /// <summary>
    /// Performs a manual backup of all *.lua files in config/stplug-in to cloud storage (account 0).
    /// Creates or updates LuaArchive.zip, LuaManifest.json, and .sync_state.
    /// Also uploads directly to Google Drive or copies to the sync folder if configured.
    /// </summary>
    public static async Task<LuaSyncResult> ManualBackupAsync(string steamPath, Action<string, int?>? progress = null)
    {
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
        {
            progress?.Invoke("Error: Steam directory not found.", 100);
            return new LuaSyncResult(false, 0, "Steam directory not found.");
        }

        var luaDir = Path.Combine(steamPath, "config", "stplug-in");
        if (!Directory.Exists(luaDir))
        {
            try { Directory.CreateDirectory(luaDir); } catch { }
            progress?.Invoke("Error: No stplug-in directory found in Steam config.", 100);
            return new LuaSyncResult(false, 0, "No stplug-in directory found in Steam config.");
        }

        progress?.Invoke("Scanning config/stplug-in for local Lua scripts...", 5);
        var luaFiles = Directory.GetFiles(luaDir, "*.lua");
        if (luaFiles.Length == 0)
        {
            progress?.Invoke("No .lua scripts found in config/stplug-in to backup.", 100);
            return new LuaSyncResult(false, 0, "No .lua scripts found in config/stplug-in to backup.");
        }

        progress?.Invoke($"Discovered {luaFiles.Length} local Lua script(s).", 10);

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
        string? primaryZipPath = null;
        string? primaryManifestPath = null;

        foreach (var acct in accountDirs)
        {
            var zeroDir = Path.Combine(acct, "0");
            Directory.CreateDirectory(zeroDir);

            var zipPath = Path.Combine(zeroDir, "LuaArchive.zip");
            var manifestPath = Path.Combine(zeroDir, "LuaManifest.json");

            progress?.Invoke($"Packing LuaArchive.zip for account {Path.GetFileName(acct)}...", 20);

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
                primaryZipPath ??= zipPath;
                primaryManifestPath ??= manifestPath;
                progress?.Invoke($"Packed {luaFiles.Length} scripts into LuaArchive.zip ({new FileInfo(zipPath).Length / 1024} KB).", 35);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempZip))
                {
                    try { File.Delete(tempZip); } catch { }
                }
                progress?.Invoke($"Packing archive failed: {ex.Message}", 100);
                return new LuaSyncResult(false, 0, $"Backup failed: {ex.Message}");
            }
        }

        // Cloud & Sync Folder propagation
        var config = SteamDetector.ReadConfig();
        if (config != null)
        {
            // 1. Google Drive propagation
            if (config.Provider == "gdrive" && primaryZipPath != null && File.Exists(primaryZipPath))
            {
                try
                {
                    await UploadToGDriveAsync(config, primaryZipPath, primaryManifestPath, luaFiles, progress);
                }
                catch (Exception ex)
                {
                    progress?.Invoke($"Warning: Google Drive sync error: {ex.Message}", 85);
                }
            }

            // 2. Sync Folder propagation
            if (!string.IsNullOrEmpty(config.SyncPath) && Directory.Exists(config.SyncPath) && primaryZipPath != null)
            {
                try
                {
                    progress?.Invoke($"Copying archive to Sync Folder: {config.SyncPath}...", 90);
                    var syncDestDir = Path.Combine(config.SyncPath, "0", "blobs");
                    Directory.CreateDirectory(syncDestDir);
                    File.Copy(primaryZipPath, Path.Combine(syncDestDir, "LuaArchive.zip"), overwrite: true);
                    File.Copy(primaryZipPath, Path.Combine(config.SyncPath, "LuaArchive.zip"), overwrite: true);

                    if (primaryManifestPath != null && File.Exists(primaryManifestPath))
                    {
                        File.Copy(primaryManifestPath, Path.Combine(syncDestDir, "LuaManifest.json"), overwrite: true);
                        File.Copy(primaryManifestPath, Path.Combine(config.SyncPath, "LuaManifest.json"), overwrite: true);
                    }

                    // Save encrypted .crlua in sync folder
                    var syncEncryptedDir = Path.Combine(config.SyncPath, CloudFolderName);
                    Directory.CreateDirectory(syncEncryptedDir);
                    foreach (var file in luaFiles)
                    {
                        var fname = Path.GetFileName(file);
                        if (uint.TryParse(Path.GetFileNameWithoutExtension(fname), out var appId))
                        {
                            var plain = File.ReadAllBytes(file);
                            var encrypted = LuaCrypto.Encrypt(plain);
                            File.WriteAllBytes(Path.Combine(syncEncryptedDir, $"{appId}.crlua"), encrypted);
                        }
                    }
                    progress?.Invoke("Sync Folder backup files successfully updated.", 95);
                }
                catch (Exception ex)
                {
                    progress?.Invoke($"Warning: Sync folder copy error: {ex.Message}", 95);
                }
            }
        }
        else
        {
            progress?.Invoke("No remote cloud provider configured (local backup only).", 95);
        }

        LuaCloudSyncService.CachedCloudGamesCount = totalBackedUp;
        progress?.Invoke($"Backup complete! {totalBackedUp} Lua script(s) safely synced.", 100);
        return new LuaSyncResult(true, totalBackedUp, "OK");
    }

    /// <summary>
    /// Fetches Lua backup data from cloud storage (Google Drive or Sync Folder) into local cache (account 0)
    /// without overwriting active local files in config/stplug-in, updating cloud counts and readying restore.
    /// </summary>
    public static async Task<LuaSyncResult> ManualFetchAsync(string steamPath, Action<string, int?>? progress = null)
    {
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
        {
            progress?.Invoke("Error: Steam directory not found.", 100);
            return new LuaSyncResult(false, 0, "Steam directory not found.");
        }

        progress?.Invoke("Initializing cloud fetch...", 10);

        var config = SteamDetector.ReadConfig();
        if (config == null)
        {
            progress?.Invoke("Error: No cloud configuration found in settings.", 100);
            return new LuaSyncResult(false, 0, "No cloud configuration found.");
        }

        var storageBase = Path.Combine(steamPath, "cloud_redirect", "storage");
        Directory.CreateDirectory(storageBase);
        var targetCacheDir = Path.Combine(storageBase, "default", "0");
        Directory.CreateDirectory(targetCacheDir);

        int count = 0;

        // 1. Google Drive
        if (config.Provider == "gdrive")
        {
            progress?.Invoke("Authenticating with Google Drive...", 20);
            var tokenPath = LuaCloudSyncService.ResolveTokenPath(config);
            var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
            if (string.IsNullOrEmpty(accessToken))
            {
                progress?.Invoke("Error: Google Drive not authenticated or token expired.", 100);
                return new LuaSyncResult(false, 0, "Google Drive authentication failed.");
            }

            progress?.Invoke("Connected to Google Drive. Querying cloud backups...", 35);
            var (zipPath, restoredDirect) = await DownloadFromGDriveAsync(config, storageBase, null, progress);

            if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath))
            {
                try
                {
                    using var zip = ZipFile.OpenRead(zipPath);
                    count = zip.Entries.Count(e => e.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
                }
                catch { }
            }
            if (restoredDirect > count) count = restoredDirect;
        }

        // 2. Sync Folder
        if (!string.IsNullOrEmpty(config.SyncPath) && Directory.Exists(config.SyncPath))
        {
            progress?.Invoke($"Checking Sync Folder at {config.SyncPath}...", 40);
            var syncZips = Directory.GetFiles(config.SyncPath, "LuaArchive.zip", SearchOption.AllDirectories);
            if (syncZips.Length > 0)
            {
                var localCopy = Path.Combine(targetCacheDir, "LuaArchive.zip");
                File.Copy(syncZips[0], localCopy, overwrite: true);
                try
                {
                    using var zip = ZipFile.OpenRead(localCopy);
                    int zCount = zip.Entries.Count(e => e.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
                    if (zCount > count) count = zCount;
                }
                catch { }
            }

            var syncLuaDir = Path.Combine(config.SyncPath, CloudFolderName);
            if (Directory.Exists(syncLuaDir))
            {
                var crluaFiles = Directory.GetFiles(syncLuaDir, "*.crlua");
                if (crluaFiles.Length > count) count = crluaFiles.Length;
            }
        }

        if (count > 0)
        {
            LuaCloudSyncService.CachedCloudGamesCount = count;
            progress?.Invoke($"Fetch successful! {count} cloud Lua script(s) discovered and cached.", 100);
            return new LuaSyncResult(true, count, $"Fetched {count} Lua script(s) from cloud storage.");
        }
        else
        {
            progress?.Invoke("No cloud Lua backups found in cloud storage.", 100);
            return new LuaSyncResult(false, 0, "No cloud Lua backup found to fetch.");
        }
    }

    /// <summary>
    /// Performs a manual restore of Lua scripts from cloud storage (Google Drive, Sync folder, or local storage)
    /// to config/stplug-in.
    /// </summary>
    public static async Task<LuaSyncResult> ManualRestoreAsync(string steamPath, Action<string, int?>? progress = null)
    {
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
        {
            progress?.Invoke("Error: Steam directory not found.", 100);
            return new LuaSyncResult(false, 0, "Steam directory not found.");
        }

        progress?.Invoke("Preparing restore destination (config/stplug-in)...", 5);
        var luaDir = Path.Combine(steamPath, "config", "stplug-in");
        Directory.CreateDirectory(luaDir);

        var storageBase = Path.Combine(steamPath, "cloud_redirect", "storage");
        string? zipSource = null;
        int directRestored = 0;

        // 1. Check local cloud_redirect storage first
        if (Directory.Exists(storageBase))
        {
            var localZips = Directory.GetFiles(storageBase, "LuaArchive.zip", SearchOption.AllDirectories);
            if (localZips.Length > 0)
                zipSource = localZips[0];
        }

        // 2. If not found locally or needs cloud download, query Cloud Provider
        var config = SteamDetector.ReadConfig();
        if ((zipSource == null || !File.Exists(zipSource)) && config != null)
        {
            // A. Google Drive Cloud Restore
            if (config.Provider == "gdrive")
            {
                progress?.Invoke("Fetching latest archive from Google Drive...", 20);
                var (gdriveZip, gdriveCount) = await DownloadFromGDriveAsync(config, storageBase, luaDir, progress);
                if (!string.IsNullOrEmpty(gdriveZip) && File.Exists(gdriveZip))
                    zipSource = gdriveZip;
                directRestored += gdriveCount;
            }

            // B. Sync Folder Restore
            if ((zipSource == null || !File.Exists(zipSource)) && !string.IsNullOrEmpty(config.SyncPath) && Directory.Exists(config.SyncPath))
            {
                progress?.Invoke($"Copying archive from Sync Folder at {config.SyncPath}...", 30);
                var syncZips = Directory.GetFiles(config.SyncPath, "LuaArchive.zip", SearchOption.AllDirectories);
                if (syncZips.Length > 0)
                {
                    var targetDir = Path.Combine(storageBase, "default", "0");
                    Directory.CreateDirectory(targetDir);
                    var localCopy = Path.Combine(targetDir, "LuaArchive.zip");
                    File.Copy(syncZips[0], localCopy, overwrite: true);
                    zipSource = localCopy;
                }

                // Also check for encrypted .crlua in sync folder
                var syncLuaDir = Path.Combine(config.SyncPath, CloudFolderName);
                if (Directory.Exists(syncLuaDir))
                {
                    foreach (var crlua in Directory.GetFiles(syncLuaDir, "*.crlua"))
                    {
                        var baseName = Path.GetFileNameWithoutExtension(crlua);
                        if (uint.TryParse(baseName, out var appId))
                        {
                            var dest = Path.Combine(luaDir, $"{appId}.lua");
                            var encrypted = File.ReadAllBytes(crlua);
                            var plain = LuaCrypto.Decrypt(encrypted);
                            File.WriteAllBytes(dest, plain);
                            directRestored++;
                        }
                    }
                }
            }
        }

        // 3. Extract from zipSource if available
        int extracted = directRestored;
        if (zipSource != null && File.Exists(zipSource))
        {
            progress?.Invoke("Extracting Lua scripts to config/stplug-in...", 60);
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
                progress?.Invoke($"Extracted {extracted} script(s) from LuaArchive.zip.", 80);
            }
            catch (Exception ex)
            {
                if (extracted == 0)
                {
                    progress?.Invoke($"Extraction failed: {ex.Message}", 100);
                    return new LuaSyncResult(false, 0, $"Restore failed: {ex.Message}");
                }
            }
        }

        if (extracted == 0)
        {
            progress?.Invoke("Error: No Lua backups found in cloud storage to restore.", 100);
            return new LuaSyncResult(false, 0, "No LuaArchive.zip cloud backup found to restore.");
        }

        // Update .sync_state in stplug-in
        progress?.Invoke("Updating local .sync_state file...", 90);
        try
        {
            var syncStatePath = Path.Combine(luaDir, ".sync_state");
            var currentLuas = Directory.GetFiles(luaDir, "*.lua").Select(Path.GetFileName).Where(f => !string.IsNullOrEmpty(f)).ToList();
            var lines = new List<string> { DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() };
            lines.AddRange(currentLuas!);
            File.WriteAllLines(syncStatePath, lines);
        }
        catch { }

        LuaCloudSyncService.CachedCloudGamesCount = extracted;
        progress?.Invoke($"Restore complete! {extracted} Lua script(s) placed into stplug-in.", 100);
        return new LuaSyncResult(true, extracted, "OK");
    }

    private static async Task UploadToGDriveAsync(CloudConfig config, string zipPath, string? manifestPath, string[] luaFiles, Action<string, int?>? progress = null)
    {
        progress?.Invoke("Checking Google Drive authentication...", 40);
        var tokenPath = LuaCloudSyncService.ResolveTokenPath(config);
        var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
        if (string.IsNullOrEmpty(accessToken))
        {
            progress?.Invoke("Warning: Google Drive token invalid or not authenticated.", 45);
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        progress?.Invoke("Ensuring CloudRedirect root folder in Google Drive...", 45);
        var rootId = await EnsureDriveFolderSimpleAsync(http, "CloudRedirect", null);
        if (string.IsNullOrEmpty(rootId))
        {
            progress?.Invoke("Warning: Could not create or access CloudRedirect folder in Drive.", 50);
            return;
        }

        // 1. Upload LuaArchive.zip directly to CloudRedirect root
        var zipBytes = await File.ReadAllBytesAsync(zipPath);
        progress?.Invoke($"Uploading LuaArchive.zip ({zipBytes.Length / 1024} KB)...", 50);
        await UploadOrReplaceDriveFileAsync(http, rootId, "LuaArchive.zip", zipBytes, "application/zip");
        progress?.Invoke("LuaArchive.zip successfully uploaded to Google Drive.", 60);

        if (manifestPath != null && File.Exists(manifestPath))
        {
            var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
            progress?.Invoke("Uploading LuaManifest.json to Google Drive...", 65);
            await UploadOrReplaceDriveFileAsync(http, rootId, "LuaManifest.json", manifestBytes, "application/json");
        }

        // 2. Encrypted individual files
        var luaFolderId = await EnsureDriveFolderSimpleAsync(http, CloudFolderName, rootId);
        if (!string.IsNullOrEmpty(luaFolderId) && luaFiles.Length > 0)
        {
            progress?.Invoke("Listing remote encrypted Lua scripts...", 70);
            var existingMap = await ListDriveFolderFilesAsync(http, luaFolderId);

            int done = 0;
            var semaphore = new SemaphoreSlim(5);
            var tasks = luaFiles.Select(async file =>
            {
                var fname = Path.GetFileName(file);
                if (uint.TryParse(Path.GetFileNameWithoutExtension(fname), out var appId))
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var plain = await File.ReadAllBytesAsync(file);
                        var encrypted = LuaCrypto.Encrypt(plain);
                        var targetName = $"{appId}.crlua";
                        string? existingId = existingMap.TryGetValue(targetName, out var id) ? id : null;
                        await UploadOrUpdateDriveFileFastAsync(http, luaFolderId, targetName, existingId, encrypted, "application/octet-stream");
                        var c = Interlocked.Increment(ref done);
                        if (c % 15 == 0 || c == luaFiles.Length)
                        {
                            int pct = 70 + (int)(20.0 * c / luaFiles.Length);
                            progress?.Invoke($"Syncing individual encrypted scripts: [{c}/{luaFiles.Length}]", pct);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
            });

            await Task.WhenAll(tasks);
            progress?.Invoke($"All {luaFiles.Length} individual encrypted scripts synced.", 90);
        }
    }

    private static async Task<(string? ZipPath, int DirectRestored)> DownloadFromGDriveAsync(
        CloudConfig config, string storageBase, string? luaDir, Action<string, int?>? progress = null)
    {
        var tokenPath = LuaCloudSyncService.ResolveTokenPath(config);
        var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
        if (string.IsNullOrEmpty(accessToken)) return (null, 0);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        string? downloadedZipPath = null;
        int directRestored = 0;

        // A. Search for LuaArchive.zip
        try
        {
            progress?.Invoke("Searching for LuaArchive.zip in Google Drive...", 40);
            var qZip = Uri.EscapeDataString("name='LuaArchive.zip' and trashed=false");
            var zipUrl = $"https://www.googleapis.com/drive/v3/files?q={qZip}&fields=files(id,size)&pageSize=5";
            var zResp = await http.GetAsync(zipUrl);
            if (zResp.IsSuccessStatusCode)
            {
                var zJson = await zResp.Content.ReadAsStringAsync();
                using var zDoc = JsonDocument.Parse(zJson);
                if (zDoc.RootElement.TryGetProperty("files", out var zFiles) && zFiles.GetArrayLength() > 0)
                {
                    var zipId = zFiles[0].GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(zipId))
                    {
                        var targetDir = Path.Combine(storageBase, "default", "0");
                        Directory.CreateDirectory(targetDir);
                        downloadedZipPath = Path.Combine(targetDir, "LuaArchive.zip");

                        progress?.Invoke("Downloading LuaArchive.zip from Google Drive...", 50);
                        var bytes = await http.GetByteArrayAsync($"https://www.googleapis.com/drive/v3/files/{zipId}?alt=media");
                        await File.WriteAllBytesAsync(downloadedZipPath, bytes);
                        progress?.Invoke($"LuaArchive.zip downloaded ({bytes.Length / 1024} KB).", 65);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            progress?.Invoke($"Warning: Download LuaArchive.zip error: {ex.Message}", 65);
        }

        // B. Search for LuaManifest.json
        try
        {
            var qM = Uri.EscapeDataString("name='LuaManifest.json' and trashed=false");
            var mUrl = $"https://www.googleapis.com/drive/v3/files?q={qM}&fields=files(id)&pageSize=5";
            var mResp = await http.GetAsync(mUrl);
            if (mResp.IsSuccessStatusCode)
            {
                var mJson = await mResp.Content.ReadAsStringAsync();
                using var mDoc = JsonDocument.Parse(mJson);
                if (mDoc.RootElement.TryGetProperty("files", out var mFiles) && mFiles.GetArrayLength() > 0)
                {
                    var mId = mFiles[0].GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(mId))
                    {
                        var targetDir = Path.Combine(storageBase, "default", "0");
                        Directory.CreateDirectory(targetDir);
                        var manifestDest = Path.Combine(targetDir, "LuaManifest.json");

                        var mBytes = await http.GetByteArrayAsync($"https://www.googleapis.com/drive/v3/files/{mId}?alt=media");
                        await File.WriteAllBytesAsync(manifestDest, mBytes);
                    }
                }
            }
        }
        catch { }

        // C. If restoring directly and luaDir is provided, also check EncryptedLua folder
        if (!string.IsNullOrEmpty(luaDir))
        {
            try
            {
                var rootId = await EnsureDriveFolderSimpleAsync(http, "CloudRedirect", null);
                if (!string.IsNullOrEmpty(rootId))
                {
                    var luaFolderId = await EnsureDriveFolderSimpleAsync(http, CloudFolderName, rootId);
                    if (!string.IsNullOrEmpty(luaFolderId))
                    {
                        var existingFiles = await ListDriveFolderFilesAsync(http, luaFolderId);
                        if (existingFiles.Count > 0)
                        {
                            progress?.Invoke($"Checking {existingFiles.Count} individual encrypted script(s)...", 75);
                            foreach (var kvp in existingFiles)
                            {
                                var baseName = Path.GetFileNameWithoutExtension(kvp.Key);
                                if (uint.TryParse(baseName, out var appId))
                                {
                                    var dest = Path.Combine(luaDir, $"{appId}.lua");
                                    var encryptedBytes = await http.GetByteArrayAsync($"https://www.googleapis.com/drive/v3/files/{kvp.Value}?alt=media");
                                    var plainBytes = LuaCrypto.Decrypt(encryptedBytes);
                                    await File.WriteAllBytesAsync(dest, plainBytes);
                                    directRestored++;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return (downloadedZipPath, directRestored);
    }

    private static async Task<Dictionary<string, string>> ListDriveFolderFilesAsync(HttpClient http, string parentFolderId)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var q = Uri.EscapeDataString($"'{parentFolderId}' in parents and mimeType!='application/vnd.google-apps.folder' and trashed=false");
            var url = $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id,name)&pageSize=1000";
            var resp = await http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("files", out var files))
                {
                    foreach (var f in files.EnumerateArray())
                    {
                        var name = f.GetProperty("name").GetString();
                        var id = f.GetProperty("id").GetString();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
                            map[name] = id;
                    }
                }
            }
        }
        catch { }
        return map;
    }

    private static async Task UploadOrUpdateDriveFileFastAsync(HttpClient http, string parentFolderId, string fileName, string? existingId, byte[] content, string contentType)
    {
        try
        {
            if (!string.IsNullOrEmpty(existingId))
            {
                var updateContent = new ByteArrayContent(content);
                updateContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                await http.PatchAsync($"https://www.googleapis.com/upload/drive/v3/files/{existingId}?uploadType=media", updateContent);
                return;
            }

            var boundary = "cr_boundary_" + Guid.NewGuid().ToString("N");
            var multipart = new MultipartContent("related", boundary);

            var metaObj = new
            {
                name = fileName,
                parents = new[] { parentFolderId }
            };
            var metaContent = new StringContent(JsonSerializer.Serialize(metaObj), Encoding.UTF8, "application/json");
            multipart.Add(metaContent);

            var fileContent = new ByteArrayContent(content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            multipart.Add(fileContent);

            var url = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";
            await http.PostAsync(url, multipart);
        }
        catch { }
    }

    private static async Task<string?> EnsureDriveFolderSimpleAsync(HttpClient http, string name, string? parentId)
    {
        try
        {
            var escapedName = name.Replace("'", "\\'");
            var parentClause = string.IsNullOrEmpty(parentId) ? "'root' in parents" : $"'{parentId}' in parents";
            var q = Uri.EscapeDataString($"name='{escapedName}' and mimeType='application/vnd.google-apps.folder' and {parentClause} and trashed=false");
            var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id)&pageSize=1";

            var resp = await http.GetAsync(searchUrl);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
                {
                    return files[0].GetProperty("id").GetString();
                }
            }

            var createObj = new Dictionary<string, object>
            {
                ["name"] = name,
                ["mimeType"] = "application/vnd.google-apps.folder"
            };
            if (!string.IsNullOrEmpty(parentId))
            {
                createObj["parents"] = new[] { parentId };
            }

            var createContent = new StringContent(JsonSerializer.Serialize(createObj), Encoding.UTF8, "application/json");
            var postResp = await http.PostAsync("https://www.googleapis.com/drive/v3/files?fields=id", createContent);
            if (postResp.IsSuccessStatusCode)
            {
                var postJson = await postResp.Content.ReadAsStringAsync();
                using var postDoc = JsonDocument.Parse(postJson);
                return postDoc.RootElement.GetProperty("id").GetString();
            }
        }
        catch { }
        return null;
    }

    private static async Task UploadOrReplaceDriveFileAsync(HttpClient http, string parentFolderId, string fileName, byte[] content, string contentType)
    {
        try
        {
            var escaped = fileName.Replace("'", "\\'");
            var q = Uri.EscapeDataString($"name='{escaped}' and '{parentFolderId}' in parents and trashed=false");
            var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id)&pageSize=1";
            var sResp = await http.GetAsync(searchUrl);
            if (sResp.IsSuccessStatusCode)
            {
                var sJson = await sResp.Content.ReadAsStringAsync();
                using var sDoc = JsonDocument.Parse(sJson);
                if (sDoc.RootElement.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
                {
                    var existingId = files[0].GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(existingId))
                    {
                        var updateContent = new ByteArrayContent(content);
                        updateContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                        await http.PatchAsync($"https://www.googleapis.com/upload/drive/v3/files/{existingId}?uploadType=media", updateContent);
                        return;
                    }
                }
            }

            var boundary = "cr_boundary_" + Guid.NewGuid().ToString("N");
            var multipart = new MultipartContent("related", boundary);

            var metaObj = new
            {
                name = fileName,
                parents = new[] { parentFolderId }
            };
            var metaContent = new StringContent(JsonSerializer.Serialize(metaObj), Encoding.UTF8, "application/json");
            multipart.Add(metaContent);

            var fileContent = new ByteArrayContent(content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            multipart.Add(fileContent);

            var url = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";
            await http.PostAsync(url, multipart);
        }
        catch { }
    }
}
