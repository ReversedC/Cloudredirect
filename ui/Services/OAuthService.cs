using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudRedirect.Resources;

namespace CloudRedirect.Services;

/// <summary>
/// DPAPI-based token file I/O.  Compatible with the C++ DLL's dpapi_util.h:
///  - Write: JSON -> DPAPI encrypt (CurrentUser) -> binary file (atomic tmp+rename)
///  - Read:  binary file -> try DPAPI decrypt; if the first byte is '{' treat as
///    legacy plaintext JSON and silently re-encrypt in place.
/// </summary>
internal static class TokenFile
{
    public static string? ReadJson(string path)
    {
        if (!File.Exists(path)) return null;
        var raw = File.ReadAllBytes(path);
        if (raw.Length == 0) return null;

        // Legacy plaintext JSON starts with '{'
        if (raw[0] == (byte)'{')
        {
            var json = Encoding.UTF8.GetString(raw);
            // Silently upgrade to DPAPI
            try { WriteJson(path, json); } catch { /* best-effort */ }
            return json;
        }

        // DPAPI-encrypted blob
        try
        {
            var plain = ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    public static void WriteJson(string path, string json)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var plain = Encoding.UTF8.GetBytes(json);
        var blob = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);

        FileUtils.AtomicWriteAllBytes(path, blob);
    }
}

/// <summary>
/// Handles OAuth2 authorization code flow for Google Drive and OneDrive.
/// Opens the user's browser, listens for the callback on localhost, exchanges
/// the auth code for tokens, and saves them in the format the DLL expects.
/// </summary>
public sealed class OAuthService : IDisposable
{
    private const string GDriveClientId = // owo what's this?
        "1072944905499-vm2v2i5dvn0a0d2o4ca36i1vge8cvbn0.apps.googleusercontent.com";
    private const string GDriveClientSecret = "v6V3fKV_zWU7iw1DrpO1rknX"; // uwuuu
    private const string GDriveScope = "https://www.googleapis.com/auth/drive.file";
    private const string GDriveAuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string GDriveTokenUrl = "https://oauth2.googleapis.com/token";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string? _oauthState;      // CSRF protection
    private string? _codeVerifier;    // PKCE code verifier
    private string? _currentProvider; // Track current provider for state validation
    private TaskCompletionSource<string>? _manualCodeTcs;
    private MobileAuthServer? _mobileServer;

    /// <summary>
    /// The authorization URL generated for the current or most recent OAuth session.
    /// </summary>
    public string? CurrentAuthUrl { get; private set; }

    /// <summary>
    /// The local mobile helper URL for scanning QR code on phone via Wi-Fi.
    /// </summary>
    public string? MobileHelperUrl => _mobileServer?.ServerUrl;

    /// <summary>
    /// Event triggered when the authorization URL is generated and ready to be used or copied.
    /// </summary>
    public event Action<string>? AuthUrlReady;

    /// <summary>
    /// The token path for the active OAuth session.
    /// </summary>
    public string? CurrentTokenPath { get; private set; }

    /// <summary>
    /// Whether the current token file is authenticated.
    /// </summary>
    public bool IsCurrentAuthenticated => !string.IsNullOrEmpty(CurrentTokenPath) && CheckTokenStatus(CurrentTokenPath).IsAuthenticated;

    /// <summary>
    /// Run the full OAuth flow for the given provider.
    /// Opens the browser, waits for the callback, exchanges the code, and saves tokens.
    /// </summary>
    /// <param name="provider">"gdrive" or "onedrive"</param>
    /// <param name="tokenPath">Where to save the resulting tokens.json</param>
    /// <param name="log">Progress callback</param>
    /// <param name="cancel">Cancellation token</param>
    /// <returns>True if tokens were obtained and saved successfully.</returns>
    public async Task<bool> AuthorizeAsync(
        string provider,
        string tokenPath,
        Action<string> log,
        CancellationToken cancel = default)
    {
        // Track current provider for state validation
        _currentProvider = provider;
        CurrentTokenPath = tokenPath;
        
        // Find an available port and start the listener
        // OneDrive uses fixed port 53682 (rclone's Azure AD app requirement)
        // Google Drive uses dynamic port
        int port = 0;
        string redirectUri;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        _listener = new HttpListener();

        // Google Drive - use dynamic port with /callback path
        for (int attempt = 0; attempt < 5; attempt++)
        {
            port = FindAvailablePort();
            redirectUri = $"http://localhost:{port}/callback";

            log($"Starting OAuth flow for Google Drive...");
            log($"Listening on {redirectUri}");

            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://localhost:{port}/callback/");

            try
            {
                _listener.Start();
                break; // success
            }
            catch (HttpListenerException) when (attempt < 4)
            {
                log($"Port {port} in use, retrying...");
                _listener.Close();
                _listener = new HttpListener();
                continue;
            }
            catch (HttpListenerException ex)
            {
                log($"ERROR: Failed to start HTTP listener after 5 attempts: {ex.Message}");
                _listener.Close();
                _listener = null;
                _cts.Dispose();
                _cts = null;
                return false;
            }
        }

        string redirectUriFinal = $"http://localhost:{port}/callback";

        // Generate CSRF state and PKCE code verifier
        _oauthState = GenerateRandomString(32);
        _codeVerifier = GenerateRandomString(64);
        string codeChallenge = ComputeCodeChallenge(_codeVerifier);

        // Build the authorization URL
        string authUrl = BuildGDriveAuthUrl(redirectUriFinal, _oauthState, codeChallenge);

        CurrentAuthUrl = authUrl;
        _manualCodeTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Start mobile helper server for QR code scanning over Wi-Fi
        try
        {
            _mobileServer = new MobileAuthServer(this, authUrl, log);
            _mobileServer.Start();
        }
        catch (Exception ex)
        {
            log($"Mobile Assist: Could not start local server: {ex.Message}");
        }

        AuthUrlReady?.Invoke(authUrl);

        // Open browser with fallback strategies
        log("Opening browser for authorization...");
        TryOpenBrowser(authUrl, log);

        log($"Authorization link generated:\n{authUrl}");
        log("Tip: If your browser did not open automatically, click 'Copy Link' or open the link above.");
        log("If using a phone/tablet or localhost is unreachable, paste the redirected URL or code below.");

        // Wait for the callback
        string? code = null;
        try
        {
            log("Waiting for authorization (complete the sign-in in your browser)...");
            code = await WaitForCallbackAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            log("Authorization cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            log($"ERROR: Callback failed: {ex.Message}");
            return false;
        }
        finally
        {
            StopListener();
        }

        if (string.IsNullOrEmpty(code))
        {
            log("ERROR: No authorization code received.");
            return false;
        }

        log("Authorization code received. Exchanging for tokens...");

        // Exchange code for tokens
        TokenResult? tokens;
        try
        {
            tokens = await ExchangeGDriveCodeAsync(code, redirectUriFinal, _codeVerifier!, cancel);
        }
        catch (Exception ex)
        {
            log($"ERROR: Token exchange failed: {ex.Message}");
            return false;
        }

        if (tokens == null || string.IsNullOrEmpty(tokens.RefreshToken))
        {
            log("ERROR: No refresh token received. The authorization may need to be revoked and re-done.");
            return false;
        }

        // Save tokens in the format the DLL expects (DPAPI-encrypted)
        try
        {
            long expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + tokens.ExpiresIn;

            var tokenObj = new
            {
                access_token = tokens.AccessToken,
                refresh_token = tokens.RefreshToken,
                expires_at = expiresAt
            };

            string json = JsonSerializer.Serialize(tokenObj, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await Task.Run(() => TokenFile.WriteJson(tokenPath, json), cancel);
            log($"Tokens saved to: {tokenPath}");
            log($"Access token expires in {tokens.ExpiresIn}s (the DLL will auto-refresh).");
            log("Authentication successful!");
            return true;
        }
        catch (Exception ex)
        {
            log($"ERROR: Failed to save tokens: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if a token file exists and contains a refresh token.
    /// Returns a status string for display.
    /// </summary>
    public static TokenStatus CheckTokenStatus(string tokenPath)
    {
        if (string.IsNullOrEmpty(tokenPath) || !File.Exists(tokenPath))
            return new TokenStatus(false, "No token file found");

        try
        {
            var json = TokenFile.ReadJson(tokenPath);
            if (json == null)
                return new TokenStatus(false, "Cannot decrypt token file");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool hasRefresh = root.TryGetProperty("refresh_token", out var rt)
                              && rt.GetString()?.Length > 0;

            if (!hasRefresh)
                return new TokenStatus(false, "Token file exists but missing refresh token");

            return new TokenStatus(true, S.Get("OAuth_Authenticated"));
        }
        catch (Exception ex)
        {
            return new TokenStatus(false, $"Cannot read token file: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets a valid access token for the given provider (refreshing if expired).
    /// Returns null if token file is missing or invalid.
    /// </summary>
    public static async Task<string?> GetValidAccessTokenAsync(string provider, string tokenPath)
    {
        if (string.IsNullOrEmpty(tokenPath) || !File.Exists(tokenPath))
            return null;

        try
        {
            var json = TokenFile.ReadJson(tokenPath);
            if (json == null) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            string? refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            long expiresAt = root.TryGetProperty("expires_at", out var exp) && exp.TryGetInt64(out var expVal) ? expVal : 0;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // If access token is valid for at least another 60 seconds, use it
            if (!string.IsNullOrEmpty(accessToken) && expiresAt > now + 60)
            {
                return accessToken;
            }

            // If we have a refresh token, attempt refresh
            if (!string.IsNullOrEmpty(refreshToken) && provider == "gdrive")
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var body = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = GDriveClientId,
                    ["client_secret"] = GDriveClientSecret,
                    ["refresh_token"] = refreshToken,
                    ["grant_type"] = "refresh_token"
                });

                var resp = await http.PostAsync(GDriveTokenUrl, body);
                if (resp.IsSuccessStatusCode)
                {
                    var respJson = await resp.Content.ReadAsStringAsync();
                    using var respDoc = JsonDocument.Parse(respJson);
                    if (respDoc.RootElement.TryGetProperty("access_token", out var newAt))
                    {
                        var newAccessToken = newAt.GetString();
                        int expiresIn = respDoc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

                        var tokenObj = new
                        {
                            access_token = newAccessToken,
                            refresh_token = refreshToken,
                            expires_at = now + expiresIn
                        };

                        TokenFile.WriteJson(tokenPath, JsonSerializer.Serialize(tokenObj, new JsonSerializerOptions { WriteIndented = true }));
                        return newAccessToken;
                    }
                }
            }

            return accessToken;
        }
        catch
        {
            return null;
        }
    }

    // --- private helpers ---

    private static string BuildGDriveAuthUrl(string redirectUri, string state, string codeChallenge)
    {
        return $"{GDriveAuthUrl}" +
               $"?client_id={Uri.EscapeDataString(GDriveClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&response_type=code" +
               $"&scope={Uri.EscapeDataString(GDriveScope)}" +
               $"&access_type=offline" +
               $"&prompt=consent" +
               $"&state={Uri.EscapeDataString(state)}" +
               $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
               $"&code_challenge_method=S256";
    }



    private async Task<string?> WaitForCallbackAsync(CancellationToken cancel)
    {
        // Wait up to 10 minutes for the user to complete auth or submit manual code
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);

        // Loop to skip non-OAuth requests (browser favicon, preflight, etc.)
        while (true)
        {
            linked.Token.ThrowIfCancellationRequested();

            Task<HttpListenerContext>? listenerTask = null;
            if (_listener != null && _listener.IsListening)
            {
                listenerTask = _listener.GetContextAsync();
            }

            Task<string>? manualTask = _manualCodeTcs?.Task;

            HttpListenerContext? ctx = null;

            if (listenerTask != null && manualTask != null)
            {
                var cancelTcs = new TaskCompletionSource<bool>();
                using var reg = linked.Token.Register(() => cancelTcs.TrySetCanceled());

                var completedTask = await Task.WhenAny(listenerTask, manualTask, cancelTcs.Task);
                if (completedTask == cancelTcs.Task)
                {
                    linked.Token.ThrowIfCancellationRequested();
                }

                if (completedTask == manualTask)
                {
                    _ = listenerTask.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
                    return await manualTask;
                }

                ctx = await listenerTask;
            }
            else if (listenerTask != null)
            {
                ctx = await listenerTask.WaitAsync(linked.Token);
            }
            else if (manualTask != null)
            {
                return await manualTask.WaitAsync(linked.Token);
            }
            else
            {
                return null;
            }

            if (ctx == null) continue;

            var query = ctx.Request.QueryString;
            string? code = query["code"];
            string? error = query["error"];
            string? state = query["state"];

            // If this request has neither code nor error nor state, it's not the OAuth
            // callback (e.g. favicon.ico request). Send a minimal response and loop.
            if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(error) && string.IsNullOrEmpty(state))
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                continue;
            }

            // Validate CSRF state parameter
            if (_oauthState != null && state != _oauthState)
            {
                error = "state_mismatch";
                code = null;
            }

            // Send a response to the browser
            string html;
            if (!string.IsNullOrEmpty(code))
            {
                html = $"""
                    <html><body style="font-family:Segoe UI,sans-serif;text-align:center;padding:60px;background:#1e1e1e;color:#fff">
                    <h1>{System.Net.WebUtility.HtmlEncode(S.Get("OAuth_AuthSuccessTitle"))}</h1>
                    <p>{System.Net.WebUtility.HtmlEncode(S.Get("OAuth_AuthSuccessBody"))}</p>
                    </body></html>
                    """;
            }
            else
            {
                html = $"""
                    <html><body style="font-family:Segoe UI,sans-serif;text-align:center;padding:60px;background:#1e1e1e;color:#fff">
                    <h1>{System.Net.WebUtility.HtmlEncode(S.Get("OAuth_AuthFailedTitle"))}</h1>
                    <p>Error: {System.Net.WebUtility.HtmlEncode(error ?? "unknown")}</p>
                    <p>{System.Net.WebUtility.HtmlEncode(S.Get("OAuth_AuthFailedBody"))}</p>
                    </body></html>
                    """;
            }

            byte[] buf = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            await ctx.Response.OutputStream.WriteAsync(buf, linked.Token);
            ctx.Response.Close();

            return code;
        } // end while
    }

    private async Task<TokenResult?> ExchangeGDriveCodeAsync(
        string code, string redirectUri, string codeVerifier, CancellationToken cancel)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = GDriveClientId,
            ["client_secret"] = GDriveClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier
        });

        var resp = await _http.PostAsync(GDriveTokenUrl, body, cancel);
        var json = await resp.Content.ReadAsStringAsync(cancel);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Token exchange failed (HTTP {(int)resp.StatusCode}): {json}");

        return ParseTokenResponse(json);
    }



    private static TokenResult ParseTokenResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new TokenResult
        {
            AccessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() ?? "" : "",
            RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "",
            ExpiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt64() : 3600
        };
    }

    private static string GenerateRandomString(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "")
            .Substring(0, length);
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static int FindAvailablePort()
    {
        // Use port 0 to let the OS pick an available port.
        // Start and immediately stop — the port is very likely still free
        // for the HttpListener that follows (same-process, localhost only).
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Attempts to open a URL using multiple fallback strategies on Windows:
    /// 1. Default ShellExecute
    /// 2. Windows cmd.exe start launcher
    /// 3. Direct execution of installed browsers (Edge, Chrome, Firefox, Brave)
    /// </summary>
    public static bool TryOpenBrowser(string url, Action<string>? log = null)
    {
        // 1. Standard ShellExecute
        try
        {
            var p = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            p?.Dispose();
            return true;
        }
        catch (Exception ex1)
        {
            log?.Invoke($"Standard browser launch failed: {ex1.Message}. Trying command launcher...");
        }

        // 2. Command launcher: cmd.exe /c start "" "<url>"
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{url.Replace("\"", "%22")}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception ex2)
        {
            log?.Invoke($"Command launcher failed: {ex2.Message}. Searching for installed browsers...");
        }

        // 3. Fallback: Directly launch known browser executables
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Mozilla Firefox\firefox.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"BraveSoftware\Brave-Browser\Application\brave.exe"),
        ];

        foreach (var exe in candidates)
        {
            if (File.Exists(exe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(exe, $"\"{url}\"") { UseShellExecute = false })?.Dispose();
                    log?.Invoke($"Opened browser via {Path.GetFileName(exe)}.");
                    return true;
                }
                catch { /* try next candidate */ }
            }
        }

        log?.Invoke("Notice: Could not automatically open the browser. Please copy and paste the link manually.");
        return false;
    }

    /// <summary>
    /// Parses and accepts a manually submitted authorization code or full redirect URL
    /// (e.g. if the user authorized on a mobile device or if localhost connection failed).
    /// </summary>
    public bool ValidateManualCodeOrUrl(string rawInput, out string code, out string errorMessage)
    {
        code = string.Empty;
        errorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            errorMessage = S.Get("CloudProvider_MissingManualCode");
            return false;
        }

        string input = rawInput.Trim().Trim('"', '\'', '`', '<', '>');
        code = input;

        // If it looks like a URL or contains query parameters:
        if (input.Contains("code=") || input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || input.Contains("?"))
        {
            try
            {
                string queryString = string.Empty;
                int qIdx = input.IndexOf('?');
                if (qIdx >= 0)
                {
                    queryString = input.Substring(qIdx + 1);
                }
                else if (input.Contains("code="))
                {
                    queryString = input;
                }

                if (!string.IsNullOrEmpty(queryString))
                {
                    var query = ParseQueryString(queryString);
                    if (query.TryGetValue("error", out var errorVal) && !string.IsNullOrEmpty(errorVal))
                    {
                        var desc = query.GetValueOrDefault("error_description", errorVal);
                        errorMessage = $"Authorization failed: {desc}";
                        return false;
                    }

                    if (query.TryGetValue("state", out var stateVal) && !string.IsNullOrEmpty(stateVal))
                    {
                        if (!string.IsNullOrEmpty(_oauthState) && !string.Equals(stateVal, _oauthState, StringComparison.Ordinal))
                        {
                            errorMessage = "State mismatch (security check failed). Please make sure you copied the URL from this exact sign-in attempt.";
                            return false;
                        }
                    }

                    if (query.TryGetValue("code", out var codeVal) && !string.IsNullOrEmpty(codeVal))
                    {
                        code = codeVal;
                    }
                    else
                    {
                        errorMessage = "Could not find 'code' parameter in the URL. Please verify you copied the full redirect URL.";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Error parsing URL: {ex.Message}";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            errorMessage = "No valid authorization code found.";
            return false;
        }

        return true;
    }

    public bool TrySubmitManualCodeOrUrl(string rawInput, out string errorMessage)
    {
        if (!ValidateManualCodeOrUrl(rawInput, out string code, out errorMessage))
            return false;


        if (_manualCodeTcs == null || _manualCodeTcs.Task.IsCompleted)
        {
            errorMessage = "Sign-in is not currently waiting for a code. Click 'Sign In' first.";
            return false;
        }

        _manualCodeTcs.TrySetResult(code);
        return true;
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return dict;
        if (query.StartsWith('?')) query = query.Substring(1);
        int hashIdx = query.IndexOf('#');
        if (hashIdx >= 0) query = query.Substring(0, hashIdx);

        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            if (eq >= 0)
            {
                var key = Uri.UnescapeDataString(pair.Substring(0, eq));
                var val = Uri.UnescapeDataString(pair.Substring(eq + 1));
                dict[key] = val;
            }
            else
            {
                dict[Uri.UnescapeDataString(pair)] = string.Empty;
            }
        }
        return dict;
    }

    private void StopListener()
    {
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;

        // Keep _mobileServer alive for a short grace period (10 seconds)
        // so in-flight HTTP requests and final confirmation finish cleanly.
        if (_mobileServer != null)
        {
            var ms = _mobileServer;
            _mobileServer = null;
            Task.Run(async () =>
            {
                await Task.Delay(10000);
                ms.Dispose();
            });
        }
    }

    public void Dispose()
    {
        _manualCodeTcs?.TrySetCanceled();
        StopListener();
        _cts?.Dispose();
        _http.Dispose();
    }
}

public record TokenStatus(bool IsAuthenticated, string Message);

public record TokenResult
{
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public long ExpiresIn { get; init; }
}
