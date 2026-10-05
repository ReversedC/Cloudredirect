using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudRedirect.Services;

/// <summary>
/// High-performance parser that converts Ludusavi manifest YAML
/// (from https://github.com/mtkennerly/ludusavi-manifest) directly into CloudRedirect's
/// community database format (schema: "cloudredirect-community-saves-v1").
/// </summary>
public static class LudusaviManifestParser
{
    public const string DefaultManifestUrl = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";

    private static readonly HashSet<string> TooBroadPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "%USERPROFILE%",
        "%USERPROFILE%\\DOCUMENTS",
        "%USERPROFILE%\\SAVED GAMES",
        "%USERPROFILE%\\APPDATA",
        "%USERPROFILE%\\APPDATA\\LOCAL",
        "%USERPROFILE%\\APPDATA\\LOCAL\\PACKAGES",
        "%USERPROFILE%\\APPDATA\\LOCALLOW",
        "%USERPROFILE%\\APPDATA\\ROAMING",
        "%APPDATA%",
        "%LOCALAPPDATA%",
        "%PUBLIC%",
        "%PROGRAMDATA%",
        "DOCUMENTS",
        "DOCUMENTS\\MY GAMES",
        "C:\\PROGRAM FILES",
        "C:\\PROGRAM FILES (X86)",
        "C:\\XBOXGAMES"
    };

    private static readonly HashSet<string> FileExtensionsToStrip = new(StringComparer.OrdinalIgnoreCase)
    {
        "sav", "save", "ini", "json", "dat", "xml", "bin", "cfg", "config",
        "txt", "db", "sqlite", "log", "ron", "profile", "dat0", "sl2", "sl3", "chr"
    };

    /// <summary>
    /// Normalizes any input URL to the raw manifest.yaml endpoint.
    /// Handles repo links like https://github.com/mtkennerly/ludusavi-manifest.
    /// </summary>
    public static string NormalizeManifestUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return DefaultManifestUrl;

        var trimmed = url.Trim();
        if (trimmed.Equals("https://github.com/mtkennerly/ludusavi-manifest", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("https://github.com/mtkennerly/ludusavi-manifest/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("mtkennerly/ludusavi-manifest", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultManifestUrl;
        }

        if (trimmed.Contains("github.com/mtkennerly/ludusavi-manifest/blob/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Replace("github.com", "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                          .Replace("/blob/", "/", StringComparison.OrdinalIgnoreCase);
        }

        return trimmed;
    }

    /// <summary>
    /// Determines whether the input text looks like a Ludusavi YAML manifest rather than JSON.
    /// </summary>
    public static bool IsYamlManifest(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var span = content.AsSpan().TrimStart();
        if (span.StartsWith("{") || span.StartsWith("["))
            return false;

        return span.StartsWith("---") ||
               content.Contains("files:\n") ||
               content.Contains("files:\r\n") ||
               content.Contains("steam:\n") ||
               content.Contains("steam:\r\n");
    }

    /// <summary>
    /// Parses a Ludusavi YAML manifest string and returns a dictionary of signatures
    /// keyed by Game Name and "steam:{appId}".
    /// </summary>
    public static (int totalGames, int totalSignatures, Dictionary<string, string[]> signatures) ParseManifest(string yamlText)
    {
        var gamesMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        int totalGames = 0;

        using var reader = new StringReader(yamlText);
        string? line;

        string? currentGame = null;
        bool inFiles = false;
        bool inSteam = false;
        bool inSteamExtra = false;

        string? currentPathRaw = null;
        var currentPathOsList = new List<string>(2);

        var pathsForCurrentGame = new List<string>(4);
        var steamIdsForCurrentGame = new List<uint>(2);

        void CommitCurrentPath()
        {
            if (string.IsNullOrEmpty(currentPathRaw)) return;

            // If an OS list was provided and Windows is not in it, skip
            if (currentPathOsList.Count > 0 && !currentPathOsList.Contains("windows"))
            {
                currentPathRaw = null;
                currentPathOsList.Clear();
                return;
            }

            var normalized = NormalizeWindowsPath(currentPathRaw);
            if (!string.IsNullOrEmpty(normalized) && !pathsForCurrentGame.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                pathsForCurrentGame.Add(normalized);
            }

            currentPathRaw = null;
            currentPathOsList.Clear();
        }

        void CommitCurrentGame()
        {
            CommitCurrentPath();
            if (!string.IsNullOrEmpty(currentGame) && pathsForCurrentGame.Count > 0)
            {
                totalGames++;
                var pathsArray = pathsForCurrentGame.ToArray();
                gamesMap[currentGame] = pathsArray;

                foreach (var sId in steamIdsForCurrentGame)
                {
                    gamesMap[$"steam:{sId}"] = pathsArray;
                }
            }

            currentGame = null;
            pathsForCurrentGame.Clear();
            steamIdsForCurrentGame.Clear();
        }

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---"))
                continue;

            int indent = 0;
            while (indent < line.Length && line[indent] == ' ')
                indent++;

            var stripped = line.Substring(indent).Trim();

            // Top-level line: Game Title (indent == 0)
            if (indent == 0)
            {
                CommitCurrentGame();
                currentGame = CleanGameTitle(stripped);
                inFiles = false;
                inSteam = false;
                inSteamExtra = false;
                continue;
            }

            if (string.IsNullOrEmpty(currentGame))
                continue;

            // Indent 2: Section header (files, launch, steam, id, etc.)
            if (indent == 2)
            {
                CommitCurrentPath();
                inFiles = stripped.StartsWith("files:");
                inSteam = stripped.StartsWith("steam:");
                inSteamExtra = false;
                continue;
            }

            // Inside files section
            if (inFiles)
            {
                if (indent == 4)
                {
                    CommitCurrentPath();
                    int colonIdx = stripped.IndexOf(':');
                    currentPathRaw = colonIdx != -1 ? stripped.Substring(0, colonIdx).Trim() : stripped;
                    currentPathOsList.Clear();
                }
                else if (indent >= 6)
                {
                    if (stripped.StartsWith("os:"))
                    {
                        currentPathOsList.Add(stripped.Substring(3).Trim().ToLowerInvariant());
                    }
                    else if (stripped.StartsWith("- os:"))
                    {
                        currentPathOsList.Add(stripped.Substring(5).Trim().ToLowerInvariant());
                    }
                }
            }

            // Inside steam section
            if (inSteam)
            {
                if (stripped.StartsWith("id:"))
                {
                    if (uint.TryParse(stripped.Substring(3).Trim(), out var sId) && sId > 0)
                    {
                        if (!steamIdsForCurrentGame.Contains(sId))
                            steamIdsForCurrentGame.Add(sId);
                    }
                }
            }

            // Steam extra IDs
            if (indent == 2 && stripped.StartsWith("id:"))
            {
                inSteam = false;
            }
            if (indent == 4 && stripped.StartsWith("steamExtra:"))
            {
                inSteamExtra = true;
            }
            else if (inSteamExtra && indent >= 6 && stripped.StartsWith("-"))
            {
                var numStr = stripped.Substring(1).Trim();
                if (uint.TryParse(numStr, out var extraId) && extraId > 0)
                {
                    if (!steamIdsForCurrentGame.Contains(extraId))
                        steamIdsForCurrentGame.Add(extraId);
                }
            }
            else if (indent < 6)
            {
                inSteamExtra = false;
            }
        }

        CommitCurrentGame();

        return (totalGames, gamesMap.Count, gamesMap);
    }

    /// <summary>
    /// Parses the YAML text and generates the full CloudRedirect JSON schema.
    /// </summary>
    public static (int totalGames, int totalSignatures, string json) ConvertToCloudRedirectJson(string yamlText)
    {
        var (totalGames, totalSignatures, signatures) = ParseManifest(yamlText);

        var model = new
        {
            schema = "cloudredirect-community-saves-v1",
            updatedAt = DateTime.UtcNow.ToString("o"),
            description = "CloudRedirect Community Game Save Location Database (Auto-generated from Ludusavi Manifest)",
            source = "https://github.com/mtkennerly/ludusavi-manifest",
            totalGames,
            totalSignatures,
            games = signatures
        };

        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return (totalGames, totalSignatures, json);
    }

    private static string CleanGameTitle(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("\""))
        {
            int endIdx = raw.LastIndexOf("\":", StringComparison.Ordinal);
            if (endIdx != -1)
                return raw.Substring(1, endIdx - 1).Replace("\\\"", "\"").Trim();
        }
        if (raw.StartsWith("'"))
        {
            int endIdx = raw.LastIndexOf("':", StringComparison.Ordinal);
            if (endIdx != -1)
                return raw.Substring(1, endIdx - 1).Replace("\\'", "'").Trim();
        }
        if (raw.EndsWith(":"))
            return raw.Substring(0, raw.Length - 1).Trim();

        int colon = raw.LastIndexOf(':');
        if (colon != -1)
            return raw.Substring(0, colon).Trim();

        return raw.Trim('"', '\'');
    }

    public static string? NormalizeWindowsPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        var p = rawPath.Trim('"', '\'').Trim();
        p = p.Replace('/', '\\');

        // Exclude non-Windows paths
        if (p.StartsWith("~") || p.StartsWith("\\"))
            return null;
        if (p.Contains("<xdgData>") || p.Contains("<xdgConfig>"))
            return null;
        if (p.Contains("<home>\\Library") || p.Contains("<home>\\.config") || p.Contains("<home>\\.local"))
            return null;

        // Replace placeholders with Windows environment variables
        p = p.Replace("<winLocalAppDataLow>", "%USERPROFILE%\\AppData\\LocalLow");
        p = p.Replace("<winLocalAppData>", "%LOCALAPPDATA%");
        p = p.Replace("<winAppData>", "%APPDATA%");
        p = p.Replace("<winDocuments>", "Documents");
        p = p.Replace("<winSavedGames>", "%USERPROFILE%\\Saved Games");
        p = p.Replace("<winProgramData>", "%PROGRAMDATA%");
        p = p.Replace("<winPublic>", "%PUBLIC%");
        p = p.Replace("<winDir>", "%WINDIR%");
        p = p.Replace("<home>\\AppData\\LocalLow", "%USERPROFILE%\\AppData\\LocalLow");
        p = p.Replace("<home>\\Saved Games", "%USERPROFILE%\\Saved Games");
        p = p.Replace("<home>\\Documents", "Documents");
        p = p.Replace("<home>", "%USERPROFILE%");
        p = p.Replace("<storeUserId>", "*");
        p = p.Replace("<osUserName>", "%USERNAME%");

        // Discard unsupported placeholders
        if (p.Contains('<') && p.Contains('>'))
            return null;

        // Separate segments to discard globs and wildcards
        var segments = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var folderSegments = new List<string>(segments.Length);
        foreach (var seg in segments)
        {
            if (seg.Contains('*') || seg.Contains('?'))
                break;
            folderSegments.Add(seg);
        }

        var folder = string.Join('\\', folderSegments);

        // If last segment has a file extension, strip it to keep the parent save directory
        if (folder.Contains('\\'))
        {
            var lastSeg = folder.Substring(folder.LastIndexOf('\\') + 1);
            int dotIdx = lastSeg.LastIndexOf('.');
            if (dotIdx > 0)
            {
                var ext = lastSeg.Substring(dotIdx + 1);
                if (FileExtensionsToStrip.Contains(ext))
                {
                    folder = folder.Substring(0, folder.LastIndexOf('\\'));
                }
            }
        }

        folder = Regex.Replace(folder, @"\\{2,}", "\\").TrimEnd('\\');

        if (string.IsNullOrWhiteSpace(folder) || TooBroadPaths.Contains(folder))
            return null;

        // Ensure valid Windows path starting with variable, Documents, or drive
        if (!(folder.StartsWith("%") || folder.StartsWith("Documents") || (folder.Length >= 2 && folder[1] == ':')))
            return null;

        return folder;
    }
}
