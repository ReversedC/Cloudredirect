#pragma once
#include <string>

namespace AutoCloudPathResolver {

// Maps a Windows AutoCloud root identifier to an appropriate Linux root equivalent
// when running natively on Linux (outside Proton).
inline std::string WindowsRootToLinux(const std::string& winRoot) {
    if (winRoot == "WinMyDocuments") {
        return "LinuxXdgDataHome";
    }
    if (winRoot == "WinAppDataLocal" || winRoot == "WinAppDataLocalLow" || winRoot == "WinSavedGames") {
        return "LinuxXdgDataHome";
    }
    if (winRoot == "WinAppDataRoaming") {
        return "LinuxXdgConfigHome";
    }
    return winRoot;
}

} // namespace AutoCloudPathResolver
