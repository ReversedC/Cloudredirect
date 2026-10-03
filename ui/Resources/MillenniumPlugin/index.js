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

                /* Bottom Bar Button (Next to Add Game / Steam Unlock) */
                .cr-bottom-bar-btn {
                    display: inline-flex;
                    align-items: center;
                    gap: 6px;
                    height: 28px;
                    padding: 0 12px;
                    background: #142230;
                    border: 1px solid #274563;
                    border-radius: 4px;
                    color: #c6d4df;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                    font-size: 12px;
                    font-weight: 600;
                    cursor: pointer;
                    user-select: none;
                    transition: all 0.2s ease;
                    margin: 0 6px;
                    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.3);
                    z-index: 1000;
                }
                .cr-bottom-bar-btn:hover {
                    background: #1c3247;
                    border-color: #66c0f4;
                    color: #ffffff;
                    box-shadow: 0 0 10px rgba(102, 192, 244, 0.35);
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

                /* Steam Library Context Menu Item */
                .cr-context-menu-item {
                    cursor: pointer !important;
                    user-select: none !important;
                    position: relative !important;
                    display: flex !important;
                    align-items: center !important;
                    justify-content: space-between !important;
                    transition: background 0.12s ease, color 0.12s ease !important;
                    color: #dcdedf !important;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important;
                    font-size: 13px !important;
                    font-weight: 500 !important;
                    width: 100% !important;
                    box-sizing: border-box !important;
                    white-space: nowrap !important;
                }
                .cr-context-menu-item:hover {
                    background: #1b2838 !important;
                    color: #ffffff !important;
                }
                .cr-ctx-row {
                    display: flex !important;
                    align-items: center !important;
                    justify-content: space-between !important;
                    width: 100% !important;
                    min-width: 0 !important;
                }
                .cr-ctx-left {
                    display: flex !important;
                    align-items: center !important;
                    gap: 8px !important;
                }
                .cr-ctx-title {
                    font-size: 13px !important;
                    font-weight: 500 !important;
                    color: inherit !important;
                }
                .cr-ctx-dot {
                    display: inline-block !important;
                    width: 6px !important;
                    height: 6px !important;
                    background: #a4d007 !important;
                    border-radius: 50% !important;
                    box-shadow: 0 0 6px #a4d007 !important;
                    margin-left: 2px !important;
                }
                .cr-ctx-arrow {
                    font-size: 14px !important;
                    color: #8f98a0 !important;
                    font-weight: bold !important;
                    margin-left: 16px !important;
                }

                /* Floating Sub-menu Flyout */
                .cr-ctx-flyout-menu {
                    position: fixed !important;
                    z-index: 2147483647 !important;
                    width: 290px !important;
                    background: #172432 !important;
                    border: 1px solid #364b63 !important;
                    border-radius: 4px !important;
                    box-shadow: 0 12px 32px rgba(0, 0, 0, 0.8), 0 0 0 1px rgba(102, 192, 244, 0.25) !important;
                    padding: 6px !important;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important;
                    user-select: none !important;
                    pointer-events: auto !important;
                }
                .cr-flyout-header {
                    padding: 6px 10px 8px 10px !important;
                    border-bottom: 1px solid #233446 !important;
                    margin-bottom: 4px !important;
                }
                .cr-flyout-title {
                    color: #66c0f4 !important;
                    font-size: 13px !important;
                    font-weight: 700 !important;
                    white-space: nowrap !important;
                    overflow: hidden !important;
                    text-overflow: ellipsis !important;
                }
                .cr-flyout-status {
                    color: #8f98a0 !important;
                    font-size: 10px !important;
                    text-transform: uppercase !important;
                    letter-spacing: 0.5px !important;
                    margin-top: 2px !important;
                }
                .cr-flyout-item {
                    display: flex !important;
                    align-items: center !important;
                    gap: 10px !important;
                    padding: 7px 10px !important;
                    border-radius: 3px !important;
                    cursor: pointer !important;
                    transition: background 0.12s ease, transform 0.12s ease !important;
                }
                .cr-flyout-item:hover {
                    background: linear-gradient(90deg, #1d334a 0%, #152739 100%) !important;
                }
                .cr-item-icon {
                    font-size: 15px !important;
                    flex-shrink: 0 !important;
                    width: 20px !important;
                    text-align: center !important;
                }
                .cr-item-text {
                    display: flex !important;
                    flex-direction: column !important;
                    gap: 1px !important;
                }
                .cr-item-title {
                    color: #dcdedf !important;
                    font-size: 12px !important;
                    font-weight: 600 !important;
                }
                .cr-flyout-item:hover .cr-item-title {
                    color: #ffffff !important;
                }
                .cr-item-desc {
                    color: #7b8b98 !important;
                    font-size: 10px !important;
                }
                .cr-flyout-divider {
                    height: 1px !important;
                    background: #233446 !important;
                    margin: 4px 6px !important;
                }
            `;
            doc.head.appendChild(style);
        }

        // Helper to locate the exact SuperNav tab bar container in doc
        function findSuperNavInfo(doc) {
            const allEls = doc.querySelectorAll('div, a, span, button');

            for (const el of allEls) {
                const t = (el.textContent || '').trim().toUpperCase();
                if (t === 'COMMUNITY' || t === 'STORE' || t === 'LIBRARY') {
                    // Traverse up within 5 levels to locate the SuperNav container
                    let curr = el;
                    let depth = 0;
                    while (curr && curr !== doc.body && depth < 5) {
                        const parent = curr.parentElement;
                        if (!parent) break;
                        const pText = (parent.textContent || '').toUpperCase();
                        if (pText.includes('STORE') && pText.includes('LIBRARY') && pText.includes('COMMUNITY')) {
                            const children = Array.from(parent.children);
                            let sampleTab = null;
                            let lastNavTab = null;
                            let passedCommunity = false;

                            for (const child of children) {
                                const cText = (child.textContent || '').trim().toUpperCase();
                                if (cText.includes('COMMUNITY')) {
                                    sampleTab = child;
                                    lastNavTab = child;
                                    passedCommunity = true;
                                } else if (cText.includes('STORE') || cText.includes('LIBRARY')) {
                                    if (!sampleTab) sampleTab = child;
                                    if (!passedCommunity) lastNavTab = child;
                                } else if (passedCommunity) {
                                    if (cText.length > 0 && !cText.includes('HTTP') && !cText.includes('🔍') && !cText.includes('SEARCH') && !cText.includes('✕')) {
                                        lastNavTab = child;
                                        sampleTab = child;
                                    }
                                    break;
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
            navItem.title = 'CloudRedirect v2.9.74 (Save Protection Active - Click to Open App)';

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

            // Directly launch CloudRedirect application when tab is clicked
            navItem.onclick = (e) => {
                e.stopPropagation();
                e.preventDefault();
                launchApp(doc);
            };
            navItem.onkeydown = (e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    launchApp(doc);
                }
            };

            // Insert directly into the row after the last tab (after MINTAMAAF5)
            if (insertAfter.nextSibling) {
                container.insertBefore(navItem, insertAfter.nextSibling);
            } else {
                container.appendChild(navItem);
            }
        }

        // 1. Inject Button in Bottom Bar (next to Add Game / Steam Unlock)
        function injectBottomBarButton(doc) {
            if (!doc || !doc.body) return;
            if (doc.getElementById('cloudredirect-bottom-btn')) return;

            let targetSibling = null;
            let parentContainer = null;

            // Strategy 1: Find existing mod buttons like "Steam Unlock"
            const allElements = doc.querySelectorAll('button, div, a');
            for (const el of allElements) {
                const text = el.textContent || '';
                if (text.includes('Steam Unlock') || (el.className && typeof el.className === 'string' && el.className.includes('activation'))) {
                    targetSibling = el;
                    parentContainer = el.parentNode;
                    break;
                }
            }

            // Strategy 2: Find "+ Add a Game" button
            if (!targetSibling) {
                for (const el of allElements) {
                    const text = el.textContent || '';
                    if (text.includes('Add a Game') || text.includes('Add Game')) {
                        targetSibling = el;
                        parentContainer = el.parentNode;
                        break;
                    }
                }
            }

            // Strategy 3: Try standard selectors for Add a Game
            if (!targetSibling) {
                const addGameCandidates = doc.querySelectorAll('button[class*="addgamebutton_"], div[class*="addgamebutton_"], [class*="AddGameButton"]');
                for (const el of addGameCandidates) {
                    if (el.offsetParent !== null || el.offsetWidth > 0) {
                        targetSibling = el;
                        parentContainer = el.parentNode;
                        break;
                    }
                }
            }

            // Strategy 4: Fallback to bottom bar container
            if (!parentContainer) {
                parentContainer = doc.querySelector('div[class*="bottombar_"], div[class*="bottombarcontrols_"], footer, .bottom_bar');
            }

            if (!parentContainer) return;

            const btn = doc.createElement('div');
            btn.id = 'cloudredirect-bottom-btn';
            btn.className = 'cr-bottom-bar-btn';
            btn.title = 'CloudRedirect v2.9.74 (Save Protection Active - Click to Open App)';
            btn.innerHTML = `
                ${cloudSvg}
                <span>CloudRedirect</span>
                <span class="cr-status-dot"></span>
            `;

            btn.onclick = (e) => {
                e.stopPropagation();
                e.preventDefault();
                launchApp(doc);
            };

            if (targetSibling && targetSibling.parentNode === parentContainer) {
                targetSibling.parentNode.insertBefore(btn, targetSibling.nextSibling);
            } else if (parentContainer.firstChild) {
                parentContainer.insertBefore(btn, parentContainer.firstChild);
            } else {
                parentContainer.appendChild(btn);
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

        // 3. Steam Library Right-Click Context Menu Support
        let lastRightClickApp = { appId: '', gameName: '', time: 0 };

        function findAppInfoFromElement(el) {
            if (!el) return null;
            let curr = el;
            let depth = 0;
            while (curr && depth < 10) {
                if (curr.getAttribute) {
                    const appId = curr.getAttribute('data-appid') || 
                                  curr.getAttribute('data-app-id') || 
                                  curr.getAttribute('data-item-appid');
                    if (appId) {
                        const name = curr.getAttribute('data-gamename') || 
                                     curr.getAttribute('data-appname') || 
                                     curr.textContent?.trim()?.split('\n')[0];
                        return { appId: String(appId), gameName: name || '' };
                    }
                    const href = curr.getAttribute('href') || '';
                    const m = href.match(/(?:details|app)\/(\d+)/i);
                    if (m) {
                        return { appId: m[1], gameName: curr.textContent?.trim()?.split('\n')[0] || '' };
                    }
                }
                const keys = Object.keys(curr);
                const fiberKey = keys.find(k => k.startsWith('__reactFiber$') || k.startsWith('__reactInternalInstance$'));
                if (fiberKey) {
                    let f = curr[fiberKey];
                    let fDepth = 0;
                    while (f && fDepth < 20) {
                        const p = f.memoizedProps;
                        if (p) {
                            if (p.overview && p.overview.appid) {
                                return { appId: String(p.overview.appid), gameName: p.overview.display_name || p.overview.name || '' };
                            }
                            if (p.appid || p.appId || p.nAppId) {
                                const id = String(p.appid || p.appId || p.nAppId);
                                const name = p.name || p.strGameName || p.overview?.display_name || '';
                                return { appId: id, gameName: name };
                            }
                            if (p.item && (p.item.appid || p.item.appId)) {
                                return { appId: String(p.item.appid || p.item.appId), gameName: p.item.name || '' };
                            }
                        }
                        f = f.return;
                        fDepth++;
                    }
                }
                curr = curr.parentElement;
                depth++;
            }
            return null;
        }

        function triggerCloudRedirectAction(action, appId, gameName, doc) {
            __call_server_method__("execute_action", { action: action, appId: appId || '', gameName: gameName || '' });
            try {
                const d = doc || document;
                const link = d.createElement('a');
                link.href = `cloudredirect://action?cmd=${encodeURIComponent(action)}&appid=${encodeURIComponent(appId || '')}&name=${encodeURIComponent(gameName || '')}`;
                d.body.appendChild(link);
                link.click();
                link.remove();
            } catch (e) { }
        }

        function trackContextMenu(doc) {
            if (!doc || doc.__cr_ctx_tracked) return;
            doc.__cr_ctx_tracked = true;

            doc.addEventListener('contextmenu', (e) => {
                try {
                    const info = findAppInfoFromElement(e.target);
                    if (info) {
                        lastRightClickApp = {
                            appId: info.appId || '',
                            gameName: info.gameName || '',
                            time: Date.now()
                        };
                    } else {
                        const row = e.target.closest('[class*="gameListRow"], [class*="gamelistentry"], [class*="GameListEntry"], [class*="libraryhome"]');
                        if (row) {
                            const rInfo = findAppInfoFromElement(row);
                            lastRightClickApp = {
                                appId: rInfo?.appId || '',
                                gameName: rInfo?.gameName || row.textContent?.trim()?.split('\n')[0] || '',
                                time: Date.now()
                            };
                        }
                    }

                    setTimeout(() => checkAndInjectContextMenu(doc), 15);
                    setTimeout(() => checkAndInjectContextMenu(doc), 50);
                    setTimeout(() => checkAndInjectContextMenu(doc), 120);
                    setTimeout(() => checkAndInjectContextMenu(doc), 300);
                    setTimeout(() => checkAndInjectContextMenu(doc), 600);
                } catch (err) {
                    console.warn('[CloudRedirect] contextmenu handler error:', err);
                }
            }, true);
        }

        function checkAndInjectContextMenu(doc) {
            if (!doc || !doc.body) return;

            const candidates = Array.from(doc.querySelectorAll(`
                [class*="contextmenu_contextMenu"],
                [class*="contextmenu_contextMenuContents"],
                [class*="contextmenu_ContextMenuPosition"],
                [class*="menu_MenuPopup"],
                div[role="menu"],
                [class*="popup_menu"],
                div[class*="ContextMenu"]
            `));

            const bodyChildren = Array.from(doc.body.children);
            for (const ch of bodyChildren) {
                if (!candidates.includes(ch) && (ch.className || '').toString().toLowerCase().includes('popup')) {
                    candidates.push(ch);
                }
            }

            for (const container of candidates) {
                if (container.querySelector('#cloudredirect-steam-ctx-item')) {
                    continue;
                }

                const allDescendants = Array.from(container.querySelectorAll('*'));
                let propertiesItem = null;
                let manageItem = null;
                let sampleItem = null;

                for (const el of allDescendants) {
                    const txt = (el.textContent || '').trim();
                    if (/^properties(\.\.\.)?$/i.test(txt) && !propertiesItem) {
                        propertiesItem = el.closest('[role="menuitem"], [class*="contextMenuItem"], [class*="MenuItem"], div') || el;
                    } else if (/^manage$/i.test(txt) && !manageItem) {
                        manageItem = el.closest('[role="menuitem"], [class*="contextMenuItem"], [class*="MenuItem"], div') || el;
                    }
                    if (!sampleItem && el.className && typeof el.className === 'string' && el.className.includes('contextMenuItem')) {
                        sampleItem = el;
                    }
                }

                if (!propertiesItem && !manageItem) {
                    continue;
                }

                const targetRef = propertiesItem || manageItem;
                const refParent = targetRef.parentElement;
                if (!refParent) continue;

                let appId = lastRightClickApp.appId;
                let gameName = lastRightClickApp.gameName;

                if (!appId || !gameName) {
                    const selectedRow = doc.querySelector('[class*="gameListRow"][class*="Selected"], [class*="isSelected"], [class*="Selected"]');
                    if (selectedRow) {
                        const info = findAppInfoFromElement(selectedRow);
                        if (info) {
                            if (!appId) appId = info.appId;
                            if (!gameName) gameName = info.gameName;
                        }
                        if (!gameName) gameName = selectedRow.textContent?.trim()?.split('\n')[0];
                    }
                }

                const crItem = doc.createElement('div');
                crItem.id = 'cloudredirect-steam-ctx-item';
                crItem.className = (targetRef.className || sampleItem?.className || '').trim() + ' cr-context-menu-item';
                crItem.setAttribute('role', 'menuitem');
                crItem.setAttribute('tabindex', '0');
                crItem.style.display = 'flex';
                crItem.style.alignItems = 'center';
                crItem.style.justifyContent = 'space-between';
                crItem.style.width = '100%';
                crItem.style.boxSizing = 'border-box';
                crItem.style.whiteSpace = 'nowrap';

                crItem.innerHTML = `
                    <div class="cr-ctx-row" style="display:flex !important; align-items:center !important; justify-content:space-between !important; width:100% !important; min-width:0 !important; white-space:nowrap !important;">
                        <div class="cr-ctx-left" style="display:flex !important; align-items:center !important; gap:8px !important; flex:1 1 auto !important; min-width:0 !important;">
                            ${cloudSvg}
                            <span class="cr-ctx-title" style="font-size:13px !important; font-weight:500 !important; color:inherit !important;">CloudRedirect</span>
                            <span class="cr-ctx-dot" style="display:inline-block !important; width:6px !important; height:6px !important; background:#a4d007 !important; border-radius:50% !important; box-shadow:0 0 6px #a4d007 !important; margin-left:2px !important;" title="Save Protection Active"></span>
                        </div>
                        <div class="cr-ctx-arrow" style="font-size:14px !important; color:#8f98a0 !important; font-weight:bold !important; margin-left:auto !important; padding-left:12px !important; flex-shrink:0 !important;">›</div>
                    </div>
                `;

                try {
                    const win = doc.defaultView || window;
                    const computed = win.getComputedStyle(targetRef);
                    if (computed) {
                        if (computed.fontFamily) crItem.style.fontFamily = computed.fontFamily;
                        if (computed.fontSize) crItem.style.fontSize = computed.fontSize;
                        if (computed.lineHeight) crItem.style.lineHeight = computed.lineHeight;
                        if (computed.padding) crItem.style.padding = computed.padding;
                        if (computed.cursor) crItem.style.cursor = computed.cursor;
                    }
                } catch (e) { }

                let flyoutEl = null;
                let hideTimeout = null;

                function showFlyout() {
                    if (hideTimeout) {
                        clearTimeout(hideTimeout);
                        hideTimeout = null;
                    }
                    if (flyoutEl && flyoutEl.parentNode) return;

                    flyoutEl = doc.createElement('div');
                    flyoutEl.id = 'cr-steam-ctx-flyout';
                    flyoutEl.className = 'cr-ctx-flyout-menu';
                    const safeName = (gameName || 'Selected Game').replace(/</g, '&lt;').replace(/>/g, '&gt;');
                    flyoutEl.innerHTML = `
                        <div class="cr-flyout-header">
                            <div class="cr-flyout-title">${safeName}</div>
                            <div class="cr-flyout-status">Universal Save Protection</div>
                        </div>
                        <div class="cr-flyout-item" data-action="export-save">
                            <span class="cr-item-icon">📦</span>
                            <div class="cr-item-text">
                                <div class="cr-item-title">1-Click Save Export (.zip)</div>
                                <div class="cr-item-desc">Package saves into portable archive</div>
                            </div>
                        </div>
                        <div class="cr-flyout-item" data-action="create-snapshot">
                            <span class="cr-item-icon">📸</span>
                            <div class="cr-item-text">
                                <div class="cr-item-title">Create Save Snapshot</div>
                                <div class="cr-item-desc">Timestamped rollback checkpoint</div>
                            </div>
                        </div>
                        <div class="cr-flyout-item" data-action="character-slots">
                            <span class="cr-item-icon">👤</span>
                            <div class="cr-item-text">
                                <div class="cr-item-title">Character Switcher (Slots)</div>
                                <div class="cr-item-desc">Branch saves & character builds</div>
                            </div>
                        </div>
                        <div class="cr-flyout-item" data-action="resign-save">
                            <span class="cr-item-icon">🔑</span>
                            <div class="cr-item-text">
                                <div class="cr-item-title">SteamID64 Account Transfer</div>
                                <div class="cr-item-desc">Re-sign save to another Steam account</div>
                            </div>
                        </div>
                        <div class="cr-flyout-item" data-action="open-save-dir">
                            <span class="cr-item-icon">📁</span>
                            <div class="cr-item-text">
                                <div class="cr-item-title">Open Save Folder in Explorer</div>
                                <div class="cr-item-desc">Reveal actual files in Windows Explorer</div>
                            </div>
                        </div>
                        <div class="cr-flyout-divider"></div>
                        <div class="cr-flyout-item" data-action="tools">
                            <span class="cr-item-icon">⚡</span>
                            <div class="cr-item-text">
                                <div class="cr-item-title">Open CloudRedirect Beta Hub</div>
                                <div class="cr-item-desc">LAN Sync, Quota Visualizer & Booster</div>
                            </div>
                        </div>
                    `;

                    doc.body.appendChild(flyoutEl);

                    const rect = crItem.getBoundingClientRect();
                    const flyWidth = 290;
                    const flyHeight = 360;
                    const winW = doc.defaultView?.innerWidth || 1920;
                    const winH = doc.defaultView?.innerHeight || 1080;

                    let left = rect.right + 4;
                    if (left + flyWidth > winW - 10) {
                        left = rect.left - flyWidth - 4;
                    }
                    let top = rect.top - 4;
                    if (top + flyHeight > winH - 10) {
                        top = Math.max(10, winH - flyHeight - 10);
                    }

                    flyoutEl.style.left = `${Math.max(10, left)}px`;
                    flyoutEl.style.top = `${Math.max(10, top)}px`;

                    flyoutEl.onmouseenter = () => {
                        if (hideTimeout) {
                            clearTimeout(hideTimeout);
                            hideTimeout = null;
                        }
                    };
                    flyoutEl.onmouseleave = () => {
                        hideFlyout();
                    };

                    flyoutEl.querySelectorAll('.cr-flyout-item').forEach(item => {
                        item.onclick = (e) => {
                            e.stopPropagation();
                            e.preventDefault();
                            const action = item.getAttribute('data-action') || 'tools';
                            triggerCloudRedirectAction(action, appId, gameName, doc);
                            removeFlyout();
                            try {
                                doc.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                            } catch (e) { }
                        };
                    });
                }

                function hideFlyout() {
                    if (hideTimeout) clearTimeout(hideTimeout);
                    hideTimeout = setTimeout(() => {
                        removeFlyout();
                    }, 300);
                }

                function removeFlyout() {
                    if (flyoutEl && flyoutEl.parentNode) {
                        flyoutEl.remove();
                        flyoutEl = null;
                    }
                }

                crItem.onmouseenter = () => showFlyout();
                crItem.onmouseleave = () => hideFlyout();

                crItem.onclick = (e) => {
                    e.stopPropagation();
                    e.preventDefault();
                    triggerCloudRedirectAction('tools', appId, gameName, doc);
                    removeFlyout();
                };

                const menuObserver = new MutationObserver(() => {
                    if (!container.parentNode || !crItem.parentNode) {
                        removeFlyout();
                        menuObserver.disconnect();
                    }
                });
                menuObserver.observe(doc.body, { childList: true, subtree: true });

                if (propertiesItem && propertiesItem.parentElement === refParent) {
                    refParent.insertBefore(crItem, propertiesItem);
                } else {
                    refParent.appendChild(crItem);
                }
            }
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
            injectBottomBarButton(doc);
            injectGameBadge(doc);
            trackContextMenu(doc);
            checkAndInjectContextMenu(doc);
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

            try {
                if (typeof Millennium !== 'undefined' && typeof Millennium.AddWindowCreateHook === 'function') {
                    Millennium.AddWindowCreateHook((popup) => {
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
                            const btmBtn = d.getElementById('cloudredirect-bottom-btn');
                            if (btmBtn) btmBtn.remove();
                            const ctxItem = d.getElementById('cloudredirect-steam-ctx-item');
                            if (ctxItem) ctxItem.remove();
                            const flyout = d.getElementById('cr-steam-ctx-flyout');
                            if (flyout) flyout.remove();
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
