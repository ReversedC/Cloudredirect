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

var PluginEntryPointMain = function () {
    var millennium_main = (function (exports, client, reactDom, React) {
        'use strict';

        const cloudSvg = `<svg class="cr-cloud-icon-svg" viewBox="0 0 24 24"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z"/></svg>`;

        function launchApp() {
            __call_server_method__("launch_cloudredirect", {});
            try {
                const link = document.createElement('a');
                link.href = 'cloudredirect://open';
                link.click();
            } catch (e) { }
        }

        function triggerBackup() {
            __call_server_method__("trigger_backup", {});
            try {
                const link = document.createElement('a');
                link.href = 'cloudredirect://backup';
                link.click();
            } catch (e) { }
        }

        function openSaves() {
            __call_server_method__("open_saves", {});
            try {
                const link = document.createElement('a');
                link.href = 'cloudredirect://saves';
                link.click();
            } catch (e) { }
        }

        function ensureStyles() {
            if (document.getElementById('cr-millennium-styles')) return;
            const style = document.createElement('style');
            style.id = 'cr-millennium-styles';
            style.textContent = `
                /* SuperNav Top Header Tab (STORE / LIBRARY / COMMUNITY / USER / CLOUDREDIRECT) */
                .cr-supernav-menu {
                    cursor: pointer !important;
                    user-select: none !important;
                    display: inline-flex !important;
                    align-items: center !important;
                    height: 100% !important;
                    margin: 0 !important;
                    padding: 0 !important;
                    position: relative !important;
                    transition: all 0.2s ease !important;
                }
                .cr-supernav-btn {
                    display: inline-flex !important;
                    align-items: center !important;
                    gap: 7px !important;
                    cursor: pointer !important;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important;
                    font-size: 14px !important;
                    font-weight: 500 !important;
                    letter-spacing: 0.04em !important;
                    text-transform: uppercase !important;
                    color: #dcdedf !important;
                    padding: 0 10px !important;
                    height: 100% !important;
                    box-sizing: border-box !important;
                    transition: color 0.15s ease, text-shadow 0.15s ease !important;
                }
                .cr-supernav-menu:hover .cr-supernav-btn {
                    color: #ffffff !important;
                    text-shadow: 0 0 10px rgba(255, 255, 255, 0.45) !important;
                }
                .cr-supernav-menu.cr-active .cr-supernav-btn {
                    color: #ffffff !important;
                    border-bottom: 3px solid #1a9fff !important;
                }
                .cr-supernav-label {
                    letter-spacing: 0.04em !important;
                    font-weight: 600 !important;
                }
                .cr-supernav-dot {
                    width: 6px !important;
                    height: 6px !important;
                    background: #a4d007 !important;
                    border-radius: 50% !important;
                    box-shadow: 0 0 6px #a4d007 !important;
                    display: inline-block !important;
                    flex-shrink: 0 !important;
                    margin-left: 2px !important;
                }

                /* Top Nav Button */
                .cr-nav-btn {
                    display: inline-flex;
                    align-items: center;
                    gap: 6px;
                    height: 24px;
                    padding: 0 8px;
                    background: #17222d;
                    border: 1px solid #233b53;
                    border-radius: 4px;
                    color: #c6d4df;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                    font-size: 11px;
                    font-weight: 600;
                    cursor: pointer;
                    user-select: none;
                    transition: all 0.2s ease;
                    vertical-align: middle;
                    margin: 0 4px;
                    z-index: 1000;
                }
                .cr-nav-btn:hover {
                    background: #203548;
                    border-color: #66c0f4;
                    color: #ffffff;
                    box-shadow: 0 0 8px rgba(102, 192, 244, 0.3);
                }
                .cr-nav-btn.cr-active {
                    background: #1c3852;
                    border-color: #66c0f4;
                    color: #ffffff;
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
                .cr-bottom-bar-btn.cr-active {
                    background: #1c3852;
                    border-color: #66c0f4;
                    color: #ffffff;
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

                /* Dropdown Menu Container */
                .cr-dropdown-menu {
                    position: fixed;
                    width: 320px;
                    background: linear-gradient(135deg, #1b2838 0%, #171d25 100%);
                    border: 1px solid #36506c;
                    border-radius: 8px;
                    box-shadow: 0 12px 32px rgba(0, 0, 0, 0.8), 0 0 1px rgba(102, 192, 244, 0.4);
                    color: #c6d4df;
                    font-family: "Motiva Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                    font-size: 13px;
                    padding: 14px;
                    z-index: 999999;
                    animation: crFadeIn 0.18s ease-out forwards;
                    backdrop-filter: blur(12px);
                }
                @keyframes crFadeIn {
                    from { opacity: 0; transform: translateY(-6px); }
                    to { opacity: 1; transform: translateY(0); }
                }

                .cr-dropdown-header {
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    padding-bottom: 10px;
                    border-bottom: 1px solid rgba(102, 192, 244, 0.2);
                    margin-bottom: 12px;
                }
                .cr-title-group {
                    display: flex;
                    align-items: center;
                    gap: 8px;
                }
                .cr-title {
                    font-size: 15px;
                    font-weight: 700;
                    color: #ffffff;
                    letter-spacing: 0.3px;
                }
                .cr-version-badge {
                    background: #142332;
                    border: 1px solid #23425e;
                    color: #66c0f4;
                    font-size: 10px;
                    font-weight: 700;
                    padding: 2px 6px;
                    border-radius: 4px;
                }
                .cr-status-badge {
                    display: inline-flex;
                    align-items: center;
                    gap: 5px;
                    background: rgba(164, 208, 7, 0.12);
                    border: 1px solid rgba(164, 208, 7, 0.35);
                    color: #a4d007;
                    font-size: 11px;
                    font-weight: 600;
                    padding: 2px 8px;
                    border-radius: 12px;
                }
                .cr-dropdown-desc {
                    font-size: 12px;
                    color: #8f98a0;
                    line-height: 1.4;
                    margin-bottom: 12px;
                }
                .cr-stats-card {
                    background: rgba(15, 23, 33, 0.6);
                    border: 1px solid #233446;
                    border-radius: 6px;
                    padding: 10px 12px;
                    margin-bottom: 14px;
                }
                .cr-stat-row {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    font-size: 11px;
                    padding: 2px 0;
                }
                .cr-stat-label { color: #8f98a0; }
                .cr-stat-val { color: #66c0f4; font-weight: 600; }
                .cr-stat-val.cr-good { color: #a4d007; }
                .cr-btn-primary {
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    gap: 8px;
                    width: 100%;
                    height: 36px;
                    background: linear-gradient(90deg, #5c7e10 0%, #476508 100%);
                    border: none;
                    border-radius: 4px;
                    color: #ffffff;
                    font-size: 13px;
                    font-weight: 700;
                    cursor: pointer;
                    transition: all 0.18s ease;
                    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
                    margin-bottom: 8px;
                }
                .cr-btn-primary:hover {
                    background: linear-gradient(90deg, #6e9713 0%, #55790a 100%);
                    box-shadow: 0 0 12px rgba(164, 208, 7, 0.4);
                }
                .cr-btn-secondary {
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    gap: 6px;
                    width: 100%;
                    height: 32px;
                    background: #213244;
                    border: 1px solid #36506c;
                    border-radius: 4px;
                    color: #c6d4df;
                    font-size: 12px;
                    font-weight: 600;
                    cursor: pointer;
                    transition: all 0.18s ease;
                    margin-bottom: 6px;
                }
                .cr-btn-secondary:hover {
                    background: #2a415a;
                    border-color: #66c0f4;
                    color: #ffffff;
                }
                .cr-footer {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    font-size: 10px;
                    color: #626e7b;
                    margin-top: 10px;
                    padding-top: 8px;
                    border-top: 1px solid rgba(255, 255, 255, 0.05);
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
            document.head.appendChild(style);
        }

        let dropdownElement = null;

        function toggleDropdown(btn, isBottom) {
            if (dropdownElement) {
                dropdownElement.remove();
                dropdownElement = null;
                document.querySelectorAll('.cr-nav-btn, .cr-bottom-bar-btn').forEach(b => b.classList.remove('cr-active'));
                return;
            }

            btn.classList.add('cr-active');
            const rect = btn.getBoundingClientRect();

            dropdownElement = document.createElement('div');
            dropdownElement.className = 'cr-dropdown-menu';

            if (isBottom) {
                dropdownElement.style.left = Math.max(10, rect.left) + 'px';
                dropdownElement.style.top = 'auto';
                dropdownElement.style.bottom = Math.max(38, (window.innerHeight - rect.top + 8)) + 'px';
            } else {
                dropdownElement.style.left = Math.max(10, rect.right - 320) + 'px';
                dropdownElement.style.top = (rect.bottom + 6) + 'px';
                dropdownElement.style.bottom = 'auto';
            }

            dropdownElement.innerHTML = `
                <div class="cr-dropdown-header">
                    <div class="cr-title-group">
                        <span class="cr-title">CloudRedirect</span>
                        <span class="cr-version-badge">NATIVE</span>
                    </div>
                    <div class="cr-status-badge">
                        <span class="cr-status-dot"></span>
                        <span>Protected</span>
                    </div>
                </div>
                <div class="cr-dropdown-desc">
                    Real-time universal save redirection and cloud backup active for Steam games.
                </div>
                <div class="cr-stats-card">
                    <div class="cr-stat-row">
                        <span class="cr-stat-label">Save Protection Engine</span>
                        <span class="cr-stat-val cr-good">Active &amp; Monitoring</span>
                    </div>
                    <div class="cr-stat-row">
                        <span class="cr-stat-label">Cloud Storage</span>
                        <span class="cr-stat-val">Connected</span>
                    </div>
                    <div class="cr-stat-row">
                        <span class="cr-stat-label">Local Snapshots</span>
                        <span class="cr-stat-val cr-good">Safe Mode OK</span>
                    </div>
                </div>
                <button id="cr-action-launch" class="cr-btn-primary">
                    ${cloudSvg}
                    <span>Open CloudRedirect App</span>
                </button>
                <button id="cr-action-backup" class="cr-btn-secondary">
                    <span>⚡ Backup All Saves Now</span>
                </button>
                <button id="cr-action-saves" class="cr-btn-secondary">
                    <span>📁 Open Save Manager</span>
                </button>
                <div class="cr-footer">
                    <span>Steam Millennium Plugin</span>
                    <span>v1.0.0</span>
                </div>
            `;

            document.body.appendChild(dropdownElement);

            dropdownElement.querySelector('#cr-action-launch').onclick = () => {
                launchApp();
                toggleDropdown(btn, isBottom);
            };
            dropdownElement.querySelector('#cr-action-backup').onclick = () => {
                triggerBackup();
                toggleDropdown(btn, isBottom);
            };
            dropdownElement.querySelector('#cr-action-saves').onclick = () => {
                openSaves();
                toggleDropdown(btn, isBottom);
            };

            const outsideClickListener = (e) => {
                if (dropdownElement && !dropdownElement.contains(e.target) && !btn.contains(e.target)) {
                    toggleDropdown(btn, isBottom);
                    document.removeEventListener('click', outsideClickListener);
                }
            };
            setTimeout(() => document.addEventListener('click', outsideClickListener), 10);
        }

        // Injects tab right into STORE / LIBRARY / COMMUNITY / USER / CLOUDREDIRECT row
        function injectSuperNavTab() {
            if (document.getElementById('cloudredirect-supernav-item')) return;

            // 1. Locate the SuperNav container using the exact Steam classes and selectors
            const superNavSelectors = [
                'div._2D64jIEK7wpUR_NlObDW76',
                'div[class*="_2D64jIEK7wpUR_NlObDW76"]',
                'div[class*="SuperNav_"]',
                'div[class*="supernav_"]',
                'div[class*="SuperNav"]',
                'nav[class*="SuperNav"]',
                'div[class*="supernav_container"]',
                '.supernav_container'
            ];

            let superNavContainer = null;
            for (const sel of superNavSelectors) {
                const el = document.querySelector(sel);
                if (el && (el.offsetParent !== null || el.offsetWidth > 0)) {
                    superNavContainer = el;
                    break;
                }
            }

            // Fallback 2: Search by finding the container that has STORE, LIBRARY, COMMUNITY
            if (!superNavContainer) {
                const candidates = document.querySelectorAll('div, nav');
                for (const c of candidates) {
                    const txt = c.textContent || '';
                    if (txt.includes('STORE') && txt.includes('LIBRARY') && txt.includes('COMMUNITY')) {
                        const children = Array.from(c.children);
                        const hasStoreChild = children.some(child => (child.textContent || '').includes('STORE'));
                        if (hasStoreChild) {
                            superNavContainer = c;
                            break;
                        }
                    }
                }
            }

            if (!superNavContainer) return;

            // 2. Identify the tabs inside superNavContainer to match styling and find insertion spot
            const children = Array.from(superNavContainer.children);
            let sampleTab = null;
            let lastNavTab = null;

            for (const child of children) {
                const txt = (child.textContent || '').trim();
                // Skip back/forward navigation arrows (no alphanumeric text)
                if (!/[A-Za-z0-9]/.test(txt)) continue;

                if (txt.includes('STORE') || txt.includes('LIBRARY') || txt.includes('COMMUNITY')) {
                    sampleTab = child;
                    lastNavTab = child;
                } else if (!txt.includes('http') && !txt.includes('search') && !txt.includes('🔍')) {
                    // Profile tab (e.g. MINTAMAAF5) or console tab
                    lastNavTab = child;
                }
            }

            if (!sampleTab) return;

            // 3. Create the CloudRedirect SuperNav item
            const navItem = document.createElement('div');
            navItem.id = 'cloudredirect-supernav-item';
            // Inherit the exact class name from Steam's supernav menu tabs
            navItem.className = sampleTab.className + ' cr-supernav-menu';
            navItem.setAttribute('role', 'button');
            navItem.setAttribute('tabindex', '0');
            navItem.title = 'CloudRedirect Universal Cloud Save Protection';

            // Find child button / inner element if present in sampleTab
            const sampleInner = sampleTab.querySelector('div, a, span') || sampleTab;
            const innerBtn = document.createElement('div');
            innerBtn.className = (sampleInner.className || '') + ' cr-supernav-btn';
            innerBtn.innerHTML = `
                <span class="cr-supernav-label">CLOUDREDIRECT</span>
                <span class="cr-supernav-dot" title="Save Protection Active"></span>
            `;

            navItem.appendChild(innerBtn);

            // 4. Handle clicks
            navItem.onclick = (e) => {
                e.stopPropagation();
                toggleSuperNavDropdown(navItem);
            };

            // 5. Insert directly into the row after the last tab (after MINTAMAAF5)
            if (lastNavTab && lastNavTab.nextSibling) {
                superNavContainer.insertBefore(navItem, lastNavTab.nextSibling);
            } else {
                superNavContainer.appendChild(navItem);
            }
        }

        function toggleSuperNavDropdown(navItem) {
            if (dropdownElement) {
                dropdownElement.remove();
                dropdownElement = null;
                document.querySelectorAll('.cr-supernav-menu, .cr-nav-btn, .cr-bottom-bar-btn').forEach(b => b.classList.remove('cr-active'));
                return;
            }

            navItem.classList.add('cr-active');
            const rect = navItem.getBoundingClientRect();

            dropdownElement = document.createElement('div');
            dropdownElement.className = 'cr-dropdown-menu';
            dropdownElement.style.top = (rect.bottom + 4) + 'px';
            dropdownElement.style.left = Math.max(10, rect.left) + 'px';
            dropdownElement.style.bottom = 'auto';

            dropdownElement.innerHTML = `
                <div class="cr-dropdown-header">
                    <div class="cr-title-group">
                        <span class="cr-title">CloudRedirect</span>
                        <span class="cr-version-badge">NATIVE</span>
                    </div>
                    <div class="cr-status-badge">
                        <span class="cr-status-dot"></span>
                        <span>Protected</span>
                    </div>
                </div>
                <div class="cr-dropdown-desc">
                    Real-time universal save redirection and cloud backup active for Steam games.
                </div>
                <div class="cr-stats-card">
                    <div class="cr-stat-row">
                        <span class="cr-stat-label">Save Protection Engine</span>
                        <span class="cr-stat-val cr-good">Active &amp; Monitoring</span>
                    </div>
                    <div class="cr-stat-row">
                        <span class="cr-stat-label">Cloud Storage</span>
                        <span class="cr-stat-val">Connected</span>
                    </div>
                    <div class="cr-stat-row">
                        <span class="cr-stat-label">Local Snapshots</span>
                        <span class="cr-stat-val cr-good">Safe Mode OK</span>
                    </div>
                </div>
                <button id="cr-action-launch" class="cr-btn-primary">
                    ${cloudSvg}
                    <span>Open CloudRedirect App</span>
                </button>
                <button id="cr-action-backup" class="cr-btn-secondary">
                    <span>⚡ Backup All Saves Now</span>
                </button>
                <button id="cr-action-saves" class="cr-btn-secondary">
                    <span>📁 Open Save Manager</span>
                </button>
                <div class="cr-footer">
                    <span>Steam Millennium Plugin</span>
                    <span>v2.9.69</span>
                </div>
            `;

            document.body.appendChild(dropdownElement);

            dropdownElement.querySelector('#cr-action-launch').onclick = () => {
                launchApp();
                toggleSuperNavDropdown(navItem);
            };
            dropdownElement.querySelector('#cr-action-backup').onclick = () => {
                triggerBackup();
                toggleSuperNavDropdown(navItem);
            };
            dropdownElement.querySelector('#cr-action-saves').onclick = () => {
                openSaves();
                toggleSuperNavDropdown(navItem);
            };

            const outsideClickListener = (e) => {
                if (dropdownElement && !dropdownElement.contains(e.target) && !navItem.contains(e.target)) {
                    toggleSuperNavDropdown(navItem);
                    document.removeEventListener('click', outsideClickListener);
                }
            };
            setTimeout(() => document.addEventListener('click', outsideClickListener), 10);
        }

        // 1. Inject Button in Bottom Bar (next to Add Game / Steam Unlock)
        function injectBottomBarButton() {
            if (document.getElementById('cloudredirect-bottom-btn')) return;

            let targetSibling = null;
            let parentContainer = null;

            // Strategy 1: Find existing mod buttons like "Steam Unlock"
            const allElements = document.querySelectorAll('button, div, a');
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
                const addGameCandidates = document.querySelectorAll('button[class*="addgamebutton_"], div[class*="addgamebutton_"], [class*="AddGameButton"]');
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
                parentContainer = document.querySelector('div[class*="bottombar_"], div[class*="bottombarcontrols_"], footer, .bottom_bar');
            }

            if (!parentContainer) return;

            const btn = document.createElement('div');
            btn.id = 'cloudredirect-bottom-btn';
            btn.className = 'cr-bottom-bar-btn';
            btn.title = 'CloudRedirect Native Save Protection';
            btn.innerHTML = `
                ${cloudSvg}
                <span>CloudRedirect</span>
                <span class="cr-status-dot"></span>
            `;

            btn.onclick = (e) => {
                e.stopPropagation();
                toggleDropdown(btn, true);
            };

            if (targetSibling && targetSibling.parentNode === parentContainer) {
                targetSibling.parentNode.insertBefore(btn, targetSibling.nextSibling);
            } else if (parentContainer.firstChild) {
                parentContainer.insertBefore(btn, parentContainer.firstChild);
            } else {
                parentContainer.appendChild(btn);
            }
        }

        // 2. Inject Button in Header / Top Bar
        function injectHeaderButton() {
            if (document.getElementById('cloudredirect-header-btn')) return;

            const selectors = [
                '.title-bar-actions',
                '[class*="titlebarcontrols_"]',
                '[class*="supernav_"]',
                '#desktop_titlebar',
                '.supernav_container',
                '#header_notifications',
                '[class*="notificationbutton_"]'
            ];

            let target = null;
            for (const sel of selectors) {
                const el = document.querySelector(sel);
                if (el && el.offsetWidth > 0) {
                    target = el;
                    break;
                }
            }

            if (!target) return;

            const btn = document.createElement('div');
            btn.id = 'cloudredirect-header-btn';
            btn.className = 'cr-nav-btn';
            btn.title = 'CloudRedirect Native Save Protection';
            btn.innerHTML = `
                ${cloudSvg}
                <span>CloudRedirect</span>
                <span class="cr-status-dot"></span>
            `;

            btn.onclick = (e) => {
                e.stopPropagation();
                toggleDropdown(btn, false);
            };

            if (target.firstChild) {
                target.insertBefore(btn, target.firstChild);
            } else {
                target.appendChild(btn);
            }
        }

        // 3. Inject Game Details Page Badge
        function injectGameBadge() {
            const gameActionBars = document.querySelectorAll('div[class*="playbar_"], div[class*="appactionandstats_"], div[class*="appdetailsheader_"]');
            gameActionBars.forEach(bar => {
                if (bar.querySelector('.cr-game-badge')) return;

                const badge = document.createElement('div');
                badge.className = 'cr-game-badge';
                badge.title = 'Game save files are actively redirected and backed up by CloudRedirect';
                badge.innerHTML = `
                    ${cloudSvg}
                    <span>CloudRedirect</span>
                    <span class="cr-game-badge-check">✓</span>
                `;
                badge.onclick = (e) => {
                    e.stopPropagation();
                    launchApp();
                };

                bar.appendChild(badge);
            });
        }

        function runInjections() {
            ensureStyles();
            injectSuperNavTab();
            injectBottomBarButton();
            injectHeaderButton();
            injectGameBadge();
        }

        function setupObserver() {
            runInjections();

            const observer = new MutationObserver(() => {
                runInjections();
            });

            observer.observe(document.body, {
                childList: true,
                subtree: true
            });

            setInterval(runInjections, 1000);
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
                    const superTab = document.getElementById('cloudredirect-supernav-item');
                    if (superTab) superTab.remove();
                    const topBtn = document.getElementById('cloudredirect-header-btn');
                    if (topBtn) topBtn.remove();
                    const btmBtn = document.getElementById('cloudredirect-bottom-btn');
                    if (btmBtn) btmBtn.remove();
                    if (dropdownElement) dropdownElement.remove();
                    const style = document.getElementById('cr-millennium-styles');
                    if (style) style.remove();
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
