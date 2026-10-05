// Linux / DroidDeck entry point and interceptor for CloudRedirect
#include "common.h"
#include "log.h"
#include "cloud_intercept.h"
#include "metadata_sync.h"
#include "file_util.h"
#include "xdg.h"
#include "vdf.h"

#include <atomic>
#include <mutex>
#include <string>
#include <vector>
#include <fstream>
#include <unistd.h>
#include <dlfcn.h>
#include <sys/types.h>

static std::once_flag g_initFlag;
static std::atomic<bool> g_initialized{false};

// Discover Steam installation path on Linux / DroidDeck rootfs
static std::string DiscoverSteamPath() {
    // 1. Explicit environment variables
    const char* envPaths[] = {
        std::getenv("STEAM_DIR"),
        std::getenv("STEAMPATH"),
        std::getenv("STEAM_BASE_FOLDER")
    };
    for (const char* p : envPaths) {
        if (p && *p != '\0') {
            std::string s(p);
            if (s.back() != '/') s += '/';
            if (std::filesystem::exists(s + "steamapps") || std::filesystem::exists(s + "config"))
                return s;
        }
    }

    // 2. Standard Steam Linux / DroidDeck locations
    std::string home = Xdg::HomeDir();
    std::vector<std::string> candidates = {
        home + "/.local/share/Steam/",
        home + "/.steam/steam/",
        home + "/.steam/root/",
        home + "/.steam/",
        home + "/.var/app/com.valvesoftware.Steam/.local/share/Steam/",
        "/data/data/com.valvesoftware.steam/rootfs/home/steamuser/.local/share/Steam/", // DroidDeck container
        "/home/droiddeck/.local/share/Steam/",
        "/home/deck/.local/share/Steam/"  // SteamOS layout
    };

    for (const auto& path : candidates) {
        std::error_code ec;
        if (std::filesystem::exists(path, ec)) {
            return path;
        }
    }

    return home + "/.local/share/Steam/";
}

// Extract most recently logged-in Steam account ID from loginusers.vdf
static uint32_t DetectAccountIdFromLoginUsers(const std::string& steamPath) {
    std::string vdfPath = steamPath + "config/loginusers.vdf";
    std::ifstream f(vdfPath);
    if (!f) return 0;

    std::string line;
    uint64_t currentSteamId = 0;
    uint32_t fallbackAccountId = 0;
    bool inUser = false;
    int braceDepth = 0;

    while (std::getline(f, line)) {
        size_t start = line.find_first_not_of(" \t\r\n");
        if (start == std::string::npos) continue;
        std::string trimmed = line.substr(start);

        if (trimmed == "{") { braceDepth++; continue; }
        if (trimmed == "}") { braceDepth--; if (braceDepth == 1) inUser = false; continue; }

        if (braceDepth == 1 && trimmed.size() > 2 && trimmed[0] == '"') {
            size_t endQuote = trimmed.find('"', 1);
            if (endQuote != std::string::npos) {
                std::string key = trimmed.substr(1, endQuote - 1);
                char* endp = nullptr;
                uint64_t sid = strtoull(key.c_str(), &endp, 10);
                if (endp == key.c_str() + key.size() && sid > 76561197960265728ULL) {
                    currentSteamId = sid;
                    if (fallbackAccountId == 0) {
                        fallbackAccountId = static_cast<uint32_t>(sid & 0xFFFFFFFF);
                    }
                    inUser = true;
                }
            }
        }

        if (inUser && braceDepth == 2) {
            if (trimmed.find("\"MostRecent\"") != std::string::npos &&
                trimmed.find("\"1\"") != std::string::npos) {
                return static_cast<uint32_t>(currentSteamId & 0xFFFFFFFF);
            }
        }
    }

    return fallbackAccountId;
}

static void InitializeCloudRedirectLinux() {
    std::call_once(g_initFlag, []() {
        try {
            std::string steamPath = DiscoverSteamPath();
            std::string logDir = XdgDataHome() + "/cloudredirect/";
            std::error_code ec;
            std::filesystem::create_directories(logDir, ec);

            std::string logPath = logDir + "cloud_redirect.log";
            Log::Init(logPath.c_str());

            LOG("=== CloudRedirect Linux/DroidDeck Interceptor loaded (PID=%d) ===", getpid());
            LOG("Steam path detected: %s", steamPath.c_str());

            uint32_t accountId = DetectAccountIdFromLoginUsers(steamPath);
            if (accountId != 0) {
                LOG("Detected Steam Account ID: %u", accountId);
            }

            CloudIntercept::Init(steamPath, /*cloudSaveOnly=*/false);
            if (accountId != 0) {
                CloudIntercept::SetAccountId(accountId);
            }

            g_initialized.store(true, std::memory_order_release);
            LOG("CloudRedirect Linux initialization successful");
        } catch (const std::exception& ex) {
            LOG("CloudRedirect Linux init error: %s", ex.what());
        } catch (...) {
            LOG("CloudRedirect Linux init error: unknown exception");
        }
    });
}

// Constructor called automatically when libcloud_redirect.so is preloaded or dlopened
__attribute__((constructor))
static void OnLibraryLoad() {
    // Check process name
    char exePath[1024] = {};
    ssize_t len = readlink("/proc/self/exe", exePath, sizeof(exePath) - 1);
    if (len > 0) {
        exePath[len] = '\0';
        std::string proc(exePath);
        // If loaded into steam or steamwebhelper or LD_PRELOAD
        if (proc.find("steam") != std::string::npos ||
            getenv("CLOUDREDIRECT_FORCE_INIT") != nullptr) {
            InitializeCloudRedirectLinux();
        }
    }
}

__attribute__((destructor))
static void OnLibraryUnload() {
    if (g_initialized.load(std::memory_order_acquire)) {
        LOG("CloudRedirect Linux unloading");
        CloudIntercept::Shutdown();
    }
}

// Exported packet interception symbol for injection frameworks / wrappers
extern "C" __attribute__((visibility("default")))
int CloudOnSendPkt(void* thisptr, const uint8_t* data, uint32_t size, void* /*recvPktFn*/) {
    InitializeCloudRedirectLinux();
    if (!g_initialized.load(std::memory_order_relaxed)) return 0;
    return CloudIntercept::OnSendPkt(thisptr, data, size) ? 1 : 0;
}
