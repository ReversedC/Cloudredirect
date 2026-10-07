pcall(function()
    if jit and jit.off then
        jit.off()
    end
end)

local logger = nil
pcall(function() logger = require("logger") end)

local function log_info(msg)
    if logger and logger.info then logger:info("[taskbar] " .. tostring(msg)) end
end

local function log_warn(msg)
    if logger and logger.warn then logger:warn("[taskbar] " .. tostring(msg)) end
end

local function log_error(msg)
    if logger and logger.error then logger:error("[taskbar] " .. tostring(msg)) end
end

local ffi_ok, ffi = pcall(require, "ffi")
local libs_ok, libs = pcall(require, "ffi_defs")

local tb = nil
local tb_SetProgressValue = nil
local tb_SetProgressState = nil
local tb_Release = nil
local user32 = nil
local initialized = false

local function init_taskbar()
    if initialized then return true end
    if not ffi_ok or not libs_ok or not libs or not ffi then
        log_warn("FFI or ffi_defs not available")
        return false
    end

    user32 = libs.user32
    local ole32 = libs.ole32
    if not user32 or not ole32 then
        log_warn("user32 or ole32 not loaded")
        return false
    end

    local ok, err = pcall(function()
        local CLSID_TaskbarList = ffi.new("CLSID[1]", { { 0x56FDF344, 0xFD6D, 0x11D0, { 0x95, 0x8A, 0x00, 0x60, 0x97, 0xC9, 0xA0, 0x90 } } })
        local IID_ITaskbarList  = ffi.new("IID[1]",   { { 0x56FDF342, 0xFD6D, 0x11D0, { 0x95, 0x8A, 0x00, 0x60, 0x97, 0xC9, 0xA0, 0x90 } } })
        local IID_ITaskbarList3 = ffi.new("IID[1]",   { { 0xEA1AFB91, 0x9E28, 0x4B86, { 0x90, 0xE9, 0x9E, 0x9F, 0x8A, 0x5E, 0xEF, 0xAF } } })

        log_info("CoInitializeEx")
        ole32.CoInitializeEx(nil, 2)

        local ppv = ffi.new("void*[1]")
        local hr = ole32.CoCreateInstance(CLSID_TaskbarList, nil, 1, IID_ITaskbarList, ppv)
        if hr ~= 0 or ppv[0] == nil then
            error(string.format("CoCreateInstance failed: 0x%08X", hr))
        end

        local initial_tb = ffi.cast("ITaskbarList3*", ppv[0])
        hr = initial_tb.lpVtbl.QueryInterface(initial_tb, IID_ITaskbarList3, ppv)
        initial_tb.lpVtbl.Release(initial_tb)

        if hr ~= 0 or ppv[0] == nil then
            error(string.format("QueryInterface(ITaskbarList3) failed: 0x%08X", hr))
        end

        tb = ffi.cast("ITaskbarList3*", ppv[0])
        hr = tb.lpVtbl.HrInit(tb)
        if hr ~= 0 then
            tb.lpVtbl.Release(tb)
            tb = nil
            error(string.format("HrInit failed: 0x%08X", hr))
        end

        tb_SetProgressValue = tb.lpVtbl.SetProgressValue
        tb_SetProgressState = tb.lpVtbl.SetProgressState
        tb_Release          = tb.lpVtbl.Release
        initialized = true
        log_info("Taskbar COM successfully initialized")
    end)

    if not ok then
        log_warn("Taskbar initialization failed safely: " .. tostring(err))
        return false
    end
    return true
end

-- Initialize safely in background
pcall(init_taskbar)

local steam_hwnd = nil

local function find_steam()
    if not user32 or not user32.FindWindowA then return false end
    local hwnd = user32.FindWindowA(nil, "Steam")
    if hwnd ~= nil then
        steam_hwnd = hwnd
        return true
    end
    return false
end

local function set_progress_percent(percent)
    if not initialized and not init_taskbar() then
        return false
    end
    if not find_steam() or not tb or not steam_hwnd then
        return false
    end

    local ok, res = pcall(function()
        if percent == -1 then
            tb_SetProgressState(tb, steam_hwnd, 0)
        elseif percent == -2 then
            tb_SetProgressState(tb, steam_hwnd, 8)
        elseif percent == 100 then
            tb_SetProgressState(tb, steam_hwnd, 0)
            if user32.FlashWindow then
                user32.FlashWindow(steam_hwnd, 1)
            end
        else
            tb_SetProgressState(tb, steam_hwnd, 2)
            tb_SetProgressValue(tb, steam_hwnd, percent, 100)
        end
        return true
    end)

    if not ok then
        log_warn("set_progress_percent failed safely: " .. tostring(res))
        return false
    end
    return true
end

local function cleanup()
    pcall(function()
        if tb and tb_Release then
            tb_Release(tb)
            tb = nil
            initialized = false
        end
    end)
end

return {
    set_progress_percent = set_progress_percent,
    cleanup              = cleanup,
}
