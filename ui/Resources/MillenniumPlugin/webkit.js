(function injectSteamDbStyles() {
    try {
        if (!document.getElementById("steamdb-styles")) {
            const link1 = document.createElement("link");
            link1.id = "steamdb-styles";
            link1.rel = "stylesheet";
            link1.href = "https://cdn.jsdelivr.net/gh/SteamDatabase/BrowserExtension@latest/styles/store.css";
            document.head.appendChild(link1);

            const link2 = document.createElement("link");
            link2.rel = "stylesheet";
            link2.href = "https://cdn.jsdelivr.net/gh/SteamDatabase/BrowserExtension@latest/styles/community.css";
            document.head.appendChild(link2);
        }
    } catch(e) {}
})();

const pluginName = "CloudRedirect";
function InitializePlugins() {
    /**
     * This function is called n times depending on n plugin count,
     * Create the plugin list if it wasn't already created
     */
    !window.PLUGIN_LIST && (window.PLUGIN_LIST = {});
    // initialize a container for the plugin
    if (!window.PLUGIN_LIST[pluginName]) {
        window.PLUGIN_LIST[pluginName] = {};
    }
}
InitializePlugins()
const __call_server_method__ = (methodName, kwargs) => {
    if (window.MILLENNIUM_API && typeof window.MILLENNIUM_API.ffi === 'function') {
        const fn = window.MILLENNIUM_API.ffi(pluginName, methodName);
        if (typeof fn === 'function') {
            if (methodName === 'GetAppPrice' && kwargs && kwargs.appid) {
                return fn(String(kwargs.appid), String(kwargs.currency || 'USD'));
            } else if (kwargs && kwargs.appid) {
                return fn(String(kwargs.appid));
            }
            return fn(kwargs);
        }
    }
    return Millennium.callServerMethod(pluginName, methodName, kwargs);
};

const __wrapped_callable__ = (route) => {
    if (window.MILLENNIUM_API && typeof window.MILLENNIUM_API.ffi === 'function') {
        const fn = window.MILLENNIUM_API.ffi(pluginName, route);
        if (typeof fn === 'function') {
            return async (kwargs) => {
                if (route === 'GetAppPrice' && kwargs && kwargs.appid) {
                    return await fn(String(kwargs.appid), String(kwargs.currency || 'USD'));
                } else if (kwargs && kwargs.appid) {
                    return await fn(String(kwargs.appid));
                }
                return await fn(kwargs);
            };
        }
    }
    return MILLENNIUM_API.callable(__call_server_method__, route);
};

var millennium_main=function(t){"use strict";!function(){const t={};try{if(process)return process.env=Object.assign({},process.env),void Object.assign(process.env,t)}catch(t){}globalThis.process={env:t}}();const e="4.14",s=`https://cdn.jsdelivr.net/gh/SteamDatabase/BrowserExtension@${e}`;function o(t){return t.startsWith("/")?`${s}${t}`:`${s}/${t}`}async function c(t){return new Promise(((e,s)=>{const o=document.createElement("script");o.setAttribute("type","text/javascript"),o.setAttribute("src",t),o.addEventListener("load",(()=>{e()})),o.addEventListener("error",(()=>{s(new Error("Failed to load script"))})),document.head.appendChild(o)}))}async function n(t){return new Promise(((e,s)=>{const o=document.createElement("link");o.setAttribute("rel","stylesheet"),o.setAttribute("type","text/css"),o.setAttribute("href",t),o.addEventListener("load",(()=>{e()})),o.addEventListener("error",(()=>{s(new Error("Failed to load style"))})),document.head.appendChild(o)}))}const a=__wrapped_callable__("Logger.error"),i=__wrapped_callable__("Logger.warn"),r=(...t)=>{console.error("%c SteamDB plugin ","background: red; color: white",...t),a({message:t.join(" ")})},m=(...t)=>{console.log("%c SteamDB plugin ","background: purple; color: white",...t)},p=(...t)=>{console.warn("%c SteamDB plugin ","background: orange; color: white",...t),i({message:t.join(" ")})};window.steamDBBrowser={runtime:{id:"kdbmhfkmnlmbkgbabkdealhhbfhlmmon",getURL:t=>`${s}/${t}`,sendMessage:async function(t){const e=__wrapped_callable__(t.contentScriptQuery),s=await e(t);return typeof s === 'string' ? JSON.parse(s) : s}},storage:{sync:{onChanged:{addListener(t){h.push(t)}},get:async function(t){const e=u(),s={};if(Array.isArray(t))t.forEach((t=>{t in e&&(s[t]=e[t])}));else if("object"==typeof t)for(const o in t)s[o]=o in e?e[o]:t[o];return Promise.resolve(s)},set:async function(t){const e=u(),s=Object.keys(t)[0];if(void 0===s)return;return h.forEach((o=>{o({[s]:{oldValue:e[s],newValue:t[s]}})})),Object.assign(e,t),localStorage.setItem(l,JSON.stringify(e)),Promise.resolve()}}},permissions:{request(){},contains:(t,e)=>{e(!0)},onAdded:{addListener(){}},onRemoved:{addListener(){}}},i18n:{getMessage:function(t,s){if("@@bidi_dir"===t)return t;Array.isArray(s)||(s=[s]);const o=JSON.parse(localStorage.getItem(f+e)??"{}");if(null===o||0===Object.keys(o).length)return r("SteamDB lang file not loaded in."),t;const c=o[t];if(void 0===c)return r(`Unknown message key: ${t}`),t;let n=c.message;c.placeholders&&Object.entries(c.placeholders).forEach((([t,e],o)=>{const c=new RegExp(`\\$${t}\\$`,"g");n=n.replace(c,s[o]??e.content)}));return n},getUILanguage:()=>"en-US"}};const l="steamdb-options";function u(){const t=localStorage.getItem(l);try{return null!==t?JSON.parse(t):{}}catch{throw new Error(`Failed to parse JSON for key: ${l}`)}}const h=[];const d="steamDB_";let f="";async function y(){const t=navigator.language.replace("-","_"),o=t.split("_")[0]??"en";f=d+o,"es_419"===t&&(f=`${d}es_419`);const c=d+t;if(null===localStorage.getItem(f+e)){if(null!==localStorage.getItem(c+e))return m(`using "${t}" lang`),void(f=c);async function n(t){return fetch(`${s}/_locales/${t}/messages.json`)}m(`fetching "${o}" lang`);let a=await n(o);if(a.ok||(p(`failed to fetch SteamDB lang file for "${o}". Trying "${t}"`),f=c,a=await n(t),a.ok||(p(`failed to fetch SteamDB lang file for "${t}". Falling back to EN.`),f=`${d}en`,a=await n("en"))),!a.ok)throw new Error("Failed to load any language file.");localStorage.setItem(f+e,JSON.stringify(await a.json()))}m(`using "${f.replace(d,"")}" lang`)}const g=document.createElement.bind(document),w=["pcgamingwiki.com"],_=["steamdb.info"];function $(t){const e=new MutationObserver((s=>{s.forEach((s=>{"attributes"===s.type&&"href"===s.attributeName&&(!function(t){w.forEach((e=>{t.href.includes(e)&&(t.href="steam://openurl_external/"+t.href)}))}(t),function(t){_.forEach((e=>{t.href.includes(e)&&(t.onclick=e=>{if(e.ctrlKey)return;e.preventDefault();const s=new MouseEvent("click",{bubbles:!0,cancelable:!0,view:window,ctrlKey:!0});t.dispatchEvent(s)})}))}(t),e.disconnect())}))}));e.observe(t,{attributes:!0})}function b(){let t={WEBAPI_BASE_URL:"https://api.steampowered.com/"};for(const e of document.querySelectorAll("script[src]")){const s=new URL(e.src).searchParams.get("l");if(null!==s){t.LANGUAGE=s;break}}const e=document.querySelector(".profile_small_header_additional .gameLogo img")?.src;if(void 0===e)return;const s=e.lastIndexOf("/apps/");s>0&&(t.STORE_ICON_BASE_URL=e.substring(0,s+6));for(const e of document.querySelectorAll(".achieveImgHolder > img")){const s=e.src.lastIndexOf("/images/apps/");if(s>0){t.MEDIA_CDN_COMMUNITY_URL=e.src.substring(0,s+1);break}}t={...t,COUNTRY:navigator.language.split("-")[1],STORE_ITEM_BASE_URL:"https://shared.fastly.steamstatic.com/store_item_assets/"};const o=document.createElement("div");o.id="application_config",o.dataset.config=JSON.stringify(t),o.dataset.loyalty_webapi_token="false",document.body.appendChild(o)}function v(){const t=document.querySelector(".two_column.left"),e=document.querySelector(".two_column.right");if(!t||!e)return;const a=document.createElement("div");a.setAttribute("id","steamdb-options"),a.classList.add("nav_item"),a.innerHTML=`<img class="ico16" src="${s}/icons/white.svg" alt="logo"> <span>SteamDB Options</span>`,t.appendChild(a),a.addEventListener("click",(async()=>async function(t,e,a){t.querySelectorAll(".active").forEach((t=>{t.classList.remove("active")})),e.classList.toggle("active");const i=new URL(window.location.href);i.search="",i.searchParams.set("steamdb","true"),window.history.replaceState({},"",i.href),a.innerHTML=await(await fetch(`${s}/options/options.html`)).text(),await Promise.all([n(o("/options/options.css")),c(o("/options/options.js"))]);const r=document.createElement("div");r.onclick=()=>{window.confirm("Are you sure you want to reset all options?")&&(localStorage.removeItem(l),window.location.reload())},r.classList.add("store_header_btn"),r.classList.add("store_header_btn_gray"),r.style.position="fixed",r.style.bottom="1em",r.style.right="1em",r.style.cursor="pointer";const m=document.createElement("span");m.dataset.tooltipText="Will reset all options to their default values.",m.innerText="Reset options!",m.style.margin="1em",r.appendChild(m),a.appendChild(r)}(t,a,e)));"true"===new URL(window.location.href).searchParams.get("steamdb")&&a.click()}async function j(t){const e=[];for(const s of t.filter((t=>t.includes(".css"))))e.push(n(o(s)));await Promise.all(e)}async function k(){let t=await(await fetch(o("scripts/common.min.js"))).text();t=t.replaceAll("browser","steamDBBrowser"),function(t){const e=document.createElement("script");e.setAttribute("type","text/javascript"),e.innerHTML=t,document.head.appendChild(e)}(t)}document.createElement=function(t,e){const s=g(t,e);return"a"===t.toLowerCase()&&$(s),s};const E=[/steamcommunity\.com\/stats\//,/steamcommunity\.com\/id\/.+?\/stats\//];return t.default=async function(){const t=window.location.href;if(!t.includes("https://store.steampowered.com")&&!t.includes("https://steamcommunity.com"))return;m("plugin is running");const e=function(){const t=window.location.href,e=[];return t.match(/^https:\/\/store\.steampowered\.com\/app\/.*$/)&&(e.push("scripts/store/app_error.js"),e.push("scripts/store/app.js"),e.push("scripts/store/app_images.js")),t.match(/^https:\/\/store\.steampowered\.com\/news\/app\/.*$/)&&(e.push("scripts/store/app_error.js"),e.push("scripts/store/app_news.js")),t.match(/^https:\/\/store\.steampowered\.com\/account\/licenses.*$/)&&(e.push("scripts/store/account_licenses.js"),e.push("styles/account_licenses.css")),t.match(/^https:\/\/store\.steampowered\.com\/account\/registerkey.*$/)&&e.push("scripts/store/registerkey.js"),t.match(/^https:\/\/store\.steampowered\.com\/sub\/.*$/)&&e.push("scripts/store/sub.js"),t.match(/^https:\/\/store\.steampowered\.com\/bundle\/.*$/)&&e.push("scripts/store/bundle.js"),t.match(/^https:\/\/store\.steampowered\.com\/widget\/.*$/)&&e.push("scripts/store/widget.js"),(t.match(/^https:\/\/store\.steampowered\.com\/app\/.*\/agecheck$/)||t.match(/^https:\/\/store\.steampowered\.com\/agecheck\/.*$/))&&(e.push("scripts/store/app_error.js"),e.push("scripts/store/agecheck.js")),t.match(/^https:\/\/store\.steampowered\.com\/explore.*$/)&&e.push("scripts/store/explore.js"),(t.match(/^https:\/\/store\.steampowered\.com\/app\/.*$/)||t.match(/^https:\/\/steamcommunity\.com\/app\/.*$/)||t.match(/^https:\/\/steamcommunity\.com\/sharedfiles\/filedetails.*$/)||t.match(/^https:\/\/steamcommunity\.com\/workshop\/filedetails.*$/)||t.match(/^https:\/\/steamcommunity\.com\/workshop\/browse.*$/)||t.match(/^https:\/\/steamcommunity\.com\/workshop\/discussions.*$/))&&e.push("scripts/appicon.js"),(t.match(/^https:\/\/steamcommunity\.com\/id\/.*$/)||t.match(/^https:\/\/steamcommunity\.com\/profiles\/.*$/))&&e.push("scripts/community/profile.js"),(t.match(/^https:\/\/steamcommunity\.com\/id\/.*\/inventory.*$/)||t.match(/^https:\/\/steamcommunity\.com\/profiles\/.*\/inventory.*$/))&&(e.push("scripts/community/profile_inventory.js"),e.push("styles/inventory.css")),(t.match(/^https:\/\/steamcommunity\.com\/id\/.*\/stats.*$/)||t.match(/^https:\/\/steamcommunity\.com\/profiles\/.*\/stats.*$/))&&(e.push("scripts/community/achievements.js"),e.push("scripts/community/achievements_profile.js"),e.push("styles/achievements.css")),(t.match(/^https:\/\/steamcommunity\.com\/id\/.*\/stats\/CSGO.*$/)||t.match(/^https:\/\/steamcommunity\.com\/profiles\/.*\/stats\/CSGO.*$/))&&(e.push("scripts/community/achievements_cs2.js"),e.push("styles/achievements_cs2.css")),t.match(/^https:\/\/steamcommunity\.com\/stats\/.*\/achievements.*$/)&&(e.push("scripts/community/achievements.js"),e.push("scripts/community/achievements_global.js"),e.push("styles/achievements.css")),t.match(/^https:\/\/steamcommunity\.com\/tradeoffer\/.*$/)&&!t.match(/^https:\/\/steamcommunity\.com\/tradeoffer\/.*\/confirm.*$/)&&e.push("scripts/community/tradeoffer.js"),(t.match(/^https:\/\/steamcommunity\.com\/id\/.*\/recommended\/.*$/)||t.match(/^https:\/\/steamcommunity\.com\/profiles\/.*\/recommended\/.*$/))&&e.push("scripts/community/profile_recommended.js"),(t.match(/^https:\/\/steamcommunity\.com\/id\/.*\/badges.*$/)||t.match(/^https:\/\/steamcommunity\.com\/profiles\/.*\/badges.*$/))&&e.push("scripts/community/profile_badges.js"),(t.match(/^https:\/\/steamcommunity\.com\/id\/.*\/gamecards\/.*$/)||t.match(/^https:\/\/steamcommunity\.com\/profiles\/.*\/gamecards\/.*$/))&&e.push("scripts/community/profile_gamecards.js"),(t.match(/^https:\/\/steamcommunity\.com\/app\/.*$/)||t.match(/^https:\/\/steamcommunity\.com\/sharedfiles\/filedetails.*$/)||t.match(/^https:\/\/steamcommunity\.com\/workshop\/filedetails.*$/)||t.match(/^https:\/\/steamcommunity\.com\/workshop\/browse.*$/)||t.match(/^https:\/\/steamcommunity\.com\/workshop\/discussions.*$/))&&e.push("scripts/community/gamehub.js"),(t.match(/^https:\/\/steamcommunity\.com\/sharedfiles\/filedetails.*$/)||t.match(/^https:\/\/steamcommunity\.com\/workshop\/filedetails.*$/))&&(e.push("scripts/community/filedetails.js"),e.push("scripts/community/filedetails_guide.js")),t.match(/^https:\/\/steamcommunity\.com\/market\/multibuy.*$/)&&e.push("scripts/community/multibuy.js"),t.match(/^https:\/\/steamcommunity\.com\/market\/.*$/)&&(e.push("scripts/community/market.js"),e.push("styles/market.css")),(t.match(/^https:\/\/steamcommunity\.com\/app\/.*$/)||t.match(/^https:\/\/steamcommunity\.com\/games\/.*$/)||t.match(/^https:\/\/steamcommunity\.com\/sharedfiles\/.*$/)||t.match(/^https:\/\/steamcommunity\.com\/workshop\/.*$/))&&e.push("scripts/community/agecheck.js"),(t.match(/^https:\/\/steamcommunity\.com\/market\/.*$/)||t.match(/^https:\/\/steamcommunity\.com\/id\/.*\/inventory.*$/)||t.match(/^https:\/\/steamcommunity\.com\/profiles\/.*\/inventory.*$/))&&e.push("scripts/community/market_ssa.js"),e}();await Promise.all([k(),y(),j(e)]),await c(o("scripts/global.min.js"));for(const e of E)if(e.test(t)){b();break}await async function(t){const e=t.filter((t=>t.includes(".js")));for(const t of e)await c(o(t.replace(".js",".min.js")))}(e),function(){const t=document.evaluate('//a[contains(@class, "popup_menu_item") and contains(text(), "Preferences")]',document,null,XPathResult.FIRST_ORDERED_NODE_TYPE,null).singleNodeValue;if(null!==t){const e=t.cloneNode();e.href+="&steamdb=true",e.innerHTML=`\n            <img class="ico16" style="background: none" src="${o("/icons/white.svg")}" alt="logo">\n            <span>${window.steamDBBrowser.i18n.getMessage("steamdb_options")}</span>\n        `,t.after(e)}}(),window.location.href.includes("https://store.steampowered.com/account")&&v()},Object.defineProperty(t,"__esModule",{value:!0}),t}({},window.MILLENNIUM_API);

function ExecuteWebkitModule() {
    // Assign the plugin on plugin list. 
    Object.assign(window.PLUGIN_LIST[pluginName], millennium_main);
    // Run the rolled up plugins default exported function 
    millennium_main["default"]();

    // Deduplicate any SteamDB elements if an external Chrome extension is also active
    setInterval(() => {
        try {
            const stats = document.querySelectorAll('.steamdb_stats');
            if (stats && stats.length > 1) {
                for (let i = 1; i < stats.length; i++) {
                    stats[i].remove();
                }
            }
            const prices = document.querySelectorAll('.steamdb_prices');
            if (prices && prices.length > 1) {
                for (let i = 1; i < prices.length; i++) {
                    prices[i].remove();
                }
            }
        } catch (e) {}
    }, 500);
}
ExecuteWebkitModule()