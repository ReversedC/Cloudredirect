#pragma once
// XDG Base Directory specification helpers for Linux / POSIX platforms.

#include <string>
#include <cstdlib>

#ifndef _WIN32
#include <unistd.h>
#include <pwd.h>
#include <sys/types.h>
#endif

namespace Xdg {

inline std::string HomeDir() {
    const char* home = std::getenv("HOME");
    if (home && *home != '\0') return home;
#ifndef _WIN32
    struct passwd* pw = getpwuid(getuid());
    if (pw && pw->pw_dir && pw->pw_dir[0] != '\0') return pw->pw_dir;
#endif
    return "/home";
}

inline std::string ConfigHome() {
    const char* xdg = std::getenv("XDG_CONFIG_HOME");
    if (xdg && *xdg != '\0') return xdg;
    return HomeDir() + "/.config";
}

inline std::string DataHome() {
    const char* xdg = std::getenv("XDG_DATA_HOME");
    if (xdg && *xdg != '\0') return xdg;
    return HomeDir() + "/.local/share";
}

inline std::string CacheHome() {
    const char* xdg = std::getenv("XDG_CACHE_HOME");
    if (xdg && *xdg != '\0') return xdg;
    return HomeDir() + "/.cache";
}

inline std::string StateHome() {
    const char* xdg = std::getenv("XDG_STATE_HOME");
    if (xdg && *xdg != '\0') return xdg;
    return HomeDir() + "/.local/state";
}

} // namespace Xdg

// Top-level aliases for direct usage
inline std::string XdgConfigHome() { return Xdg::ConfigHome(); }
inline std::string XdgDataHome()   { return Xdg::DataHome(); }
inline std::string XdgCacheHome()  { return Xdg::CacheHome(); }
inline std::string XdgStateHome()  { return Xdg::StateHome(); }
