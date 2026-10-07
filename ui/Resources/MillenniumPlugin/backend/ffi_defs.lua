pcall(function()
    if jit and jit.off then
        jit.off()
    end
end)

local logger = nil
pcall(function() logger = require("logger") end)

local ffi_ok, ffi = pcall(require, "ffi")
if not ffi_ok or not ffi then
    return {}
end

pcall(function()
    ffi.cdef [[
    typedef struct { uint32_t Data1; uint16_t Data2; uint16_t Data3; uint8_t Data4[8]; } GUID;
    typedef GUID CLSID; typedef GUID IID;
    typedef void*     HWND; typedef void* LPVOID; typedef void* LPUNKNOWN;
    typedef intptr_t  LPARAM;
    typedef long      HRESULT;
    typedef unsigned long      ULONG; typedef unsigned long DWORD;
    typedef unsigned long long ULONGLONG;
    typedef int  BOOL; typedef char* LPSTR;

    typedef struct ITaskbarList3 ITaskbarList3;
    typedef struct {
      HRESULT (*QueryInterface)(ITaskbarList3*, const void*, void**);
      ULONG   (*AddRef)        (ITaskbarList3*);
      ULONG   (*Release)       (ITaskbarList3*);
      HRESULT (*HrInit)        (ITaskbarList3*);
      HRESULT (*AddTab)        (ITaskbarList3*, HWND);
      HRESULT (*DeleteTab)     (ITaskbarList3*, HWND);
      HRESULT (*ActivateTab)   (ITaskbarList3*, HWND);
      HRESULT (*SetActiveAlt)  (ITaskbarList3*, HWND);
      HRESULT (*MarkFullscreenWindow)(ITaskbarList3*, HWND, int);
      HRESULT (*SetProgressValue)   (ITaskbarList3*, HWND, ULONGLONG, ULONGLONG);
      HRESULT (*SetProgressState)   (ITaskbarList3*, HWND, int);
    } ITaskbarList3Vtbl;
    struct ITaskbarList3 { ITaskbarList3Vtbl* lpVtbl; };

    HWND    FindWindowA     (const char*, const char*);
    BOOL    FlashWindow     (HWND, BOOL);
    HRESULT CoInitializeEx  (LPVOID, DWORD);
    HRESULT CoCreateInstance(const CLSID*, LPUNKNOWN, DWORD, const IID*, LPVOID*);
    ]]
end)

local u32_ok, user32 = pcall(ffi.load, "user32")
local o32_ok, ole32  = pcall(ffi.load, "ole32")

return {
    user32 = u32_ok and user32 or nil,
    ole32  = o32_ok and ole32 or nil,
}
