using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Web;

namespace CloudRedirect.Services;

/// <summary>
/// A lightweight local LAN HTTP server that serves a mobile-friendly web page
/// allowing users to scan a QR code on their phone, complete Google OAuth,
/// and automatically submit the authorization code back to CloudRedirect on their PC.
/// </summary>
public sealed class MobileAuthServer : IDisposable
{
    private readonly OAuthService _oauthService;
    private readonly string _authUrl;
    private readonly Action<string> _log;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public string? LanIp { get; private set; }
    public int Port { get; private set; }
    public string? ServerUrl { get; private set; }

    public MobileAuthServer(OAuthService oauthService, string authUrl, Action<string> log)
    {
        _oauthService = oauthService;
        _authUrl = authUrl;
        _log = log;
    }

    public bool Start()
    {
        LanIp = GetLocalLanIp();
        if (string.IsNullOrEmpty(LanIp))
        {
            _log("Mobile LAN Server: Could not determine local IPv4 address.");
            return false;
        }

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            ServerUrl = $"http://{LanIp}:{Port}/";

            _listenTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            _log($"Mobile Assist: Listening for phone on {ServerUrl}");
            return true;
        }
        catch (Exception ex)
        {
            _log($"Mobile Assist failed to start: {ex.Message}");
            Stop();
            return false;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancel);
                _ = Task.Run(() => HandleClientAsync(client, cancel), cancel);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                if (cancel.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancel)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);

                // Use an independent timeout CTS for client socket interactions
                using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                clientCts.CancelAfter(TimeSpan.FromSeconds(15));
                var clientToken = clientCts.Token;

                // Read request line (e.g. GET / HTTP/1.1 or POST /api/submit HTTP/1.1)
                string? requestLine = await reader.ReadLineAsync(clientToken);
                if (string.IsNullOrEmpty(requestLine)) return;

                var lineParts = requestLine.Split(' ');
                if (lineParts.Length < 2) return;
                string method = lineParts[0].ToUpperInvariant();
                string path = lineParts[1];

                // Read headers
                int contentLength = 0;
                string? header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(clientToken)))
                {
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(header.Substring(15).Trim(), out contentLength);
                    }
                }

                // Handle CORS preflight
                if (method == "OPTIONS")
                {
                    string respHeader = "HTTP/1.1 204 No Content\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\nConnection: close\r\n\r\n";
                    byte[] headBytes = Encoding.ASCII.GetBytes(respHeader);
                    await stream.WriteAsync(headBytes, clientToken);
                    await stream.FlushAsync(clientToken);
                    return;
                }

                // Handle routing
                if (method == "GET" && (path == "/" || path.StartsWith("/?") || path.StartsWith("/mobile")))
                {
                    string html = GetMobileHtml();
                    byte[] body = Encoding.UTF8.GetBytes(html);
                    string respHeader = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
                    byte[] headBytes = Encoding.ASCII.GetBytes(respHeader);
                    await stream.WriteAsync(headBytes, clientToken);
                    await stream.WriteAsync(body, clientToken);
                    await stream.FlushAsync(clientToken);
                }
                else if (method == "GET" && path.StartsWith("/api/status"))
                {
                    var statusObj = new { authenticated = _oauthService.IsCurrentAuthenticated };
                    string json = JsonSerializer.Serialize(statusObj);
                    byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

                    string respHeader = $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {jsonBytes.Length}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
                    byte[] headBytes = Encoding.ASCII.GetBytes(respHeader);
                    await stream.WriteAsync(headBytes, clientToken);
                    await stream.WriteAsync(jsonBytes, clientToken);
                    await stream.FlushAsync(clientToken);
                }
                else if (method == "POST" && path.StartsWith("/api/submit"))
                {
                    char[] bodyChars = new char[contentLength];
                    int readTotal = 0;
                    while (readTotal < contentLength)
                    {
                        int read = await reader.ReadAsync(bodyChars, readTotal, contentLength - readTotal);
                        if (read <= 0) break;
                        readTotal += read;
                    }
                    string postBody = new string(bodyChars, 0, readTotal);

                    // Parse input=...
                    string codeInput = string.Empty;
                    var parsed = ParseFormBody(postBody);
                    if (parsed.TryGetValue("input", out var val))
                    {
                        codeInput = val;
                    }
                    else
                    {
                        codeInput = postBody.Trim();
                    }

                    bool isValid = _oauthService.ValidateManualCodeOrUrl(codeInput, out string validatedCode, out string error);

                    var resObj = new { success = isValid, error = isValid ? "" : error };
                    string json = JsonSerializer.Serialize(resObj);
                    byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

                    string respHeader = $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {jsonBytes.Length}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
                    byte[] headBytes = Encoding.ASCII.GetBytes(respHeader);
                    await stream.WriteAsync(headBytes, clientToken);
                    await stream.WriteAsync(jsonBytes, clientToken);
                    await stream.FlushAsync(clientToken);

                    if (isValid)
                    {
                        _log("Mobile Assist: Code verified! Completing PC sign-in...");
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(200);
                            _oauthService.TrySubmitManualCodeOrUrl(codeInput, out _);
                        });
                    }
                }
                else
                {
                    byte[] notFound = Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(notFound, clientToken);
                    await stream.FlushAsync(clientToken);
                }
            }
            catch { }
        }
    }

    private static Dictionary<string, string> ParseFormBody(string body)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(body)) return dict;
        var pairs = body.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            if (eq >= 0)
            {
                var k = Uri.UnescapeDataString(pair.Substring(0, eq).Replace('+', ' '));
                var v = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                dict[k] = v;
            }
        }
        return dict;
    }

    private string GetMobileHtml()
    {
        string encodedAuthUrl = System.Net.WebUtility.HtmlEncode(_authUrl);
        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
          <title>CloudRedirect Mobile Auth</title>
          <style>
            * { box-sizing: border-box; -webkit-tap-highlight-color: transparent; }
            body {
              font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
              background-color: #171a21;
              background-image: radial-gradient(circle at 50% 0%, #1f2f42 0%, #171a21 70%);
              color: #f3f3f3;
              margin: 0;
              padding: 16px;
              display: flex;
              justify-content: center;
              min-height: 100vh;
            }
            .container {
              width: 100%;
              max-width: 450px;
              background: #1b2838;
              border: 1px solid #2a475e;
              border-radius: 12px;
              padding: 20px;
              box-shadow: 0 12px 32px rgba(0, 0, 0, 0.6);
              display: flex;
              flex-direction: column;
            }
            .header-top {
              display: flex;
              justify-content: space-between;
              align-items: center;
              margin-bottom: 16px;
            }
            .brand-badge {
              display: inline-flex;
              align-items: center;
              font-size: 13px;
              font-weight: 700;
              letter-spacing: 0.5px;
              color: #66c0f4;
              text-transform: uppercase;
            }
            .brand-dot {
              width: 8px;
              height: 8px;
              background: #66c0f4;
              border-radius: 50%;
              margin-right: 6px;
              box-shadow: 0 0 6px #66c0f4;
            }
            /* Language Switcher */
            .lang-switch {
              display: inline-flex;
              background: #121922;
              border: 1px solid #2a3d54;
              border-radius: 20px;
              padding: 2px;
            }
            .lang-btn {
              background: transparent;
              border: none;
              color: #8f98a0;
              font-size: 11.5px;
              font-weight: 600;
              padding: 4px 10px;
              border-radius: 16px;
              cursor: pointer;
              transition: all 0.2s ease;
            }
            .lang-btn.active {
              background: linear-gradient(135deg, #214b6b, #1b3d57);
              color: #66c0f4;
              border: 1px solid #3877a5;
              box-shadow: 0 2px 6px rgba(0, 0, 0, 0.3);
            }
            .header {
              text-align: center;
              margin-bottom: 16px;
              border-bottom: 1px solid #2a3d54;
              padding-bottom: 14px;
            }
            .title {
              font-size: 20px;
              font-weight: 700;
              color: #ffffff;
              margin: 0 0 6px 0;
            }
            .subtitle {
              font-size: 13px;
              color: #8f98a0;
              margin: 0;
              line-height: 1.45;
            }
            /* Security Warning Banner */
            .security-banner {
              background: rgba(245, 166, 35, 0.12);
              border: 1px solid rgba(245, 166, 35, 0.45);
              border-radius: 8px;
              padding: 12px 14px;
              margin-bottom: 16px;
              display: flex;
              align-items: flex-start;
              gap: 10px;
            }
            .security-icon {
              font-size: 18px;
              line-height: 1;
              flex-shrink: 0;
              margin-top: 1px;
            }
            .security-text {
              font-size: 12px;
              color: #ffd27d;
              line-height: 1.45;
            }
            .security-text strong {
              color: #ffa500;
              display: inline-block;
              margin-right: 4px;
            }
            /* Step Cards */
            .step-card {
              background: #1a2432;
              border: 1px solid #2a3d54;
              border-radius: 8px;
              padding: 14px;
              margin-bottom: 14px;
            }
            .step-header {
              display: flex;
              align-items: center;
              margin-bottom: 8px;
            }
            .step-badge {
              background: #1a9fff;
              color: #fff;
              font-weight: bold;
              font-size: 12px;
              border-radius: 12px;
              padding: 2px 8px;
              margin-right: 8px;
            }
            .step-title {
              font-size: 14px;
              font-weight: 600;
              color: #f3f3f3;
            }
            .step-desc {
              font-size: 12px;
              color: #8f98a0;
              margin: 4px 0 12px 0;
              line-height: 1.45;
            }
            .step-desc em {
              color: #e06c75;
              font-style: normal;
              font-weight: 600;
            }
            .step-desc strong {
              color: #66c0f4;
            }
            .btn {
              display: block;
              width: 100%;
              padding: 13px;
              border: none;
              border-radius: 6px;
              font-size: 14px;
              font-weight: 700;
              text-align: center;
              text-decoration: none;
              cursor: pointer;
              transition: filter 0.2s, transform 0.1s;
            }
            .btn:active { filter: brightness(0.85); transform: scale(0.99); }
            .btn-green {
              background: linear-gradient(to bottom, #5c7e10, #4b6b0d);
              color: #fff;
              border: 1px solid #6ca015;
              box-shadow: 0 4px 12px rgba(92, 126, 16, 0.35);
            }
            .btn-blue {
              background: linear-gradient(to bottom, #214b6b, #1b3d57);
              color: #66c0f4;
              border: 1px solid #3877a5;
              margin-top: 10px;
            }
            .btn-blue:disabled {
              opacity: 0.6;
              cursor: not-allowed;
            }
            .input-box {
              width: 100%;
              padding: 12px;
              border: 1px solid #2a3d54;
              border-radius: 6px;
              background: #121922;
              color: #fff;
              font-size: 13px;
              font-family: inherit;
            }
            .input-box:focus {
              outline: none;
              border-color: #1a9fff;
            }
            .status-box {
              margin-top: 12px;
              padding: 10px;
              border-radius: 6px;
              font-size: 12.5px;
              display: none;
              text-align: center;
              font-weight: 600;
              line-height: 1.4;
            }
            .status-error {
              display: block;
              background: rgba(224, 108, 117, 0.15);
              border: 1px solid #e06c75;
              color: #e06c75;
            }
            .status-busy {
              display: block;
              background: rgba(26, 159, 255, 0.15);
              border: 1px solid #1a9fff;
              color: #1a9fff;
            }
            /* Setup Complete Celebration Screen */
            #setupCompleteCard {
              display: none;
              text-align: center;
              padding: 16px 8px 8px 8px;
              animation: fadeInUp 0.5s cubic-bezier(0.16, 1, 0.3, 1) forwards;
            }
            @keyframes fadeInUp {
              0% { opacity: 0; transform: translateY(20px) scale(0.96); }
              100% { opacity: 1; transform: translateY(0) scale(1); }
            }
            .success-icon-wrap {
              width: 84px;
              height: 84px;
              margin: 0 auto 16px auto;
              border-radius: 50%;
              display: flex;
              align-items: center;
              justify-content: center;
              background: rgba(164, 208, 7, 0.12);
              border: 2px solid rgba(164, 208, 7, 0.35);
              box-shadow: 0 0 25px rgba(164, 208, 7, 0.3);
            }
            .checkmark-svg {
              width: 52px;
              height: 52px;
              border-radius: 50%;
              display: block;
              stroke-width: 3.5;
              stroke: #a4d007;
              stroke-miterlimit: 10;
            }
            .checkmark-circle {
              stroke-dasharray: 166;
              stroke-dashoffset: 166;
              stroke-width: 3;
              stroke-miterlimit: 10;
              stroke: #a4d007;
              animation: check-stroke 0.6s cubic-bezier(0.65, 0, 0.45, 1) forwards;
            }
            .checkmark-check {
              transform-origin: 50% 50%;
              stroke-dasharray: 48;
              stroke-dashoffset: 48;
              stroke-width: 3.8;
              stroke: #a4d007;
              animation: check-stroke 0.35s cubic-bezier(0.65, 0, 0.45, 1) 0.5s forwards;
            }
            @keyframes check-stroke {
              100% {
                stroke-dashoffset: 0;
              }
            }
            .complete-title {
              font-size: 22px;
              font-weight: 700;
              color: #a4d007;
              margin: 0 0 6px 0;
            }
            .complete-sub {
              font-size: 13.5px;
              color: #66c0f4;
              font-weight: 600;
              margin: 0 0 16px 0;
            }
            .info-card {
              background: #121922;
              border: 1px solid #2a3d54;
              border-radius: 8px;
              padding: 12px 14px;
              margin-bottom: 16px;
              text-align: left;
            }
            .info-row {
              display: flex;
              justify-content: space-between;
              align-items: center;
              font-size: 12px;
              padding: 5px 0;
              border-bottom: 1px solid rgba(42, 61, 84, 0.5);
            }
            .info-row:last-child {
              border-bottom: none;
            }
            .info-label {
              color: #8f98a0;
            }
            .info-value {
              color: #f3f3f3;
              font-weight: 600;
              display: inline-flex;
              align-items: center;
            }
            .status-dot {
              width: 7px;
              height: 7px;
              border-radius: 50%;
              background: #a4d007;
              margin-right: 6px;
              box-shadow: 0 0 5px #a4d007;
            }
            .complete-hint {
              font-size: 12px;
              color: #8f98a0;
              line-height: 1.45;
              margin: 0 0 18px 0;
            }
          </style>
        </head>
        <body>
          <div class="container">
            <!-- Header with Brand & Language Switcher -->
            <div class="header-top">
              <div class="brand-badge">
                <span class="brand-dot"></span>
                <span>CloudRedirect</span>
              </div>
              <div class="lang-switch" role="group" aria-label="Language selector">
                <button type="button" class="lang-btn active" id="langEnBtn" onclick="setLanguage('en')">English</button>
                <button type="button" class="lang-btn" id="langMsBtn" onclick="setLanguage('ms')">B. Melayu</button>
              </div>
            </div>

            <!-- Page Title -->
            <div class="header">
              <h1 class="title" id="pageTitle">CloudRedirect Mobile Sign-In</h1>
              <p class="subtitle" id="pageSubtitle">Complete authentication on your phone to link Google Drive to CloudRedirect on your PC.</p>
            </div>

            <!-- Security Warning -->
            <div class="security-banner">
              <div class="security-icon">⚠️</div>
              <div class="security-text">
                <strong id="warnTitle">Security Warning:</strong>
                <span id="warnText">Never share this QR code, link, or authorization code with anyone. It gives access to your Google account.</span>
              </div>
            </div>

            <!-- Steps Form -->
            <div id="stepsContainer">
              <div class="step-card">
                <div class="step-header">
                  <span class="step-badge">1</span>
                  <span class="step-title" id="step1Title">Sign In with Google</span>
                </div>
                <p class="step-desc" id="step1Desc">Tap below to sign in and grant CloudRedirect permissions. After allowing access, your browser will reach a page that says <em>"This site can't be reached"</em>. <strong>Copy the entire link from your browser's address bar.</strong></p>
                <a class="btn btn-green" href="{{encodedAuthUrl}}" target="_blank" id="step1Btn">Open Google Sign-In ↗</a>
              </div>

              <div class="step-card">
                <div class="step-header">
                  <span class="step-badge">2</span>
                  <span class="step-title" id="step2Title">Send Code to PC</span>
                </div>
                <p class="step-desc" id="step2Desc">Paste the redirected link (or authorization code) below and tap Send. CloudRedirect on your PC will automatically link your account.</p>
                <input type="text" id="codeBox" class="input-box" placeholder="Paste http://localhost:... or code here" autocomplete="off" autocorrect="off" autocapitalize="off" spellcheck="false">
                <button class="btn btn-blue" id="sendBtn" onclick="submitToPc()">
                  <span id="sendBtnText">Send to CloudRedirect on PC</span>
                </button>
                <div id="statusBox" class="status-box"></div>
              </div>
            </div>

            <!-- Setup Complete Celebration Card -->
            <div id="setupCompleteCard">
              <div class="success-icon-wrap">
                <svg class="checkmark-svg" viewBox="0 0 52 52">
                  <circle class="checkmark-circle" cx="26" cy="26" r="23" fill="none" />
                  <path class="checkmark-check" fill="none" d="M14.1 27.2l7.1 7.2 16.7-16.8" />
                </svg>
              </div>

              <h2 class="complete-title" id="compTitle">Setup Complete!</h2>
              <p class="complete-sub" id="compSub">Sent to PC • Successfully Linked</p>

              <div class="info-card">
                <div class="info-row">
                  <span class="info-label" id="compServiceLabel">Cloud Service</span>
                  <span class="info-value"><span class="status-dot"></span>Google Drive</span>
                </div>
                <div class="info-row">
                  <span class="info-label" id="compStatusLabel">Status</span>
                  <span class="info-value" id="compStatusVal" style="color:#a4d007;">Active &amp; Connected</span>
                </div>
                <div class="info-row">
                  <span class="info-label" id="compTargetLabel">Destination</span>
                  <span class="info-value" id="compTargetVal">CloudRedirect (PC)</span>
                </div>
              </div>

              <p class="complete-hint" id="compHint">Your authorization code was received and verified by your PC. You can safely close this browser tab.</p>
              <button class="btn btn-green" onclick="handleDoneClick()" id="compCloseBtn">Done (Close Tab)</button>
            </div>
          </div>

          <script>
            const I18N = {
              en: {
                pageTitle: "CloudRedirect Mobile Sign-In",
                pageSubtitle: "Complete authentication on your phone to link Google Drive to CloudRedirect on your PC.",
                warnTitle: "Security Warning:",
                warnText: "Never share this QR code, link, or authorization code with anyone. It gives access to your Google account.",
                step1Title: "Sign In with Google",
                step1Desc: "Tap below to sign in and grant CloudRedirect permissions. After allowing access, your browser will reach a page that says <em>\"This site can't be reached\"</em>. <strong>Copy the entire link from your browser's address bar.</strong>",
                step1Btn: "Open Google Sign-In ↗",
                step2Title: "Send Code to PC",
                step2Desc: "Paste the redirected link (or authorization code) below and tap Send. CloudRedirect on your PC will automatically link your account.",
                codePlaceholder: "Paste http://localhost:... or code here",
                sendBtnText: "Send to CloudRedirect on PC",
                sending: "Sending code to your PC...",
                errorEmpty: "Please paste the redirected link or authorization code first.",
                errorNetwork: "Could not reach your PC. Ensure your phone and PC are on the same Wi-Fi network.",
                compTitle: "Setup Complete!",
                compSub: "Sent to PC • Successfully Linked",
                compServiceLabel: "Cloud Service",
                compStatusLabel: "Status",
                compStatusVal: "Active & Connected",
                compTargetLabel: "Destination",
                compTargetVal: "CloudRedirect (PC)",
                compHint: "Your authorization code was received and verified by your PC. You can safely close this browser tab.",
                compCloseBtn: "Done (Close Tab)",
                closeNotice: "All set! You can return to CloudRedirect on your PC now."
              },
              ms: {
                pageTitle: "Log Masuk Mudah Alih CloudRedirect",
                pageSubtitle: "Selesaikan pengesahan pada telefon untuk menghubungkan Google Drive ke CloudRedirect pada PC anda.",
                warnTitle: "Amaran Keselamatan:",
                warnText: "Jangan sekali-kali berkongsi kod QR, pautan atau kod kebenaran ini dengan sesiapa pun. Ia memberikan akses kepada akaun Google anda.",
                step1Title: "Log Masuk dengan Google",
                step1Desc: "Ketik di bawah untuk log masuk dan berikan kebenaran. Selepas membenarkan akses, penyemak imbas anda akan memaparkan halaman <em>\"Laman web ini tidak dapat dicapai\"</em>. <strong>Salin keseluruhan pautan dari bar alamat penyemak imbas anda.</strong>",
                step1Btn: "Buka Log Masuk Google ↗",
                step2Title: "Hantar Kod ke PC",
                step2Desc: "Tampal pautan yang dihalakan (atau kod kebenaran) di bawah dan ketik Hantar. CloudRedirect pada PC anda akan menghubungkan akaun anda secara automatik.",
                codePlaceholder: "Tampal http://localhost:... atau kod di sini",
                sendBtnText: "Hantar ke CloudRedirect pada PC",
                sending: "Menghantar kod ke PC anda...",
                errorEmpty: "Sila tampal pautan yang dihalakan atau kod kebenaran terlebih dahulu.",
                errorNetwork: "Tidak dapat berhubung dengan PC anda. Pastikan telefon dan PC berada dalam rangkaian Wi-Fi yang sama.",
                compTitle: "Persediaan Selesai!",
                compSub: "Dihantar ke PC • Berjaya Dihubungkan",
                compServiceLabel: "Perkhidmatan Awan",
                compStatusLabel: "Status",
                compStatusVal: "Aktif & Dihubungkan",
                compTargetLabel: "Destinasi",
                compTargetVal: "CloudRedirect (PC)",
                compHint: "Kod kebenaran anda telah diterima dan disahkan oleh PC anda. Anda boleh menutup tab penyemak imbas ini dengan selamat.",
                compCloseBtn: "Selesai (Tutup Tab)",
                closeNotice: "Semuanya telah selesai! Anda kini boleh kembali ke CloudRedirect pada PC anda."
              }
            };

            let currentLang = 'en';

            function setLanguage(lang) {
              if (!I18N[lang]) return;
              currentLang = lang;
              try { localStorage.setItem('cr_mobile_lang', lang); } catch (e) {}

              // Update active button
              document.getElementById('langEnBtn').className = lang === 'en' ? 'lang-btn active' : 'lang-btn';
              document.getElementById('langMsBtn').className = lang === 'ms' ? 'lang-btn active' : 'lang-btn';

              const dict = I18N[lang];
              document.getElementById('pageTitle').textContent = dict.pageTitle;
              document.getElementById('pageSubtitle').textContent = dict.pageSubtitle;
              document.getElementById('warnTitle').textContent = dict.warnTitle;
              document.getElementById('warnText').textContent = dict.warnText;
              document.getElementById('step1Title').textContent = dict.step1Title;
              document.getElementById('step1Desc').innerHTML = dict.step1Desc;
              document.getElementById('step1Btn').textContent = dict.step1Btn;
              document.getElementById('step2Title').textContent = dict.step2Title;
              document.getElementById('step2Desc').textContent = dict.step2Desc;
              document.getElementById('codeBox').placeholder = dict.codePlaceholder;
              document.getElementById('sendBtnText').textContent = dict.sendBtnText;

              document.getElementById('compTitle').textContent = dict.compTitle;
              document.getElementById('compSub').textContent = dict.compSub;
              document.getElementById('compServiceLabel').textContent = dict.compServiceLabel;
              document.getElementById('compStatusLabel').textContent = dict.compStatusLabel;
              document.getElementById('compStatusVal').textContent = dict.compStatusVal;
              document.getElementById('compTargetLabel').textContent = dict.compTargetLabel;
              document.getElementById('compTargetVal').textContent = dict.compTargetVal;
              document.getElementById('compHint').textContent = dict.compHint;
              document.getElementById('compCloseBtn').textContent = dict.compCloseBtn;
            }

            // Init language from storage or browser locale
            (function initLang() {
              let saved = null;
              try { saved = localStorage.getItem('cr_mobile_lang'); } catch (e) {}
              if (saved && I18N[saved]) {
                setLanguage(saved);
              } else if (navigator.language && navigator.language.toLowerCase().startsWith('ms')) {
                setLanguage('ms');
              } else {
                setLanguage('en');
              }
            })();

            function showSuccessScreen() {
              document.getElementById('stepsContainer').style.display = 'none';
              const completeCard = document.getElementById('setupCompleteCard');
              completeCard.style.display = 'block';
              try { window.scrollTo({ top: 0, behavior: 'smooth' }); } catch (e) {}
            }

            function handleDoneClick() {
              try {
                window.close();
              } catch (e) {}
              const dict = I18N[currentLang] || I18N.en;
              alert(dict.closeNotice);
            }

            async function submitToPc() {
              const box = document.getElementById('codeBox');
              const status = document.getElementById('statusBox');
              const btn = document.getElementById('sendBtn');
              const dict = I18N[currentLang] || I18N.en;
              const val = box.value.trim();

              if (!val) {
                status.className = 'status-box status-error';
                status.textContent = dict.errorEmpty;
                return;
              }

              status.className = 'status-box status-busy';
              status.textContent = dict.sending;
              btn.disabled = true;

              try {
                const resp = await fetch('/api/submit', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                  body: 'input=' + encodeURIComponent(val)
                });
                const data = await resp.json();
                if (data.success) {
                  showSuccessScreen();
                  box.value = '';
                } else {
                  status.className = 'status-box status-error';
                  status.textContent = data.error || 'Failed to authenticate.';
                  btn.disabled = false;
                }
              } catch (err) {
                // Secondary check: verify if PC is already authenticated despite network close
                try {
                  const checkResp = await fetch('/api/status');
                  const checkData = await checkResp.json();
                  if (checkData.authenticated) {
                    showSuccessScreen();
                    box.value = '';
                    return;
                  }
                } catch (e2) {}

                status.className = 'status-box status-error';
                status.textContent = dict.errorNetwork;
                btn.disabled = false;
              }
            }
          </script>
        </body>
        </html>
        """;
    }

    public static string? GetLocalLanIp()
    {
        // 1. Query OS routing table via dummy UDP connect
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep)
            {
                string ip = ep.Address.ToString();
                if (IsPrivateLanIp(ip))
                    return ip;
            }
        }
        catch { }

        // 2. Scan network interfaces, prioritizing Wi-Fi and Ethernet over virtual/VPN adapters
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 2 :
                                        (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 1 : 0));

            foreach (var ni in interfaces)
            {
                var name = ni.Name.ToLowerInvariant();
                var desc = ni.Description.ToLowerInvariant();
                if (name.Contains("tailscale") || name.Contains("zerotier") || name.Contains("wireguard") ||
                    desc.Contains("tailscale") || desc.Contains("virtual") || desc.Contains("vpn"))
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ip = ua.Address.ToString();
                        if (IsPrivateLanIp(ip))
                            return ip;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static bool IsPrivateLanIp(string ip)
    {
        if (string.IsNullOrEmpty(ip) || ip.StartsWith("127.") || ip.StartsWith("169.254.") || ip.StartsWith("100."))
            return false;

        // RFC 1918: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
        if (ip.StartsWith("192.168.") || ip.StartsWith("10."))
            return true;

        if (ip.StartsWith("172."))
        {
            var parts = ip.Split('.');
            if (parts.Length > 1 && int.TryParse(parts[1], out int secondOctet) && secondOctet >= 16 && secondOctet <= 31)
                return true;
        }

        return false;
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
