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
            if (doc.getElementById('cr-millennium-styles')) {
                return;
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

                /* Floating Action Bar (FAB) for Steam Library */
                .cr-fab-container {
                    position: fixed !important;
                    bottom: 54px !important;
                    right: 28px !important;
                    z-index: 99999 !important;
                    display: inline-flex !important;
                    align-items: center !important;
                    gap: 6px !important;
                    padding: 5px 12px 5px 8px !important;
                    background: rgba(16, 26, 37, 0.94) !important;
                    backdrop-filter: blur(14px) !important;
                    border: 1px solid #2f5073 !important;
                    border-radius: 24px !important;
                    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.75), 0 0 16px rgba(102, 192, 244, 0.22) !important;
                    user-select: none !important;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important;
                    transition: box-shadow 0.2s ease, border-color 0.2s ease, transform 0.2s ease !important;
                    cursor: grab !important;
                    max-width: calc(100vw - 32px) !important;
                    width: auto !important;
                    box-sizing: border-box !important;
                }
                .cr-fab-container:hover {
                    border-color: #66c0f4 !important;
                    box-shadow: 0 10px 28px rgba(0, 0, 0, 0.85), 0 0 22px rgba(102, 192, 244, 0.4) !important;
                    transform: translateY(-1px) !important;
                }
                .cr-fab-container.cr-fab-dragging,
                .cr-fab-container.cr-fab-dragging * {
                    cursor: grabbing !important;
                    user-select: none !important;
                    transition: none !important;
                }
                .cr-fab-drag {
                    cursor: grab !important;
                    color: #557599 !important;
                    font-size: 13px !important;
                    line-height: 1 !important;
                    padding: 0 4px !important;
                    letter-spacing: -1px !important;
                    display: flex !important;
                    align-items: center !important;
                    justify-content: center !important;
                    transition: color 0.15s ease !important;
                }
                .cr-fab-drag:hover {
                    color: #66c0f4 !important;
                }
                .cr-fab-drag:active {
                    cursor: grabbing !important;
                }
                .cr-fab-item {
                    display: inline-flex !important;
                    align-items: center !important;
                    gap: 6px !important;
                    height: 28px !important;
                    padding: 0 11px !important;
                    border-radius: 14px !important;
                    font-size: 11px !important;
                    font-weight: 600 !important;
                    color: #c6d4df !important;
                    cursor: pointer !important;
                    transition: all 0.18s ease !important;
                    box-sizing: border-box !important;
                    white-space: nowrap !important;
                }
                .cr-fab-cr {
                    background: rgba(22, 38, 54, 0.85) !important;
                    border: 1px solid #2d4c6b !important;
                }
                .cr-fab-cr:hover {
                    background: #233f5d !important;
                    border-color: #66c0f4 !important;
                    color: #ffffff !important;
                }
                .cr-fab-check {
                    color: #a4d007 !important;
                    font-weight: bold !important;
                    font-size: 12px !important;
                    line-height: 1 !important;
                }
                .cr-fab-divider {
                    width: 1px !important;
                    height: 16px !important;
                    background: rgba(255, 255, 255, 0.18) !important;
                    margin: 0 2px !important;
                    flex-shrink: 0 !important;
                }
                .cr-fab-wiki {
                    background: linear-gradient(135deg, rgba(28, 56, 82, 0.95), rgba(16, 34, 52, 0.95)) !important;
                    border: 1px solid #3d6e99 !important;
                    color: #66c0f4 !important;
                }
                .cr-fab-wiki:hover {
                    background: linear-gradient(135deg, rgba(38, 76, 110, 1), rgba(24, 48, 72, 1)) !important;
                    border-color: #66c0f4 !important;
                    color: #ffffff !important;
                    box-shadow: 0 0 10px rgba(102, 192, 244, 0.4) !important;
                }
                .cr-fab-label {
                    line-height: 1 !important;
                }
                @media (max-width: 1200px) {
                    .cr-fab-container {
                        padding: 4px 10px 4px 6px !important;
                        gap: 5px !important;
                    }
                    .cr-fab-item {
                        height: 26px !important;
                        padding: 0 9px !important;
                        font-size: 10.5px !important;
                    }
                }
                @media (max-width: 900px) {
                    .cr-fab-container {
                        padding: 3px 8px 3px 5px !important;
                        gap: 4px !important;
                        border-radius: 20px !important;
                    }
                    .cr-fab-item {
                        height: 24px !important;
                        padding: 0 7px !important;
                        font-size: 10px !important;
                    }
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
                    display: inline-flex !important;
                    align-items: center !important;
                    gap: 6px !important;
                    background: rgba(22, 34, 46, 0.9) !important;
                    border: 1px solid #2d4c6b !important;
                    border-radius: 4px !important;
                    padding: 0 10px !important;
                    height: 32px !important;
                    min-height: 32px !important;
                    max-height: 32px !important;
                    line-height: 32px !important;
                    white-space: nowrap !important;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important;
                    font-size: 11px !important;
                    font-weight: 600 !important;
                    color: #c6d4df !important;
                    cursor: pointer !important;
                    transition: all 0.2s ease !important;
                    user-select: none !important;
                    margin: 0 !important;
                    vertical-align: middle !important;
                    box-sizing: border-box !important;
                    flex-shrink: 0 !important;
                    z-index: 10 !important;
                }
                .cr-game-badge:hover {
                    background: #233b52 !important;
                    border-color: #66c0f4 !important;
                    color: #ffffff !important;
                    box-shadow: 0 0 10px rgba(102, 192, 244, 0.4) !important;
                }
                .cr-game-badge-check {
                    color: #a4d007;
                    font-weight: bold;
                    font-size: 12px;
                    line-height: 1;
                }

                /* Game Page Button: .cr-patchwiki-btn */
                .cr-patchwiki-btn {
                    display: inline-flex !important;
                    align-items: center !important;
                    gap: 6px !important;
                    background: linear-gradient(135deg, rgba(28, 52, 75, 0.95), rgba(16, 32, 48, 0.95)) !important;
                    border: 1px solid #3d6e99 !important;
                    border-radius: 4px !important;
                    padding: 0 10px !important;
                    height: 32px !important;
                    min-height: 32px !important;
                    max-height: 32px !important;
                    line-height: 32px !important;
                    white-space: nowrap !important;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important;
                    font-size: 11px !important;
                    font-weight: 600 !important;
                    color: #66c0f4 !important;
                    cursor: pointer !important;
                    transition: all 0.2s ease !important;
                    user-select: none !important;
                    margin: 0 !important;
                    vertical-align: middle !important;
                    box-shadow: 0 2px 5px rgba(0, 0, 0, 0.35) !important;
                    box-sizing: border-box !important;
                    flex-shrink: 0 !important;
                    z-index: 10 !important;
                }
                .cr-patchwiki-btn:hover {
                    background: linear-gradient(135deg, rgba(38, 72, 104, 1), rgba(24, 46, 68, 1)) !important;
                    border-color: #66c0f4 !important;
                    color: #ffffff !important;
                    box-shadow: 0 0 12px rgba(102, 192, 244, 0.5) !important;
                    transform: translateY(-1px) !important;
                }
                .cr-patchwiki-svg {
                    width: 14px;
                    height: 14px;
                    fill: currentColor;
                    flex-shrink: 0;
                }
                .cr-patchwiki-pill {
                    background: #a4d007;
                    color: #0d141b;
                    font-size: 9px;
                    font-weight: 800;
                    text-transform: uppercase;
                    padding: 1px 5px;
                    border-radius: 8px;
                    letter-spacing: 0.5px;
                    margin-left: 2px;
                }
                .cr-patchwiki-pill.cr-pill-wiki {
                    background: #a4d007;
                    color: #0d141b;
                }
                .cr-patchwiki-pill.cr-pill-search {
                    background: #3878b5;
                    color: #ffffff;
                }

                /* In-Steam PatchWiki Mini Window Modal */
                .cr-patchwiki-window {
                    position: fixed;
                    top: 80px;
                    right: 40px;
                    width: 820px;
                    height: 620px;
                    min-width: 440px;
                    min-height: 340px;
                    max-width: 95vw;
                    max-height: 92vh;
                    background: #101822;
                    border: 1px solid #2a475e;
                    border-radius: 8px;
                    box-shadow: 0 18px 48px rgba(0, 0, 0, 0.75), 0 0 0 1px rgba(102, 192, 244, 0.2);
                    z-index: 9999999;
                    display: flex;
                    flex-direction: column;
                    overflow: hidden;
                    resize: both;
                    font-family: "Motiva Sans", "Twemoji", "Noto Sans", Helvetica, sans-serif;
                    color: #c6d4df;
                    animation: crFadeIn 0.18s ease-out;
                }
                @keyframes crFadeIn {
                    from { opacity: 0; transform: scale(0.97); }
                    to { opacity: 1; transform: scale(1); }
                }
                .cr-patchwiki-window.cr-modal-maximized {
                    top: 20px !important;
                    left: 20px !important;
                    right: 20px !important;
                    bottom: 20px !important;
                    width: auto !important;
                    height: auto !important;
                    max-width: none !important;
                    max-height: none !important;
                    resize: none !important;
                }
                .cr-modal-header {
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    height: 42px;
                    background: linear-gradient(to right, #1b2838, #172432);
                    border-bottom: 1px solid #24384a;
                    padding: 0 12px;
                    user-select: none;
                    cursor: move;
                    flex-shrink: 0;
                }
                .cr-modal-title-left {
                    display: flex;
                    align-items: center;
                    gap: 8px;
                    overflow: hidden;
                    white-space: nowrap;
                    text-overflow: ellipsis;
                    margin-right: 12px;
                }
                .cr-modal-icon { font-size: 15px; flex-shrink: 0; }
                .cr-modal-title-text {
                    display: flex;
                    align-items: center;
                    gap: 8px;
                    overflow: hidden;
                    text-overflow: ellipsis;
                }
                .cr-modal-app-name {
                    font-weight: 700;
                    font-size: 13px;
                    color: #ffffff;
                    overflow: hidden;
                    text-overflow: ellipsis;
                }
                .cr-modal-guide-badge {
                    background: rgba(102, 192, 244, 0.2);
                    border: 1px solid #66c0f4;
                    color: #66c0f4;
                    font-size: 10px;
                    font-weight: 700;
                    text-transform: uppercase;
                    padding: 1px 6px;
                    border-radius: 4px;
                    letter-spacing: 0.5px;
                }
                .cr-modal-actions {
                    display: flex;
                    align-items: center;
                    gap: 6px;
                    flex-shrink: 0;
                }
                .cr-modal-btn {
                    width: 28px;
                    height: 28px;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    background: transparent;
                    border: 1px solid transparent;
                    border-radius: 4px;
                    color: #8f98a0;
                    cursor: pointer;
                    font-size: 13px;
                    transition: all 0.15s ease;
                }
                .cr-modal-btn:hover {
                    background: #243b4f;
                    color: #ffffff;
                    border-color: #3d5f7d;
                }
                .cr-modal-btn.cr-btn-close:hover {
                    background: #c93838;
                    border-color: #e85555;
                    color: #ffffff;
                }
                .cr-modal-body {
                    flex: 1;
                    display: flex;
                    flex-direction: column;
                    overflow: hidden;
                    position: relative;
                    background: #0b1016;
                }
                .cr-patchwiki-frame {
                    width: 100%;
                    height: 100%;
                    border: none;
                    background: #0f1722;
                }
                .cr-modal-footer {
                    height: 26px;
                    background: #0d141b;
                    border-top: 1px solid #1c2834;
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    padding: 0 12px;
                    font-size: 11px;
                    color: #67727e;
                    flex-shrink: 0;
                }
                .cr-footer-status { color: #8f98a0; }
                .cr-footer-link { font-family: Consolas, monospace; color: #536473; }
            `;
            doc.head.appendChild(style);
        }

        // Helper to locate the exact SuperNav tab bar container in doc
        function findSuperNavInfo(doc) {
            if (!doc || !doc.body) return null;

            // Fast targeted check: look for SuperNav container
            const container = doc.querySelector('div[class*="supernav_container"], nav[class*="supernav"], div[class*="SuperNavContainer"]');
            if (container && container.children && container.children.length > 0) {
                const tabs = Array.from(container.children);
                return {
                    container: container,
                    sampleTab: tabs[0],
                    insertAfter: tabs[tabs.length - 1]
                };
            }

            // Fast fallback: look specifically for community or store nav links
            const navLink = doc.querySelector('a[href*="steamcommunity.com"], a[href*="store.steampowered.com"]');
            if (navLink) {
                const tab = navLink.closest('div[class*="supernav_"], div[class*="menuitem"], div') || navLink;
                const parent = tab.parentElement;
                if (parent && parent.children && parent.children.length > 0) {
                    return {
                        container: parent,
                        sampleTab: tab,
                        insertAfter: parent.lastElementChild || tab
                    };
                }
            }

            return null;
        }

        // Injects tab right into STORE / LIBRARY / COMMUNITY / USER / CLOUDREDIRECT row
        function injectSuperNavTab(doc) {
            if (!doc || !doc.body) return;

            const existing = doc.getElementById('cloudredirect-supernav-item');
            if (existing && existing.parentNode) {
                return;
            }
            if (existing) {
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
            navItem.title = 'CloudRedirect (Save Protection Active - Click to Open App)';

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

        // Clean up any stray buttons or old bottom bar artifacts
        function cleanupStrayElements(doc) {
            if (!doc || !doc.body) return;
            try {
                // Remove bottom bar artifacts that created the giant empty shelf in game details
                doc.querySelectorAll('#cloudredirect-bottom-btn, .cr-bottom-bar-btn').forEach(el => el.remove());
                doc.querySelectorAll('#cloudredirect-header-btn, .cr-nav-btn, .cr-dropdown-menu').forEach(el => el.remove());
                doc.querySelectorAll('#cr-action-group, .cr-action-group, .cr-game-badge, .cr-patchwiki-btn').forEach(el => el.remove());
            } catch (e) { }
        }

        // PatchWiki Cache and Manifest
        let patchWikiCache = null;
        let manifestCache = null;
        let isFetchingPatchWiki = false;
        let currentModalTutorial = null;

        function extractAppIdFromMetadata(id, title, game) {
            if (id) {
                id = id.trim();
                if (/^\d{3,9}$/.test(id)) return parseInt(id, 10);
                const mSuff = id.match(/-(\d{3,9})$/);
                if (mSuff) return parseInt(mSuff[1], 10);
            }
            if (title) {
                const mTitle = title.match(/\b(\d{3,9})\b/);
                if (mTitle) return parseInt(mTitle[1], 10);
            }
            if (game) {
                const mGame = game.match(/\b(\d{3,9})\b/);
                if (mGame) return parseInt(mGame[1], 10);
            }
            if (id) {
                const mAny = id.match(/(\d{3,9})/);
                if (mAny) return parseInt(mAny[1], 10);
            }
            return null;
        }

        let patchWikiPromise = null;
        let appStatusCache = null;

        async function fetchPluginStatus() {
            try {
                let res = await __call_server_method__("get_status", {});
                if (typeof res === 'string') {
                    try { res = JSON.parse(res); } catch (e) { }
                }
                let data = res;
                if (res && res.data) {
                    data = res.data;
                }
                if (typeof data === 'string') {
                    const cleanJson = data.replace(/^\uFEFF/, '').trim();
                    try { data = JSON.parse(cleanJson); } catch (e) { }
                }
                if (data && typeof data === 'object') {
                    appStatusCache = data;
                }
            } catch (e) {
                console.warn('[CloudRedirect] fetchPluginStatus error:', e);
            }
            return appStatusCache;
        }

        function buildPatchWikiMap(list) {
            if (!Array.isArray(list)) return {};
            const map = {};
            for (const item of list) {
                const appId = item.appId || extractAppIdFromMetadata(item.id, item.title, item.game);
                const tObj = {
                    id: item.id,
                    title: item.title,
                    game: item.game,
                    desc: item.desc,
                    tags: item.tags,
                    author: item.author,
                    date: item.date,
                    appId: appId,
                    url: `https://mirzaarsyad74-cmyk.github.io/patchwiki/?tutorial=${encodeURIComponent(item.id)}#read/${encodeURIComponent(item.id)}`
                };
                if (appId) {
                    map[appId.toString()] = tObj;
                }
                if (item.game) {
                    const norm = item.game.toLowerCase().replace(/[^a-z0-9]/g, '');
                    if (norm.length >= 3) {
                        map['name:' + norm] = tObj;
                    }
                }
            }
            return map;
        }

        async function refreshPatchWikiData() {
            try {
                const resp = await fetch(`https://mirzaarsyad74-cmyk.github.io/patchwiki/index.json?_t=${Date.now()}`, { cache: "no-cache" });
                if (resp.ok) {
                    const list = await resp.json();
                    if (Array.isArray(list) && list.length > 0) {
                        patchWikiCache = buildPatchWikiMap(list);
                        console.log(`[CloudRedirect] Guides auto-updated: ${list.length} tutorials loaded`);
                        return patchWikiCache;
                    }
                }
            } catch (e) {
                console.warn('[CloudRedirect] refreshPatchWikiData error:', e);
            }
            return patchWikiCache;
        }

        function loadPatchWikiData() {
            if (patchWikiCache && Object.keys(patchWikiCache).length > 0) {
                refreshPatchWikiData().catch(() => {});
                return Promise.resolve(patchWikiCache);
            }
            if (patchWikiPromise) return patchWikiPromise;

            patchWikiPromise = (async () => {
                // 1. Try Backend Lua get_patchwiki_tutorials() for instant local load
                try {
                    let res = await __call_server_method__("get_patchwiki_tutorials", {});
                    if (typeof res === 'string') {
                        try { res = JSON.parse(res); } catch (e) { }
                    }
                    let data = res;
                    if (res && res.data) {
                        data = res.data;
                    }
                    if (typeof data === 'string') {
                        const cleanJson = data.replace(/^\uFEFF/, '').trim();
                        try { data = JSON.parse(cleanJson); } catch (e) { }
                    }
                    if (data && typeof data === 'object' && Object.keys(data).length > 0) {
                        patchWikiCache = data;
                    }
                } catch (e) { }

                // 2. Fetch directly from GitHub Pages index.json with cache-busting
                try {
                    const resp = await fetch(`https://mirzaarsyad74-cmyk.github.io/patchwiki/index.json?_t=${Date.now()}`, { cache: "no-cache" });
                    if (resp.ok) {
                        const list = await resp.json();
                        if (Array.isArray(list) && list.length > 0) {
                            patchWikiCache = buildPatchWikiMap(list);
                            return patchWikiCache;
                        }
                    }
                } catch (e) {
                    console.warn('[CloudRedirect] PatchWiki initial web fetch error:', e);
                }

                patchWikiCache = patchWikiCache || {};
                return patchWikiCache;
            })();

            return patchWikiPromise;
        }

        function getAppDetails(doc, targetEl) {
            let appId = null;
            let title = null;
            let isOwned = null;
            let isFree = null;
            let isShortcut = null;

            // 1. Traverse React Fiber on targetEl (or active game details container) and its ancestors
            const detailsEl = targetEl || (doc ? doc.querySelector('div[class*="PlayBar"], div[class*="playbar"], div[class*="gameDetail"], div[class*="GameDetail"], div[class*="HeroContainer"], div[class*="appDetails"], div[class*="headerImageContainer"]') : null);
            if (detailsEl) {
                try {
                    let curr = detailsEl;
                    let depth = 0;
                    while (curr && depth < 25) {
                        for (const k of Object.keys(curr)) {
                            if (k.startsWith('__reactFiber$') || k.startsWith('__reactInternalInstance$')) {
                                let fiber = curr[k];
                                for (let i = 0; fiber && i < 40; i++) {
                                    const p = fiber.memoizedProps;
                                    if (p) {
                                        if (!appId) {
                                            const rawId = p.appid || p.appId || p.overview?.appid || p.details?.unAppID || p.game?.appid || p.app?.appid || p.item?.appid;
                                            if (rawId) {
                                                const parsed = parseInt(rawId, 10);
                                                if (parsed > 0) appId = parsed;
                                            }
                                        }
                                        if (!title) {
                                            const rawTitle = p.overview?.display_name || p.details?.strDisplayName || p.game?.name || p.app?.name || p.item?.display_name || p.display_name || p.name;
                                            if (typeof rawTitle === 'string' && rawTitle.trim().length > 0) title = rawTitle.trim();
                                        }
                                        if (isOwned === null) {
                                            const rawOwned = p.overview?.m_bOwned ?? p.overview?.bOwned ?? p.details?.bOwned ?? p.game?.bOwned ?? p.app?.bOwned;
                                            if (rawOwned !== undefined) isOwned = Boolean(rawOwned);
                                        }
                                        if (isFree === null) {
                                            const rawFree = p.overview?.m_bIsFree ?? p.overview?.bIsFree ?? p.overview?.is_free ?? p.details?.bFreeToPlay ?? p.details?.bIsFree ?? p.game?.is_free ?? p.app?.is_free;
                                            if (rawFree !== undefined) isFree = Boolean(rawFree);
                                        }
                                        if (isShortcut === null) {
                                            const rawShortcut = p.overview?.m_bIsShortcut ?? p.overview?.bIsShortcut ?? p.game?.is_shortcut ?? (p.overview?.app_type === 1073741824) ?? (p.overview?.rt_custom_game_id && p.overview?.rt_custom_game_id > 0);
                                            if (rawShortcut !== undefined) isShortcut = Boolean(rawShortcut);
                                        }
                                        if (appId && title && isOwned !== null && isFree !== null) {
                                            return { appId, title, isOwned: Boolean(isOwned), isFree: Boolean(isFree), isShortcut: Boolean(isShortcut) };
                                        }
                                    }
                                    fiber = fiber.return;
                                }
                            }
                        }
                        curr = curr.parentElement;
                        depth++;
                    }
                } catch (e) { }
            }

            // 2. Selected game in library list sidebar (class _1UBpAXP408Ez_L_mXhW5Q9 / Selected)
            if (doc && (!appId || !title)) {
                try {
                    const selected = doc.querySelector('div[class*="_1UBpAXP408Ez_L_mXhW5Q9"], div[class*="Selected"], div[class*="selected_"]');
                    if (selected) {
                        let curr = selected;
                        let depth = 0;
                        while (curr && depth < 15) {
                            for (const k of Object.keys(curr)) {
                                if (k.startsWith('__reactFiber$') || k.startsWith('__reactInternalInstance$')) {
                                    let fiber = curr[k];
                                    for (let i = 0; fiber && i < 30; i++) {
                                        const p = fiber.memoizedProps;
                                        if (p) {
                                            if (!appId) {
                                                const rawId = p.appid || p.appId || p.overview?.appid || p.item?.appid || p.game?.appid;
                                                if (rawId) {
                                                    const parsed = parseInt(rawId, 10);
                                                    if (parsed > 0) appId = parsed;
                                                }
                                            }
                                            if (!title) {
                                                const rawTitle = p.overview?.display_name || p.item?.display_name || p.game?.name || p.name;
                                                if (typeof rawTitle === 'string' && rawTitle.trim().length > 0) title = rawTitle.trim();
                                            }
                                            if (isOwned === null) {
                                                const rawOwned = p.overview?.m_bOwned ?? p.overview?.bOwned ?? p.game?.bOwned;
                                                if (rawOwned !== undefined) isOwned = Boolean(rawOwned);
                                            }
                                            if (isFree === null) {
                                                const rawFree = p.overview?.m_bIsFree ?? p.overview?.bIsFree ?? p.overview?.is_free ?? p.game?.is_free;
                                                if (rawFree !== undefined) isFree = Boolean(rawFree);
                                            }
                                            if (isShortcut === null) {
                                                const rawShortcut = p.overview?.m_bIsShortcut ?? p.overview?.bIsShortcut ?? p.game?.is_shortcut ?? (p.overview?.app_type === 1073741824);
                                                if (rawShortcut !== undefined) isShortcut = Boolean(rawShortcut);
                                            }
                                            if (appId && title) break;
                                        }
                                        fiber = fiber.return;
                                    }
                                }
                            }
                            if (appId && title) break;
                            curr = curr.parentElement;
                            depth++;
                        }

                        if (!title) {
                            const nameEl = selected.querySelector('div[class*="_2SXJM0PeFEi3gbC7V3S5pE"], div[class*="GameListEntryName"], span');
                            if (nameEl && nameEl.textContent) title = nameEl.textContent.trim();
                        }
                    }
                } catch (e) { }
            }

            // 3. Fallback: Check links in game details (Store Page, Community Hub, Discussions, Guides)
            if (doc && !appId) {
                try {
                    const links = doc.querySelectorAll('a[href*="/app/"], a[href*="steam://store/"], a[href*="steam://url/StoreAppPage/"]');
                    for (const link of links) {
                        const href = link.getAttribute('href') || '';
                        const m = href.match(/(?:app|store|StoreAppPage)\/(\d{3,9})/i);
                        if (m) {
                            const parsed = parseInt(m[1], 10);
                            if (parsed > 0) {
                                appId = parsed;
                                break;
                            }
                        }
                    }
                } catch (e) { }
            }

            // 4. Fallback: Hero / Banner / Logo image or background-image
            if (doc && !appId) {
                try {
                    const imgs = doc.querySelectorAll('img[src*="/apps/"], img[src*="/app/"]');
                    for (const img of imgs) {
                        const m = (img.getAttribute('src') || '').match(/(?:app|apps)\/(\d{3,9})/i);
                        if (m) {
                            const parsed = parseInt(m[1], 10);
                            if (parsed > 0) {
                                appId = parsed;
                                break;
                            }
                        }
                    }
                    if (!appId) {
                        const styledEls = doc.querySelectorAll('div[style*="/apps/"], div[style*="/app/"]');
                        for (const el of styledEls) {
                            const style = el.getAttribute('style') || '';
                            const m = style.match(/(?:app|apps)\/(\d{3,9})/i);
                            if (m) {
                                const parsed = parseInt(m[1], 10);
                                if (parsed > 0) {
                                    appId = parsed;
                                    break;
                                }
                            }
                        }
                    }
                } catch (e) { }
            }

            // 5. Fallback: URL check
            if (!appId) {
                try {
                    const href = doc.defaultView?.location?.href || window.location?.href || '';
                    const m = href.match(/(?:app|details)\/(\d{3,9})/i);
                    if (m) appId = parseInt(m[1], 10);
                } catch (e) { }
            }

            // 6. Title Fallbacks
            if (!title && doc) {
                try {
                    const logoImg = doc.querySelector('img[class*="Logo"][alt], img[class*="logo"][alt]');
                    if (logoImg && logoImg.getAttribute('alt')) {
                        title = logoImg.getAttribute('alt').trim();
                    }
                    if (!title && doc.title) {
                        let clean = doc.title.replace(/^Steam\s*[-–]\s*/i, '').replace(/\s*[-–]\s*Steam$/i, '').trim();
                        if (clean && !clean.toLowerCase().includes('library') && !clean.toLowerCase().includes('steam')) {
                            title = clean;
                        }
                    }
                } catch (e) { }
            }

            // 7. Infer isOwned and isShortcut if not determined from fiber
            if (isOwned === null && doc) {
                const hasPlayBar = doc.querySelector('div[class*="PlayBar"], div[class*="playbar"], div[class*="PlayButton"], button[class*="PlayButton"], button[class*="playButton"], div[class*="InstallButton"], div[class*="UpdateButton"]');
                if (hasPlayBar) {
                    isOwned = true;
                }
            }

            if (isShortcut === null && appId) {
                if (appId > 2147483648) {
                    isShortcut = true;
                }
            }

            return {
                appId,
                title,
                isOwned: isOwned ?? true,
                isFree: Boolean(isFree),
                isShortcut: Boolean(isShortcut)
            };
        }

        const knownFreeAppIds = new Set([
            730, 570, 440, 1172470, 578080, 230410, 1085660, 238960, 1938090, 252490, 1046930, 304930, 438100, 236390
        ]);
        const appDetailsFreeCache = new Map();

        function isAppFree(doc, appId, appDetails) {
            if (appDetails && appDetails.isFree) return true;
            if (!appId) return false;
            const numId = parseInt(appId, 10);
            if (knownFreeAppIds.has(numId)) return true;
            if (appDetailsFreeCache.has(numId)) return appDetailsFreeCache.get(numId);

            if (doc) {
                const freeTag = doc.querySelector('a[href*="/genre/Free"], a[href*="/tag/113"], div[class*="FreeToPlay"]');
                if (freeTag) {
                    appDetailsFreeCache.set(numId, true);
                    return true;
                }
            }

            if (!appDetailsFreeCache.has(numId)) {
                appDetailsFreeCache.set(numId, false);
                fetch(`https://store.steampowered.com/api/appdetails?appids=${numId}`)
                    .then(r => r.json())
                    .then(data => {
                        const isFree = data && data[numId] && data[numId].data && data[numId].data.is_free === true;
                        if (isFree) {
                            appDetailsFreeCache.set(numId, true);
                            getAllSteamDocuments().forEach(d => {
                                scheduleInjectionsForDoc(d);
                            });
                        }
                    })
                    .catch(() => {});
            }

            return appDetailsFreeCache.get(numId) || false;
        }

        function isAppRedirected(appId, appDetails) {
            if (appDetails && appDetails.isShortcut) return true;
            if (!appId) return false;
            const strId = String(appId);
            const numId = parseInt(appId, 10);
            if (appStatusCache && Array.isArray(appStatusCache.unlockedAppIds)) {
                if (appStatusCache.unlockedAppIds.some(id => String(id) === strId || Number(id) === numId)) {
                    return true;
                }
            }
            return false;
        }

        function escapeHtml(text) {
            if (!text) return '';
            return String(text)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        function getBustedTutorialUrl(url) {
            if (!url) return '';
            const parts = url.split('#');
            const baseWithQuery = parts[0];
            const hash = parts.length > 1 ? '#' + parts.slice(1).join('#') : '';
            const separator = baseWithQuery.includes('?') ? '&' : '?';
            return `${baseWithQuery}${separator}embed=steam&_v=${Date.now()}${hash}`;
        }

        // Opens the in-Steam CEF Mini Window Modal
        function openSteamPatchWikiMiniWindow(tutorial, doc) {
            if (!doc || !doc.body) doc = document;
            currentModalTutorial = tutorial;

            let modal = doc.getElementById('cr-patchwiki-modal');
            if (modal) {
                modal.style.display = 'flex';
                modal.style.zIndex = '9999999';

                const titleEl = modal.querySelector('.cr-modal-app-name');
                if (titleEl) titleEl.textContent = tutorial.game || tutorial.title;
                const textEl = modal.querySelector('.cr-modal-title-text');
                if (textEl) textEl.title = tutorial.title;

                const iframe = modal.querySelector('.cr-patchwiki-frame');
                if (iframe) {
                    iframe.src = getBustedTutorialUrl(tutorial.url);
                }

                const extBtn = modal.querySelector('#cr-modal-open-external');
                if (extBtn) extBtn.onclick = () => window.open(tutorial.url, '_blank');

                const deepLink = modal.querySelector('#cr-footer-deep-link');
                if (deepLink) deepLink.textContent = tutorial.id;

                return;
            }

            modal = doc.createElement('div');
            modal.id = 'cr-patchwiki-modal';
            modal.className = 'cr-patchwiki-window';
            modal.innerHTML = `
                <div class="cr-modal-header" id="cr-modal-drag-handle">
                    <div class="cr-modal-title-left">
                        <span class="cr-modal-icon">🛠️</span>
                        <div class="cr-modal-title-text" title="${escapeHtml(tutorial.title)}">
                            <span class="cr-modal-app-name">${escapeHtml(tutorial.game || tutorial.title)}</span>
                            <span class="cr-modal-guide-badge">Tutorial</span>
                        </div>
                    </div>

                    <div class="cr-modal-actions">
                        <button class="cr-modal-btn cr-btn-refresh" title="Refresh Guides (Auto-update)" id="cr-modal-refresh-btn">&#x21bb;</button>

                        <button class="cr-modal-btn cr-btn-external" title="Open in External Browser / Steam Overlay" id="cr-modal-open-external">
                            <svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor">
                                <path d="M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z"/>
                            </svg>
                        </button>

                        <button class="cr-modal-btn cr-btn-maximize" title="Maximize / Restore" id="cr-modal-toggle-maximize">
                            <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor">
                                <path d="M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z"/>
                            </svg>
                        </button>

                        <button class="cr-modal-btn cr-btn-close" title="Close" id="cr-modal-close-btn">✕</button>
                    </div>
                </div>

                <div class="cr-modal-body">
                    <iframe class="cr-patchwiki-frame" src="${getBustedTutorialUrl(tutorial.url)}" allow="clipboard-read; clipboard-write; fullscreen"></iframe>
                </div>

                <div class="cr-modal-footer">
                    <span class="cr-footer-status">✓ Steam CEF In-Client Mini Window • Tutorial Community</span>
                    <span class="cr-footer-link" id="cr-footer-deep-link">${escapeHtml(tutorial.id)}</span>
                </div>
            `;

            // 1. Draggable logic
            const header = modal.querySelector('#cr-modal-drag-handle');
            let isDragging = false;
            let dragStartX = 0;
            let dragStartY = 0;
            let initialLeft = 0;
            let initialTop = 0;

            header.onmousedown = (e) => {
                if (e.target.closest('button') || e.target.closest('a')) {
                    return;
                }
                if (modal.classList.contains('cr-modal-maximized')) return;

                isDragging = true;
                const rect = modal.getBoundingClientRect();
                dragStartX = e.clientX;
                dragStartY = e.clientY;
                initialLeft = rect.left;
                initialTop = rect.top;

                modal.style.left = initialLeft + 'px';
                modal.style.top = initialTop + 'px';
                modal.style.right = 'auto';
                modal.style.bottom = 'auto';

                function onMouseMove(moveEvent) {
                    if (!isDragging) return;
                    const deltaX = moveEvent.clientX - dragStartX;
                    const deltaY = moveEvent.clientY - dragStartY;

                    let newLeft = initialLeft + deltaX;
                    let newTop = initialTop + deltaY;

                    const maxLeft = (doc.defaultView?.innerWidth || window.innerWidth || 1200) - 100;
                    const maxTop = (doc.defaultView?.innerHeight || window.innerHeight || 800) - 50;

                    if (newLeft < 10) newLeft = 10;
                    if (newLeft > maxLeft) newLeft = maxLeft;
                    if (newTop < 10) newTop = 10;
                    if (newTop > maxTop) newTop = maxTop;

                    modal.style.left = newLeft + 'px';
                    modal.style.top = newTop + 'px';
                }

                function onMouseUp() {
                    isDragging = false;
                    doc.removeEventListener('mousemove', onMouseMove);
                    doc.removeEventListener('mouseup', onMouseUp);
                }

                doc.addEventListener('mousemove', onMouseMove);
                doc.addEventListener('mouseup', onMouseUp);
                e.preventDefault();
            };

            // 2. Control buttons
            const refreshBtn = modal.querySelector('#cr-modal-refresh-btn');
            if (refreshBtn) {
                refreshBtn.onclick = async (e) => {
                    e.stopPropagation();
                    refreshBtn.style.transform = 'rotate(360deg)';
                    refreshBtn.style.transition = 'transform 0.4s ease';
                    setTimeout(() => { refreshBtn.style.transform = 'none'; refreshBtn.style.transition = 'none'; }, 400);

                    await refreshPatchWikiData();
                    const iframe = modal.querySelector('.cr-patchwiki-frame');
                    if (iframe) {
                        try {
                            if (iframe.contentWindow) {
                                iframe.contentWindow.postMessage({ type: 'CR_REFRESH_TUTORIALS' }, '*');
                            }
                        } catch (err) { }
                        if (currentModalTutorial) {
                            iframe.src = getBustedTutorialUrl(currentModalTutorial.url);
                        }
                    }
                };
            }

            modal.querySelector('#cr-modal-open-external').onclick = (e) => {
                e.stopPropagation();
                try { window.open(currentModalTutorial.url, '_blank'); } catch (err) { }
            };

            modal.querySelector('#cr-modal-toggle-maximize').onclick = (e) => {
                e.stopPropagation();
                modal.classList.toggle('cr-modal-maximized');
            };

            modal.querySelector('#cr-modal-close-btn').onclick = (e) => {
                e.stopPropagation();
                modal.style.display = 'none';
            };

            modal.onmousedown = () => {
                modal.style.zIndex = '9999999';
            };

            doc.body.appendChild(modal);
        }

        let isInjecting = false;
        let injectionDebounceTimers = new WeakMap();

        function scheduleInjectionsForDoc(doc) {
            if (!doc || !doc.body) return;
            if (injectionDebounceTimers.has(doc)) {
                clearTimeout(injectionDebounceTimers.get(doc));
            }

            const timer = setTimeout(() => {
                injectionDebounceTimers.delete(doc);
                runInjectionsForDoc(doc);
            }, 120);
            injectionDebounceTimers.set(doc, timer);
        }

        // Floating Action Bar (FAB) for active Steam game details
        function injectGameFAB(doc) {
            if (!doc || !doc.body) return;

            try {
                // Remove any old in-line badges or stray buttons from previous versions
                doc.querySelectorAll('#cr-action-group, .cr-action-group, .cr-game-badge, .cr-patchwiki-btn').forEach(el => el.remove());

                const appDetails = getAppDetails(doc, null);
                const appId = appDetails?.appId;
                const gameTitle = appDetails?.title;

                let fab = doc.getElementById('cr-library-fab');

                // Check if currently browsing Store or Community
                const href = doc.defaultView?.location?.href || '';
                if (href.includes('store.steampowered.com') || href.includes('steamcommunity.com')) {
                    if (fab) fab.style.display = 'none';
                    return;
                }

                const activeNav = doc.querySelector('div[class*="supernav_container"] [class*="active"], nav[class*="supernav"] [class*="active"], div[class*="SuperNav"] [class*="Active"]');
                if (activeNav) {
                    const navText = (activeNav.textContent || '').trim().toUpperCase();
                    if (navText.includes('STORE') || navText.includes('COMMUNITY') || navText.includes('POINTS SHOP')) {
                        if (fab) fab.style.display = 'none';
                        return;
                    }
                }

                if (!appId) {
                    if (fab) fab.style.display = 'none';
                    return;
                }

                const strAppId = String(appId);

                if (!fab) {
                    fab = doc.createElement('div');
                    fab.id = 'cr-library-fab';
                    fab.className = 'cr-fab-container';
                    doc.body.appendChild(fab);

                    // Smooth "When drag just drag" anywhere on the FAB with auto-resize docking
                    let isDragging = false;
                    let hasMoved = false;
                    let dragStartX = 0;
                    let dragStartY = 0;
                    let initialLeft = 0;
                    let initialTop = 0;

                    fab.onmousedown = (e) => {
                        if (e.button !== 0) return; // Left click only

                        const rect = fab.getBoundingClientRect();
                        dragStartX = e.clientX;
                        dragStartY = e.clientY;
                        initialLeft = rect.left;
                        initialTop = rect.top;
                        isDragging = true;
                        hasMoved = false;

                        function onMouseMove(ev) {
                            if (!isDragging) return;
                            const dx = ev.clientX - dragStartX;
                            const dy = ev.clientY - dragStartY;
                            if (!hasMoved && Math.hypot(dx, dy) > 4) {
                                hasMoved = true;
                                fab.classList.add('cr-fab-dragging');
                            }
                            if (hasMoved) {
                                const winW = doc.defaultView?.innerWidth || window.innerWidth || 1200;
                                const winH = doc.defaultView?.innerHeight || window.innerHeight || 800;
                                const maxLeft = winW - rect.width - 10;
                                const maxTop = winH - rect.height - 10;
                                const newLeft = Math.max(10, Math.min(maxLeft, initialLeft + dx));
                                const newTop = Math.max(10, Math.min(maxTop, initialTop + dy));

                                fab.style.left = newLeft + 'px';
                                fab.style.top = newTop + 'px';
                                fab.style.right = 'auto';
                                fab.style.bottom = 'auto';
                            }
                        }

                        function onMouseUp() {
                            if (isDragging) {
                                isDragging = false;
                                fab.classList.remove('cr-fab-dragging');
                                if (hasMoved) {
                                    fab.__wasJustDragged = true;
                                    setTimeout(() => { fab.__wasJustDragged = false; }, 120);

                                    // Auto-dock to nearest edges so resizing the Steam window keeps FAB in place
                                    const finalRect = fab.getBoundingClientRect();
                                    const winW = doc.defaultView?.innerWidth || window.innerWidth || 1200;
                                    const winH = doc.defaultView?.innerHeight || window.innerHeight || 800;

                                    if (finalRect.left + finalRect.width / 2 > winW / 2) {
                                        const distRight = Math.max(10, winW - finalRect.right);
                                        fab.style.right = distRight + 'px';
                                        fab.style.left = 'auto';
                                    } else {
                                        fab.style.left = Math.max(10, finalRect.left) + 'px';
                                        fab.style.right = 'auto';
                                    }

                                    if (finalRect.top + finalRect.height / 2 > winH / 2) {
                                        const distBottom = Math.max(10, winH - finalRect.bottom);
                                        fab.style.bottom = distBottom + 'px';
                                        fab.style.top = 'auto';
                                    } else {
                                        fab.style.top = Math.max(10, finalRect.top) + 'px';
                                        fab.style.bottom = 'auto';
                                    }
                                }
                            }
                            doc.removeEventListener('mousemove', onMouseMove);
                            doc.removeEventListener('mouseup', onMouseUp);
                        }

                        doc.addEventListener('mousemove', onMouseMove);
                        doc.addEventListener('mouseup', onMouseUp);
                    };

                    // Auto-resize / reposition listener when Steam CEF window resizes
                    const win = doc.defaultView || window;
                    if (win && !win.__cr_fab_resize_attached) {
                        win.__cr_fab_resize_attached = true;
                        win.addEventListener('resize', () => {
                            const f = doc.getElementById('cr-library-fab');
                            if (f && f.style.display !== 'none') {
                                const rect = f.getBoundingClientRect();
                                const winW = win.innerWidth || 1200;
                                const winH = win.innerHeight || 800;
                                if (rect.right > winW - 10) {
                                    f.style.left = 'auto';
                                    f.style.right = '16px';
                                }
                                if (rect.bottom > winH - 10) {
                                    f.style.top = 'auto';
                                    f.style.bottom = '16px';
                                }
                            }
                        });
                    }
                }

                let tutorial = null;
                if (patchWikiCache) {
                    // Match ONLY by exact Steam AppID
                    if (strAppId && patchWikiCache[strAppId]) {
                        tutorial = patchWikiCache[strAppId];
                    } else if (gameTitle) {
                        // Strict fallback: ONLY if title exactly matches, with minimum 4 chars (NEVER substring/includes)
                        const normTitle = gameTitle.toLowerCase().replace(/[^a-z0-9]/g, '');
                        if (normTitle.length >= 4) {
                            for (const k of Object.keys(patchWikiCache)) {
                                const tItem = patchWikiCache[k];
                                if (!tItem) continue;
                                const itemNorm = (tItem.game || tItem.title || '').toLowerCase().replace(/[^a-z0-9]/g, '');
                                if (itemNorm.length >= 4 && itemNorm === normTitle) {
                                    tutorial = tItem;
                                    break;
                                }
                            }
                        }
                    }
                } else {
                    loadPatchWikiData().then(() => {
                        scheduleInjectionsForDoc(doc);
                    }).catch(() => {});
                }

                if (!appStatusCache) {
                    fetchPluginStatus().then(() => {
                        scheduleInjectionsForDoc(doc);
                    }).catch(() => {});
                }

                // Rule 1: Only show Tutorial button if available!
                const shouldShowTutorial = Boolean(tutorial);

                // Rule 2: Only show CloudRedirect button except own/free game at Steam!
                const isFree = isAppFree(doc, appId, appDetails);
                const isRedirected = isAppRedirected(appId, appDetails);
                const isGenuineOwned = Boolean(appDetails.isOwned && !isRedirected && !appDetails.isShortcut);
                const shouldShowCloudRedirect = !isFree && !isGenuineOwned;

                // If neither button should be shown, hide FAB completely!
                if (!shouldShowTutorial && !shouldShowCloudRedirect) {
                    fab.style.display = 'none';
                    fab.removeAttribute('data-cr-key');
                    return;
                }

                const currentKey = `${strAppId}:${shouldShowCloudRedirect ? 1 : 0}:${shouldShowTutorial ? 1 : 0}:${tutorial?.id || ''}`;
                if (fab.getAttribute('data-cr-key') === currentKey && fab.style.display !== 'none') {
                    return;
                }

                fab.setAttribute('data-appid', strAppId);
                fab.setAttribute('data-cr-key', currentKey);
                fab.style.display = 'inline-flex';

                let innerHtml = '<div class="cr-fab-drag" title="Drag to reposition">⋮⋮</div>';

                if (shouldShowCloudRedirect) {
                    innerHtml += `
                        <div class="cr-fab-item cr-fab-cr" id="cr-fab-cr-btn" title="CloudRedirect Save Protection Active (Click to Open)">
                            ${cloudSvg}
                            <span class="cr-fab-label">CloudRedirect</span>
                            <span class="cr-fab-check">&#10003;</span>
                        </div>
                    `;
                }

                if (shouldShowCloudRedirect && shouldShowTutorial) {
                    innerHtml += '<div class="cr-fab-divider"></div>';
                }

                if (shouldShowTutorial) {
                    innerHtml += `
                        <div class="cr-fab-item cr-fab-wiki" id="cr-fab-wiki-btn" title="Open Tutorial for ${escapeHtml(tutorial.title || tutorial.game)}">
                            <svg class="cr-patchwiki-svg" viewBox="0 0 24 24">
                                <path d="M19 2H6c-1.2 0-2 .9-2 2v16c0 1.1.9 2 2 2h13c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zM6 4h5v8l-2.5-1.5L6 12V4zm13 16H6c-.55 0-1-.45-1-1V5.5c.31.29.7.5 1.17.5H19v14z"/>
                            </svg>
                            <span class="cr-fab-label">Tutorial</span>
                            <span class="cr-patchwiki-pill cr-pill-wiki">Guide</span>
                        </div>
                    `;
                }

                fab.innerHTML = innerHtml;

                // Wire click events
                const crBtn = fab.querySelector('#cr-fab-cr-btn');
                if (crBtn) {
                    crBtn.onclick = (e) => {
                        if (fab.__wasJustDragged) return;
                        e.stopPropagation();
                        e.preventDefault();
                        launchApp(doc);
                    };
                }

                const wikiBtn = fab.querySelector('#cr-fab-wiki-btn');
                if (wikiBtn) {
                    wikiBtn.onclick = (e) => {
                        if (fab.__wasJustDragged) return;
                        e.stopPropagation();
                        e.preventDefault();
                        if (tutorial) {
                            openSteamPatchWikiMiniWindow(tutorial, doc);
                        }
                    };
                }
            } catch (err) {
                console.warn('[CloudRedirect] injectGameFAB error:', err);
            }
        }

        function runInjectionsForDoc(doc) {
            if (!doc || !doc.body || isInjecting) return;
            isInjecting = true;

            try {
                cleanupStrayElements(doc);
                ensureStyles(doc);
                injectSuperNavTab(doc);
                injectGameFAB(doc);
            } finally {
                // Keep guard active across microtasks to ignore mutations caused by our own DOM inserts
                setTimeout(() => {
                    isInjecting = false;
                }, 60);
            }
        }

        function runInjections() {
            const docs = getAllSteamDocuments();
            for (const doc of docs) {
                try {
                    scheduleInjectionsForDoc(doc);
                } catch (e) { }
            }
        }

        function setupObserver() {
            runInjections();

            const observedDocs = new WeakSet();
            function registerDocObserver(d) {
                if (!d || !d.body || observedDocs.has(d)) return;
                observedDocs.add(d);
                try {
                    const observer = new MutationObserver((mutations) => {
                        if (isInjecting) return;

                        // Verify if any mutation was caused by elements outside CloudRedirect
                        let relevantMutation = false;
                        for (let i = 0; i < mutations.length; i++) {
                            const m = mutations[i];
                            const t = m.target;
                            if (t && t.nodeType === 1) {
                                if (t.id === 'cr-patchwiki-modal' ||
                                    t.id === 'cr-library-fab' ||
                                    t.id === 'cloudredirect-supernav-item' ||
                                    t.id === 'cloudredirect-bottom-btn' ||
                                    t.id === 'cr-millennium-styles' ||
                                    t.classList?.contains('cr-fab-container') ||
                                    t.classList?.contains('cr-fab-item') ||
                                    t.classList?.contains('cr-action-group') ||
                                    t.classList?.contains('cr-game-badge') ||
                                    t.classList?.contains('cr-patchwiki-btn') ||
                                    t.closest?.('#cr-library-fab, #cr-patchwiki-modal, #cloudredirect-supernav-item, #cr-millennium-styles, .cr-fab-container')) {
                                    continue;
                                }
                            }
                            if (m.addedNodes && m.addedNodes.length > 0) {
                                let allOurs = true;
                                for (let j = 0; j < m.addedNodes.length; j++) {
                                    const n = m.addedNodes[j];
                                    if (n.nodeType === 1) {
                                        if (n.id === 'cr-library-fab' || n.id === 'cr-patchwiki-modal' || n.id === 'cloudredirect-supernav-item' || n.classList?.contains('cr-fab-container')) {
                                            continue;
                                        }
                                    }
                                    allOurs = false;
                                    break;
                                }
                                if (allOurs) continue;
                            }
                            relevantMutation = true;
                            break;
                        }

                        if (relevantMutation) {
                            scheduleInjectionsForDoc(d);
                        }
                    });
                    observer.observe(d.body, {
                        childList: true,
                        subtree: true
                    });
                } catch (e) { }
            }

            // Periodically check all windows lightly
            setInterval(() => {
                const docs = getAllSteamDocuments();
                for (const d of docs) {
                    registerDocObserver(d);
                }
                runInjections();
            }, 3500);

            try {
                if (typeof Millennium !== 'undefined' && typeof Millennium.AddWindowCreateHook === 'function') {
                    Millennium.AddWindowCreateHook((popup) => {
                        setTimeout(() => runInjections(), 400);
                    });
                }
            } catch (e) { }

            window.addEventListener("millennium-main-window-ready", () => {
                setTimeout(() => runInjections(), 300);
            });
        }

        const index = async function PluginMain() {
            try {
                await Promise.allSettled([fetchPluginStatus(), loadPatchWikiData()]);
            } catch (e) { }

            setupObserver();

            // Auto-refresh PatchWiki tutorials & status every 2.5 minutes so Steam guides are always live
            setInterval(() => {
                refreshPatchWikiData().then(() => {
                    const docs = getAllSteamDocuments();
                    for (const d of docs) {
                        scheduleInjectionsForDoc(d);
                    }
                }).catch(() => {});
                fetchPluginStatus().then(() => {
                    const docs = getAllSteamDocuments();
                    for (const d of docs) {
                        scheduleInjectionsForDoc(d);
                    }
                }).catch(() => {});
            }, 150000);

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
                            const modal = d.getElementById('cr-patchwiki-modal');
                            if (modal) modal.remove();
                            const fab = d.getElementById('cr-library-fab');
                            if (fab) fab.remove();
                            d.querySelectorAll('#cr-action-group, .cr-action-group, .cr-game-badge, .cr-patchwiki-btn').forEach(el => el.remove());
                            const superTab = d.getElementById('cloudredirect-supernav-item');
                            if (superTab) superTab.remove();
                            const topBtn = d.getElementById('cloudredirect-header-btn');
                            if (topBtn) topBtn.remove();
                            d.querySelectorAll('.cr-nav-btn, [id*="cloudredirect-header"], .cr-dropdown-menu').forEach(el => el.remove());
                            d.querySelectorAll('#cloudredirect-bottom-btn, .cr-bottom-bar-btn').forEach(el => el.remove());
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
