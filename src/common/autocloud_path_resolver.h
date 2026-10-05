#pragma once
#include <string>
#include <algorithm>

namespace AutoCloudUtil {
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
    if (winRoot == "LinuxHome" || winRoot == "LinuxXdgDataHome" || winRoot == "LinuxXdgConfigHome") {
        return winRoot;
    }
    return winRoot;
}

} // namespace AutoCloudPathResolver
} // namespace AutoCloudUtil
