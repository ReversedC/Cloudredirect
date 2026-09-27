using System;

namespace CloudRedirect.Services;

/// <summary>
/// Embeds the GameHub-inspired responsive mobile web UI and Touch Studio for SUO Link.
/// Used both for direct mobile browser streaming (instant play) and inside the native SUO Link Android APK.
/// </summary>
public static class SuoLinkWebUi
{
    public static string GetHtml(string token, string hostIp, int port)
    {
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>SUO Link</title>
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no, viewport-fit=cover">
  <meta name="theme-color" content="#0e141b">
  <meta name="mobile-web-app-capable" content="yes">
  <meta name="apple-mobile-web-app-capable" content="yes">
  <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Rajdhani:wght@500;600;700&family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
  <style>
    :root {
      --bg-dark: #0b0e14;
      --bg-card: #151b24;
      --bg-card-hover: #1c2533;
      --accent-cyan: #00d2ff;
      --accent-green: #38ef7d;
      --accent-glow: rgba(0, 210, 255, 0.4);
      --text-main: #f1f5f9;
      --text-muted: #94a3b8;
      --border-dark: #222d3d;
      --touch-btn-bg: rgba(21, 27, 36, 0.72);
      --touch-btn-border: rgba(0, 210, 255, 0.6);
      --touch-btn-active: rgba(56, 239, 125, 0.85);
    }
    * {
      box-sizing: border-box;
      margin: 0;
      padding: 0;
      user-select: none;
      -webkit-user-select: none;
      touch-action: none;
    }
    body {
      background: var(--bg-dark);
      color: var(--text-main);
      font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
      overflow: hidden;
      width: 100vw;
      height: 100vh;
    }
    /* Views */
    #hub-view, #stream-view {
      position: absolute;
      top: 0; left: 0; width: 100%; height: 100%;
    }
    #stream-view {
      display: none;
      background: #000;
      z-index: 10;
    }

    /* GameHub Header */
    .hub-header {
      height: 60px;
      background: rgba(14, 20, 27, 0.95);
      backdrop-filter: blur(10px);
      border-bottom: 1px solid var(--border-dark);
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 16px;
      position: relative;
      z-index: 5;
    }
    .brand {
      display: flex;
      align-items: center;
      gap: 10px;
      font-family: 'Rajdhani', sans-serif;
      font-size: 22px;
      font-weight: 700;
      color: #fff;
      letter-spacing: 0.5px;
    }
    .brand span { color: var(--accent-cyan); }
    .badge-status {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      background: rgba(56, 239, 125, 0.12);
      color: var(--accent-green);
      padding: 4px 10px;
      border-radius: 20px;
      border: 1px solid rgba(56, 239, 125, 0.3);
    }
    .badge-dot {
      width: 7px;
      height: 7px;
      background: var(--accent-green);
      border-radius: 50%;
      animation: pulse 1.5s infinite;
    }
    @keyframes pulse {
      0%, 100% { opacity: 1; transform: scale(1); }
      50% { opacity: 0.4; transform: scale(0.85); }
    }

    /* Content Area */
    .hub-body {
      height: calc(100% - 60px);
      overflow-y: auto;
      padding: 16px;
      -webkit-overflow-scrolling: touch;
      touch-action: pan-y;
    }
    .search-bar {
      margin-bottom: 16px;
    }
    .search-input {
      width: 100%;
      background: var(--bg-card);
      border: 1px solid var(--border-dark);
      border-radius: 8px;
      padding: 10px 14px;
      color: #fff;
      font-size: 14px;
      outline: none;
      touch-action: auto;
    }
    .search-input:focus {
      border-color: var(--accent-cyan);
    }
    .section-title {
      font-size: 13px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 1px;
      color: var(--text-muted);
      margin-bottom: 12px;
      display: flex;
      justify-content: space-between;
    }

    /* Game Cards Grid */
    .game-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
      gap: 14px;
      padding-bottom: 80px;
    }
    .game-card {
      background: var(--bg-card);
      border: 1px solid var(--border-dark);
      border-radius: 10px;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      cursor: pointer;
      transition: transform 0.15s, border-color 0.15s;
    }
    .game-card:active {
      transform: scale(0.97);
      border-color: var(--accent-cyan);
    }
    .game-card img {
      width: 100%;
      height: 80px;
      object-fit: cover;
      background: #111;
    }
    .game-card-info {
      padding: 10px;
      display: flex;
      flex-direction: column;
      gap: 6px;
      flex: 1;
    }
    .game-title {
      font-size: 13px;
      font-weight: 600;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .game-source {
      font-size: 11px;
      color: var(--accent-cyan);
      display: flex;
      align-items: center;
      gap: 4px;
    }
    .btn-play-card {
      margin-top: auto;
      background: linear-gradient(135deg, #11998e, #38ef7d);
      color: #000;
      font-weight: 700;
      font-size: 11px;
      border: none;
      border-radius: 6px;
      padding: 6px 0;
      text-align: center;
      cursor: pointer;
    }

    /* Stream Viewport */
    #stream-img {
      width: 100%;
      height: 100%;
      object-fit: contain;
      display: block;
      pointer-events: none;
    }

    /* Floating GameHub HUD Pill */
    .hud-pill {
      position: absolute;
      top: 14px;
      left: 50%;
      transform: translateX(-50%);
      height: 32px;
      padding: 0 16px;
      background: rgba(14, 20, 27, 0.85);
      backdrop-filter: blur(8px);
      border: 1px solid rgba(0, 210, 255, 0.4);
      border-radius: 16px;
      display: flex;
      align-items: center;
      gap: 12px;
      z-index: 100;
      box-shadow: 0 4px 14px rgba(0,0,0,0.6);
      cursor: pointer;
    }
    .hud-pill span {
      font-size: 12px;
      font-weight: 700;
      letter-spacing: 0.5px;
      color: var(--accent-cyan);
    }

    /* Quick Menu Overlay */
    .quick-menu-modal {
      display: none;
      position: absolute;
      top: 0; left: 0; width: 100%; height: 100%;
      background: rgba(0, 0, 0, 0.7);
      backdrop-filter: blur(10px);
      z-index: 150;
      align-items: center;
      justify-content: center;
    }
    .quick-menu-box {
      background: var(--bg-card);
      border: 1px solid var(--border-dark);
      border-radius: 14px;
      width: 320px;
      max-width: 90vw;
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 12px;
      box-shadow: 0 10px 30px rgba(0,0,0,0.8);
    }
    .quick-menu-box h3 {
      font-size: 16px;
      color: #fff;
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .menu-btn {
      background: #1e293b;
      border: 1px solid #334155;
      color: #fff;
      border-radius: 8px;
      padding: 10px 14px;
      font-size: 13px;
      font-weight: 600;
      display: flex;
      align-items: center;
      gap: 10px;
      cursor: pointer;
    }
    .menu-btn.active {
      border-color: var(--accent-cyan);
      background: rgba(0, 210, 255, 0.15);
      color: var(--accent-cyan);
    }

    /* In-Game Touch Layer */
    #touch-layer {
      position: absolute;
      top: 0; left: 0; width: 100%; height: 100%;
      z-index: 50;
      pointer-events: auto;
    }
    .touch-btn {
      position: absolute;
      border-radius: 50%;
      background: var(--touch-btn-bg);
      border: 2px solid var(--touch-btn-border);
      color: #fff;
      font-weight: 700;
      font-size: 14px;
      display: flex;
      align-items: center;
      justify-content: center;
      box-shadow: 0 4px 10px rgba(0,0,0,0.5);
      transform: translate(-50%, -50%);
      touch-action: none;
    }
    .touch-btn.active {
      background: var(--touch-btn-active);
      color: #000;
      border-color: #fff;
    }
    .touch-btn.toggle-on {
      border-color: var(--accent-green);
      background: rgba(56, 239, 125, 0.4);
      color: #fff;
    }

    /* Virtual Analog Stick */
    .touch-stick-base {
      position: absolute;
      border-radius: 50%;
      background: rgba(21, 27, 36, 0.4);
      border: 2px dashed rgba(0, 210, 255, 0.5);
      transform: translate(-50%, -50%);
      pointer-events: none;
    }
    .touch-stick-nub {
      position: absolute;
      width: 50px;
      height: 50px;
      border-radius: 50%;
      background: linear-gradient(135deg, rgba(0, 210, 255, 0.8), rgba(56, 239, 125, 0.8));
      transform: translate(-50%, -50%);
      box-shadow: 0 0 15px var(--accent-glow);
      pointer-events: none;
    }

    /* Trackpad swipe area */
    .touch-trackpad {
      position: absolute;
      border-radius: 12px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px dashed rgba(255, 255, 255, 0.15);
      transform: translate(-50%, -50%);
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 11px;
      color: rgba(255,255,255,0.4);
      letter-spacing: 1px;
      pointer-events: none;
    }

    /* Touch Studio Editor Bar */
    #editor-bar {
      display: none;
      position: absolute;
      top: 0; left: 0; width: 100%;
      height: 48px;
      background: rgba(14, 20, 27, 0.95);
      backdrop-filter: blur(10px);
      border-bottom: 2px solid var(--accent-cyan);
      z-index: 200;
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 16px;
    }
    .edit-mode-highlight {
      outline: 2px solid var(--accent-cyan) !important;
      animation: pulse 1s infinite alternate;
    }

    /* QWERTY Drawer */
    #qwerty-drawer {
      display: none;
      position: absolute;
      bottom: 0; left: 0; width: 100%;
      background: rgba(14, 20, 27, 0.95);
      backdrop-filter: blur(12px);
      border-top: 1px solid var(--border-dark);
      padding: 8px 4px;
      z-index: 120;
    }
    .qwerty-row {
      display: flex;
      justify-content: center;
      gap: 4px;
      margin-bottom: 4px;
    }
    .q-key {
      flex: 1;
      max-width: 38px;
      height: 42px;
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 5px;
      color: #fff;
      font-size: 13px;
      font-weight: 600;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .q-key.wide { flex: 1.5; max-width: 60px; font-size: 11px; }
  </style>
</head>
<body>

  <!-- GameHub Library View -->
  <div id="hub-view">
    <header class="hub-header">
      <div class="brand">SUO <span>LINK</span></div>
      <div class="badge-status">
        <div class="badge-dot"></div>
        <span id="host-name">PC CONNECTED</span>
      </div>
    </header>

    <div class="hub-body">
      <div class="search-bar">
        <input type="text" class="search-input" id="search-box" placeholder="Search installed games...">
      </div>

      <div class="section-title">
        <span>Installed Games</span>
        <span id="game-count">0 Titles</span>
      </div>

      <div class="game-grid" id="games-container">
        <!-- Injected dynamically -->
      </div>
    </div>
  </div>

  <!-- In-Game Remote Play Stream View -->
  <div id="stream-view">
    <img id="stream-img" alt="Stream Canvas">

    <!-- Floating HUD Pill -->
    <div class="hud-pill" id="hud-toggle">
      <span>SUO HUD</span>
      <div class="badge-dot"></div>
    </div>

    <!-- Touch Elements Overlay -->
    <div id="touch-layer"></div>

    <!-- QWERTY Keyboard Drawer -->
    <div id="qwerty-drawer">
      <div class="qwerty-row" id="q-row-1"></div>
      <div class="qwerty-row" id="q-row-2"></div>
      <div class="qwerty-row" id="q-row-3"></div>
      <div class="qwerty-row" id="q-row-4"></div>
    </div>
  </div>

  <!-- Quick Menu Modal -->
  <div class="quick-menu-modal" id="quick-menu">
    <div class="quick-menu-box">
      <h3>SUO Link Quick Menu <span style="cursor:pointer;" id="menu-close">&times;</span></h3>
      <button class="menu-btn" id="btn-edit-controls">
        &#9881; Customize Touch Controls
      </button>
      <button class="menu-btn" id="btn-toggle-qwerty">
        &#9000; Toggle Keyboard Drawer
      </button>
      <button class="menu-btn" id="btn-toggle-hide-touch">
        &#128065; Hide / Show Touch Buttons
      </button>
      <button class="menu-btn" id="btn-reconnect-stream">
        &#128257; Refresh Stream Viewport
      </button>
      <button class="menu-btn" style="color:#ef4444;" id="btn-exit-hub">
        &#8592; Exit to GameHub Library
      </button>
    </div>
  </div>

  <script>
    const CONFIG = {
      token: "{{token}}",
      host: "{{hostIp}}",
      port: {{port}}
    };

    let ws = null;
    let currentGameAppId = "default";
    let activeProfile = null;
    let isEditMode = false;
    let touchControlsVisible = true;
    const touches = new Map(); // identifier -> touch data

    // --- WebSocket Input Relay ---
    function connectInputWs() {
      const wsProto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
      const wsUrl = `${wsProto}//${window.location.host}/ws/input?auth=${CONFIG.token}`;
      ws = new WebSocket(wsUrl);
      ws.onopen = () => console.log("[SUO Link] Input WebSocket connected");
      ws.onclose = () => setTimeout(connectInputWs, 2000);
    }
    connectInputWs();

    function sendInput(data) {
      if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(data));
      }
    }

    function haptic() {
      if (navigator.vibrate) navigator.vibrate(15);
    }

    // --- GameHub Library Loading ---
    async function loadGames() {
      try {
        const res = await fetch('/api/games');
        const games = await res.json();
        document.getElementById('game-count').innerText = `${games.length} Titles`;
        renderGames(games);
      } catch (err) {
        console.error("Failed to load games:", err);
      }
    }

    function renderGames(games) {
      const container = document.getElementById('games-container');
      container.innerHTML = '';
      games.forEach(g => {
        const card = document.createElement('div');
        card.className = 'game-card';
        card.innerHTML = `
          <img src="${g.HeaderUrl}" onerror="this.src='https://placehold.co/300x150/151b24/00d2ff?text=${encodeURIComponent(g.Name)}'">
          <div class="game-card-info">
            <div class="game-title" title="${g.Name}">${g.Name}</div>
            <div class="game-source">${g.Source}</div>
            <button class="btn-play-card" data-appid="${g.AppId}">LAUNCH & PLAY</button>
          </div>
        `;
        card.querySelector('.btn-play-card').addEventListener('click', (e) => {
          e.stopPropagation();
          launchAndPlay(g.AppId);
        });
        card.addEventListener('click', () => launchAndPlay(g.AppId));
        container.appendChild(card);
      });
    }

    document.getElementById('search-box').addEventListener('input', (e) => {
      const term = e.target.value.toLowerCase();
      document.querySelectorAll('.game-card').forEach(card => {
        const title = card.querySelector('.game-title').innerText.toLowerCase();
        card.style.display = title.includes(term) ? 'flex' : 'none';
      });
    });

    // --- Launch & Start Stream ---
    async function launchAndPlay(appId) {
      currentGameAppId = appId;
      // Trigger launch on PC
      fetch('/api/launch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ appId: appId })
      }).catch(console.error);

      // Load Layout Profile for this game
      await loadProfile(appId);

      // Switch to stream view
      document.getElementById('hub-view').style.display = 'none';
      const streamView = document.getElementById('stream-view');
      streamView.style.display = 'block';
      document.getElementById('stream-img').src = `/api/stream?auth=${CONFIG.token}&t=${Date.now()}`;
      buildTouchControls();

      // Request fullscreen
      if (document.documentElement.requestFullscreen) {
        document.documentElement.requestFullscreen().catch(() => {});
      }
    }

    // --- Layout Profile Engine ---
    async function loadProfile(appId) {
      try {
        const res = await fetch(`/api/layouts/${appId}`);
        activeProfile = await res.json();
      } catch (e) {
        console.error("Layout load failed:", e);
      }
    }

    async function saveProfile() {
      if (!activeProfile) return;
      try {
        await fetch(`/api/layouts/${currentGameAppId}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(activeProfile)
        });
      } catch (e) {
        console.error("Save failed:", e);
      }
    }

    // --- In-Game Touch Controls Renderer ---
    function buildTouchControls() {
      const layer = document.getElementById('touch-layer');
      layer.innerHTML = '';
      if (!activeProfile || !activeProfile.Elements) return;

      activeProfile.Elements.forEach(elem => {
        if (elem.Type === 'button') {
          const btn = document.createElement('div');
          btn.className = 'touch-btn';
          btn.id = `elem_${elem.Id}`;
          btn.innerText = elem.Label;
          btn.style.left = `${elem.X * 100}%`;
          btn.style.top = `${elem.Y * 100}%`;
          btn.style.width = `${elem.Width}px`;
          btn.style.height = `${elem.Height}px`;
          btn.style.opacity = elem.Opacity;
          btn.dataset.key = elem.Key;
          btn.dataset.behavior = elem.Behavior;
          btn.dataset.id = elem.Id;
          layer.appendChild(btn);
        } else if (elem.Type === 'stick') {
          const base = document.createElement('div');
          base.className = 'touch-stick-base';
          base.id = `elem_${elem.Id}`;
          base.style.left = `${elem.X * 100}%`;
          base.style.top = `${elem.Y * 100}%`;
          base.style.width = `${elem.Width}px`;
          base.style.height = `${elem.Height}px`;
          base.style.opacity = elem.Opacity;

          const nub = document.createElement('div');
          nub.className = 'touch-stick-nub';
          nub.id = `nub_${elem.Id}`;
          nub.style.left = `${elem.X * 100}%`;
          nub.style.top = `${elem.Y * 100}%`;
          layer.appendChild(base);
          layer.appendChild(nub);
        } else if (elem.Type === 'trackpad') {
          const pad = document.createElement('div');
          pad.className = 'touch-trackpad';
          pad.id = `elem_${elem.Id}`;
          pad.innerText = elem.Label;
          pad.style.left = `${elem.X * 100}%`;
          pad.style.top = `${elem.Y * 100}%`;
          pad.style.width = `${elem.Width}px`;
          pad.style.height = `${elem.Height}px`;
          pad.style.opacity = elem.Opacity;
          layer.appendChild(pad);
        }
      });
    }

    // --- Multi-Touch Input Handling ---
    const touchLayer = document.getElementById('touch-layer');
    touchLayer.addEventListener('touchstart', handleTouchEvents, { passive: false });
    touchLayer.addEventListener('touchmove', handleTouchEvents, { passive: false });
    touchLayer.addEventListener('touchend', handleTouchEvents, { passive: false });
    touchLayer.addEventListener('touchcancel', handleTouchEvents, { passive: false });

    function handleTouchEvents(e) {
      e.preventDefault();
      const rect = touchLayer.getBoundingClientRect();

      for (let i = 0; i < e.changedTouches.length; i++) {
        const t = e.changedTouches[i];
        const x = t.clientX;
        const y = t.clientY;

        if (e.type === 'touchstart') {
          // Find which element was touched
          const hit = document.elementFromPoint(x, y);
          if (hit && hit.classList.contains('touch-btn')) {
            touches.set(t.identifier, { type: 'btn', el: hit });
            haptic();
            hit.classList.add('active');
            const key = hit.dataset.key;
            if (key.startsWith('MOUSE_')) {
              const btnNum = key === 'MOUSE_LEFT' ? 0 : (key === 'MOUSE_RIGHT' ? 1 : 2);
              sendInput({ type: 'mouse_down', button: btnNum });
            } else {
              sendInput({ type: 'key_down', key: key });
            }
          } else {
            // Check if touched left side (WASD stick) or right side (trackpad look)
            const normX = (x - rect.left) / rect.width;
            if (normX < 0.4) {
              // Left virtual stick
              touches.set(t.identifier, { type: 'stick', startX: x, startY: y });
            } else {
              // Right swipe trackpad
              touches.set(t.identifier, { type: 'look', lastX: x, lastY: y });
            }
          }
        } else if (e.type === 'touchmove') {
          const item = touches.get(t.identifier);
          if (!item) continue;
          if (item.type === 'look') {
            const dx = Math.round((x - item.lastX) * 1.8);
            const dy = Math.round((y - item.lastY) * 1.8);
            item.lastX = x;
            item.lastY = y;
            if (dx !== 0 || dy !== 0) {
              sendInput({ type: 'mouse_move_relative', dx: dx, dy: dy });
            }
          } else if (item.type === 'stick') {
            const dx = x - item.startX;
            const dy = y - item.startY;
            const dist = Math.hypot(dx, dy);
            const maxR = 50;
            const normDist = Math.min(dist / maxR, 1.0);
            const angle = Math.atan2(dy, dx);
            const vx = Math.cos(angle) * normDist;
            const vy = Math.sin(angle) * normDist;

            // Move nub
            const nub = document.querySelector('.touch-stick-nub');
            if (nub) {
              nub.style.transform = `translate(calc(-50% + ${vx * 40}px), calc(-50% + ${vy * 40}px))`;
            }
            sendInput({ type: 'stick_vector', x: vx, y: vy });
          }
        } else if (e.type === 'touchend' || e.type === 'touchcancel') {
          const item = touches.get(t.identifier);
          if (item) {
            if (item.type === 'btn') {
              item.el.classList.remove('active');
              const key = item.el.dataset.key;
              if (key.startsWith('MOUSE_')) {
                const btnNum = key === 'MOUSE_LEFT' ? 0 : (key === 'MOUSE_RIGHT' ? 1 : 2);
                sendInput({ type: 'mouse_up', button: btnNum });
              } else {
                sendInput({ type: 'key_up', key: key });
              }
            } else if (item.type === 'stick') {
              sendInput({ type: 'stick_vector', x: 0, y: 0 });
              const nub = document.querySelector('.touch-stick-nub');
              if (nub) nub.style.transform = 'translate(-50%, -50%)';
            }
            touches.delete(t.identifier);
          }
        }
      }
    }

    // --- Floating GameHub HUD & Quick Menu ---
    const hudPill = document.getElementById('hud-toggle');
    const quickMenu = document.getElementById('quick-menu');
    hudPill.addEventListener('click', () => quickMenu.style.display = 'flex');
    document.getElementById('menu-close').addEventListener('click', () => quickMenu.style.display = 'none');

    document.getElementById('btn-exit-hub').addEventListener('click', () => {
      quickMenu.style.display = 'none';
      document.getElementById('stream-view').style.display = 'none';
      document.getElementById('stream-img').src = '';
      document.getElementById('hub-view').style.display = 'block';
    });

    document.getElementById('btn-toggle-hide-touch').addEventListener('click', () => {
      touchControlsVisible = !touchControlsVisible;
      document.getElementById('touch-layer').style.display = touchControlsVisible ? 'block' : 'none';
      quickMenu.style.display = 'none';
    });

    document.getElementById('btn-reconnect-stream').addEventListener('click', () => {
      document.getElementById('stream-img').src = `/api/stream?auth=${CONFIG.token}&t=${Date.now()}`;
      quickMenu.style.display = 'none';
    });

    document.getElementById('btn-toggle-qwerty').addEventListener('click', () => {
      const drawer = document.getElementById('qwerty-drawer');
      drawer.style.display = drawer.style.display === 'block' ? 'none' : 'block';
      quickMenu.style.display = 'none';
    });

    // Build QWERTY Drawer Keys
    const QWERTY_ROWS = [
      ["1","2","3","4","5","6","7","8","9","0"],
      ["Q","W","E","R","T","Y","U","I","O","P"],
      ["A","S","D","F","G","H","J","K","L","ENTER"],
      ["SHIFT","Z","X","C","V","B","N","M","SPACE","ESC"]
    ];
    QWERTY_ROWS.forEach((row, idx) => {
      const container = document.getElementById(`q-row-${idx+1}`);
      row.forEach(k => {
        const el = document.createElement('div');
        el.className = 'q-key' + (k.length > 1 ? ' wide' : '');
        el.innerText = k;
        el.addEventListener('touchstart', (e) => {
          e.preventDefault();
          haptic();
          sendInput({ type: 'key_down', key: k });
        });
        el.addEventListener('touchend', (e) => {
          e.preventDefault();
          sendInput({ type: 'key_up', key: k });
        });
        container.appendChild(el);
      });
    });

    // Initialize
    loadGames();
  </script>
</body>
</html>
""";
    }
}
