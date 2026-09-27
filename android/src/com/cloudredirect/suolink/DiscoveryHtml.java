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
"  <script src='file:///android_asset/jsqr.js'></script>" +
"  <style>" +
"    * { box-sizing: border-box; margin: 0; padding: 0; user-select: none; -webkit-user-select: none; }" +
"    body {" +
"      background: #0b0e14;" +
"      color: #f1f5f9;" +
"      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;" +
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
"    .subtitle { color: #94a3b8; font-size: 13px; max-width: 420px; line-height: 1.4; margin-bottom: 16px; }" +
"    /* Update Notification Banner */" +
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
"      animation: pulse 1.5s infinite;" +
"    }" +
"    @keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.7; } }" +
"    /* Primary 1-Tap QR Scanner Button */" +
"    .btn-qr {" +
"      background: linear-gradient(135deg, #00d2ff, #38ef7d);" +
"      color: #000;" +
"      font-weight: 800;" +
"      font-size: 15px;" +
"      padding: 14px 28px;" +
"      border-radius: 30px;" +
"      border: none;" +
"      display: inline-flex;" +
"      align-items: center;" +
"      gap: 10px;" +
"      box-shadow: 0 4px 20px rgba(0, 210, 255, 0.4);" +
"      cursor: pointer;" +
"      margin-bottom: 14px;" +
"      transition: transform 0.15s;" +
"    }" +
"    .btn-qr:active { transform: scale(0.96); }" +
"    .btn-file {" +
"      background: transparent;" +
"      border: 1px solid #334155;" +
"      color: #94a3b8;" +
"      font-size: 12px;" +
"      padding: 6px 14px;" +
"      border-radius: 16px;" +
"      cursor: pointer;" +
"      margin-bottom: 16px;" +
"    }" +
"    .divider { width: 100%; max-width: 360px; height: 1px; background: #1e293b; margin: 12px 0; }" +
"    /* Detected Host Card (From UDP Beacon) */" +
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
"    /* Manual Input */" +
"    .manual-box { width: 100%; max-width: 360px; display: flex; gap: 8px; }" +
"    .manual-input { flex: 1; background: #151b24; border: 1px solid #222d3d; border-radius: 8px; padding: 10px 14px; color: #fff; font-size: 13px; outline: none; }" +
"    .manual-input:focus { border-color: #00d2ff; }" +
"    .btn-manual { background: #1e293b; border: 1px solid #334155; color: #fff; font-weight: 600; padding: 10px 16px; border-radius: 8px; font-size: 13px; cursor: pointer; }" +
"    /* Live Camera QR Scanner Modal */" +
"    #qr-modal {" +
"      display: none;" +
"      position: fixed; top: 0; left: 0; width: 100vw; height: 100vh;" +
"      background: #000; z-index: 1000;" +
"      flex-direction: column; align-items: center; justify-content: space-between;" +
"      padding: 20px 16px;" +
"    }" +
"    #qr-video { position: absolute; top: 0; left: 0; width: 100%; height: 100%; object-fit: cover; }" +
"    .qr-overlay {" +
"      position: absolute; top: 0; left: 0; width: 100%; height: 100%;" +
"      display: flex; flex-direction: column; align-items: center; justify-content: center;" +
"      pointer-events: none;" +
"    }" +
"    .qr-target-box {" +
"      width: 250px; height: 250px;" +
"      border: 3px solid #00d2ff;" +
"      border-radius: 16px;" +
"      box-shadow: 0 0 0 9999px rgba(0, 0, 0, 0.68);" +
"      position: relative;" +
"      overflow: hidden;" +
"    }" +
"    .qr-laser {" +
"      width: 100%; height: 3px;" +
"      background: #38ef7d;" +
"      box-shadow: 0 0 10px #38ef7d;" +
"      position: absolute; top: 0; left: 0;" +
"      animation: scanLaser 2s infinite ease-in-out;" +
"    }" +
"    @keyframes scanLaser { 0% { top: 0; } 50% { top: 96%; } 100% { top: 0; } }" +
"    .qr-top-bar { position: relative; z-index: 10; width: 100%; display: flex; justify-content: space-between; align-items: center; }" +
"    .qr-title { color: #fff; font-size: 15px; font-weight: 700; text-shadow: 0 2px 4px rgba(0,0,0,0.8); }" +
"    .btn-close-qr { background: rgba(0,0,0,0.6); border: 1px solid rgba(255,255,255,0.3); color: #fff; padding: 6px 16px; border-radius: 20px; font-size: 13px; font-weight: bold; cursor: pointer; }" +
"    .qr-bottom-hint { position: relative; z-index: 10; color: #fff; font-size: 12px; background: rgba(0,0,0,0.75); padding: 8px 16px; border-radius: 20px; }" +
"    #qr-canvas { display: none; }" +
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
"  <button class='btn-qr' onclick='startCameraScanner()'>" +
"    <span style='font-size:20px;'>📷</span> SCAN PC QR CODE" +
"  </button>" +
"  <div>" +
"    <input type='file' id='qr-file-input' accept='image/*' style='display:none;' onchange='handleFileQr(this.files)'>" +
"    <button class='btn-file' onclick='document.getElementById(\"qr-file-input\").click()'>📁 Or select QR image from gallery</button>" +
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
"  <!-- LIVE CAMERA QR SCANNER MODAL -->" +
"  <div id='qr-modal'>" +
"    <video id='qr-video' playsinline autoplay></video>" +
"    <canvas id='qr-canvas'></canvas>" +
"    <div class='qr-top-bar'>" +
"      <div class='qr-title'>Align PC QR Code in Box</div>" +
"      <button class='btn-close-qr' onclick='stopCameraScanner()'>✕ CLOSE</button>" +
"    </div>" +
"    <div class='qr-overlay'>" +
"      <div class='qr-target-box'>" +
"        <div class='qr-laser'></div>" +
"      </div>" +
"    </div>" +
"    <div class='qr-bottom-hint' id='qr-hint'>Point camera at CloudRedirect on your PC screen</div>" +
"  </div>" +
"  <script>" +
"    let videoStream = null;" +
"    let scanning = false;" +
"    function connectUrl(url) {" +
"      if (!url) return;" +
"      url = url.trim();" +
"      if (window.SuoNative && window.SuoNative.vibrate) window.SuoNative.vibrate(40);" +
"      if (window.SuoNative && window.SuoNative.connectSmart) {" +
"        window.SuoNative.connectSmart(url);" +
"        return;" +
"      }" +
"      if (!url.startsWith('http://') && !url.startsWith('https://')) {" +
"        url = 'http://' + url;" +
"      }" +
"      window.location.href = url;" +
"    }" +
"    function connectManual() {" +
"      const val = document.getElementById('manual-ip').value;" +
"      connectUrl(val);" +
"    }" +
"    window.onHostDiscovered = function(name, ip, port, auth, tunnel) {" +
"      let fullUrl = 'http://' + ip + ':' + port + '/?auth=' + auth;" +
"      if (tunnel) fullUrl += '&tunnel=' + encodeURIComponent(tunnel);" +
"      document.getElementById('dh-name').innerText = name || 'CloudRedirect PC';" +
"      document.getElementById('dh-ip').innerText = ip + ':' + port + (tunnel ? ' • Remote Tunnel Ready' : '');" +
"      document.getElementById('detected-host-card').style.display = 'block';" +
"      document.getElementById('dh-btn').onclick = function() { connectUrl(fullUrl); };" +
"    };" +
"    window.showUpdateNotice = function(msg) {" +
"      const banner = document.getElementById('update-banner');" +
"      banner.innerText = msg || '⬆️ Updating SUO Link... Check notifications to install.';" +
"      banner.style.display = 'block';" +
"    };" +
"    // Live Camera QR Scanner" +
"    async function startCameraScanner() {" +
"      const modal = document.getElementById('qr-modal');" +
"      modal.style.display = 'flex';" +
"      scanning = true;" +
"      try {" +
"        videoStream = await navigator.mediaDevices.getUserMedia({" +
"          video: { facingMode: 'environment', width: { ideal: 1280 }, height: { ideal: 720 } }" +
"        });" +
"        const video = document.getElementById('qr-video');" +
"        video.srcObject = videoStream;" +
"        video.play();" +
"        requestAnimationFrame(tickScan);" +
"      } catch (err) {" +
"        alert('Camera access required to scan QR code. Please grant camera permission.');" +
"        stopCameraScanner();" +
"      }" +
"    }" +
"    function stopCameraScanner() {" +
"      scanning = false;" +
"      if (videoStream) {" +
"        videoStream.getTracks().forEach(t => t.stop());" +
"        videoStream = null;" +
"      }" +
"      document.getElementById('qr-modal').style.display = 'none';" +
"    }" +
"    function tickScan() {" +
"      if (!scanning) return;" +
"      const video = document.getElementById('qr-video');" +
"      if (video.readyState === video.HAVE_ENOUGH_DATA) {" +
"        const canvas = document.getElementById('qr-canvas');" +
"        const ctx = canvas.getContext('2d');" +
"        canvas.width = video.videoWidth;" +
"        canvas.height = video.videoHeight;" +
"        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);" +
"        const imgData = ctx.getImageData(0, 0, canvas.width, canvas.height);" +
"        if (window.jsQR) {" +
"          const code = jsQR(imgData.data, imgData.width, imgData.height, { inversionAttempts: 'dontInvert' });" +
"          if (code && code.data && code.data.length > 5) {" +
"            document.getElementById('qr-hint').innerText = '✓ QR Code Recognized! Connecting...';" +
"            stopCameraScanner();" +
"            connectUrl(code.data);" +
"            return;" +
"          }" +
"        }" +
"      }" +
"      if (scanning) requestAnimationFrame(tickScan);" +
"    }" +
"    // File image scan fallback" +
"    function handleFileQr(files) {" +
"      if (!files || files.length === 0) return;" +
"      const file = files[0];" +
"      const reader = new FileReader();" +
"      reader.onload = function(e) {" +
"        const img = new Image();" +
"        img.onload = function() {" +
"          const canvas = document.getElementById('qr-canvas');" +
"          const ctx = canvas.getContext('2d');" +
"          canvas.width = img.width;" +
"          canvas.height = img.height;" +
"          ctx.drawImage(img, 0, 0);" +
"          const imgData = ctx.getImageData(0, 0, img.width, img.height);" +
"          if (window.jsQR) {" +
"            const code = jsQR(imgData.data, imgData.width, imgData.height);" +
"            if (code && code.data) {" +
"              connectUrl(code.data);" +
"            } else {" +
"              alert('Could not find QR code in this image. Please try another or use camera.');" +
"            }" +
"          }" +
"        };" +
"        img.src = e.target.result;" +
"      };" +
"      reader.readAsDataURL(file);" +
"    }" +
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
"  <button class='btn btn-sec' onclick='if(window.SuoNative && window.SuoNative.showDiscovery) window.SuoNative.showDiscovery(); else window.location.href=\"about:blank\";'>📷 SCAN QR CODE</button>" +
"</div>" +
"</body></html>";
    }
}
