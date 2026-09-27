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
"    <input type='text' id='manual-ip' class='manual-input' placeholder='192.168.1.xxx:8585' value='" + safeLastHost + "'>" +
"    <button class='btn-manual' onclick='connectManual()'>CONNECT</button>" +
"  </div>" +
"  <script>" +
"    function connectUrl(url) {" +
"      if (!url) return;" +
"      url = url.trim();" +
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
        String safeUrl = failedUrl != null ? failedUrl : "";
        return "<!DOCTYPE html>" +
"<html><head><meta name='viewport' content='width=device-width, initial-scale=1.0'>" +
"<style>" +
"body { background: #0b0e14; color: #f1f5f9; font-family: sans-serif; display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 24px; box-sizing: border-box; text-align: center; }" +
"h1 { color: #ef4444; font-size: 24px; margin-bottom: 8px; }" +
"p { color: #94a3b8; font-size: 13px; line-height: 1.5; max-width: 400px; margin-bottom: 16px; }" +
".url-badge { background: #151b24; border: 1px solid #334155; padding: 6px 12px; border-radius: 6px; font-family: monospace; font-size: 12px; color: #66c0f4; margin-bottom: 20px; word-break: break-all; }" +
".btn { background: #00d2ff; color: #000; font-weight: bold; border: none; padding: 12px 24px; border-radius: 20px; cursor: pointer; font-size: 14px; margin: 6px; }" +
".btn-sec { background: #1e293b; color: #fff; border: 1px solid #334155; }" +
"</style></head><body>" +
"<h1>⚠️ Connection Refused</h1>" +
"<p>Could not connect to CloudRedirect Host PC.<br>Ensure CloudRedirect is running on your PC and both devices are on the same Wi-Fi.</p>" +
"<div class='url-badge'>" + safeUrl + "</div>" +
"<div>" +
"  <button class='btn' onclick='window.location.reload()'>🔄 RETRY</button>" +
"  <button class='btn btn-sec' onclick='if(window.SuoNative && window.SuoNative.showDiscovery) window.SuoNative.showDiscovery(); else window.location.href=\"suolink://scan\";'>📷 SCAN QR CODE</button>" +
"</div>" +
"</body></html>";
    }
}
