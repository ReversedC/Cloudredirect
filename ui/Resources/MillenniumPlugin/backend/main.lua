pcall(function()
    if jit and jit.off then
        jit.off()
    end
end)

local logger = nil
pcall(function() logger = require("logger") end)
local millennium = nil
pcall(function() millennium = require("millennium") end)

local function log_info(msg)
    if logger and logger.info then
        logger:info("[CloudRedirect] " .. tostring(msg))
    else
        print("[CloudRedirect] " .. tostring(msg))
    end
end

local function log_error(msg)
    if logger and logger.error then
        logger:error("[CloudRedirect] " .. tostring(msg))
    else
        print("[CloudRedirect ERROR] " .. tostring(msg))
    end
end

-- ============================================================================
-- 1. CLOUDREDIRECT COMPANION RPC
-- ============================================================================

-- Launches CloudRedirect application
function launch_cloudredirect()
    log_info("launch_cloudredirect invoked from Steam WebUI")
    local res = os.execute('start "" "cloudredirect://open"')
    return { success = true, result = res }
end

-- Triggers save backup
function trigger_backup()
    log_info("trigger_backup invoked from Steam WebUI")
    local res = os.execute('start "" "cloudredirect://backup"')
    return { success = true, result = res }
end

-- Opens Universal Saves Manager
function open_saves()
    log_info("open_saves invoked from Steam WebUI")
    local res = os.execute('start "" "cloudredirect://saves"')
    return { success = true, result = res }
end

-- Restarts Steam
function restart_steam()
    log_info("restart_steam invoked from Steam WebUI")
    local res = os.execute('start "" "steam://restart"')
    return { success = true, result = res }
end

-- Get status from status file
function get_status()
    local appdata = os.getenv("APPDATA")
    if not appdata then
        return { isRunning = true, status = "active" }
    end
    local status_file = appdata .. "\\CloudRedirect\\steam_plugin_status.json"
    local f = io.open(status_file, "r")
    if f then
        local content = f:read("*a")
        f:close()
        return { isRunning = true, data = content }
    end
    return { isRunning = true, status = "active", message = "CloudRedirect Active" }
end

-- ============================================================================
-- 2. TASKBAR DOWNLOAD PROGRESS
-- ============================================================================

local ok_tb, taskbar = pcall(require, "taskbar")
if ok_tb and taskbar then
    log_info("Taskbar progress module loaded successfully")
else
    log_info("Taskbar progress module not available: " .. tostring(taskbar))
end

function set_progress_percent(kwargs)
    local pct = -1
    if type(kwargs) == "table" then
        pct = kwargs.percent or kwargs["percent"] or -1
    elseif type(kwargs) == "number" then
        pct = kwargs
    end
    if ok_tb and taskbar and taskbar.set_progress_percent then
        return taskbar.set_progress_percent(pct)
    end
    return false
end

function get_plugin_status()
    return "Ready"
end

-- ============================================================================
-- 3. STEAMDB RPC PROXY
-- ============================================================================

local ok_http, http = pcall(require, "http")

local function steamdb_get(url)
    if not ok_http or not http or not http.get then
        return '{"success":false,"data":{}}'
    end
    local req_opts = {
        headers = {
            ["Accept"] = "application/json",
            ["X-Requested-With"] = "SteamDB",
            ["Origin"] = "https://github.com/BossSloth/Steam-SteamDB-extension",
            ["Sec-Fetch-Dest"] = "empty",
            ["Sec-Fetch-Mode"] = "cors",
            ["Sec-Fetch-Site"] = "cross-site"
        },
        user_agent = "https://github.com/BossSloth/Steam-SteamDB-extension",
        timeout = 15
    }
    local ok, res = pcall(http.get, url, req_opts)
    if ok and res and res.body and #res.body > 0 then
        return res.body
    end
    return '{"success":false,"data":{}}'
end

---@ffi
---@param appid any
---@return string
function GetApp(appid)
    if type(appid) == "table" then
        appid = appid.appid or appid["appid"]
    end
    return steamdb_get("https://extension.steamdb.info/api/ExtensionApp/?appid=" .. tostring(appid or 0))
end

---@ffi
---@param appid any
---@param currency any
---@return string
function GetAppPrice(appid, currency)
    if type(appid) == "table" then
        currency = appid.currency or appid["currency"]
        appid = appid.appid or appid["appid"]
    end
    currency = tostring(currency or "USD")
    return steamdb_get("https://extension.steamdb.info/api/ExtensionAppPrice/?appid=" .. tostring(appid or 0) .. "&currency=" .. currency)
end

---@ffi
---@param appid any
---@return string
function GetAchievementsGroups(appid)
    if type(appid) == "table" then
        appid = appid.appid or appid["appid"]
    end
    return steamdb_get("https://extension.steamdb.info/api/ExtensionGetAchievements/?appid=" .. tostring(appid or 0))
end

-- ============================================================================
-- 4. MILLENNIUM LIFECYCLE HOOKS
-- ============================================================================

local function on_frontend_loaded()
    log_info("Frontend UI loaded into Steam CEF successfully")
end

local function on_load()
    log_info("Backend Lua module loaded with CloudRedirect + Taskbar + SteamDB")
    if millennium and millennium.ready then
        millennium.ready()
    end
end

local function on_unload()
    log_info("Backend Lua module unloaded")
    if ok_tb and taskbar and taskbar.cleanup then
        pcall(taskbar.cleanup)
    end
end

return {
    on_frontend_loaded = on_frontend_loaded,
    on_load = on_load,
    on_unload = on_unload,
    -- CloudRedirect
    launch_cloudredirect = launch_cloudredirect,
    trigger_backup = trigger_backup,
    open_saves = open_saves,
    get_status = get_status,
    -- Taskbar
    set_progress_percent = set_progress_percent,
    get_plugin_status = get_plugin_status,
    -- SteamDB
    GetApp = GetApp,
    GetAppPrice = GetAppPrice,
    GetAchievementsGroups = GetAchievementsGroups
}
