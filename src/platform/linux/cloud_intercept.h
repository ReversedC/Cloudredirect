#pragma once
#include "cloud_metadata_paths.h"
#include "cr_api.h"
#include "common.h"

namespace CloudIntercept {

struct CNetPacket {
    uint64_t pad0;
    uint8_t* pubData;
    uint32_t cubData;
    uint32_t m_cRef;
    uint8_t* ownedDataCopy;
};

using RecvPktFn = int64_t(*)(void* thisptr, CNetPacket* pkt);

void Init(const std::string& steamPath, bool cloudSaveOnly = false,
          CR_NotifyFn notifyCallback = nullptr);

void InstallRecvPktDetour();
void InstallManifestPinHook();
void InstallReleaseStateNop();
void InstallGamesPlayedHook();
void InstallManifestEndpointOverride();
void SetSendPktAddr(void* recvPktGlobalAddr);

bool OnSendPkt(void* thisptr, const uint8_t* data, uint32_t size);

uint32_t GetAccountId();
void SetAccountId(uint32_t accountId);
const std::string& GetSteamPath();

void AddNamespaceApp(uint32_t appId);
void RemoveNamespaceApp(uint32_t appId);
bool IsNamespaceApp(uint32_t appId);
void SetNamespaceApps(const uint32_t* appIds, uint32_t count,
                      size_t* outAdded, size_t* outRemoved);

void InstallServiceMethodHook();
bool VtableHookInstalled();

void SetNeedsSeed(bool v);
void TriggerDeferredSeed(const std::vector<uint32_t>& apps);
void DrainPlaytimeUpdates();
void QueueLocalPlaytimePush(const std::vector<uint32_t>& endedApps);

void Shutdown();

} // namespace CloudIntercept
