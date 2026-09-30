using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

/// <summary>
/// Handles automatic cloud uploads and synchronization for Universal Cloud Saves (Safe Mode).
/// Uploads saves directly to Google Drive, OneDrive, or local sync folders.
/// </summary>
public static class UniversalCloudSyncService
{
    public record SyncResult(bool Success, int FileCount, long TotalBytes, string Message, string? FolderUrl = null);

    /// <summary>
    /// Synchronizes a Universal Game Profile's save files to the active cloud provider (Google Drive, Folder, etc.).
    /// </summary>
    public static async Task<SyncResult> UploadProfileSavesToCloudAsync(UniversalGameProfile profile)
    {
        var saveDir = profile.ExpandedSavePath;
        if (!Directory.Exists(saveDir))
        {
            return new SyncResult(false, 0, 0, "Save directory does not exist locally.");
        }

        var saveFiles = Directory.GetFiles(saveDir, "*", SearchOption.AllDirectories);
        if (saveFiles.Length == 0)
        {
            return new SyncResult(false, 0, 0, "No save files found in directory yet. Please play the game and save first.");
        }

        var config = SteamDetector.ReadConfig();
        if (config == null)
        {
            return new SyncResult(false, 0, 0, "No cloud provider configured in CloudRedirect.");
        }

        // 1. Google Drive Direct API Sync
        if (config.Provider == "gdrive")
        {
            return await UploadToGoogleDriveAsync(profile, saveDir, saveFiles, config);
        }

        // 2. Folder / Local / Network Sync
        if (config.IsFolder || config.IsLocal || !string.IsNullOrEmpty(config.SyncPath))
        {
            return await UploadToLocalSyncFolderAsync(profile, saveDir, saveFiles, config.SyncPath ?? "");
        }

        // 3. Fallback: Local snapshot only
        return new SyncResult(true, saveFiles.Length, 0, "Local snapshots active (Safe Mode).");
    }

    /// <summary>
    /// Synchronizes all enabled game save profiles to the cloud.
    /// </summary>
    public static async Task<int> SyncAllProfilesAsync()
    {
        var profiles = UniversalSaveWatcherService.GetProfiles();
        int count = 0;
        foreach (var profile in profiles.Where(p => p.Enabled))
        {
            try
            {
                var res = await UploadProfileSavesToCloudAsync(profile);
                if (res.Success) count++;
            }
            catch { }
        }
        return count;
    }

    private static async Task<SyncResult> UploadToGoogleDriveAsync(
        UniversalGameProfile profile,
        string saveDir,
        string[] saveFiles,
        CloudConfig config)
    {
        try
        {
            var tokenPath = config.TokenPath ?? Path.Combine(SteamDetector.GetConfigDir(), "google_tokens.json");
            var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);

            if (string.IsNullOrEmpty(accessToken))
            {
                return new SyncResult(false, 0, 0, "Google Drive authentication token expired. Please re-authenticate in Cloud Settings.");
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Step 1: Resolve or create root 'CloudRedirect' folder
            var rootFolderId = await EnsureDriveFolderAsync(http, "CloudRedirect", null);
            if (string.IsNullOrEmpty(rootFolderId))
            {
                return new SyncResult(false, 0, 0, "Failed to create or access 'CloudRedirect' root folder on Google Drive.");
            }

            // Step 2: Resolve or create 'UniversalCloudSaves' folder inside 'CloudRedirect'
            var universalFolderId = await EnsureDriveFolderAsync(http, "UniversalCloudSaves", rootFolderId);
            if (string.IsNullOrEmpty(universalFolderId))
            {
                return new SyncResult(false, 0, 0, "Failed to create 'UniversalCloudSaves' directory on Google Drive.");
            }

            // Step 3: Resolve or create Game folder inside 'UniversalCloudSaves'
            var sanitizedGameName = SaveHistoryManager.SanitizeFolderName(profile.GameName);
            var gameFolderId = await EnsureDriveFolderAsync(http, sanitizedGameName, universalFolderId);
            if (string.IsNullOrEmpty(gameFolderId))
            {
                return new SyncResult(false, 0, 0, $"Failed to create folder for '{profile.GameName}' on Google Drive.");
            }

            // Fetch web link of the game folder for direct opening
            var folderLink = await GetDriveFolderWebLinkAsync(http, gameFolderId);

            // Step 4: List existing files in this game folder recursively to support nested paths and in-place PATCH updates
            var existingFilesList = new List<CloudDriveFileInfo>();
            await ListDriveFilesRecursivelyAsync(http, gameFolderId, "", existingFilesList);
            var existingFilesMap = existingFilesList
                .Where(f => !f.IsDirectory)
                .GroupBy(f => f.Name.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var folderIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [""] = gameFolderId
            };

            int uploadedCount = 0;
            int failedCount = 0;
            long totalBytesUploaded = 0;

            foreach (var filePath in saveFiles)
            {
                var relPath = Path.GetRelativePath(saveDir, filePath).Replace('\\', '/');
                var fileName = Path.GetFileName(filePath);
                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Length;

                // Handle nested directories if any
                var targetFolderId = gameFolderId;
                var subDir = Path.GetDirectoryName(Path.GetRelativePath(saveDir, filePath))?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(subDir))
                {
                    if (folderIdCache.TryGetValue(subDir, out var cachedId))
                    {
                        targetFolderId = cachedId;
                    }
                    else
                    {
                        var parts = subDir.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        var curParent = gameFolderId;
                        string accumulatedPath = "";
                        foreach (var part in parts)
                        {
                            accumulatedPath = string.IsNullOrEmpty(accumulatedPath) ? part : $"{accumulatedPath}/{part}";
                            if (!folderIdCache.TryGetValue(accumulatedPath, out var nextId))
                            {
                                nextId = await EnsureDriveFolderAsync(http, part, curParent);
                                if (!string.IsNullOrEmpty(nextId))
                                {
                                    folderIdCache[accumulatedPath] = nextId;
                                }
                            }
                            if (string.IsNullOrEmpty(nextId)) break;
                            curParent = nextId;
                        }
                        if (!string.IsNullOrEmpty(curParent))
                        {
                            targetFolderId = curParent;
                            folderIdCache[subDir] = targetFolderId;
                        }
                    }
                }

                // Check if file already exists with same size
                existingFilesMap.TryGetValue(relPath, out var driveFile);
                if (driveFile != null && driveFile.Size == fileSize)
                {
                    totalBytesUploaded += fileSize;
                    continue; // Up-to-date, skip re-uploading
                }

                // Upload new / updated file
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                bool ok = await UploadFileToDriveAsync(http, targetFolderId, fileName, fileBytes, driveFile?.Id);
                if (ok)
                {
                    uploadedCount++;
                    totalBytesUploaded += fileBytes.Length;
                }
                else
                {
                    failedCount++;
                }
            }

            // Write telemetry entry to log for SaveUploadWatcher
            LogUploadActivity(profile, uploadedCount, totalBytesUploaded);

            if (failedCount > 0)
            {
                return new SyncResult(
                    false,
                    uploadedCount,
                    totalBytesUploaded,
                    $"Failed to upload {failedCount} of {saveFiles.Length} file(s) to Google Drive. Check internet connection or quota.",
                    folderLink);
            }

            return new SyncResult(
                true,
                saveFiles.Length,
                totalBytesUploaded,
                uploadedCount > 0
                    ? $"Successfully synced {saveFiles.Length} file(s) ({uploadedCount} uploaded) to Google Drive."
                    : $"All {saveFiles.Length} save file(s) are already up to date on Google Drive.",
                folderLink);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UniversalCloudSync error for {profile.GameName}: {ex}");
            return new SyncResult(false, 0, 0, $"Google Drive sync error: {ex.Message}");
        }
    }

    private static async Task<SyncResult> UploadToLocalSyncFolderAsync(
        UniversalGameProfile profile,
        string saveDir,
        string[] saveFiles,
        string syncPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(syncPath) || !Directory.Exists(syncPath))
                {
                    return new SyncResult(false, 0, 0, "Configured sync path does not exist.");
                }

                var sanitizedName = SaveHistoryManager.SanitizeFolderName(profile.GameName);
                var destRoot = Path.Combine(syncPath, "UniversalCloudSaves", sanitizedName);
                if (!Directory.Exists(destRoot))
                    Directory.CreateDirectory(destRoot);

                int copied = 0;
                long totalBytes = 0;

                foreach (var src in saveFiles)
                {
                    var rel = Path.GetRelativePath(saveDir, src);
                    var dest = Path.Combine(destRoot, rel);
                    var destDir = Path.GetDirectoryName(dest)!;
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    var info = new FileInfo(src);
                    File.Copy(src, dest, overwrite: true);
                    copied++;
                    totalBytes += info.Length;
                }

                return new SyncResult(
                    true,
                    copied,
                    totalBytes,
                    $"Successfully synced {copied} file(s) to local cloud storage folder.");
            }
            catch (Exception ex)
            {
                return new SyncResult(false, 0, 0, $"Local sync error: {ex.Message}");
            }
        });
    }

    private static async Task<string?> EnsureDriveFolderAsync(HttpClient http, string name, string? parentId)
    {
        try
        {
            var escapedName = name.Replace("\\", "\\\\").Replace("'", "\\'");
            var parentClause = string.IsNullOrEmpty(parentId)
                ? "'root' in parents"
                : $"'{parentId}' in parents";

            var q = Uri.EscapeDataString($"name='{escapedName}' and mimeType='application/vnd.google-apps.folder' and {parentClause} and trashed=false");
            var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id,name)&pageSize=1";

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

            // Not found, create folder
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
        catch (Exception ex)
        {
            Debug.WriteLine($"EnsureDriveFolderAsync error for '{name}': {ex}");
        }
        return null;
    }

    private static async Task<string?> GetDriveFolderWebLinkAsync(HttpClient http, string folderId)
    {
        try
        {
            var url = $"https://www.googleapis.com/drive/v3/files/{folderId}?fields=id,webViewLink";
            var resp = await http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("webViewLink", out var linkProp))
                {
                    return linkProp.GetString();
                }
            }
        }
        catch { }
        return $"https://drive.google.com/drive/folders/{folderId}";
    }

    /// <summary>
    /// Resolves the direct Google Drive web URL for a game's Universal Cloud Saves folder.
    /// Returns null if provider is not gdrive or folder cannot be resolved.
    /// </summary>
    public static async Task<string?> GetGameDriveFolderWebLinkAsync(string gameName)
    {
        try
        {
            var config = SteamDetector.ReadConfig();
            if (config == null || config.Provider != "gdrive") return null;

            var tokenPath = config.TokenPath ?? Path.Combine(SteamDetector.GetConfigDir(), "google_tokens.json");
            var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
            if (string.IsNullOrEmpty(accessToken)) return null;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var rootFolderId = await EnsureDriveFolderAsync(http, "CloudRedirect", null);
            if (string.IsNullOrEmpty(rootFolderId)) return null;

            var universalFolderId = await EnsureDriveFolderAsync(http, "UniversalCloudSaves", rootFolderId);
            if (string.IsNullOrEmpty(universalFolderId)) return null;

            var sanitizedGameName = SaveHistoryManager.SanitizeFolderName(gameName);
            var gameFolderId = await EnsureDriveFolderAsync(http, sanitizedGameName, universalFolderId);
            if (string.IsNullOrEmpty(gameFolderId)) return null;

            return await GetDriveFolderWebLinkAsync(http, gameFolderId);
        }
        catch
        {
            return null;
        }
    }

    private record DriveFileInfo(string Id, string Name, long Size);

    private static async Task<Dictionary<string, DriveFileInfo>> ListDriveFolderFilesAsync(HttpClient http, string folderId)
    {
        var dict = new Dictionary<string, DriveFileInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var q = Uri.EscapeDataString($"'{folderId}' in parents and mimeType!='application/vnd.google-apps.folder' and trashed=false");
            var url = $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id,name,size)&pageSize=1000";

            var resp = await http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("files", out var files))
                {
                    foreach (var f in files.EnumerateArray())
                    {
                        var id = f.GetProperty("id").GetString()!;
                        var name = f.GetProperty("name").GetString()!;
                        long size = 0;
                        if (f.TryGetProperty("size", out var sProp))
                        {
                            long.TryParse(sProp.GetString(), out size);
                        }
                        dict[name] = new DriveFileInfo(id, name, size);
                    }
                }
            }
        }
        catch { }
        return dict;
    }

    private static async Task<bool> UploadFileToDriveAsync(
        HttpClient http,
        string parentFolderId,
        string fileName,
        byte[] content,
        string? existingFileId = null)
    {
        try
        {
            var boundary = "cr_boundary_" + Guid.NewGuid().ToString("N");
            var multipart = new MultipartContent("related", boundary);

            string metaJson;
            if (string.IsNullOrEmpty(existingFileId))
            {
                var metaObj = new
                {
                    name = fileName,
                    parents = new[] { parentFolderId }
                };
                metaJson = JsonSerializer.Serialize(metaObj);
            }
            else
            {
                var metaObj = new
                {
                    name = fileName
                };
                metaJson = JsonSerializer.Serialize(metaObj);
            }

            var metaContent = new StringContent(metaJson, Encoding.UTF8, "application/json");
            multipart.Add(metaContent);

            var fileContent = new ByteArrayContent(content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(fileContent);

            HttpResponseMessage resp;
            if (string.IsNullOrEmpty(existingFileId))
            {
                var url = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";
                resp = await http.PostAsync(url, multipart);
            }
            else
            {
                var url = $"https://www.googleapis.com/upload/drive/v3/files/{existingFileId}?uploadType=multipart";
                resp = await http.PatchAsync(url, multipart);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Fallback to POST if previously tracked file was deleted from Drive
                    var fallbackBoundary = "cr_boundary_" + Guid.NewGuid().ToString("N");
                    var fallbackMultipart = new MultipartContent("related", fallbackBoundary);
                    var postMeta = new
                    {
                        name = fileName,
                        parents = new[] { parentFolderId }
                    };
                    fallbackMultipart.Add(new StringContent(JsonSerializer.Serialize(postMeta), Encoding.UTF8, "application/json"));
                    var postBytes = new ByteArrayContent(content);
                    postBytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    fallbackMultipart.Add(postBytes);
                    resp = await http.PostAsync("https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart", fallbackMultipart);
                }
            }

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                Debug.WriteLine($"UploadFileToDriveAsync failed for {fileName} ({resp.StatusCode}): {err}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UploadFileToDriveAsync error for {fileName}: {ex}");
            return false;
        }
    }

    private static void LogUploadActivity(UniversalGameProfile profile, int uploadedCount, long bytes)
    {
        try
        {
            var steamPath = SteamDetector.FindSteamPath();
            if (steamPath != null)
            {
                var logFile = Path.Combine(steamPath, "cloud_redirect.log");
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [GDriveProvider] UploadBatch: {uploadedCount} file(s) ({uploadedCount} uploaded, 0 CAS-skipped) for {profile.GameName} (Universal Save)\n";
                File.AppendAllText(logFile, line);
            }
        }
        catch { }
    }

    public class CloudDriveFileInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public DateTime? ModifiedTime { get; set; }
        public string? WebViewLink { get; set; }
        public bool IsDirectory { get; set; }

        public string FormattedSize => IsDirectory ? "-" : FileUtils.FormatSize(Size);
        public string FormattedTime => ModifiedTime.HasValue
            ? ModifiedTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "-";
        public string IconSymbol => IsDirectory ? "Folder24" : "Document24";
    }

    public class CloudFolderViewResult
    {
        public bool Success { get; set; }
        public string GameName { get; set; } = "";
        public string FolderPathDisplay { get; set; } = "";
        public string? WebLink { get; set; }
        public List<CloudDriveFileInfo> Files { get; set; } = new();
        public string ErrorMessage { get; set; } = "";
        public long TotalSizeBytes => Files.Where(f => !f.IsDirectory).Sum(f => f.Size);
        public string FormattedTotalSize => FileUtils.FormatSize(TotalSizeBytes);
    }

    private static async Task<string?> FindDriveFolderByNameAndParentAsync(HttpClient http, string name, string? parentId)
    {
        try
        {
            var escapedName = name.Replace("\\", "\\\\").Replace("'", "\\'");
            var parentClause = string.IsNullOrEmpty(parentId)
                ? "'root' in parents"
                : $"'{parentId}' in parents";

            var q = Uri.EscapeDataString($"name='{escapedName}' and mimeType='application/vnd.google-apps.folder' and {parentClause} and trashed=false");
            var searchUrl = $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id,name)&pageSize=1";

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
        }
        catch { }
        return null;
    }

    private static async Task ListDriveFilesRecursivelyAsync(
        HttpClient http,
        string folderId,
        string prefix,
        List<CloudDriveFileInfo> resultList,
        int maxDepth = 6)
    {
        if (maxDepth <= 0) return;
        try
        {
            var q = Uri.EscapeDataString($"'{folderId}' in parents and trashed=false");
            var url = $"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id,name,size,modifiedTime,webViewLink,mimeType)&pageSize=1000";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("files", out var filesArray)) return;

            var subFolders = new List<(string Id, string Name)>();

            foreach (var f in filesArray.EnumerateArray())
            {
                var id = f.GetProperty("id").GetString() ?? "";
                var name = f.GetProperty("name").GetString() ?? "";
                var mime = f.TryGetProperty("mimeType", out var m) ? m.GetString() : "";
                bool isDir = mime == "application/vnd.google-apps.folder";

                if (isDir)
                {
                    subFolders.Add((id, name));
                }
                else
                {
                    long size = 0;
                    if (f.TryGetProperty("size", out var sProp))
                    {
                        long.TryParse(sProp.GetString(), out size);
                    }

                    DateTime? modTime = null;
                    if (f.TryGetProperty("modifiedTime", out var mtProp) &&
                        DateTime.TryParse(mtProp.GetString(), out var parsedMt))
                    {
                        modTime = parsedMt;
                    }

                    var webLink = f.TryGetProperty("webViewLink", out var wlProp) ? wlProp.GetString() : null;

                    resultList.Add(new CloudDriveFileInfo
                    {
                        Id = id,
                        Name = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}",
                        Size = size,
                        ModifiedTime = modTime,
                        WebViewLink = webLink,
                        IsDirectory = false
                    });
                }
            }

            foreach (var sub in subFolders)
            {
                var nextPrefix = string.IsNullOrEmpty(prefix) ? sub.Name : $"{prefix}/{sub.Name}";
                await ListDriveFilesRecursivelyAsync(http, sub.Id, nextPrefix, resultList, maxDepth - 1);
            }
        }
        catch { }
    }

    public static async Task<CloudFolderViewResult> GetCloudFolderViewAsync(string gameName, uint appId = 0, string? accountId = null)
    {
        var result = new CloudFolderViewResult { GameName = gameName };
        try
        {
            var config = SteamDetector.ReadConfig();
            if (config == null)
            {
                result.ErrorMessage = "No cloud provider is configured.";
                return result;
            }

            if (config.Provider == "gdrive")
            {
                var tokenPath = config.TokenPath ?? Path.Combine(SteamDetector.GetConfigDir(), "google_tokens.json");
                var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
                if (string.IsNullOrEmpty(accessToken))
                {
                    result.ErrorMessage = "Google Drive authorization expired or not found. Please sign in under Cloud Settings.";
                    return result;
                }

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var rootFolderId = await EnsureDriveFolderAsync(http, "CloudRedirect", null);
                if (string.IsNullOrEmpty(rootFolderId))
                {
                    result.ErrorMessage = "Could not locate 'CloudRedirect' root folder on Google Drive.";
                    return result;
                }

                string? gameFolderId = null;
                string folderDisplay = "";

                // 1) First check UniversalCloudSaves/{sanitizedGameName}
                var sanitizedGameName = SaveHistoryManager.SanitizeFolderName(gameName);
                var universalFolderId = await EnsureDriveFolderAsync(http, "UniversalCloudSaves", rootFolderId);
                if (!string.IsNullOrEmpty(universalFolderId))
                {
                    var foundId = await FindDriveFolderByNameAndParentAsync(http, sanitizedGameName, universalFolderId);
                    if (!string.IsNullOrEmpty(foundId))
                    {
                        gameFolderId = foundId;
                        folderDisplay = $"Google Drive ➔ CloudRedirect / UniversalCloudSaves / {sanitizedGameName}";
                    }
                }

                // 2) If not found and appId > 0, check Steam redirection folder: CloudRedirect/{accountId}/{appId}
                if (string.IsNullOrEmpty(gameFolderId) && appId > 0)
                {
                    if (!string.IsNullOrEmpty(accountId) && accountId != "0")
                    {
                        var acctFolderId = await FindDriveFolderByNameAndParentAsync(http, accountId, rootFolderId);
                        if (!string.IsNullOrEmpty(acctFolderId))
                        {
                            var appFolderId = await FindDriveFolderByNameAndParentAsync(http, appId.ToString(), acctFolderId);
                            if (!string.IsNullOrEmpty(appFolderId))
                            {
                                gameFolderId = appFolderId;
                                folderDisplay = $"Google Drive ➔ CloudRedirect / {accountId} / {appId}";
                            }
                        }
                    }

                    // Also search anywhere under Drive/CloudRedirect for folder named appId
                    if (string.IsNullOrEmpty(gameFolderId))
                    {
                        var qSearch = Uri.EscapeDataString($"name='{appId}' and mimeType='application/vnd.google-apps.folder' and trashed=false");
                        var searchResp = await http.GetAsync($"https://www.googleapis.com/drive/v3/files?q={qSearch}&fields=files(id,name,webViewLink)&pageSize=1");
                        if (searchResp.IsSuccessStatusCode)
                        {
                            var sJson = await searchResp.Content.ReadAsStringAsync();
                            using var sDoc = JsonDocument.Parse(sJson);
                            if (sDoc.RootElement.TryGetProperty("files", out var fArray) && fArray.GetArrayLength() > 0)
                            {
                                gameFolderId = fArray[0].GetProperty("id").GetString();
                                folderDisplay = $"Google Drive ➔ CloudRedirect / {appId}";
                            }
                        }
                    }
                }

                // 3) Fallback: if not found, ensure folder under UniversalCloudSaves
                if (string.IsNullOrEmpty(gameFolderId) && !string.IsNullOrEmpty(universalFolderId))
                {
                    gameFolderId = await EnsureDriveFolderAsync(http, sanitizedGameName, universalFolderId);
                    folderDisplay = $"Google Drive ➔ CloudRedirect / UniversalCloudSaves / {sanitizedGameName}";
                }

                if (string.IsNullOrEmpty(gameFolderId))
                {
                    result.ErrorMessage = $"Could not locate cloud folder for '{gameName}'.";
                    return result;
                }

                result.FolderPathDisplay = folderDisplay;
                result.WebLink = await GetDriveFolderWebLinkAsync(http, gameFolderId);

                // Fetch files recursively
                var filesList = new List<CloudDriveFileInfo>();
                await ListDriveFilesRecursivelyAsync(http, gameFolderId, "", filesList);

                result.Files = filesList.OrderBy(f => f.Name).ToList();
                result.Success = true;
                return result;
            }
            else if (config.IsFolder || config.IsLocal || !string.IsNullOrEmpty(config.SyncPath))
            {
                var basePath = config.SyncPath ?? "";
                var sanitizedGameName = SaveHistoryManager.SanitizeFolderName(gameName);
                var targetDir = Path.Combine(basePath, "UniversalCloudSaves", sanitizedGameName);

                if (!Directory.Exists(targetDir) && appId > 0)
                {
                    var acctDir = !string.IsNullOrEmpty(accountId) && accountId != "0"
                        ? Path.Combine(basePath, accountId, appId.ToString())
                        : Path.Combine(basePath, appId.ToString());
                    if (Directory.Exists(acctDir))
                    {
                        targetDir = acctDir;
                    }
                }

                result.FolderPathDisplay = $"Local Cloud Folder ➔ {targetDir}";
                if (Directory.Exists(targetDir))
                {
                    var di = new DirectoryInfo(targetDir);
                    foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(targetDir, fi.FullName).Replace('\\', '/');
                        result.Files.Add(new CloudDriveFileInfo
                        {
                            Id = fi.FullName,
                            Name = rel,
                            Size = fi.Length,
                            ModifiedTime = fi.LastWriteTimeUtc,
                            IsDirectory = false
                        });
                    }
                }
                result.Files = result.Files.OrderBy(f => f.Name).ToList();
                result.Success = true;
                return result;
            }
            else
            {
                result.ErrorMessage = "In-app cloud explorer is supported for Google Drive and Local Folders.";
                return result;
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Error loading cloud folder: {ex.Message}";
            return result;
        }
    }

    public static async Task<bool> DownloadCloudFileAsync(CloudDriveFileInfo file, string destinationPath)
    {
        try
        {
            var config = SteamDetector.ReadConfig();
            if (config?.Provider == "gdrive")
            {
                var tokenPath = config.TokenPath ?? Path.Combine(SteamDetector.GetConfigDir(), "google_tokens.json");
                var accessToken = await OAuthService.GetValidAccessTokenAsync("gdrive", tokenPath);
                if (string.IsNullOrEmpty(accessToken)) return false;

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var url = $"https://www.googleapis.com/drive/v3/files/{file.Id}?alt=media";
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return false;

                var dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                await using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await resp.Content.CopyToAsync(fs);
                return true;
            }
            else if (File.Exists(file.Id))
            {
                var dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(file.Id, destinationPath, true);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
