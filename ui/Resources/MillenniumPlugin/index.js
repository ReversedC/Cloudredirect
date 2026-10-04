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
                    margin: 0 4px !important;
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
                    margin: 0 4px !important;
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
                .cr-modal-tabs {
                    display: flex;
                    background: #0d141b;
                    border: 1px solid #233748;
                    border-radius: 4px;
                    padding: 2px;
                    margin-right: 6px;
                }
                .cr-tab-btn {
                    background: transparent;
                    border: none;
                    color: #8f98a0;
                    font-size: 11px;
                    font-weight: 600;
                    padding: 3px 10px;
                    border-radius: 3px;
                    cursor: pointer;
                    transition: all 0.15s ease;
                    display: flex;
                    align-items: center;
                    gap: 4px;
                }
                .cr-tab-btn:hover { color: #ffffff; }
                .cr-tab-btn.cr-tab-active {
                    background: #2a475e;
                    color: #66c0f4;
                    box-shadow: 0 1px 3px rgba(0,0,0,0.3);
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
                .cr-tab-pane {
                    display: none;
                    width: 100%;
                    height: 100%;
                    overflow: hidden;
                }
                .cr-tab-pane.cr-pane-active {
                    display: flex;
                    flex-direction: column;
                }
                .cr-patchwiki-frame {
                    width: 100%;
                    height: 100%;
                    border: none;
                    background: #0f1722;
                }
                .cr-reader-container {
                    flex: 1;
                    overflow-y: auto;
                    padding: 20px 28px;
                    line-height: 1.6;
                    color: #dcdedf;
                }
                .cr-reader-summary-card {
                    background: #141f2c;
                    border: 1px solid #233749;
                    border-radius: 6px;
                    padding: 14px 18px;
                    margin-bottom: 20px;
                }
                .cr-reader-meta-row {
                    display: flex;
                    flex-wrap: wrap;
                    gap: 16px;
                    font-size: 12px;
                    color: #8f98a0;
                    margin-bottom: 8px;
                }
                .cr-reader-tags-row {
                    display: flex;
                    flex-wrap: wrap;
                    gap: 6px;
                    margin-bottom: 8px;
                }
                .cr-reader-tag {
                    background: rgba(102, 192, 244, 0.15);
                    border: 1px solid rgba(102, 192, 244, 0.4);
                    color: #66c0f4;
                    padding: 2px 8px;
                    border-radius: 12px;
                    font-size: 11px;
                    font-weight: 600;
                }
                .cr-reader-desc-text {
                    font-size: 13px;
                    color: #c6d4df;
                    margin: 0;
                }
                .cr-reader-body-markdown h1,
                .cr-reader-body-markdown h2,
                .cr-reader-body-markdown h3 {
                    color: #ffffff;
                    margin-top: 18px;
                    margin-bottom: 8px;
                    border-bottom: 1px solid #243547;
                    padding-bottom: 4px;
                }
                .cr-reader-body-markdown h1 { font-size: 18px; }
                .cr-reader-body-markdown h2 { font-size: 15px; color: #66c0f4; }
                .cr-reader-body-markdown h3 { font-size: 13px; color: #a4d007; }
                .cr-reader-body-markdown p { margin: 8px 0; font-size: 13px; }
                .cr-reader-body-markdown ul,
                .cr-reader-body-markdown ol { margin: 8px 0 8px 24px; padding: 0; font-size: 13px; }
                .cr-reader-body-markdown li { margin: 4px 0; }
                .cr-code-block {
                    background: #070b0f;
                    border: 1px solid #1f2f3e;
                    border-radius: 6px;
                    margin: 12px 0;
                    overflow: hidden;
                }
                .cr-code-header {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    background: #111a24;
                    padding: 4px 10px;
                    font-size: 11px;
                    color: #8f98a0;
                    border-bottom: 1px solid #1f2f3e;
                }
                .cr-code-copy-btn {
                    background: #1c2b3a;
                    border: 1px solid #2f4961;
                    color: #66c0f4;
                    border-radius: 3px;
                    padding: 2px 8px;
                    font-size: 11px;
                    cursor: pointer;
                }
                .cr-code-copy-btn:hover { background: #273d52; color: #ffffff; }
                .cr-code-block pre {
                    margin: 0;
                    padding: 10px 14px;
                    overflow-x: auto;
                    font-family: Consolas, "Courier New", monospace;
                    font-size: 12px;
                    color: #9cdcfe;
                }
                .cr-inline-code {
                    background: rgba(0, 0, 0, 0.4);
                    border: 1px solid #233446;
                    padding: 1px 5px;
                    border-radius: 3px;
                    font-family: Consolas, "Courier New", monospace;
                    font-size: 12px;
                    color: #e5c07b;
                }
                .cr-guide-link { color: #66c0f4; text-decoration: underline; }
                .cr-guide-link:hover { color: #ffffff; }
                .cr-reader-loading { padding: 24px 0; color: #8f98a0; font-size: 13px; }
                .cr-reader-fallback-box {
                    padding: 20px;
                    background: #141f2c;
                    border: 1px solid #233749;
                    border-radius: 6px;
                    margin-top: 14px;
                    text-align: center;
                }
                .cr-switch-to-web-btn {
                    background: linear-gradient(to right, #47bfff, #1a9fff);
                    border: none;
                    border-radius: 4px;
                    color: #ffffff;
                    font-weight: 600;
                    font-size: 12px;
                    padding: 6px 16px;
                    cursor: pointer;
                    margin-top: 8px;
                }
                .cr-switch-to-web-btn:hover { background: linear-gradient(to right, #6cd0ff, #38afff); }
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
            try {
                if (doc.getElementById('cloudredirect-bottom-btn')) return;

                let targetSibling = null;
                let parentContainer = null;

                // Strategy 1: Find existing mod buttons like "Steam Unlock"
                const allElements = doc.querySelectorAll('button, div, a');
                for (const el of allElements) {
                    const text = (el.innerText || el.textContent || '').trim();
                    if (text.includes('Steam Unlock') || (el.className && typeof el.className === 'string' && el.className.includes('activation'))) {
                        const btn = el.closest('button, [role="button"], div[class*="button"]') || el;
                        targetSibling = btn;
                        parentContainer = btn.parentNode;
                        break;
                    }
                }

                // Strategy 2: Find "+ Add a Game" button
                if (!targetSibling) {
                    for (const el of allElements) {
                        const text = (el.innerText || el.textContent || '').trim();
                        if (text.includes('Add a Game') || text.includes('Add Game')) {
                            const btn = el.closest('button, [role="button"], div[class*="button"]') || el;
                            targetSibling = btn;
                            parentContainer = btn.parentNode;
                            break;
                        }
                    }
                }

                // Strategy 3: Try standard selectors for Add a Game
                if (!targetSibling) {
                    const addGameCandidates = doc.querySelectorAll('button[class*="addgamebutton_"], div[class*="addgamebutton_"], [class*="AddGameButton"]');
                    for (const el of addGameCandidates) {
                        if (el.offsetWidth > 0 || el.offsetHeight > 0) {
                            targetSibling = el;
                            parentContainer = el.parentNode;
                            break;
                        }
                    }
                }

                // Strategy 4: Fallback to bottom bar container
                if (!parentContainer) {
                    parentContainer = doc.querySelector('div[class*="bottombar_"], div[class*="bottombarcontrols_"], footer, .bottom_bar, [class*="BottomBar"]');
                }

                if (!parentContainer) return;

                const btn = doc.createElement('div');
                btn.id = 'cloudredirect-bottom-btn';
                btn.className = 'cr-bottom-bar-btn';
                btn.title = 'CloudRedirect (Save Protection Active - Click to Open App)';
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
                    parentContainer.insertBefore(btn, targetSibling.nextSibling);
                } else if (parentContainer.firstChild) {
                    parentContainer.insertBefore(btn, parentContainer.firstChild);
                } else {
                    parentContainer.appendChild(btn);
                }
            } catch (err) {
                console.warn('[CloudRedirect] injectBottomBarButton error:', err);
            }
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

        async function loadPatchWikiData() {
            if (patchWikiCache && Object.keys(patchWikiCache).length > 0) return patchWikiCache;
            if (isFetchingPatchWiki) return null;
            isFetchingPatchWiki = true;

            // 1. Try Backend Lua get_patchwiki_tutorials()
            try {
                const res = await __call_server_method__("get_patchwiki_tutorials", {});
                if (res && res.success && res.data) {
                    const parsed = JSON.parse(res.data);
                    if (parsed && typeof parsed === 'object' && Object.keys(parsed).length > 0) {
                        patchWikiCache = parsed;
                        isFetchingPatchWiki = false;
                        return patchWikiCache;
                    }
                }
            } catch (e) { }

            // 2. Fetch directly from GitHub Pages index.json
            try {
                const resp = await fetch("https://mirzaarsyad74-cmyk.github.io/patchwiki/index.json", { cache: "force-cache" });
                if (resp.ok) {
                    const list = await resp.json();
                    if (Array.isArray(list)) {
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
                        patchWikiCache = map;
                        isFetchingPatchWiki = false;
                        return patchWikiCache;
                    }
                }
            } catch (e) {
                console.warn('[CloudRedirect] PatchWiki web fetch error:', e);
            }

            isFetchingPatchWiki = false;
            return patchWikiCache || {};
        }

        function getAppDetails(doc, targetEl) {
            let appId = null;
            let title = null;

            // 1. Traverse React Fiber on targetEl and its ancestors
            if (targetEl) {
                try {
                    let curr = targetEl;
                    let depth = 0;
                    while (curr && depth < 20) {
                        for (const k of Object.keys(curr)) {
                            if (k.startsWith('__reactFiber$') || k.startsWith('__reactInternalInstance$')) {
                                let fiber = curr[k];
                                for (let i = 0; fiber && i < 35; i++) {
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
                                        if (appId && title) return { appId, title };
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

            // 2. Check selected game in library list sidebar (class _1UBpAXP408Ez_L_mXhW5Q9 / Selected)
            if (doc && (!appId || !title)) {
                try {
                    const selected = doc.querySelector('div[class*="_1UBpAXP408Ez_L_mXhW5Q9"], div[class*="Selected"], div[class*="selected_"]');
                    if (selected) {
                        let curr = selected;
                        let depth = 0;
                        while (curr && depth < 10) {
                            for (const k of Object.keys(curr)) {
                                if (k.startsWith('__reactFiber$') || k.startsWith('__reactInternalInstance$')) {
                                    let fiber = curr[k];
                                    for (let i = 0; fiber && i < 25; i++) {
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
                                            if (appId && title) return { appId, title };
                                        }
                                        fiber = fiber.return;
                                    }
                                }
                            }
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

            // 3. Fallback: Banner / Hero Images (e.g. https://shared.steamstatic.com/store_item_assets/steam/apps/1020790/header.jpg)
            if (doc && !appId) {
                try {
                    const imgs = doc.querySelectorAll('img[src*="/apps/"], img[src*="/app/"]');
                    for (const img of imgs) {
                        const src = img.getAttribute('src') || '';
                        const m = src.match(/(?:app|apps)\/(\d{3,9})/i);
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

            // 4. Fallback: Links (Store Page, Community Hub, etc.)
            if (doc && !appId) {
                try {
                    const links = doc.querySelectorAll('a[href*="/app/"], a[href*="steam://nav/games/details/"], a[href*="rungameid/"]');
                    for (const a of links) {
                        const href = a.getAttribute('href') || '';
                        const m = href.match(/(?:app\/|details\/|rungameid\/)(\d{3,9})/i);
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

            // 5. Fallback: doc.title for Title
            if (!title && doc && doc.title) {
                try {
                    let clean = doc.title.replace(/^Steam\s*[-–]\s*/i, '').replace(/\s*[-–]\s*Steam$/i, '').trim();
                    if (clean && !clean.toLowerCase().includes('library') && !clean.toLowerCase().includes('steam')) {
                        title = clean;
                    }
                } catch (e) { }
            }

            return { appId, title };
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

        function renderMarkdown(md) {
            if (!md) return '';
            let html = escapeHtml(md);

            // Code blocks
            html = html.replace(/```([a-zA-Z0-9_-]*)\n([\s\S]*?)```/g, (match, lang, code) => {
                return `<div class="cr-code-block"><div class="cr-code-header"><span>${lang || 'Code'}</span><button class="cr-code-copy-btn">Copy</button></div><pre><code>${code}</code></pre></div>`;
            });

            // Inline code
            html = html.replace(/`([^`]+)`/g, '<code class="cr-inline-code">$1</code>');

            // Headers
            html = html.replace(/^### (.*$)/gim, '<h3>$1</h3>');
            html = html.replace(/^## (.*$)/gim, '<h2>$1</h2>');
            html = html.replace(/^# (.*$)/gim, '<h1>$1</h1>');

            // Bold
            html = html.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

            // Links
            html = html.replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" target="_blank" class="cr-guide-link">$1</a>');

            // Blockquotes
            html = html.replace(/^> (.*$)/gim, '<blockquote>$1</blockquote>');

            // Unordered lists
            html = html.replace(/^\s*[-*]\s+(.*$)/gim, '<li>$1</li>');
            html = html.replace(/(<li>.*<\/li>)/gms, '<ul>$1</ul>');

            // Line breaks
            html = html.replace(/\n\n+/g, '</p><p>').replace(/\n/g, '<br>');
            return `<p>${html}</p>`;
        }

        async function loadReaderContent(modal, tutorial) {
            const container = modal.querySelector('#cr-reader-content-area');
            if (!container) return;

            const tagsHtml = (tutorial.tags && Array.isArray(tutorial.tags))
                ? tutorial.tags.map(t => `<span class="cr-reader-tag">#${escapeHtml(t)}</span>`).join(' ')
                : '';

            container.innerHTML = `
                <div class="cr-reader-summary-card">
                    <div class="cr-reader-meta-row">
                        ${tutorial.author ? `<span class="cr-meta-item">👤 <b>Author:</b> ${escapeHtml(tutorial.author)}</span>` : ''}
                        ${tutorial.date ? `<span class="cr-meta-item">📅 <b>Updated:</b> ${escapeHtml(tutorial.date)}</span>` : ''}
                        ${tutorial.appId ? `<span class="cr-meta-item">🆔 <b>AppID:</b> ${escapeHtml(tutorial.appId.toString())}</span>` : ''}
                    </div>
                    ${tagsHtml ? `<div class="cr-reader-tags-row">${tagsHtml}</div>` : ''}
                    ${tutorial.desc ? `<p class="cr-reader-desc-text">${escapeHtml(tutorial.desc)}</p>` : ''}
                </div>
                <div class="cr-reader-body-markdown">
                    <div class="cr-reader-loading">⏳ Loading full tutorial guide content...</div>
                </div>
            `;

            const bodyArea = container.querySelector('.cr-reader-body-markdown');

            try {
                let chunkFile = null;
                if (manifestCache && manifestCache[tutorial.id]) {
                    chunkFile = manifestCache[tutorial.id];
                } else {
                    const mfResp = await fetch("https://mirzaarsyad74-cmyk.github.io/patchwiki/manifest.json");
                    if (mfResp.ok) {
                        manifestCache = await mfResp.json();
                        chunkFile = manifestCache[tutorial.id];
                    }
                }

                if (chunkFile) {
                    const chunkResp = await fetch("https://mirzaarsyad74-cmyk.github.io/patchwiki/" + chunkFile);
                    if (chunkResp.ok) {
                        const chunkData = await chunkResp.json();
                        let guide = null;
                        if (Array.isArray(chunkData)) {
                            guide = chunkData.find(x => x.id === tutorial.id);
                        } else if (chunkData && typeof chunkData === 'object') {
                            guide = chunkData[tutorial.id] || chunkData;
                        }

                        if (guide && guide.content) {
                            bodyArea.innerHTML = renderMarkdown(guide.content);
                            bodyArea.querySelectorAll('.cr-code-copy-btn').forEach(btn => {
                                btn.onclick = () => {
                                    const code = btn.closest('.cr-code-block')?.querySelector('code')?.innerText || '';
                                    if (navigator.clipboard) {
                                        navigator.clipboard.writeText(code);
                                        const orig = btn.textContent;
                                        btn.textContent = '✓ Copied!';
                                        setTimeout(() => btn.textContent = orig, 2000);
                                    }
                                };
                            });
                            return;
                        }
                    }
                }
            } catch (e) {
                console.warn('[CloudRedirect] Error fetching tutorial chunk:', e);
            }

            bodyArea.innerHTML = `
                <div class="cr-reader-fallback-box">
                    <p>Full interactive guide available in Web View.</p>
                    <button class="cr-switch-to-web-btn">Switch to 🌐 Web View</button>
                </div>
            `;
            bodyArea.querySelector('.cr-switch-to-web-btn')?.addEventListener('click', () => {
                modal.querySelector('.cr-tab-btn[data-tab="web"]')?.click();
            });
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
                if (iframe && iframe.src !== tutorial.url) {
                    iframe.src = tutorial.url;
                }

                const extBtn = modal.querySelector('#cr-modal-open-external');
                if (extBtn) extBtn.onclick = () => window.open(tutorial.url, '_blank');

                const deepLink = modal.querySelector('#cr-footer-deep-link');
                if (deepLink) deepLink.textContent = tutorial.id;

                if (tutorial.id && tutorial.id.startsWith('search-')) {
                    modal.querySelector('.cr-tab-btn[data-tab="web"]')?.click();
                } else {
                    const activeTab = modal.querySelector('.cr-tab-btn.cr-tab-active');
                    if (activeTab && activeTab.getAttribute('data-tab') === 'reader') {
                        loadReaderContent(modal, tutorial);
                    }
                }
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
                            <span class="cr-modal-guide-badge">PatchWiki</span>
                        </div>
                    </div>

                    <div class="cr-modal-actions">
                        <div class="cr-modal-tabs">
                            <button class="cr-tab-btn cr-tab-active" data-tab="web" title="Interactive Web View">🌐 Web</button>
                            <button class="cr-tab-btn" data-tab="reader" title="Fast Offline Reader">📖 Reader</button>
                        </div>

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
                    <div class="cr-tab-pane cr-pane-web cr-pane-active">
                        <iframe class="cr-patchwiki-frame" src="${tutorial.url}" allow="clipboard-read; clipboard-write; fullscreen"></iframe>
                    </div>

                    <div class="cr-tab-pane cr-pane-reader">
                        <div class="cr-reader-container">
                            <div id="cr-reader-content-area"></div>
                        </div>
                    </div>
                </div>

                <div class="cr-modal-footer">
                    <span class="cr-footer-status">✓ Steam CEF In-Client Mini Window • PatchWiki Community</span>
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
                if (e.target.closest('button') || e.target.closest('.cr-modal-tabs') || e.target.closest('a')) {
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

            // 2. Tab switching
            const tabBtns = modal.querySelectorAll('.cr-tab-btn');
            tabBtns.forEach(btn => {
                btn.onclick = (e) => {
                    e.stopPropagation();
                    const tab = btn.getAttribute('data-tab');
                    modal.querySelectorAll('.cr-tab-btn').forEach(b => b.classList.remove('cr-tab-active'));
                    btn.classList.add('cr-tab-active');

                    modal.querySelectorAll('.cr-tab-pane').forEach(p => p.classList.remove('cr-pane-active'));
                    const pane = modal.querySelector(`.cr-pane-${tab}`);
                    if (pane) pane.classList.add('cr-pane-active');

                    if (tab === 'reader') {
                        loadReaderContent(modal, currentModalTutorial);
                    }
                };
            });

            // 3. Control buttons
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

        function findGameActionAnchors(doc) {
            if (!doc || !doc.body) return [];
            const anchors = [];
            const seen = new Set();

            function addAnchor(el, type, container) {
                if (!el || seen.has(el)) return;
                seen.add(el);
                anchors.push({ el, type, container: container || el.parentElement || el });
            }

            // 1. Primary: Install or Play action buttons (AppActionButton)
            // Production Steam CSS hash: QsZdWtHTlIK9KIKbscNTt
            const appActionBtns = doc.querySelectorAll('div[class*="QsZdWtHTlIK9KIKbscNTt"], button[class*="QsZdWtHTlIK9KIKbscNTt"], [class*="AppActionButton"], [class*="PlayButton"]');
            appActionBtns.forEach(btn => {
                if (btn.offsetWidth > 0 || btn.offsetHeight > 0) {
                    addAnchor(btn, 'action-button', btn.parentElement);
                }
            });

            // 2. Text-based search for Play/Install buttons if not found by class
            if (anchors.length === 0) {
                const candidateBtns = doc.querySelectorAll('button, div[role="button"], div[class*="Button"], a[role="button"]');
                for (const btn of candidateBtns) {
                    if (btn.offsetWidth === 0 || btn.offsetHeight === 0) continue;
                    // Ignore sidebar, titlebar, supernav, bottombar
                    if (btn.closest('#cloudredirect-supernav-item, .cr-supernav-menu, [class*="GameList"], [class*="TitleBar"], [class*="BottomBar"], [class*="bottombar"]')) continue;

                    const t = (btn.innerText || btn.textContent || '').trim().toUpperCase();
                    if (t.startsWith('INSTALL') || t.startsWith('PLAY') || t.startsWith('LAUNCH') || t.startsWith('RESUME') || t.startsWith('UPDATE') || t.startsWith('PRE-LOAD') || t.startsWith('STREAM')) {
                        if (btn.offsetWidth >= 35 && btn.offsetHeight >= 18) {
                            addAnchor(btn, 'action-button', btn.parentElement);
                        }
                    }
                }
            }

            // 3. ActionButtonAndStatusPanel (_1fHBRg7vFnKszK6EiOdIEY) or ActionRow (_2Gj21aHb49txPLFed_iV9M)
            const panels = doc.querySelectorAll('div[class*="_1fHBRg7vFnKszK6EiOdIEY"], div[class*="_2Gj21aHb49txPLFed_iV9M"], [class*="ActionButtonAndStatusPanel"], [class*="ActionRow"]');
            panels.forEach(p => {
                if ((p.offsetWidth > 0 || p.offsetHeight > 0) && !seen.has(p)) {
                    addAnchor(p, 'action-panel', p);
                }
            });

            // 4. Sticky PlayBar (_3fLo166MlaNqP8r8tTyRz / _2zxzStp5qY5M1evOm8keES)
            const stickyPlayBars = doc.querySelectorAll('div[class*="_3fLo166MlaNqP8r8tTyRz"], div[class*="_2zxzStp5qY5M1evOm8keES"], div[class*="PlayBar"], div[class*="ActionSection"]');
            stickyPlayBars.forEach(pb => {
                if ((pb.offsetWidth > 0 || pb.offsetHeight > 0) && !seen.has(pb)) {
                    addAnchor(pb, 'sticky-bar', pb);
                }
            });

            // 5. RightControls (_1EAxK56o5a9Nieu5HYkJ4k / _1thLDT_28YIf6OkgIb6n-4 / AppButtons)
            if (anchors.length === 0) {
                const rightControls = doc.querySelectorAll('div[class*="_1EAxK56o5a9Nieu5HYkJ4k"], div[class*="_1thLDT_28YIf6OkgIb6n-4"], [class*="RightControls"], [class*="AppButtons"]');
                rightControls.forEach(rc => {
                    if ((rc.offsetWidth > 0 || rc.offsetHeight > 0) && !seen.has(rc)) {
                        addAnchor(rc, 'right-controls', rc.parentElement);
                    }
                });
            }

            return anchors;
        }

        // 2. Inject Game Details Page Badge and PatchWiki Button
        function injectGameBadge(doc) {
            if (!doc || !doc.body) return;

            try {
                const anchors = findGameActionAnchors(doc);
                if (!anchors || anchors.length === 0) return;

                for (const item of anchors) {
                    const { el: anchorEl, type, container } = item;
                    if (!container) continue;

                    const appDetails = getAppDetails(doc, anchorEl) || getAppDetails(doc, null);
                    const appId = appDetails?.appId;
                    const gameTitle = appDetails?.title;

                    // If we don't have an appId, skip this anchor
                    if (!appId) continue;

                    const strAppId = String(appId);

                    // 1. Handle CloudRedirect Badge
                    let badge = container.querySelector('.cr-game-badge');
                    if (badge && badge.getAttribute('data-appid') !== strAppId) {
                        badge.remove();
                        badge = null;
                    }

                    if (!badge) {
                        badge = doc.createElement('div');
                        badge.className = 'cr-game-badge';
                        badge.setAttribute('data-appid', strAppId);
                        badge.title = 'Game save files are actively redirected and backed up by CloudRedirect (Click to Open)';
                        badge.innerHTML = `
                            ${cloudSvg}
                            <span>CloudRedirect</span>
                            <span class="cr-game-badge-check">&#10003;</span>
                        `;
                        badge.onclick = (e) => {
                            e.stopPropagation();
                            e.preventDefault();
                            launchApp(doc);
                        };
                    }

                    // 2. Handle PatchWiki Guide Button
                    let pwBtn = container.querySelector('.cr-patchwiki-btn');

                    // Find matching tutorial from PatchWiki cache
                    let tutorial = null;
                    if (patchWikiCache) {
                        tutorial = patchWikiCache[strAppId];
                        if (!tutorial && gameTitle) {
                            const normTitle = gameTitle.toLowerCase().replace(/[^a-z0-9]/g, '');
                            if (normTitle.length >= 3) {
                                for (const k of Object.keys(patchWikiCache)) {
                                    const tItem = patchWikiCache[k];
                                    const itemNorm = (tItem.game || tItem.title || '').toLowerCase().replace(/[^a-z0-9]/g, '');
                                    if (itemNorm.includes(normTitle) || normTitle.includes(itemNorm)) {
                                        tutorial = tItem;
                                        break;
                                    }
                                }
                            }
                        }
                    } else {
                        loadPatchWikiData().then(() => {
                            if (doc) injectGameBadge(doc);
                        }).catch(() => {});
                    }

                    const hasTutorial = Boolean(tutorial);
                    if (pwBtn && (pwBtn.getAttribute('data-appid') !== strAppId || pwBtn.getAttribute('data-has-tutorial') !== String(hasTutorial))) {
                        pwBtn.remove();
                        pwBtn = null;
                    }

                    if (!pwBtn) {
                        pwBtn = doc.createElement('div');
                        pwBtn.className = 'cr-patchwiki-btn';
                        pwBtn.setAttribute('data-appid', strAppId);
                        pwBtn.setAttribute('data-has-tutorial', String(hasTutorial));

                        if (tutorial) {
                            pwBtn.title = `Open PatchWiki Guide for ${tutorial.title || tutorial.game} (In-Steam Mini Window)`;
                            pwBtn.innerHTML = `
                                <svg class="cr-patchwiki-svg" viewBox="0 0 24 24">
                                    <path d="M19 2H6c-1.2 0-2 .9-2 2v16c0 1.1.9 2 2 2h13c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zM6 4h5v8l-2.5-1.5L6 12V4zm13 16H6c-.55 0-1-.45-1-1V5.5c.31.29.7.5 1.17.5H19v14z"/>
                                </svg>
                                <span>PatchWiki Guide</span>
                                <span class="cr-patchwiki-pill cr-pill-wiki">Wiki</span>
                            `;
                            pwBtn.onclick = (e) => {
                                e.stopPropagation();
                                e.preventDefault();
                                openSteamPatchWikiMiniWindow(tutorial, doc);
                            };
                        } else {
                            pwBtn.title = `Search PatchWiki Community Guides for ${gameTitle || ('App ' + appId)} (In-Steam Mini Window)`;
                            pwBtn.innerHTML = `
                                <svg class="cr-patchwiki-svg" viewBox="0 0 24 24">
                                    <path d="M19 2H6c-1.2 0-2 .9-2 2v16c0 1.1.9 2 2 2h13c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zM6 4h5v8l-2.5-1.5L6 12V4zm13 16H6c-.55 0-1-.45-1-1V5.5c.31.29.7.5 1.17.5H19v14z"/>
                                </svg>
                                <span>PatchWiki Guide</span>
                                <span class="cr-patchwiki-pill cr-pill-search">Guide</span>
                            `;
                            pwBtn.onclick = (e) => {
                                e.stopPropagation();
                                e.preventDefault();
                                const searchObj = {
                                    id: 'search-' + appId,
                                    appId: appId,
                                    game: gameTitle || ('App ' + appId),
                                    title: gameTitle ? `${gameTitle} - Community Guides` : `App ${appId} - Community Guides`,
                                    url: `https://mirzaarsyad74-cmyk.github.io/patchwiki/?search=${encodeURIComponent(gameTitle || appId)}&appid=${appId}`,
                                    tags: ['guide'],
                                    author: 'Community',
                                    date: new Date().toISOString().split('T')[0],
                                    desc: `Search PatchWiki guides and tutorials for ${gameTitle || appId}`
                                };
                                openSteamPatchWikiMiniWindow(searchObj, doc);
                            };
                        }
                    }

                    // 3. Insert Elements
                    if (type === 'action-button' && anchorEl.parentNode === container) {
                        if (anchorEl.nextSibling !== badge) {
                            container.insertBefore(badge, anchorEl.nextSibling);
                        }
                        if (badge.nextSibling !== pwBtn) {
                            container.insertBefore(pwBtn, badge.nextSibling);
                        }
                    } else if (type === 'right-controls' && anchorEl.parentNode === container) {
                        if (badge.parentNode !== container) {
                            container.insertBefore(badge, anchorEl);
                        }
                        if (pwBtn.parentNode !== container) {
                            container.insertBefore(pwBtn, anchorEl);
                        }
                    } else {
                        if (badge.parentNode !== container) {
                            container.appendChild(badge);
                        }
                        if (pwBtn.parentNode !== container) {
                            container.appendChild(pwBtn);
                        }
                    }
                }
            } catch (err) {
                console.warn('[CloudRedirect] injectGameBadge error:', err);
            }
        }

        function runInjectionsForDoc(doc) {
            if (!doc || !doc.body) return;

            try {
                // Remove any legacy header buttons or clipped dropdown menus
                const legacyBtn = doc.getElementById('cloudredirect-header-btn');
                if (legacyBtn) legacyBtn.remove();
                doc.querySelectorAll('.cr-nav-btn, [id*="cloudredirect-header"], .cr-dropdown-menu').forEach(el => el.remove());
            } catch (e) { }

            try {
                // If supernav item has old classes or wrong height/display, replace it
                const superItem = doc.getElementById('cloudredirect-supernav-item');
                if (superItem) {
                    if (superItem.style.height || superItem.style.display || superItem.querySelector('.cr-supernav-btn')?.style.height) {
                        superItem.remove();
                    } else if (superItem.classList.contains('cr-active')) {
                        superItem.classList.remove('cr-active');
                    }
                }
            } catch (e) { }

            try { ensureStyles(doc); } catch (e) { }
            try { injectSuperNavTab(doc); } catch (e) { }
            try { injectBottomBarButton(doc); } catch (e) { }
            try { injectGameBadge(doc); } catch (e) { }
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
            loadPatchWikiData().catch(() => {});

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
                            d.querySelectorAll('.cr-patchwiki-btn').forEach(el => el.remove());
                            const superTab = d.getElementById('cloudredirect-supernav-item');
                            if (superTab) superTab.remove();
                            const topBtn = d.getElementById('cloudredirect-header-btn');
                            if (topBtn) topBtn.remove();
                            d.querySelectorAll('.cr-nav-btn, [id*="cloudredirect-header"], .cr-dropdown-menu').forEach(el => el.remove());
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
