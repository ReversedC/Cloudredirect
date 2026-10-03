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

-- Executes specific CloudRedirect actions from Steam Context Menu
function execute_action(params)
    local action = (params and params.action) or "tools"
    local appid = (params and params.appId) or ""
    local name = (params and params.gameName) or ""
    log_info("execute_action: " .. tostring(action) .. " appid=" .. tostring(appid) .. " name=" .. tostring(name))
    local uri = string.format('cloudredirect://action?cmd=%s&appid=%s&name=%s', action, appid, name)
    local res = os.execute('start "" "' .. uri .. '"')
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
    execute_action = execute_action,
    get_status = get_status
}
