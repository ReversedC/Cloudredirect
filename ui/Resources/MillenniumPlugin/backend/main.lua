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

-- Opens CloudRedirect Mini Browser Window
function open_minibrowser(kwargs)
    local url = kwargs and kwargs.url or ""
    log_info("open_minibrowser invoked from Steam WebUI: " .. tostring(url))
    local res = os.execute('start "" "cloudredirect://minibrowser?url=' .. tostring(url) .. '"')
    return { success = true, result = res }
end

-- Get cached PatchWiki tutorials
function get_patchwiki_tutorials()
    local appdata = os.getenv("APPDATA")
    if not appdata then return { success = false } end
    local file = appdata .. "\\CloudRedirect\\steam_plugin_patchwiki.json"
    local f = io.open(file, "r")
    if f then
        local content = f:read("*a")
        f:close()
        return { success = true, data = content }
    end
    return { success = false }
end

-- Millennium Lifecycle Hooks
local function on_frontend_loaded()
    log_info("Frontend UI loaded into Steam CEF successfully")
end

local function on_load()
    log_info("Backend Lua module loaded")
    if millennium and millennium.ready then
        millennium.ready()
    end
end

local function on_unload()
    log_info("Backend Lua module unloaded")
end

return {
    on_frontend_loaded = on_frontend_loaded,
    on_load = on_load,
    on_unload = on_unload,
    launch_cloudredirect = launch_cloudredirect,
    trigger_backup = trigger_backup,
    open_saves = open_saves,
    get_status = get_status,
    open_minibrowser = open_minibrowser,
    get_patchwiki_tutorials = get_patchwiki_tutorials
}
