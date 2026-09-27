package com.cloudredirect.suolink;

public class DiscoveryHtml {
    public static String getHtml(String lastHost) {
        return getHtml(lastHost, null);
    }

    public static String getHtml(String lastHost, String lastTunnel) {
        String safeLastHost = lastHost != null ? lastHost : "";
        return "<!DOCTYPE html>" +
"<html>" +
"<head>" +
"  <meta charset='utf-8'>" +
"  <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no'>" +
"  <title>SUO Link</title>" +
"  <style>" +
"    * { box-sizing: border-box; margin: 0; padding: 0; user-select: none; -webkit-user-select: none; }" +
"    body {" +
"      background: #0b0e14;" +
"      color: #f1f5f9;" +
"      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;" +
"      display: flex;" +
"      flex-direction: column;" +
"      align-items: center;" +
"      justify-content: center;" +
"      min-height: 100vh;" +
"      padding: 16px;" +
"      text-align: center;" +
"      overflow-x: hidden;" +
"    }" +
"    .logo-badge { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }" +
"    .logo-title { font-size: 26px; font-weight: 800; color: #fff; letter-spacing: 1px; }" +
"    .logo-title span { color: #00d2ff; }" +
"    .subtitle { color: #94a3b8; font-size: 13px; max-width: 440px; line-height: 1.4; margin-bottom: 18px; }" +
"    #connecting-banner {" +
"      display: none;" +
"      width: 100%; max-width: 360px;" +
"      background: rgba(0, 210, 255, 0.15);" +
"      border: 1px solid #00d2ff;" +
"      color: #00d2ff;" +
"      font-size: 13px; font-weight: 700;" +
"      padding: 10px 14px; border-radius: 10px;" +
"      margin-bottom: 16px;" +
"      box-shadow: 0 0 15px rgba(0, 210, 255, 0.2);" +
"      word-break: break-all;" +
"    }" +
"    #update-banner {" +
"      display: none;" +
"      width: 100%; max-width: 360px;" +
"      background: rgba(56, 239, 125, 0.15);" +
"      border: 1px solid #38ef7d;" +
"      color: #38ef7d;" +
"      font-size: 12px; font-weight: 600;" +
"      padding: 10px 14px; border-radius: 10px;" +
"      margin-bottom: 16px;" +
"      box-shadow: 0 0 15px rgba(56, 239, 125, 0.2);" +
"    }" +
"    .btn-qr {" +
"      background: linear-gradient(135deg, #00d2ff, #38ef7d);" +
"      color: #0b0e14;" +
"      font-weight: 800;" +
"      font-size: 16px;" +
"      letter-spacing: 0.5px;" +
"      padding: 16px 36px;" +
"      border-radius: 32px;" +
"      border: none;" +
"      display: inline-flex;" +
"      align-items: center;" +
"      gap: 10px;" +
"      box-shadow: 0 4px 20px rgba(0, 210, 255, 0.4);" +
"      cursor: pointer;" +
"      margin-bottom: 14px;" +
"      transition: transform 0.1s, opacity 0.1s;" +
"    }" +
"    .btn-qr:active { transform: scale(0.96); opacity: 0.9; }" +
"    .btn-file {" +
"      background: #151b24;" +
"      border: 1px solid #334155;" +
"      color: #94a3b8;" +
"      font-size: 13px;" +
"      font-weight: 600;" +
"      padding: 10px 20px;" +
"      border-radius: 20px;" +
"      cursor: pointer;" +
"      margin-bottom: 16px;" +
"    }" +
"    .btn-file:active { background: #1e293b; color: #fff; }" +
"    .divider { width: 100%; max-width: 360px; height: 1px; background: #1e293b; margin: 12px 0; }" +
"    #detected-host-card {" +
"      display: none;" +
"      width: 100%; max-width: 360px;" +
"      background: #151b24; border: 1px solid #00d2ff;" +
"      border-radius: 12px; padding: 14px; margin-bottom: 16px;" +
"      box-shadow: 0 0 15px rgba(0, 210, 255, 0.2);" +
"      text-align: left;" +
"    }" +
"    .host-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px; }" +
"    .host-name { font-weight: 700; color: #fff; font-size: 14px; }" +
"    .host-badge { background: rgba(56, 239, 125, 0.15); color: #38ef7d; font-size: 10px; font-weight: 700; padding: 2px 8px; border-radius: 10px; border: 1px solid rgba(56, 239, 125, 0.4); }" +
"    .host-ip { font-size: 12px; color: #94a3b8; margin-bottom: 10px; }" +
"    .btn-connect-host { width: 100%; background: #11998e; color: #fff; font-weight: 700; font-size: 13px; border: none; padding: 10px; border-radius: 6px; cursor: pointer; }" +
"    .manual-box { width: 100%; max-width: 360px; display: flex; gap: 8px; }" +
"    .manual-input { flex: 1; background: #151b24; border: 1px solid #222d3d; border-radius: 8px; padding: 10px 14px; color: #fff; font-size: 13px; outline: none; }" +
"    .manual-input:focus { border-color: #00d2ff; }" +
"    .btn-manual { background: #1e293b; border: 1px solid #334155; color: #fff; font-weight: 600; padding: 10px 16px; border-radius: 8px; font-size: 13px; cursor: pointer; }" +
"  </style>" +
"</head>" +
"<body>" +
"  <div class='logo-badge'>" +
"    <div style='width:12px;height:12px;border-radius:50%;background:#00d2ff;box-shadow:0 0 10px #00d2ff;'></div>" +
"    <div class='logo-title'>SUO <span>LINK</span></div>" +
"  </div>" +
"  <p class='subtitle'>Steam Link &amp; GameHub Remote Play for CloudRedirect.<br>Scan PC screen to connect in 1 second.</p>" +
"  <div style='background:rgba(0,210,255,0.08); border:1px solid rgba(0,210,255,0.25); border-radius:20px; padding:6px 14px; font-size:11px; color:#66c0f4; margin-bottom:14px; display:inline-flex; align-items:center; gap:6px;'>" +
"    <span>🌐</span> Connects on ANY network: Home Wi-Fi or 4G/5G mobile" +
"  </div>" +
"  <!-- CONNECTING BANNER -->" +
"  <div id='connecting-banner'>🚀 Connecting to PC...</div>" +
"  <!-- AUTO UPDATE BANNER -->" +
"  <div id='update-banner'>⬆️ New SUO Link update downloading...</div>" +
"  <!-- 1-TAP QR CODE SCANNER BUTTON -->" +
"  <button class='btn-qr' id='btn-scan-qr' onclick='triggerQrScan()'>" +
"    <span style='font-size:22px;'>📷</span> SCAN PC QR CODE" +
"  </button>" +
"  <div>" +
"    <button class='btn-file' id='btn-gallery' onclick='triggerGallery()'>📁 Or select QR image from gallery</button>" +
"  </div>" +
"  <!-- DETECTED HOST CARD (From UDP Beacon) -->" +
"  <div id='detected-host-card'>" +
"    <div class='host-header'>" +
"      <div class='host-name' id='dh-name'>PC Host</div>" +
"      <div class='host-badge'>FOUND ON WI-FI</div>" +
"    </div>" +
"    <div class='host-ip' id='dh-ip'>192.168.1.xxx:8585</div>" +
"    <button class='btn-connect-host' id='dh-btn'>🚀 CONNECT TO PC</button>" +
"  </div>" +
"  <div class='divider'></div>" +
"  <!-- MANUAL IP FALLBACK -->" +
"  <div class='manual-box'>" +
"    <input type='text' id='manual-ip' class='manual-input' placeholder='192.168.1.xxx:8585 or trycloudflare URL' value='" + safeLastHost + "'>" +
"    <button class='btn-manual' onclick='connectManual()'>CONNECT</button>" +
"  </div>" +
"  <script>" +
"    function connectUrl(url) {" +
"      if (!url) return;" +
"      url = url.trim();" +
"      var input = document.getElementById('manual-ip');" +
"      if (input) input.value = url;" +
"      var banner = document.getElementById('connecting-banner');" +
"      if (banner) {" +
"        banner.innerText = '🚀 Connecting to ' + url + '...';" +
"        banner.style.display = 'block';" +
"      }" +
"      try {" +
"        if (window.SuoNative && window.SuoNative.vibrate) window.SuoNative.vibrate(40);" +
"        if (window.SuoNative && window.SuoNative.connectSmart) {" +
"          window.SuoNative.connectSmart(url);" +
"          return;" +
"        }" +
"      } catch (e) {}" +
"      if (url.indexOf('http://') !== 0 && url.indexOf('https://') !== 0) {" +
"        url = 'http://' + url;" +
"      }" +
"      window.location.href = url;" +
"    }" +
"    function connectManual() {" +
"      var input = document.getElementById('manual-ip');" +
"      if (input && input.value) {" +
"        connectUrl(input.value);" +
"      }" +
"    }" +
"    function triggerQrScan() {" +
"      try {" +
"        if (window.SuoNative && window.SuoNative.vibrate) window.SuoNative.vibrate(30);" +
"        if (window.SuoNative && window.SuoNative.openCameraScanner) {" +
"          window.SuoNative.openCameraScanner();" +
"          return;" +
"        }" +
"      } catch (e) {}" +
"      window.location.href = 'suolink://scan';" +
"    }" +
"    function triggerGallery() {" +
"      try {" +
"        if (window.SuoNative && window.SuoNative.vibrate) window.SuoNative.vibrate(20);" +
"        if (window.SuoNative && window.SuoNative.openGalleryPicker) {" +
"          window.SuoNative.openGalleryPicker();" +
"          return;" +
"        }" +
"      } catch (e) {}" +
"      window.location.href = 'suolink://gallery';" +
"    }" +
"    window.setScannedUrl = function(url) {" +
"      if (!url) return;" +
"      var input = document.getElementById('manual-ip');" +
"      if (input) input.value = url;" +
"      var banner = document.getElementById('connecting-banner');" +
"      if (banner) {" +
"        banner.innerText = '✓ Scanned: ' + url + '\\n🚀 Connecting...';" +
"        banner.style.display = 'block';" +
"      }" +
"    };" +
"    window.onHostDiscovered = function(name, ip, port, auth, tunnel) {" +
"      var fullUrl = 'http://' + ip + ':' + port + '/?auth=' + auth;" +
"      if (tunnel) fullUrl += '&tunnel=' + encodeURIComponent(tunnel);" +
"      var nameEl = document.getElementById('dh-name');" +
"      var ipEl = document.getElementById('dh-ip');" +
"      var cardEl = document.getElementById('detected-host-card');" +
"      var btnEl = document.getElementById('dh-btn');" +
"      if (nameEl) nameEl.innerText = name || 'CloudRedirect PC';" +
"      if (ipEl) ipEl.innerText = ip + ':' + port + (tunnel ? ' • Remote Tunnel Ready' : '');" +
"      if (cardEl) cardEl.style.display = 'block';" +
"      if (btnEl) btnEl.onclick = function() { connectUrl(fullUrl); };" +
"    };" +
"    window.showUpdateNotice = function(msg) {" +
"      var banner = document.getElementById('update-banner');" +
"      if (banner) {" +
"        banner.innerText = msg || '⬆️ Updating SUO Link... Check notifications to install.';" +
"        banner.style.display = 'block';" +
"      }" +
"    };" +
"  </script>" +
"</body>" +
"</html>";
    }

    public static String getErrorHtml(String failedUrl, String description) {
        return getErrorHtml(failedUrl, description, null, null);
    }

    public static String getErrorHtml(String failedUrl, String description, String savedTunnelUrl) {
        return getErrorHtml(failedUrl, description, savedTunnelUrl, null);
    }

    public static String getErrorHtml(String failedUrl, String description, String savedTunnelUrl, String savedLanUrl) {
        String safeUrl = failedUrl != null ? failedUrl : "";
        if (safeUrl.isEmpty() || safeUrl.startsWith("file://") || safeUrl.contains("suolink.local") || safeUrl.startsWith("data:") || safeUrl.startsWith("about:")) {
            safeUrl = "Unable to reach PC host";
        }
        String safeTunnel = savedTunnelUrl != null ? savedTunnelUrl : "";
        String safeLan = savedLanUrl != null ? savedLanUrl : "";
        boolean hasTunnel = !safeTunnel.isEmpty();
        boolean hasLan = !safeLan.isEmpty();

        String descLower = description != null ? description.toLowerCase() : "";
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
        sb.append(".guide-title { font-weight: 700; color: #00d2ff; margin-bottom: 6px; display: flex; align-items: center; gap: 6px; }");
        sb.append(".guide-title-warn { color: #f87171; }");
        sb.append(".btn-row { display: flex; flex-direction: column; gap: 10px; width: 100%; max-width: 380px; align-items: center; }");
        sb.append(".btn { width: 100%; background: #00d2ff; color: #0b0e14; font-weight: 800; border: none; padding: 13px 20px; border-radius: 24px; cursor: pointer; font-size: 14px; letter-spacing: 0.3px; box-shadow: 0 4px 14px rgba(0, 210, 255, 0.3); }");
        sb.append(".btn-wifi { background: linear-gradient(135deg, #00d2ff, #0077ff); color: #ffffff; box-shadow: 0 4px 14px rgba(0, 210, 255, 0.35); }");
        sb.append(".btn-tunnel { background: linear-gradient(135deg, #38ef7d, #11998e); color: #0b0e14; box-shadow: 0 4px 14px rgba(56, 239, 125, 0.35); }");
        sb.append(".btn-dns { background: #8b5cf6; color: #ffffff; box-shadow: 0 4px 14px rgba(139, 92, 246, 0.35); }");
        sb.append(".btn-sec { background: #1e293b; color: #f1f5f9; border: 1px solid #334155; font-weight: 600; box-shadow: none; }");
        sb.append(".btn:active { transform: scale(0.98); opacity: 0.9; }");
        sb.append("</style></head><body>");

        if (isTunnelError) {
            sb.append("<h1>⚠️ Remote Tunnel Blocked (DNS Error)</h1>");
            sb.append("<p>Could not find host domain (<code>ERR_NAME_NOT_RESOLVED</code>). Many mobile carriers / ISPs in Indonesia (IndiHome, Telkomsel, XL, etc.) filter <b>trycloudflare.com</b> by default.</p>");
            sb.append("<div class='url-badge'>").append(safeUrl).append("</div>");

            if (hasLan) {
                sb.append("<div class='guide-card'>");
                sb.append("<div class='guide-title'>📶 At Home on the Same Wi-Fi? (Recommended)</div>");
                sb.append("Local Wi-Fi connects directly to your PC with <b>zero lag (&lt;1ms)</b> and 0 data usage. It does not use the internet or Cloudflare!");
                sb.append("</div>");
                sb.append("<div class='btn-row' style='margin-bottom:12px;'>");
                sb.append("<button class='btn btn-wifi' onclick='connectLan()'>📶 CONNECT VIA LOCAL WI-FI (OFFLINE)</button>");
                sb.append("</div>");
            }

            sb.append("<div class='guide-card guide-card-warn'>");
            sb.append("<div class='guide-title guide-title-warn'>🌐 On 4G/5G Cellular or Outside Home?</div>");
            sb.append("To bypass the ISP block in 10 seconds:<br>");
            sb.append("1. Tap <b>OPEN PRIVATE DNS SETTINGS</b> below.<br>");
            sb.append("2. Select <b>Private DNS</b> ➔ Provider Hostname.<br>");
            sb.append("3. Enter <code style='color:#38ef7d;font-weight:bold;'>dns.google</code> or <code style='color:#38ef7d;font-weight:bold;'>one.one.one.one</code> and Save.");
            sb.append("</div>");
            sb.append("<div class='btn-row' style='margin-bottom:12px;'>");
            sb.append("<button class='btn btn-dns' onclick='openDnsSettings()'>⚙️ OPEN PRIVATE DNS SETTINGS</button>");
            sb.append("</div>");
        } else {
            sb.append("<h1>⚠️ Connection Refused</h1>");
            sb.append("<p>Could not connect to host PC on local Wi-Fi. Verify CloudRedirect is running on PC.</p>");
            sb.append("<div class='url-badge'>").append(safeUrl).append("</div>");

            if (hasTunnel) {
                sb.append("<div class='guide-card'>");
                sb.append("<div class='guide-title'>🌐 Using a Different Network (Cellular 4G/5G or Outside Home)?</div>");
                sb.append("Private IP (192.168.x.x) only works on the <b>exact same Wi-Fi router</b>.<br>");
                sb.append("To connect from mobile data or another network, use the Remote Tunnel:");
                sb.append("</div>");
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
