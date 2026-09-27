package com.cloudredirect.suolink;

public class DiscoveryHtml {
    public static String getHtml(String lastHost) {
        return getHtml(lastHost, null, null);
    }

    public static String getHtml(String lastHost, String lastTunnel) {
        return getHtml(lastHost, null, lastTunnel);
    }

    public static String getHtml(String lastHost, String lastTailscale, String lastTunnel) {
        String safeLastHost = lastHost != null ? lastHost : "";
        String safeTailscale = lastTailscale != null ? lastTailscale : "";
        boolean hasTailscale = !safeTailscale.isEmpty();

        StringBuilder sb = new StringBuilder();
        sb.append("<!DOCTYPE html><html><head>");
        sb.append("<meta charset='utf-8'>");
        sb.append("<meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no'>");
        sb.append("<title>SUO Link</title>");
        sb.append("<style>");
        sb.append("* { box-sizing: border-box; margin: 0; padding: 0; user-select: none; -webkit-user-select: none; }");
        sb.append("body { background: #0b0e14; color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 100vh; padding: 16px; text-align: center; overflow-x: hidden; }");
        sb.append(".logo-badge { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }");
        sb.append(".logo-title { font-size: 26px; font-weight: 800; color: #fff; letter-spacing: 1px; }");
        sb.append(".logo-title span { color: #00d2ff; }");
        sb.append(".subtitle { color: #94a3b8; font-size: 13px; max-width: 440px; line-height: 1.4; margin-bottom: 18px; }");
        sb.append("#connecting-banner { display: none; width: 100%; max-width: 360px; background: rgba(0, 210, 255, 0.15); border: 1px solid #00d2ff; color: #00d2ff; font-size: 13px; font-weight: 700; padding: 10px 14px; border-radius: 10px; margin-bottom: 16px; box-shadow: 0 0 15px rgba(0, 210, 255, 0.2); word-break: break-all; }");
        sb.append("#update-banner { display: none; width: 100%; max-width: 360px; background: rgba(56, 239, 125, 0.15); border: 1px solid #38ef7d; color: #38ef7d; font-size: 12px; font-weight: 600; padding: 10px 14px; border-radius: 10px; margin-bottom: 16px; box-shadow: 0 0 15px rgba(56, 239, 125, 0.2); }");
        sb.append(".btn-qr { background: linear-gradient(135deg, #00d2ff, #38ef7d); color: #0b0e14; font-weight: 800; font-size: 16px; letter-spacing: 0.5px; padding: 16px 36px; border-radius: 32px; border: none; display: inline-flex; align-items: center; gap: 10px; box-shadow: 0 4px 20px rgba(0, 210, 255, 0.4); cursor: pointer; margin-bottom: 12px; transition: transform 0.1s, opacity 0.1s; }");
        sb.append(".btn-qr:active { transform: scale(0.96); opacity: 0.9; }");
        sb.append(".btn-row-chips { display: flex; gap: 8px; width: 100%; max-width: 360px; margin-bottom: 16px; }");
        sb.append(".btn-chip { flex: 1; background: #151b24; border: 1px solid #334155; color: #94a3b8; font-size: 12px; font-weight: 600; padding: 9px 12px; border-radius: 18px; cursor: pointer; display: flex; align-items: center; justify-content: center; gap: 6px; }");
        sb.append(".btn-chip-ts { border-color: rgba(56, 239, 125, 0.4); color: #38ef7d; background: rgba(56, 239, 125, 0.06); }");
        sb.append(".btn-chip:active { background: #1e293b; color: #fff; }");
        sb.append(".card-ts { width: 100%; max-width: 360px; background: rgba(56, 239, 125, 0.08); border: 1px solid rgba(56, 239, 125, 0.3); border-radius: 12px; padding: 10px 14px; margin-bottom: 14px; text-align: left; display: flex; justify-content: space-between; align-items: center; }");
        sb.append(".divider { width: 100%; max-width: 360px; height: 1px; background: #1e293b; margin: 12px 0; }");
        sb.append("#detected-host-card { display: none; width: 100%; max-width: 360px; background: #151b24; border: 1px solid #00d2ff; border-radius: 12px; padding: 14px; margin-bottom: 16px; box-shadow: 0 0 15px rgba(0, 210, 255, 0.2); text-align: left; }");
        sb.append(".host-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px; }");
        sb.append(".host-name { font-weight: 700; color: #fff; font-size: 14px; }");
        sb.append(".host-badge { background: rgba(56, 239, 125, 0.15); color: #38ef7d; font-size: 10px; font-weight: 700; padding: 2px 8px; border-radius: 10px; border: 1px solid rgba(56, 239, 125, 0.4); }");
        sb.append(".host-ip { font-size: 12px; color: #94a3b8; margin-bottom: 10px; }");
        sb.append(".btn-connect-host { width: 100%; background: #11998e; color: #fff; font-weight: 700; font-size: 13px; border: none; padding: 10px; border-radius: 6px; cursor: pointer; }");
        sb.append(".manual-box { width: 100%; max-width: 360px; display: flex; gap: 8px; }");
        sb.append(".manual-input { flex: 1; background: #151b24; border: 1px solid #222d3d; border-radius: 8px; padding: 10px 14px; color: #fff; font-size: 13px; outline: none; }");
        sb.append(".manual-input:focus { border-color: #00d2ff; }");
        sb.append(".btn-manual { background: #1e293b; border: 1px solid #334155; color: #fff; font-weight: 600; padding: 10px 16px; border-radius: 8px; font-size: 13px; cursor: pointer; }");
        sb.append("</style></head><body>");

        sb.append("<div class='logo-badge'>");
        sb.append("<div style='width:12px;height:12px;border-radius:50%;background:#00d2ff;box-shadow:0 0 10px #00d2ff;'></div>");
        sb.append("<div class='logo-title'>SUO <span>LINK</span></div>");
        sb.append("</div>");
        sb.append("<p class='subtitle'>Steam Link &amp; GameHub Remote Play for CloudRedirect.<br>Scan PC screen to connect in 1 second.</p>");

        sb.append("<div style='background:rgba(0,210,255,0.08); border:1px solid rgba(0,210,255,0.25); border-radius:20px; padding:6px 14px; font-size:11px; color:#66c0f4; margin-bottom:14px; display:inline-flex; align-items:center; gap:6px;'>");
        sb.append("<span>🛡️</span> Tailscale WireGuard &amp; Wi-Fi supported");
        sb.append("</div>");

        sb.append("<div id='connecting-banner'>🚀 Connecting to PC...</div>");
        sb.append("<div id='update-banner'>⬆️ New SUO Link update downloading...</div>");

        if (hasTailscale) {
            sb.append("<div class='card-ts'>");
            sb.append("<div>");
            sb.append("<div style='font-size:12px;font-weight:700;color:#38ef7d;'>🛡️ Tailscale WireGuard Ready</div>");
            sb.append("<div style='font-size:11px;color:#94a3b8;'>Direct permanent IP for 4G/5G mobile</div>");
            sb.append("</div>");
            sb.append("<button style='background:#10b981;color:#0b0e14;border:none;padding:7px 14px;border-radius:16px;font-size:11px;font-weight:800;cursor:pointer;' onclick='connectUrl(\"").append(safeTailscale.replace("\"", "\\\"")).append("\")'>CONNECT</button>");
            sb.append("</div>");
        }

        sb.append("<button class='btn-qr' id='btn-scan-qr' onclick='triggerQrScan()'>");
        sb.append("<span style='font-size:22px;'>📷</span> SCAN PC QR CODE");
        sb.append("</button>");

        sb.append("<div class='btn-row-chips'>");
        sb.append("<button class='btn-chip' onclick='triggerGallery()'>📁 Gallery QR</button>");
        sb.append("<button class='btn-chip btn-chip-ts' onclick='openTailscale()'>🛡️ Open Tailscale</button>");
        sb.append("</div>");

        sb.append("<div id='detected-host-card'>");
        sb.append("<div class='host-header'>");
        sb.append("<div class='host-name' id='dh-name'>PC Host</div>");
        sb.append("<div class='host-badge' id='dh-badge'>FOUND ON WI-FI</div>");
        sb.append("</div>");
        sb.append("<div class='host-ip' id='dh-ip'>192.168.1.xxx:8585</div>");
        sb.append("<button class='btn-connect-host' id='dh-btn'>🚀 CONNECT TO PC</button>");
        sb.append("</div>");

        sb.append("<div class='divider'></div>");

        sb.append("<div class='manual-box'>");
        sb.append("<input type='text' id='manual-ip' class='manual-input' placeholder='192.168.1.xxx:8585 or 100.x.y.z:8585' value='").append(safeLastHost).append("'>");
        sb.append("<button class='btn-manual' onclick='connectManual()'>CONNECT</button>");
        sb.append("</div>");

        sb.append("<script>");
        sb.append("function connectUrl(url) {");
        sb.append("  if (!url) return;");
        sb.append("  url = url.trim();");
        sb.append("  var input = document.getElementById('manual-ip');");
        sb.append("  if (input) input.value = url;");
        sb.append("  var banner = document.getElementById('connecting-banner');");
        sb.append("  if (banner) {");
        sb.append("    banner.innerText = '🚀 Connecting to ' + url + '...';");
        sb.append("    banner.style.display = 'block';");
        sb.append("  }");
        sb.append("  try {");
        sb.append("    if (window.SuoNative && window.SuoNative.vibrate) window.SuoNative.vibrate(40);");
        sb.append("    if (window.SuoNative && window.SuoNative.connectSmart) {");
        sb.append("      window.SuoNative.connectSmart(url);");
        sb.append("      return;");
        sb.append("    }");
        sb.append("  } catch (e) {}");
        sb.append("  if (url.indexOf('http://') !== 0 && url.indexOf('https://') !== 0) {");
        sb.append("    url = 'http://' + url;");
        sb.append("  }");
        sb.append("  window.location.href = url;");
        sb.append("}");
        sb.append("function connectManual() {");
        sb.append("  var input = document.getElementById('manual-ip');");
        sb.append("  if (input && input.value) { connectUrl(input.value); }");
        sb.append("}");
        sb.append("function triggerQrScan() {");
        sb.append("  try {");
        sb.append("    if (window.SuoNative && window.SuoNative.vibrate) window.SuoNative.vibrate(30);");
        sb.append("    if (window.SuoNative && window.SuoNative.openCameraScanner) {");
        sb.append("      window.SuoNative.openCameraScanner();");
        sb.append("      return;");
        sb.append("    }");
        sb.append("  } catch (e) {}");
        sb.append("  window.location.href = 'suolink://scan';");
        sb.append("}");
        sb.append("function triggerGallery() {");
        sb.append("  try {");
        sb.append("    if (window.SuoNative && window.SuoNative.vibrate) window.SuoNative.vibrate(20);");
        sb.append("    if (window.SuoNative && window.SuoNative.openGalleryPicker) {");
        sb.append("      window.SuoNative.openGalleryPicker();");
        sb.append("      return;");
        sb.append("    }");
        sb.append("  } catch (e) {}");
        sb.append("  window.location.href = 'suolink://gallery';");
        sb.append("}");
        sb.append("function openTailscale() {");
        sb.append("  try {");
        sb.append("    if (window.SuoNative && window.SuoNative.vibrate) window.SuoNative.vibrate(20);");
        sb.append("    if (window.SuoNative && window.SuoNative.openTailscaleApp) {");
        sb.append("      window.SuoNative.openTailscaleApp();");
        sb.append("      return;");
        sb.append("    }");
        sb.append("  } catch (e) {}");
        sb.append("  window.location.href = 'suolink://tailscale';");
        sb.append("}");
        sb.append("window.setScannedUrl = function(url) {");
        sb.append("  if (!url) return;");
        sb.append("  var input = document.getElementById('manual-ip');");
        sb.append("  if (input) input.value = url;");
        sb.append("  var banner = document.getElementById('connecting-banner');");
        sb.append("  if (banner) {");
        sb.append("    banner.innerText = '✓ Scanned: ' + url + '\\n🚀 Connecting...';");
        sb.append("    banner.style.display = 'block';");
        sb.append("  }");
        sb.append("};");
        sb.append("window.onHostDiscovered = function(name, ip, port, auth, tunnel, tailscale) {");
        sb.append("  var fullUrl = 'http://' + ip + ':' + port + '/?auth=' + auth;");
        sb.append("  if (tailscale) fullUrl += '&tailscale=' + encodeURIComponent(tailscale);");
        sb.append("  if (tunnel) fullUrl += '&tunnel=' + encodeURIComponent(tunnel);");
        sb.append("  var nameEl = document.getElementById('dh-name');");
        sb.append("  var ipEl = document.getElementById('dh-ip');");
        sb.append("  var badgeEl = document.getElementById('dh-badge');");
        sb.append("  var cardEl = document.getElementById('detected-host-card');");
        sb.append("  var btnEl = document.getElementById('dh-btn');");
        sb.append("  if (nameEl) nameEl.innerText = name || 'CloudRedirect PC';");
        sb.append("  if (ipEl) {");
        sb.append("    var info = ip + ':' + port;");
        sb.append("    if (tailscale) info += ' • 🛡️ Tailscale';");
        sb.append("    if (tunnel) info += ' • 🌐 Remote';");
        sb.append("    ipEl.innerText = info;");
        sb.append("  }");
        sb.append("  if (badgeEl && tailscale) { badgeEl.innerText = 'WI-FI + TAILSCALE'; }");
        sb.append("  if (cardEl) cardEl.style.display = 'block';");
        sb.append("  if (btnEl) btnEl.onclick = function() { connectUrl(fullUrl); };");
        sb.append("};");
        sb.append("window.showUpdateNotice = function(msg) {");
        sb.append("  var banner = document.getElementById('update-banner');");
        sb.append("  if (banner) {");
        sb.append("    banner.innerText = msg || '⬆️ Updating SUO Link... Check notifications to install.';");
        sb.append("    banner.style.display = 'block';");
        sb.append("  }");
        sb.append("};");
        sb.append("</script></body></html>");
        return sb.toString();
    }

    public static String getErrorHtml(String failedUrl, String description) {
        return getErrorHtml(failedUrl, description, null, null, null);
    }

    public static String getErrorHtml(String failedUrl, String description, String savedTunnelUrl) {
        return getErrorHtml(failedUrl, description, savedTunnelUrl, null, null);
    }

    public static String getErrorHtml(String failedUrl, String description, String savedTunnelUrl, String savedLanUrl) {
        return getErrorHtml(failedUrl, description, savedTunnelUrl, null, savedLanUrl);
    }

    public static String getErrorHtml(String failedUrl, String description, String savedTunnelUrl, String savedTailscaleUrl, String savedLanUrl) {
        String safeUrl = failedUrl != null ? failedUrl : "";
        if (safeUrl.isEmpty() || safeUrl.startsWith("file://") || safeUrl.contains("suolink.local") || safeUrl.startsWith("data:") || safeUrl.startsWith("about:")) {
            safeUrl = "Unable to reach PC host";
        }
        String safeTunnel = savedTunnelUrl != null ? savedTunnelUrl : "";
        String safeTailscale = savedTailscaleUrl != null ? savedTailscaleUrl : "";
        String safeLan = savedLanUrl != null ? savedLanUrl : "";

        boolean hasTunnel = !safeTunnel.isEmpty();
        boolean hasTailscale = !safeTailscale.isEmpty();
        boolean hasLan = !safeLan.isEmpty();

        String descLower = description != null ? description.toLowerCase() : "";
        boolean isTailscaleError = (failedUrl != null && (failedUrl.contains("100.") || failedUrl.contains(".ts.net")));
        boolean isTunnelError = (failedUrl != null && (failedUrl.contains("trycloudflare.com") || failedUrl.startsWith("https://")))
                || descLower.contains("name_not_resolved") || descLower.contains("resolve") || descLower.contains("gateway") || descLower.contains("tunnel");

        StringBuilder sb = new StringBuilder();
        sb.append("<!DOCTYPE html><html><head><meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.append("<style>");
        sb.append("body { background: #0b0e14; color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 20px; box-sizing: border-box; text-align: center; }");
        sb.append("h1 { color: #ef4444; font-size: 20px; margin-bottom: 8px; font-weight: 800; }");
        sb.append("p { color: #94a3b8; font-size: 13px; line-height: 1.5; max-width: 440px; margin-bottom: 12px; }");
        sb.append(".url-badge { background: #151b24; border: 1px solid #334155; padding: 6px 14px; border-radius: 6px; font-family: monospace; font-size: 12px; color: #66c0f4; margin-bottom: 16px; word-break: break-all; max-width: 90%; }");
        sb.append(".guide-card { width: 100%; max-width: 440px; background: rgba(0, 210, 255, 0.06); border: 1px solid rgba(0, 210, 255, 0.25); border-radius: 10px; padding: 14px; margin-bottom: 14px; text-align: left; font-size: 12px; line-height: 1.5; color: #cbd5e1; }");
        sb.append(".guide-card-warn { background: rgba(239, 68, 68, 0.06); border-color: rgba(239, 68, 68, 0.3); }");
        sb.append(".guide-card-ts { background: rgba(56, 239, 125, 0.06); border-color: rgba(56, 239, 125, 0.3); }");
        sb.append(".guide-title { font-weight: 700; color: #00d2ff; margin-bottom: 6px; display: flex; align-items: center; gap: 6px; }");
        sb.append(".guide-title-warn { color: #f87171; }");
        sb.append(".guide-title-ts { color: #38ef7d; }");
        sb.append(".btn-row { display: flex; flex-direction: column; gap: 10px; width: 100%; max-width: 380px; align-items: center; }");
        sb.append(".btn { width: 100%; background: #00d2ff; color: #0b0e14; font-weight: 800; border: none; padding: 13px 20px; border-radius: 24px; cursor: pointer; font-size: 14px; letter-spacing: 0.3px; box-shadow: 0 4px 14px rgba(0, 210, 255, 0.3); }");
        sb.append(".btn-wifi { background: linear-gradient(135deg, #00d2ff, #0077ff); color: #ffffff; box-shadow: 0 4px 14px rgba(0, 210, 255, 0.35); }");
        sb.append(".btn-ts { background: linear-gradient(135deg, #38ef7d, #10b981); color: #0b0e14; box-shadow: 0 4px 14px rgba(56, 239, 125, 0.35); }");
        sb.append(".btn-tunnel { background: linear-gradient(135deg, #66c0f4, #1b2838); color: #ffffff; box-shadow: 0 4px 14px rgba(102, 192, 244, 0.35); }");
        sb.append(".btn-dns { background: #8b5cf6; color: #ffffff; box-shadow: 0 4px 14px rgba(139, 92, 246, 0.35); }");
        sb.append(".btn-sec { background: #1e293b; color: #f1f5f9; border: 1px solid #334155; font-weight: 600; box-shadow: none; }");
        sb.append(".btn:active { transform: scale(0.98); opacity: 0.9; }");
        sb.append("</style></head><body>");

        if (isTailscaleError) {
            sb.append("<h1>🛡️ Tailscale WireGuard Unreachable</h1>");
            sb.append("<p>Could not connect to host PC via Tailscale. Make sure the Tailscale VPN is turned <b>ON</b> in the Tailscale app on this phone.</p>");
            sb.append("<div class='url-badge'>").append(safeUrl).append("</div>");

            sb.append("<div class='guide-card guide-card-ts'>");
            sb.append("<div class='guide-title guide-title-ts'>📱 Tailscale Setup on Android</div>");
            sb.append("1. Tap <b>OPEN TAILSCALE APP</b> below to ensure VPN is connected.<br>");
            sb.append("2. Verify your phone and PC are logged in to the same Tailscale account.<br>");
            sb.append("3. Once active, your connection is direct P2P WireGuard with zero lag!");
            sb.append("</div>");

            sb.append("<div class='btn-row' style='margin-bottom:12px;'>");
            sb.append("<button class='btn btn-ts' onclick='openTailscale()'>🚀 OPEN TAILSCALE APP</button>");
            if (hasLan) {
                sb.append("<button class='btn btn-wifi' onclick='connectLan()'>📶 CONNECT VIA LOCAL WI-FI (OFFLINE)</button>");
            }
            if (hasTunnel) {
                sb.append("<button class='btn btn-tunnel' onclick='connectTunnel()'>🌐 CONNECT VIA REMOTE TUNNEL</button>");
            }
            sb.append("</div>");
        } else if (isTunnelError) {
            sb.append("<h1>⚠️ Remote Tunnel Blocked (DNS Error)</h1>");
            sb.append("<p>Could not resolve host domain (<code>ERR_NAME_NOT_RESOLVED</code>). Many mobile carriers / ISPs filter <b>trycloudflare.com</b> by default.</p>");
            sb.append("<div class='url-badge'>").append(safeUrl).append("</div>");

            if (hasTailscale) {
                sb.append("<div class='guide-card guide-card-ts'>");
                sb.append("<div class='guide-title guide-title-ts'>🛡️ Recommended: Use Tailscale WireGuard</div>");
                sb.append("Tailscale uses direct WireGuard UDP and is <b>100% unaffected by ISP DNS blocks</b>!");
                sb.append("</div>");
                sb.append("<div class='btn-row' style='margin-bottom:12px;'>");
                sb.append("<button class='btn btn-ts' onclick='connectTailscale()'>🛡️ CONNECT VIA TAILSCALE WIREGUARD</button>");
                sb.append("</div>");
            }

            if (hasLan) {
                sb.append("<div class='guide-card'>");
                sb.append("<div class='guide-title'>📶 At Home on the Same Wi-Fi? (Offline)</div>");
                sb.append("Local Wi-Fi connects directly to your PC with <b>zero lag (&lt;1ms)</b> without using the internet or Cloudflare.");
                sb.append("</div>");
                sb.append("<div class='btn-row' style='margin-bottom:12px;'>");
                sb.append("<button class='btn btn-wifi' onclick='connectLan()'>📶 CONNECT VIA LOCAL WI-FI (OFFLINE)</button>");
                sb.append("</div>");
            }

            sb.append("<div class='guide-card guide-card-warn'>");
            sb.append("<div class='guide-title guide-title-warn'>🌐 Bypass ISP Block via Private DNS</div>");
            sb.append("1. Tap <b>OPEN PRIVATE DNS SETTINGS</b>.<br>");
            sb.append("2. Set Provider Hostname to <code style='color:#38ef7d;font-weight:bold;'>dns.google</code> or <code style='color:#38ef7d;font-weight:bold;'>one.one.one.one</code>.");
            sb.append("</div>");
            sb.append("<div class='btn-row' style='margin-bottom:12px;'>");
            sb.append("<button class='btn btn-dns' onclick='openDnsSettings()'>⚙️ OPEN PRIVATE DNS SETTINGS</button>");
            sb.append("<button class='btn btn-ts' onclick='openTailscale()'>📱 OPEN TAILSCALE APP</button>");
            sb.append("</div>");
        } else {
            sb.append("<h1>⚠️ Connection Refused</h1>");
            sb.append("<p>Could not connect to host PC on local Wi-Fi. Verify CloudRedirect is running on PC.</p>");
            sb.append("<div class='url-badge'>").append(safeUrl).append("</div>");

            if (hasTailscale) {
                sb.append("<div class='guide-card guide-card-ts'>");
                sb.append("<div class='guide-title guide-title-ts'>🛡️ Using 4G/5G Cellular or Outside Home?</div>");
                sb.append("Connect via Tailscale Virtual LAN for permanent, secure WireGuard access from anywhere:");
                sb.append("</div>");
                sb.append("<div class='btn-row' style='margin-bottom:12px;'>");
                sb.append("<button class='btn btn-ts' onclick='connectTailscale()'>🛡️ CONNECT VIA TAILSCALE WIREGUARD</button>");
                sb.append("</div>");
            }

            if (hasTunnel) {
                sb.append("<div class='btn-row' style='margin-bottom:12px;'>");
                sb.append("<button class='btn btn-tunnel' onclick='connectTunnel()'>🌐 CONNECT VIA REMOTE TUNNEL</button>");
                sb.append("</div>");
            }
        }

        sb.append("<div class='btn-row'>");
        sb.append("<button class='btn' onclick='triggerQrScan()'>📷 SCAN PC QR CODE</button>");
        sb.append("<button class='btn btn-sec' onclick='window.location.reload()'>🔄 RETRY CONNECTION</button>");
        sb.append("<button class='btn btn-sec' onclick='showDiscovery()'>🏠 BACK TO DISCOVERY</button>");
        sb.append("</div>");

        sb.append("<script>");
        sb.append("function triggerQrScan() {");
        sb.append("  try { if (window.SuoNative && window.SuoNative.openCameraScanner) { window.SuoNative.openCameraScanner(); return; } } catch(e){}");
        sb.append("  window.location.href = 'suolink://scan';");
        sb.append("}");
        sb.append("function showDiscovery() {");
        sb.append("  try { if (window.SuoNative && window.SuoNative.showDiscovery) { window.SuoNative.showDiscovery(); return; } } catch(e){}");
        sb.append("  window.location.href = 'suolink://discovery';");
        sb.append("}");
        sb.append("function openTailscale() {");
        sb.append("  try { if (window.SuoNative && window.SuoNative.openTailscaleApp) { window.SuoNative.openTailscaleApp(); return; } } catch(e){}");
        sb.append("  window.location.href = 'suolink://tailscale';");
        sb.append("}");
        sb.append("function connectTailscale() {");
        sb.append("  var tsUrl = '").append(safeTailscale.replace("'", "\\'")).append("';");
        sb.append("  try { if (window.SuoNative && window.SuoNative.connectTailscale) { window.SuoNative.connectTailscale(tsUrl); return; } } catch(e){}");
        sb.append("  try { if (window.SuoNative && window.SuoNative.connectSmart) { window.SuoNative.connectSmart(tsUrl); return; } } catch(e){}");
        sb.append("  window.location.href = tsUrl;");
        sb.append("}");
        sb.append("function connectTunnel() {");
        sb.append("  var tUrl = '").append(safeTunnel.replace("'", "\\'")).append("';");
        sb.append("  try { if (window.SuoNative && window.SuoNative.connectSmart) { window.SuoNative.connectSmart(tUrl); return; } } catch(e){}");
        sb.append("  window.location.href = tUrl;");
        sb.append("}");
        sb.append("function connectLan() {");
        sb.append("  var lUrl = '").append(safeLan.replace("'", "\\'")).append("';");
        sb.append("  try { if (window.SuoNative && window.SuoNative.connectLan) { window.SuoNative.connectLan(lUrl); return; } } catch(e){}");
        sb.append("  try { if (window.SuoNative && window.SuoNative.saveAndLoadHost) { window.SuoNative.saveAndLoadHost(lUrl); return; } } catch(e){}");
        sb.append("  window.location.href = lUrl;");
        sb.append("}");
        sb.append("function openDnsSettings() {");
        sb.append("  try { if (window.SuoNative && window.SuoNative.openDnsSettings) { window.SuoNative.openDnsSettings(); return; } } catch(e){}");
        sb.append("  alert('Open Android Settings > Network & internet > Private DNS and set to dns.google or one.one.one.one');");
        sb.append("}");
        sb.append("</script></body></html>");
        return sb.toString();
    }
}
