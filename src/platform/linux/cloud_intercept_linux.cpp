// Linux implementation of CloudIntercept for CloudRedirect
#include "cloud_intercept.h"
#include "metadata_sync.h"
#include "rpc_handlers.h"
#include "cloud_rpc_utils.h"
#include "stats_handlers.h"
#include "stats_store.h"
#include "app_state.h"
#include "protobuf.h"
#include "log.h"
#include "http_server.h"
#include "http_util.h"
#include "local_storage.h"
#include "cloud_storage.h"
#include "cloud_provider.h"
#include "pending_ops_journal.h"
#include "json.h"
#include "legacy_metadata_cleanup.h"
#include "file_util.h"
#include "xdg.h"

#include <unordered_map>
#include <unordered_set>
#include <atomic>
#include <mutex>
#include <string>
#include <vector>
#include <fstream>
#include <cstring>
#include <unistd.h>

namespace CloudIntercept {

static std::string g_steamPath;
static std::atomic<uint32_t> g_accountId{0};
static std::mutex g_appMutex;
static std::unordered_set<uint32_t> g_namespaceApps;
static std::atomic<bool> g_initialized{false};
static std::atomic<bool> g_allAppsManaged{false};

void Init(const std::string& steamPath, bool /*cloudSaveOnly*/, CR_NotifyFn /*notifyCallback*/) {
    if (g_initialized.exchange(true)) return;

    g_steamPath = steamPath;
    if (!g_steamPath.empty() && g_steamPath.back() != '/') {
        g_steamPath += '/';
    }

    std::string configDir = XdgConfigHome() + "/CloudRedirect/";
    std::string configPath = configDir + "config.json";
    if (!std::filesystem::exists(configPath)) {
        configPath = g_steamPath + "config.json";
    }

    std::string blobRoot = XdgDataHome() + "/cloudredirect/blobs/";
    std::error_code ec;
    std::filesystem::create_directories(blobRoot, ec);

    // Start local embedded blob HTTP server
    HttpServer::Start(blobRoot, g_accountId.load());

    // Initialize core subsystems
    PendingOpsJournal::Init(g_steamPath);
    StatsStore::Init(blobRoot, g_steamPath);

    // Read config if present
    std::ifstream cfgFile(configPath);
    if (cfgFile) {
        std::string cfgStr((std::istreambuf_iterator<char>(cfgFile)),
                            std::istreambuf_iterator<char>());
        auto root = Json::Parse(cfgStr);
        if (root.type == Json::Type::Object) {
            if (root.has("apps") && root["apps"].type == Json::Type::Array) {
                std::lock_guard<std::mutex> lock(g_appMutex);
                for (const auto& item : root["apps"].arrVal) {
                    if (item.type == Json::Type::Number) {
                        g_namespaceApps.insert(static_cast<uint32_t>(item.integer()));
                    }
                }
            }
            if (root.has("all_apps") && root["all_apps"].type == Json::Type::Bool) {
                g_allAppsManaged.store(root["all_apps"].boolean());
            }
        }
    }

    LOG("[CloudIntercept-Linux] Initialized. Apps tracked: %zu (all_apps=%d)",
        g_namespaceApps.size(), g_allAppsManaged.load() ? 1 : 0);
}

void Shutdown() {
    if (!g_initialized.exchange(false)) return;
    LOG("[CloudIntercept-Linux] Shutting down subsystems...");
    HttpServer::Stop();
    ShutdownRpcHandlers();
    LOG("[CloudIntercept-Linux] Shutdown complete");
}

uint32_t GetAccountId() {
    return g_accountId.load(std::memory_order_relaxed);
}

void SetAccountId(uint32_t accountId) {
    g_accountId.store(accountId, std::memory_order_relaxed);
    HttpServer::SetAccountId(accountId);
}

const std::string& GetSteamPath() {
    return g_steamPath;
}

void AddNamespaceApp(uint32_t appId) {
    std::lock_guard<std::mutex> lock(g_appMutex);
    g_namespaceApps.insert(appId);
}

void RemoveNamespaceApp(uint32_t appId) {
    std::lock_guard<std::mutex> lock(g_appMutex);
    g_namespaceApps.erase(appId);
}

bool IsNamespaceApp(uint32_t appId) {
    if (appId == 0) return false;
    if (g_allAppsManaged.load(std::memory_order_relaxed)) return true;
    std::lock_guard<std::mutex> lock(g_appMutex);
    return g_namespaceApps.find(appId) != g_namespaceApps.end();
}

void SetNamespaceApps(const uint32_t* appIds, uint32_t count,
                      size_t* outAdded, size_t* outRemoved) {
    std::lock_guard<std::mutex> lock(g_appMutex);
    std::unordered_set<uint32_t> next;
    if (appIds && count > 0) {
        next.insert(appIds, appIds + count);
    }

    size_t added = 0;
    for (uint32_t id : next) {
        if (!g_namespaceApps.count(id)) ++added;
    }
    size_t removed = 0;
    for (uint32_t id : g_namespaceApps) {
        if (!next.count(id)) ++removed;
    }

    if (outAdded) *outAdded = added;
    if (outRemoved) *outRemoved = removed;
    g_namespaceApps = std::move(next);
}

void InstallRecvPktDetour() {
    LOG("[RecvPkt] RecvPkt detour not required on Linux");
}

void InstallManifestPinHook() {
    LOG("[ManifestPin] ManifestPin detour not required on Linux");
}

void InstallReleaseStateNop() {}
void InstallGamesPlayedHook() {}
void InstallManifestEndpointOverride() {}
void SetSendPktAddr(void* /*recvPktGlobalAddr*/) {}
void InstallServiceMethodHook() {}
bool VtableHookInstalled() { return false; }
void SetNeedsSeed(bool /*v*/) {}
void TriggerDeferredSeed(const std::vector<uint32_t>& /*apps*/) {}
void DrainPlaytimeUpdates() {}
void QueueLocalPlaytimePush(const std::vector<uint32_t>& /*endedApps*/) {}

// Packet inspector and dispatcher for Linux
bool OnSendPkt(void* /*thisptr*/, const uint8_t* data, uint32_t size) {
    if (!data || size < 8) return false;

    // Steam net packets have an EMsg as the first 32-bit integer (low 31 bits)
    uint32_t rawEmsg = 0;
    memcpy(&rawEmsg, data, sizeof(uint32_t));
    uint32_t emsg = rawEmsg & 0x7FFFFFFF;

    // EMsgServiceMethod = 151
    if (emsg != 151) return false;

    // Header structure: [rawEmsg (4B)] [hdrSize (4B)] [header protobuf bytes] [body protobuf bytes]
    uint32_t hdrSize = 0;
    memcpy(&hdrSize, data + 4, sizeof(uint32_t));
    if (8 + hdrSize > size) return false;

    const uint8_t* hdrData = data + 8;
    auto hdrFields = PB::Parse(hdrData, hdrSize);

    // Target job / service method name is field 12 in the header
    std::string method(PB::GetString(hdrFields, 12));
    if (method.empty()) return false;

    const uint8_t* bodyData = data + 8 + hdrSize;
    uint32_t bodyLen = size - (8 + hdrSize);
    auto innerFields = PB::Parse(bodyData, bodyLen);

    uint32_t appId = CloudRpcUtils::ExtractAppId(method.c_str(), innerFields);
    if (appId == 0 || !IsNamespaceApp(appId)) return false;

    LOG("[OnSendPkt-Linux] Intercepting %s for appId=%u", method.c_str(), appId);

    std::optional<RpcResult> result;
    if (method == RPC_GET_CHANGELIST)       result = HandleGetChangelist(appId, innerFields);
    else if (method == RPC_LAUNCH_INTENT)   result = HandleLaunchIntent(appId, innerFields);
    else if (method == RPC_SUSPEND_SESSION)  result = HandleSuspendSession(appId, innerFields);
    else if (method == RPC_RESUME_SESSION)   result = HandleResumeSession(appId, innerFields);
    else if (method == RPC_QUOTA_USAGE)     result = HandleQuotaUsage(appId, innerFields);
    else if (method == RPC_BEGIN_BATCH)      result = HandleBeginBatch(appId, innerFields);
    else if (method == RPC_BEGIN_UPLOAD)     result = HandleBeginFileUpload(appId, innerFields);
    else if (method == RPC_COMMIT_UPLOAD)    result = HandleCommitFileUpload(appId, innerFields);
    else if (method == RPC_COMPLETE_BATCH)   result = HandleCompleteBatch(appId, innerFields);
    else if (method == RPC_FILE_DOWNLOAD)    result = HandleFileDownload(appId, innerFields);
    else if (method == RPC_DELETE_FILE)      result = HandleDeleteFile(appId, innerFields);

    return result.has_value();
}

} // namespace CloudIntercept
