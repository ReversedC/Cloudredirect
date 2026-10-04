using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using CloudRedirect.Windows;

namespace CloudRedirect.Services;

/// <summary>
/// Model representing a community guide / tutorial from PatchWiki
/// (https://mirzaarsyad74-cmyk.github.io/patchwiki/).
/// </summary>
public sealed class PatchWikiTutorial
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("game")]
    public string Game { get; set; } = "";

    [JsonPropertyName("desc")]
    public string Desc { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("appId")]
    public uint AppId { get; set; }

    [JsonIgnore]
    public string TutorialUrl => !string.IsNullOrEmpty(Id)
        ? $"https://mirzaarsyad74-cmyk.github.io/patchwiki/?tutorial={Uri.EscapeDataString(Id)}#read/{Uri.EscapeDataString(Id)}"
        : "https://mirzaarsyad74-cmyk.github.io/patchwiki/";
}

/// <summary>
/// Service to synchronize, cache, and resolve community guides from PatchWiki
/// for games by AppID, enabling direct deep-linking in CloudRedirect Mini Window
/// and Steam WebUI / Millennium integration.
/// </summary>
public sealed class PatchWikiService
{
    private static readonly Lazy<PatchWikiService> _instance = new(() => new PatchWikiService());
    public static PatchWikiService Instance => _instance.Value;

    public const string PatchWikiIndexUrl = "https://mirzaarsyad74-cmyk.github.io/patchwiki/index.json";
    public const string PatchWikiHomeUrl = "https://mirzaarsyad74-cmyk.github.io/patchwiki/";

    private readonly ConcurrentDictionary<uint, PatchWikiTutorial> _appIdMap = new();
    private readonly ConcurrentDictionary<string, PatchWikiTutorial> _idMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;
    private bool _initialized;
    private readonly object _initLock = new();

    public event Action? TutorialsUpdated;

    public PatchWikiService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CloudRedirect-Companion/2.9");
    }

    public static string CacheFilePath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "CloudRedirect", "patchwiki_cache.json");
        }
    }

    public static string SteamPluginCacheFilePath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "CloudRedirect", "steam_plugin_patchwiki.json");
        }
    }

    /// <summary>
    /// Initializes the PatchWiki service. Loads existing disk cache immediately
    /// so the UI has zero delay, then asynchronously updates in the background.
    /// </summary>
    public void Initialize()
    {
        lock (_initLock)
        {
            if (_initialized) return;
            _initialized = true;
        }

        // 1. Instant disk cache load
        LoadFromCache();

        // 2. Background fresh sync + continuous auto-update loop
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await SyncTutorialsFromWebAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PatchWikiService] Web sync failed: {ex.Message}");
                }

                // Auto-update every 10 minutes to pull newly published community guides
                await Task.Delay(TimeSpan.FromMinutes(10));
            }
        });
    }

    public bool HasTutorial(uint appId)
    {
        return appId > 0 && _appIdMap.ContainsKey(appId);
    }

    public PatchWikiTutorial? GetTutorial(uint appId)
    {
        if (appId == 0) return null;
        _appIdMap.TryGetValue(appId, out var tutorial);
        return tutorial;
    }

    public string? GetTutorialUrl(uint appId)
    {
        var tutorial = GetTutorial(appId);
        return tutorial?.TutorialUrl;
    }

    public void OpenTutorial(uint appId)
    {
        var tutorial = GetTutorial(appId);
        string url = tutorial?.TutorialUrl ?? $"{PatchWikiHomeUrl}?appid={appId}";
        MiniBrowserWindow.Open(url);
    }

    /// <summary>
    /// High-precision AppID extractor from PatchWiki metadata (handles pure numbers,
    /// slug suffixes, title patterns, and game names).
    /// </summary>
    public static uint? ExtractAppId(string? id, string? title, string? game)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            // Case 1: Pure numeric id (e.g. "500", "550", "10180")
            if (uint.TryParse(id.Trim(), out var pureId) && pureId > 0)
            {
                return pureId;
            }

            // Case 2: Ends with AppID suffix (e.g. "planet-coaster-493340", "carx-street-1114150")
            var matchSuffix = Regex.Match(id, @"-(\d{3,9})$");
            if (matchSuffix.Success && uint.TryParse(matchSuffix.Groups[1].Value, out var suffId) && suffId > 0)
            {
                return suffId;
            }
        }

        // Case 3: Title contains explicit AppID (e.g. "DiRT 4 online patch - 421020", "Assassin Creed Mirage (3035570)")
        if (!string.IsNullOrWhiteSpace(title))
        {
            var matchTitle = Regex.Match(title, @"\b(\d{3,9})\b");
            if (matchTitle.Success && uint.TryParse(matchTitle.Groups[1].Value, out var titleId) && titleId > 0)
            {
                return titleId;
            }
        }

        // Case 4: Game name contains explicit AppID
        if (!string.IsNullOrWhiteSpace(game))
        {
            var matchGame = Regex.Match(game, @"\b(\d{3,9})\b");
            if (matchGame.Success && uint.TryParse(matchGame.Groups[1].Value, out var gameId) && gameId > 0)
            {
                return gameId;
            }
        }

        // Case 5: Any sequence of 3-9 digits in id
        if (!string.IsNullOrWhiteSpace(id))
        {
            var matchAny = Regex.Match(id, @"(\d{3,9})");
            if (matchAny.Success && uint.TryParse(matchAny.Groups[1].Value, out var anyId) && anyId > 0)
            {
                return anyId;
            }
        }

        return null;
    }

    private void LoadFromCache()
    {
        try
        {
            var path = CacheFilePath;
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<PatchWikiTutorial>>(json);
            if (list != null && list.Count > 0)
            {
                ApplyTutorials(list);
                if (!File.Exists(SteamPluginCacheFilePath))
                {
                    SaveToCache(list);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PatchWikiService] LoadFromCache error: {ex.Message}");
        }
    }

    public async Task SyncTutorialsFromWebAsync()
    {
        try
        {
            var url = $"{PatchWikiIndexUrl}?_t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[PatchWikiService] Fetch returned status {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var list = JsonSerializer.Deserialize<List<PatchWikiTutorial>>(json);
            if (list == null || list.Count == 0) return;

            // Process AppIDs
            foreach (var item in list)
            {
                if (item.AppId == 0)
                {
                    var appId = ExtractAppId(item.Id, item.Title, item.Game);
                    if (appId.HasValue && appId.Value > 0)
                    {
                        item.AppId = appId.Value;
                    }
                }
            }

            ApplyTutorials(list);
            SaveToCache(list);

            // Notify UI
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                TutorialsUpdated?.Invoke();
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PatchWikiService] SyncTutorialsFromWebAsync error: {ex}");
        }
    }

    private void ApplyTutorials(IEnumerable<PatchWikiTutorial> list)
    {
        foreach (var item in list)
        {
            if (!string.IsNullOrEmpty(item.Id))
            {
                _idMap[item.Id] = item;
            }

            if (item.AppId == 0)
            {
                var id = ExtractAppId(item.Id, item.Title, item.Game);
                if (id.HasValue) item.AppId = id.Value;
            }

            if (item.AppId > 0)
            {
                _appIdMap[item.AppId] = item;
            }
        }
    }

    private void SaveToCache(List<PatchWikiTutorial> list)
    {
        try
        {
            var dir = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(list, opts);
            File.WriteAllText(CacheFilePath, json);

            // Export compact map for Steam Millennium plugin: { "<appId>": { "id": "...", "title": "...", "url": "...", "game": "...", "desc": "...", "tags": [...], "author": "...", "date": "..." } }
            var pluginMap = new Dictionary<string, object>();
            foreach (var kvp in _appIdMap)
            {
                pluginMap[kvp.Key.ToString()] = new
                {
                    appId = kvp.Key,
                    id = kvp.Value.Id,
                    title = kvp.Value.Title,
                    url = kvp.Value.TutorialUrl,
                    game = kvp.Value.Game,
                    desc = kvp.Value.Desc,
                    tags = kvp.Value.Tags,
                    author = kvp.Value.Author,
                    date = kvp.Value.Date
                };
            }
            var pluginJson = JsonSerializer.Serialize(pluginMap);
            File.WriteAllText(SteamPluginCacheFilePath, pluginJson);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PatchWikiService] SaveToCache error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a URL or AppID from a custom protocol invocation (e.g. cloudredirect://minibrowser?url=...
    /// or cloudredirect://guide?appid=...).
    /// </summary>
    public static string? ExtractUrlFromProtocolCommand(string cmd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;

            cmd = cmd.Trim().Trim('"', '\'');

            int qIdx = cmd.IndexOf('?');
            if (qIdx >= 0 && qIdx < cmd.Length - 1)
            {
                string qs = cmd.Substring(qIdx + 1);
                string[] pairs = qs.Split('&');
                foreach (var pair in pairs)
                {
                    var kv = pair.Split(new[] { '=' }, 2);
                    if (kv.Length == 2)
                    {
                        string key = kv[0].Trim();
                        string val = Uri.UnescapeDataString(kv[1].Trim());

                        if (key.Equals("url", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val))
                        {
                            return val;
                        }

                        if (key.Equals("appid", StringComparison.OrdinalIgnoreCase) && uint.TryParse(val, out var appId) && appId > 0)
                        {
                            var tutorial = Instance.GetTutorial(appId);
                            return tutorial?.TutorialUrl ?? $"{PatchWikiHomeUrl}?appid={appId}";
                        }
                    }
                }
            }

            if (cmd.StartsWith("cloudredirect://patchwiki", StringComparison.OrdinalIgnoreCase) ||
                cmd.StartsWith("cloudredirect://guide", StringComparison.OrdinalIgnoreCase))
            {
                return PatchWikiHomeUrl;
            }
        }
        catch { }

        return null;
    }
}
