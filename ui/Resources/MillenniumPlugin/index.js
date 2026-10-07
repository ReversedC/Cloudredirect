const MILLENNIUM_IS_CLIENT_MODULE = true;
const pluginName = "CloudRedirect";

function InitializePlugins() {
    var _a;
    (_a = (window.PLUGIN_LIST || (window.PLUGIN_LIST = {})))[pluginName] || (_a[pluginName] = {});
    window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS || (window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS = {});
}
InitializePlugins();

const __call_server_method__ = (methodName, kwargs) => {
    try {
        if (typeof Millennium !== 'undefined' && Millennium.callServerMethod) {
            return Millennium.callServerMethod(pluginName, methodName, kwargs || {});
        }
    } catch (e) {
        console.warn('[CloudRedirect] CallServerMethod error:', e);
    }
    return Promise.resolve(null);
};

// Returns all active Steam documents (SharedJSContext, main desktop window SP Desktop_uid0, and popups)
function getAllSteamDocuments() {
    const docs = [];
    if (typeof document !== 'undefined' && document && document.body) {
        docs.push(document);
    }

    try {
        if (typeof g_PopupManager !== 'undefined' && g_PopupManager) {
            if (typeof g_PopupManager.GetPopups === 'function') {
                const popups = g_PopupManager.GetPopups();
                if (popups) {
                    for (const p of popups) {
                        const d = p?.window?.document || p?.m_popup?.window?.document || p?.m_popup?.document;
                        if (d && d.body && !docs.includes(d)) docs.push(d);
                    }
                }
            }
            if (typeof g_PopupManager.GetExistingPopup === 'function') {
                const sp = g_PopupManager.GetExistingPopup("SP Desktop_uid0");
                const d = sp?.window?.document || sp?.m_popup?.window?.document || sp?.m_popup?.document;
                if (d && d.body && !docs.includes(d)) docs.push(d);
            }
            if (g_PopupManager.m_mapPopups && g_PopupManager.m_mapPopups.data_) {
                g_PopupManager.m_mapPopups.data_.forEach(entry => {
                    const val = entry?.value_ || entry;
                    const d = val?.m_popup?.window?.document || val?.window?.document || val?.m_popup?.document;
                    if (d && d.body && !docs.includes(d)) docs.push(d);
                });
            }
        }
    } catch (e) {
        console.warn('[CloudRedirect] Error retrieving popup documents:', e);
    }

    try {
        if (typeof window !== 'undefined' && window.PLUGIN_LIST && window.PLUGIN_LIST.core && window.PLUGIN_LIST.core.mainWindow) {
            const d = window.PLUGIN_LIST.core.mainWindow.document;
            if (d && d.body && !docs.includes(d)) docs.push(d);
        }
    } catch (e) { }

    return docs;
}

var PluginEntryPointMain = function () {
    var millennium_main = (function (exports, client, reactDom, React) {
        'use strict';

        const cloudSvg = `<svg class="cr-cloud-icon-svg" viewBox="0 0 24 24"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z"/></svg>`;
        const reloadSvg = `<svg class="cr-icon-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg>`;
        const restartSvg = `<svg class="cr-icon-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M18.36 6.64a9 9 0 1 1-12.73 0M12 2v10"/></svg>`;

        function launchApp(doc) {
            __call_server_method__("launch_cloudredirect", {});
            try {
                const d = doc || document;
                const link = d.createElement('a');
                link.href = 'cloudredirect://open';
                d.body.appendChild(link);
                link.click();
                link.remove();
            } catch (e) { }
        }

        function ensureStyles(doc) {
            if (!doc || !doc.head) return;
            const existing = doc.getElementById('cr-millennium-styles');
            if (existing) {
                existing.remove();
            }
            const style = doc.createElement('style');
            style.id = 'cr-millennium-styles';
            style.textContent = `
                /* Never show legacy titlebar buttons in window controls */
                #cloudredirect-header-btn,
                .cr-nav-btn {
                    display: none !important;
                }

                /* Suppress any clipped dropdown containers */
                .cr-dropdown-menu {
                    display: none !important;
                }

                /* SuperNav Top Header Tab (STORE / LIBRARY / COMMUNITY / USER / CLOUDREDIRECT) */
                #cloudredirect-supernav-item,
                .cr-supernav-menu {
                    cursor: pointer !important;
                    user-select: none !important;
                    position: relative !important;
                    font-size: 18px;
                    font-family: "Motiva Sans", "Twemoji", "Noto Sans", Helvetica, sans-serif;
                    font-weight: 500;
                    text-transform: uppercase;
                    padding: 0 10px;
                    height: inherit;
                }

                .cr-supernav-btn {
                    cursor: pointer !important;
                    white-space: nowrap !important;
                    color: #dcdedf;
                    transition: color 0.15s ease-out, text-shadow 0.15s ease-out !important;
                }

                #cloudredirect-supernav-item:hover .cr-supernav-btn,
                .cr-supernav-menu:hover .cr-supernav-btn {
                    color: #ffffff !important;
                    text-shadow: 0 0 10px rgba(255, 255, 255, 0.45) !important;
                }

                .cr-supernav-label {
                    font-family: inherit !important;
                    font-size: inherit !important;
                    font-weight: inherit !important;
                    line-height: inherit !important;
                    letter-spacing: inherit !important;
                    text-transform: uppercase !important;
                }

                .cr-supernav-dot {
                    display: inline-block !important;
                    width: 6px !important;
                    height: 6px !important;
                    background: #a4d007 !important;
                    border-radius: 50% !important;
                    box-shadow: 0 0 6px #a4d007 !important;
                    margin-left: 6px !important;
                    vertical-align: middle !important;
                    position: relative !important;
                    top: -1px !important;
                    line-height: normal !important;
                }

                /* Bottom Bar Actions Group (Next to Add Game / Steam Unlock) */
                .cr-bottom-group {
                    display: inline-flex !important;
                    align-items: center !important;
                    gap: 6px !important;
                    margin-left: 8px !important;
                    flex: 0 0 auto !important;
                    z-index: 1000 !important;
                }

                .cr-bottom-action-btn {
                    display: inline-flex !important;
                    align-items: center !important;
                    gap: 6px !important;
                    height: 26px !important;
                    padding: 0 10px !important;
                    background: rgba(24, 38, 54, 0.75) !important;
                    border: 1px solid rgba(102, 192, 244, 0.25) !important;
                    border-radius: 3px !important;
                    color: #c6d4df !important;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important;
                    font-size: 12px !important;
                    font-weight: 600 !important;
                    cursor: pointer !important;
                    user-select: none !important;
                    white-space: nowrap !important;
                    transition: all 0.15s ease !important;
                    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.3) !important;
                }
                .cr-bottom-action-btn:hover {
                    background: rgba(42, 71, 94, 0.95) !important;
                    border-color: #66c0f4 !important;
                    color: #ffffff !important;
                    box-shadow: 0 0 8px rgba(102, 192, 244, 0.4) !important;
                }
                .cr-bottom-action-btn:active {
                    background: #142230 !important;
                    transform: translateY(1px) !important;
                }

                .cr-bottom-cr-btn {
                    background: rgba(20, 34, 48, 0.85) !important;
                    border-color: #274563 !important;
                }

                .cr-icon-svg {
                    width: 13px !important;
                    height: 13px !important;
                    stroke: currentColor !important;
                    flex-shrink: 0 !important;
                    vertical-align: middle !important;
                }

                .cr-status-dot {
                    width: 7px;
                    height: 7px;
                    background: #a4d007;
                    border-radius: 50%;
                    box-shadow: 0 0 6px #a4d007;
                    display: inline-block;
                    flex-shrink: 0;
                }

                .cr-cloud-icon-svg {
                    width: 14px;
                    height: 14px;
                    fill: currentColor;
                    flex-shrink: 0;
                }

                /* Game Detail Page Badge */
                .cr-game-badge {
                    display: inline-flex;
                    align-items: center;
                    gap: 6px;
                    background: rgba(22, 34, 46, 0.85);
                    border: 1px solid #2d4c6b;
                    border-radius: 14px;
                    padding: 4px 10px;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                    font-size: 11px;
                    font-weight: 600;
                    color: #c6d4df;
                    cursor: pointer;
                    transition: all 0.2s ease;
                    user-select: none;
                    margin-left: 10px;
                    vertical-align: middle;
                }
                .cr-game-badge:hover {
                    background: #233b52;
                    border-color: #66c0f4;
                    color: #ffffff;
                    box-shadow: 0 0 10px rgba(102, 192, 244, 0.4);
                }
                .cr-game-badge-check {
                    color: #a4d007;
                    font-weight: bold;
                }
            `;
            doc.head.appendChild(style);
        }

        // Helper to locate the exact SuperNav tab bar container in doc (Multi-language & URL resilient)
        function findSuperNavInfo(doc) {
            const allEls = doc.querySelectorAll('a, div, span, button');

            for (const el of allEls) {
                const href = (el.getAttribute && el.getAttribute('href')) || '';
                const isNavUrl = href.includes('store.steampowered.com') || href.includes('steamcommunity.com');
                const t = (el.textContent || '').trim().toUpperCase();
                const isNavText = t === 'COMMUNITY' || t === 'STORE' || t === 'LIBRARY' ||
                                  t === 'TOKO' || t === 'KOMUNITAS' || t === 'PERPUSTAKAAN' ||
                                  t === 'KEDAI' || t === 'KOMUNITI' ||
                                  t === 'TIENDA' || t === 'COMUNIDAD' || t === 'BIBLIOTECA';

                if (isNavUrl || isNavText) {
                    // Traverse up within 5 levels to locate the SuperNav container
                    let curr = el;
                    let depth = 0;
                    while (curr && curr !== doc.body && depth < 5) {
                        const parent = curr.parentElement;
                        if (!parent) break;
                        const pText = (parent.textContent || '').toUpperCase();
                        const hasStore = (parent.querySelector && parent.querySelector('a[href*="store.steampowered.com"]')) ||
                                         pText.includes('STORE') || pText.includes('TOKO') || pText.includes('KEDAI') || pText.includes('TIENDA');
                        const hasCommunity = (parent.querySelector && parent.querySelector('a[href*="steamcommunity.com"]')) ||
                                             pText.includes('COMMUNITY') || pText.includes('KOMUNITAS') || pText.includes('KOMUNITI') || pText.includes('COMUNIDAD');

                        if (hasStore && hasCommunity) {
                            const children = Array.from(parent.children);
                            let sampleTab = null;
                            let lastNavTab = null;

                            for (const child of children) {
                                const cText = (child.textContent || '').trim().toUpperCase();
                                const cTag = child.tagName;
                                // Ignore search inputs, window control buttons, or hidden spacers
                                if (child.querySelector && (child.querySelector('input') || child.querySelector('svg[class*="close"]'))) {
                                    continue;
                                }
                                if (cText.length > 0 && !cText.includes('HTTP') && !cText.includes('🔍') && !cText.includes('SEARCH') && !cText.includes('✕')) {
                                    if (!sampleTab) sampleTab = child;
                                    lastNavTab = child;
                                }
                            }

                            if (!sampleTab) sampleTab = lastNavTab;

                            if (parent && lastNavTab) {
                                return {
                                    container: parent,
                                    sampleTab: sampleTab,
                                    insertAfter: lastNavTab
                                };
                            }
                        }
                        curr = parent;
                        depth++;
                    }
                }
            }

            return null;
        }

        function showSuperNavMenu(e, doc) {
            const oldMenu = doc.getElementById('cr-supernav-dropdown');
            if (oldMenu) oldMenu.remove();

            const menu = doc.createElement('div');
            menu.id = 'cr-supernav-dropdown';
            menu.style.position = 'fixed';
            menu.style.left = `${Math.min(e.clientX, (doc.defaultView?.innerWidth || 1200) - 230)}px`;
            menu.style.top = `${e.clientY + 8}px`;
            menu.style.background = '#1b2838';
            menu.style.border = '1px solid #3d4450';
            menu.style.borderRadius = '4px';
            menu.style.boxShadow = '0 8px 16px rgba(0, 0, 0, 0.6)';
            menu.style.zIndex = '999999';
            menu.style.minWidth = '210px';
            menu.style.padding = '6px 0';
            menu.style.color = '#c6d4df';
            menu.style.fontFamily = '"Motiva Sans", sans-serif';
            menu.style.fontSize = '13px';

            const items = [
                {
                    label: '🚀 Open CloudRedirect App',
                    action: () => launchApp(doc)
                },
                {
                    label: '💾 Trigger Cloud Backup Now',
                    action: () => {
                        __call_server_method__("trigger_backup", {});
                        try {
                            const link = doc.createElement('a');
                            link.href = 'cloudredirect://backup';
                            doc.body.appendChild(link);
                            link.click();
                            link.remove();
                        } catch (err) { }
                    }
                },
                { separator: true },
                {
                    label: '🔃 Fast Reload Steam UI',
                    action: () => {
                        try {
                            const win = doc.defaultView || window;
                            if (win.SteamClient?.Browser?.RestartJSContext) {
                                win.SteamClient.Browser.RestartJSContext();
                                return;
                            }
                            if (window.SteamClient?.Browser?.RestartJSContext) {
                                window.SteamClient.Browser.RestartJSContext();
                                return;
                            }
                        } catch (err) { }
                    }
                },
                {
                    label: '🔄 Quick Restart Steam',
                    action: () => {
                        try {
                            const win = doc.defaultView || window;
                            if (win.SteamClient?.User?.StartRestart) {
                                win.SteamClient.User.StartRestart(false);
                                return;
                            }
                            if (window.SteamClient?.User?.StartRestart) {
                                window.SteamClient.User.StartRestart(false);
                                return;
                            }
                        } catch (err) { }
                        __call_server_method__("restart_steam", {});
                    }
                }
            ];

            items.forEach(item => {
                if (item.separator) {
                    const sep = doc.createElement('div');
                    sep.style.height = '1px';
                    sep.style.background = '#2a3f5a';
                    sep.style.margin = '4px 0';
                    menu.appendChild(sep);
                    return;
                }
                const btn = doc.createElement('div');
                btn.textContent = item.label;
                btn.style.padding = '8px 16px';
                btn.style.cursor = 'pointer';
                btn.style.transition = 'background 0.15s, color 0.15s';
                btn.onmouseenter = () => {
                    btn.style.background = '#2a475e';
                    btn.style.color = '#ffffff';
                };
                btn.onmouseleave = () => {
                    btn.style.background = 'transparent';
                    btn.style.color = '#c6d4df';
                };
                btn.onclick = (ev) => {
                    ev.stopPropagation();
                    menu.remove();
                    item.action();
                };
                menu.appendChild(btn);
            });

            const closeHandler = () => {
                menu.remove();
                doc.removeEventListener('click', closeHandler);
            };
            setTimeout(() => doc.addEventListener('click', closeHandler), 10);
            doc.body.appendChild(menu);
        }

        // Injects tab right into STORE / LIBRARY / COMMUNITY / USER / CLOUDREDIRECT row
        function injectSuperNavTab(doc) {
            if (!doc || !doc.body) return;

            const existing = doc.getElementById('cloudredirect-supernav-item');
            if (existing) {
                if (existing.parentNode) return;
                existing.remove();
            }

            const navInfo = findSuperNavInfo(doc);
            if (!navInfo || !navInfo.container || !navInfo.insertAfter) return;

            const { container, sampleTab, insertAfter } = navInfo;

            // Create the CloudRedirect SuperNav item
            const navItem = doc.createElement('div');
            navItem.id = 'cloudredirect-supernav-item';
            navItem.className = (sampleTab.className || '').replace(/\bactive\b/gi, '').trim() + ' cr-supernav-menu';
            navItem.setAttribute('role', 'button');
            navItem.setAttribute('tabindex', '0');
            navItem.title = 'CloudRedirect (Left click: Open | Right click: Menu / Reload / Restart)';

            // Find child button / inner element if present in sampleTab
            const sampleInner = sampleTab.querySelector('div, a, span') || sampleTab;
            const innerBtn = doc.createElement('div');
            innerBtn.className = (sampleInner.className || '').replace(/\bactive\b/gi, '').trim() + ' cr-supernav-btn';
            innerBtn.innerHTML = `
                <span class="cr-supernav-label">CLOUDREDIRECT</span>
                <span class="cr-supernav-dot" title="Save Protection Active"></span>
            `;

            // Inherit computed typography dynamically without forcing disruptive inline height/display
            try {
                const sampleTarget = sampleInner || sampleTab;
                const win = doc.defaultView || window;
                if (win && sampleTarget) {
                    const computed = win.getComputedStyle(sampleTarget);
                    if (computed) {
                        if (computed.fontSize) {
                            innerBtn.style.fontSize = computed.fontSize;
                        }
                        if (computed.fontWeight) {
                            innerBtn.style.fontWeight = computed.fontWeight;
                        }
                        if (computed.fontFamily) {
                            innerBtn.style.fontFamily = computed.fontFamily;
                        }
                        if (computed.lineHeight && computed.lineHeight !== 'normal') {
                            innerBtn.style.lineHeight = computed.lineHeight;
                        }
                    }
                }
            } catch (e) { }

            navItem.appendChild(innerBtn);

            // Directly launch CloudRedirect application when tab is left-clicked
            navItem.onclick = (e) => {
                e.stopPropagation();
                e.preventDefault();
                launchApp(doc);
            };
            // Right-click opens context menu with Fast Reload & Quick Restart
            navItem.oncontextmenu = (e) => {
                e.preventDefault();
                e.stopPropagation();
                showSuperNavMenu(e, doc);
            };
            navItem.onkeydown = (e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    launchApp(doc);
                }
            };

            // Insert directly into the row after the last tab (after username)
            if (insertAfter.nextSibling) {
                container.insertBefore(navItem, insertAfter.nextSibling);
            } else {
                container.appendChild(navItem);
            }
        }

        // Find anchor button in bottom bar (prefers after Steam Unlock, falls back to after Add a Game)
        function findBottomBarAnchor(doc) {
            const unlockBtn = doc.getElementById('onegamers-activate-btn');
            if (unlockBtn && unlockBtn.parentElement) return unlockBtn;

            const allButtons = doc.querySelectorAll('button, a, [role="button"], div, span');
            for (const el of allButtons) {
                const t = (el.textContent || '').trim().toLowerCase();
                if (t.includes('steam unlock') && (t.includes('✓') || t.includes('✔') || el.dataset?.activated)) {
                    const btn = el.closest('button, [role="button"], div');
                    if (btn && btn.parentElement) return btn;
                }
            }

            for (const el of allButtons) {
                const t = (el.textContent || '').trim().toLowerCase();
                if ((t === 'add a game' || t === '+ add a game') && el.children.length <= 2) {
                    const btn = el.closest('button, a, [role="button"]') || el;
                    if (btn && btn.parentElement) return btn;
                }
            }

            const addGameCandidates = doc.querySelectorAll('button[class*="addgamebutton_"], div[class*="addgamebutton_"], [class*="AddGameButton"]');
            for (const el of addGameCandidates) {
                if (el.parentElement) return el;
            }

            return null;
        }

        // 1. Inject Buttons in Bottom Bar (Reload UI, Restart Steam, CloudRedirect)
        function injectBottomBarButtons(doc) {
            if (!doc || !doc.body) return;
            if (doc.getElementById('cr-bottom-bar-group')) return;

            const anchor = findBottomBarAnchor(doc);
            if (!anchor || !anchor.parentElement) return;

            const group = doc.createElement('div');
            group.id = 'cr-bottom-bar-group';
            group.className = 'cr-bottom-group';

            // 1. Reload UI button (uses native SteamClient.Browser.RestartJSContext to prevent blank screen)
            const reloadBtn = doc.createElement('button');
            reloadBtn.id = 'cr-reload-ui-btn';
            reloadBtn.className = 'cr-bottom-action-btn';
            reloadBtn.title = 'Fast Reload Steam Web UI';
            reloadBtn.innerHTML = `
                ${reloadSvg}
                <span>Reload UI</span>
            `;
            reloadBtn.onclick = (e) => {
                e.stopPropagation();
                e.preventDefault();
                try {
                    const win = doc.defaultView || window;
                    if (win.SteamClient?.Browser?.RestartJSContext) {
                        win.SteamClient.Browser.RestartJSContext();
                        return;
                    }
                    if (window.SteamClient?.Browser?.RestartJSContext) {
                        window.SteamClient.Browser.RestartJSContext();
                        return;
                    }
                    if (win.SteamClient?.User?.StartRestart) {
                        win.SteamClient.User.StartRestart(false);
                        return;
                    }
                    if (window.SteamClient?.User?.StartRestart) {
                        window.SteamClient.User.StartRestart(false);
                        return;
                    }
                } catch (err) {
                    console.warn('[CloudRedirect] RestartJSContext error:', err);
                }
            };

            // 2. Restart Steam button
            const restartBtn = doc.createElement('button');
            restartBtn.id = 'cr-restart-steam-btn';
            restartBtn.className = 'cr-bottom-action-btn';
            restartBtn.title = 'Quick Restart Steam Client';
            restartBtn.innerHTML = `
                ${restartSvg}
                <span>Restart Steam</span>
            `;
            restartBtn.onclick = (e) => {
                e.stopPropagation();
                e.preventDefault();
                try {
                    const win = doc.defaultView || window;
                    if (win.SteamClient?.User?.StartRestart) {
                        win.SteamClient.User.StartRestart(false);
                        return;
                    }
                    if (window.SteamClient?.User?.StartRestart) {
                        window.SteamClient.User.StartRestart(false);
                        return;
                    }
                } catch (err) { }
                __call_server_method__("restart_steam", {});
            };

            // Clean up any old bottom bar CloudRedirect button
            const oldCrBtn = doc.getElementById('cloudredirect-bottom-btn');
            if (oldCrBtn) oldCrBtn.remove();

            group.appendChild(reloadBtn);
            group.appendChild(restartBtn);

            if (anchor.nextSibling) {
                anchor.parentElement.insertBefore(group, anchor.nextSibling);
            } else {
                anchor.parentElement.appendChild(group);
            }
        }

        // 2. Inject Game Details Page Badge
        function injectGameBadge(doc) {
            if (!doc || !doc.body) return;
            const gameActionBars = doc.querySelectorAll('div[class*="playbar_"], div[class*="appactionandstats_"], div[class*="appdetailsheader_"]');
            gameActionBars.forEach(bar => {
                if (bar.querySelector('.cr-game-badge')) return;

                const badge = doc.createElement('div');
                badge.className = 'cr-game-badge';
                badge.title = 'Game save files are actively redirected and backed up by CloudRedirect (Click to Open)';
                badge.innerHTML = `
                    ${cloudSvg}
                    <span>CloudRedirect</span>
                    <span class="cr-game-badge-check">✓</span>
                `;
                badge.onclick = (e) => {
                    e.stopPropagation();
                    e.preventDefault();
                    launchApp(doc);
                };

                bar.appendChild(badge);
            });
        }

        function runInjectionsForDoc(doc) {
            if (!doc || !doc.body) return;

            // Remove any legacy header buttons or clipped dropdown menus
            const legacyBtn = doc.getElementById('cloudredirect-header-btn');
            if (legacyBtn) legacyBtn.remove();
            doc.querySelectorAll('.cr-nav-btn, [id*="cloudredirect-header"], .cr-dropdown-menu').forEach(el => el.remove());

            // If supernav item has old classes or wrong height/display, replace it
            const superItem = doc.getElementById('cloudredirect-supernav-item');
            if (superItem) {
                if (superItem.style.height || superItem.style.display || superItem.querySelector('.cr-supernav-btn')?.style.height) {
                    superItem.remove();
                } else if (superItem.classList.contains('cr-active')) {
                    superItem.classList.remove('cr-active');
                }
            }

            ensureStyles(doc);
            injectSuperNavTab(doc);
            injectBottomBarButtons(doc);
            injectGameBadge(doc);
        }

        function runInjections() {
            const docs = getAllSteamDocuments();
            for (const doc of docs) {
                try {
                    runInjectionsForDoc(doc);
                } catch (e) {
                    console.warn('[CloudRedirect] Injection error:', e);
                }
            }
        }

        function setupObserver() {
            runInjections();

            const observedDocs = new WeakSet();
            function registerDocObserver(d) {
                if (!d || !d.body || observedDocs.has(d)) return;
                observedDocs.add(d);
                try {
                    const observer = new MutationObserver(() => {
                        runInjectionsForDoc(d);
                    });
                    observer.observe(d.body, {
                        childList: true,
                        subtree: true
                    });
                } catch (e) { }
            }

            // Periodically check all windows (including after page transitions)
            setInterval(() => {
                const docs = getAllSteamDocuments();
                for (const d of docs) {
                    registerDocObserver(d);
                }
                runInjections();
            }, 800);

            function hookSteamRootMenu(popup) {
                try {
                    const r = popup?.m_popup?.document || popup?.document || popup?.window?.document;
                    if (!r) return;
                    setTimeout(() => {
                        if (r.getElementById('cr-root-menu-item')) return;
                        const menuItems = r.querySelectorAll('div#popup_target div[role="menuitem"]');
                        if (menuItems.length === 0) return;
                        const lastItem = menuItems[menuItems.length - 1];
                        const parent = lastItem?.parentNode;
                        if (!parent) return;

                        const crItem = lastItem.cloneNode(true);
                        crItem.id = 'cr-root-menu-item';
                        crItem.textContent = 'CloudRedirect';
                        crItem.onclick = (ev) => {
                            ev.stopPropagation();
                            launchApp(r);
                        };
                        parent.insertBefore(crItem, lastItem);
                    }, 50);
                } catch (e) { }
            }

            try {
                if (typeof Millennium !== 'undefined' && typeof Millennium.AddWindowCreateHook === 'function') {
                    Millennium.AddWindowCreateHook((popup) => {
                        if (popup && (popup.m_strTitle === 'Steam Root Menu' || popup.title === 'Steam Root Menu')) {
                            hookSteamRootMenu(popup);
                        }
                        setTimeout(() => runInjections(), 300);
                        setTimeout(() => runInjections(), 1500);
                    });
                }
            } catch (e) { }

            window.addEventListener("millennium-main-window-ready", () => {
                setTimeout(() => runInjections(), 300);
            });
        }

        const index = async function PluginMain() {
            setupObserver();

            try {
                if (typeof MILLENNIUM_BACKEND_IPC !== 'undefined' && MILLENNIUM_BACKEND_IPC.postMessage) {
                    MILLENNIUM_BACKEND_IPC.postMessage(1, { pluginName: pluginName });
                }
            } catch (e) { }

            return {
                title: "CloudRedirect",
                content: () => {
                    const div = document.createElement('div');
                    div.style.padding = '20px';
                    div.style.color = '#c6d4df';
                    div.innerHTML = `
                        <h2 style="color: #66c0f4; margin-bottom: 8px;">CloudRedirect Steam Integration</h2>
                        <p style="margin-bottom: 16px; color: #8f98a0;">Universal cloud save redirection and automated cloud backup for Steam games.</p>
                        <button class="cr-btn-primary" style="max-width: 240px;" onclick="window.open('cloudredirect://open')">Open CloudRedirect App</button>
                    `;
                    return div;
                },
                onDismount() {
                    const docs = getAllSteamDocuments();
                    for (const d of docs) {
                        try {
                            const superTab = d.getElementById('cloudredirect-supernav-item');
                            if (superTab) superTab.remove();
                            const topBtn = d.getElementById('cloudredirect-header-btn');
                            if (topBtn) topBtn.remove();
                            d.querySelectorAll('.cr-nav-btn, [id*="cloudredirect-header"], .cr-dropdown-menu').forEach(el => el.remove());
                            const btmGroup = d.getElementById('cr-bottom-bar-group');
                            if (btmGroup) btmGroup.remove();
                            const btmBtn = d.getElementById('cloudredirect-bottom-btn');
                            if (btmBtn) btmBtn.remove();
                            const style = d.getElementById('cr-millennium-styles');
                            if (style) style.remove();
                        } catch (e) { }
                    }
                }
            };
        };

        exports.default = index;
        Object.defineProperty(exports, '__esModule', { value: true });
        return exports;
    })({}, window.MILLENNIUM_API, window.SP_REACTDOM, window.SP_REACT);

    return millennium_main;
};

if (typeof window !== 'undefined') {
    window.PluginEntryPointMain = PluginEntryPointMain;
}

async function ExecutePluginModule() {
    try {
        let PluginModule = PluginEntryPointMain();
        if (typeof window !== 'undefined' && window.PLUGIN_LIST) {
            Object.assign(window.PLUGIN_LIST[pluginName], {
                ...PluginModule,
                __millennium_internal_plugin_name_do_not_use_or_change__: pluginName,
            });
        }
        if (PluginModule && typeof PluginModule.default === 'function') {
            let pluginProps = await PluginModule.default();
            function isValidSidebarNavComponent(obj) {
                return obj && obj.title !== undefined && obj.content !== undefined;
            }
            if (pluginProps && isValidSidebarNavComponent(pluginProps)) {
                if (typeof window !== 'undefined' && window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS) {
                    window.MILLENNIUM_SIDEBAR_NAVIGATION_PANELS[pluginName] = pluginProps;
                }
            }
        }
        if (MILLENNIUM_IS_CLIENT_MODULE && typeof MILLENNIUM_BACKEND_IPC !== 'undefined' && MILLENNIUM_BACKEND_IPC.postMessage) {
            MILLENNIUM_BACKEND_IPC.postMessage(1, { pluginName: pluginName });
        }
    } catch (e) {
        console.warn('[CloudRedirect] ExecutePluginModule error:', e);
    }
}
ExecutePluginModule();

// ============================================================================
// TASKBAR DOWNLOAD PROGRESS LISTENER
// ============================================================================
(function initTaskbarProgress() {
    try {
        if (typeof SteamClient !== 'undefined' && SteamClient.Downloads) {
            console.log('[CloudRedirect] Registering Taskbar Download Progress listeners...');

            const setTaskbar = async (pct) => {
                try {
                    await Millennium.callServerMethod("CloudRedirect", "set_progress_percent", { percent: pct });
                } catch (err) { }
            };

            let currentAppId = 0;

            SteamClient.Downloads.RegisterForDownloadOverview(async (event) => {
                if (!event || event.update_appid === 0) return;
                if (event.paused) {
                    await setTaskbar(-2);
                    return;
                }
                const state = event.update_state;
                if (state === "Downloading" || state === "Updating" || state === "Patching" || state === "Installing") {
                    const pct = Math.round(event.overall_percent_complete || 0);
                    currentAppId = event.update_appid;
                    await setTaskbar(pct);
                    return;
                }
                await setTaskbar(-1);
            });

            SteamClient.Downloads.RegisterForDownloadItems(async (isDownloading, downloadItems) => {
                if (!Array.isArray(downloadItems)) return;
                const item = downloadItems.find(el => el.item_data && el.item_data[0] && el.item_data[0].appid === currentAppId);
                if (item && item.item_data[0] && item.item_data[0].completed) {
                    await setTaskbar(100);
                    currentAppId = 0;
                }
            });
        }
    } catch (e) {
        console.warn('[CloudRedirect] Taskbar progress init warning:', e);
    }
})();

