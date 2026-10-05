#include "http_server.h"
#include "http_util.h"
#include "file_util.h"
#include "cloud_storage.h"
#include "log.h"

#include "miniz.h"

#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <unistd.h>
#include <fcntl.h>
#include <errno.h>

#include <thread>
#include <chrono>
#include <atomic>
#include <mutex>
#include <memory>
#include <vector>
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <unordered_map>
#include <cstring>
#include <strings.h>

namespace HttpServer {

static const char* stristr(const char* haystack, const char* needle) {
    if (!haystack || !needle) return nullptr;
    size_t nlen = strlen(needle);
    for (; *haystack; ++haystack) {
        if (strncasecmp(haystack, needle, nlen) == 0)
            return haystack;
    }
    return nullptr;
}

static int g_listenSock = -1;
static std::atomic<uint16_t> g_port{0};
static std::thread g_acceptThread;
static std::atomic<bool> g_running{false};
static std::string g_blobRoot;
static std::atomic<uint32_t> g_accountId{0};
static std::atomic<int64_t> g_maxUploadBytes{256 * 1024 * 1024};
static std::mutex g_clientMtx;

struct BlobKey {
    uint32_t accountId;
    uint32_t appId;
    std::string filename;
    bool operator==(const BlobKey& o) const {
        return accountId == o.accountId && appId == o.appId && filename == o.filename;
    }
};

struct BlobKeyHash {
    size_t operator()(const BlobKey& k) const {
        size_t h = std::hash<uint32_t>{}(k.accountId);
        h ^= std::hash<uint32_t>{}(k.appId) + 0x9e3779b9 + (h << 6) + (h >> 2);
        h ^= std::hash<std::string>{}(k.filename) + 0x9e3779b9 + (h << 6) + (h >> 2);
        return h;
    }
};

static std::mutex g_memBlobMtx;
static std::unordered_map<BlobKey, std::vector<uint8_t>, BlobKeyHash> g_memBlobs;

struct ClientSlot {
    std::thread thread;
    std::shared_ptr<std::atomic<bool>> done;
};

static std::vector<ClientSlot> g_clientSlots;
static constexpr size_t kMaxClientThreads = 64;
static constexpr int kAcceptBackpressureMaxMs = 5000;

static bool TryDecompressZip(const std::vector<uint8_t>& data, std::vector<uint8_t>& out) {
    if (data.size() < 4) return false;
    if (data[0] != 0x50 || data[1] != 0x4B || data[2] != 0x03 || data[3] != 0x04)
        return false;

    mz_zip_archive zip{};
    if (!mz_zip_reader_init_mem(&zip, data.data(), data.size(), 0)) {
        LOG("[HTTP] ZIP init failed: %s", mz_zip_get_error_string(mz_zip_get_last_error(&zip)));
        return false;
    }

    if (mz_zip_reader_get_num_files(&zip) != 1) {
        mz_zip_reader_end(&zip);
        return false;
    }
    mz_zip_archive_file_stat fstat{};
    if (!mz_zip_reader_file_stat(&zip, 0, &fstat) ||
        strcmp(fstat.m_filename, "z") != 0) {
        mz_zip_reader_end(&zip);
        return false;
    }

    if (fstat.m_uncomp_size > 512ULL * 1024 * 1024) {
        LOG("[HTTP] ZIP rejected: declared size %llu", (unsigned long long)fstat.m_uncomp_size);
        mz_zip_reader_end(&zip);
        return false;
    }

    size_t uncompSize = 0;
    void* p = mz_zip_reader_extract_to_heap(&zip, 0, &uncompSize, 0);
    mz_zip_reader_end(&zip);

    if (!p) {
        LOG("[HTTP] ZIP extract failed");
        return false;
    }

    out.assign(static_cast<uint8_t*>(p), static_cast<uint8_t*>(p) + uncompSize);
    mz_free(p);
    return true;
}

static void PruneClientThreads() {
    std::vector<ClientSlot> alive;
    alive.reserve(g_clientSlots.size());
    for (auto& slot : g_clientSlots) {
        if (slot.done->load()) {
            if (slot.thread.joinable()) slot.thread.join();
        } else {
            alive.push_back(std::move(slot));
        }
    }
    g_clientSlots = std::move(alive);
}

static std::string BlobPath(uint32_t accountId, uint32_t appId, const std::string& filename) {
    std::string path = g_blobRoot + std::to_string(accountId) + "/" +
                       std::to_string(appId) + "/" + filename;
    for (auto& c : path) { if (c == '\\') c = '/'; }
    return path;
}

static bool ParseBlobPath(const char* path, const char* prefix,
                          uint32_t& accountId, uint32_t& appId, std::string& filename) {
    size_t prefixLen = strlen(prefix);
    if (strncmp(path, prefix, prefixLen) != 0) return false;
    const char* p = path + prefixLen;

    char* end = nullptr;
    unsigned long a = strtoul(p, &end, 10);
    if (end == p || *end != '/') return false;
    accountId = static_cast<uint32_t>(a);
    p = end + 1;

    unsigned long ap = strtoul(p, &end, 10);
    if (end == p || *end != '/') return false;
    appId = static_cast<uint32_t>(ap);
    p = end + 1;

    if (*p == '\0') return false;

    std::string raw(p);
    size_t q = raw.find('?');
    if (q != std::string::npos) raw = raw.substr(0, q);

    filename = HttpUtil::UrlDecode(raw);

    // Path traversal rejection
    if (filename.find("..") != std::string::npos ||
        filename.find('/') != std::string::npos ||
        filename.find('\\') != std::string::npos) {
        LOG("[HTTP] BLOCKED path traversal attempt: raw='%s' decoded='%s'", p, filename.c_str());
        return false;
    }
    return !filename.empty();
}

static bool IsConnectionFromSteam(int client) {
    sockaddr_in peer{};
    socklen_t peerLen = sizeof(peer);
    if (getpeername(client, (sockaddr*)&peer, &peerLen) != 0) return false;

    // Must be from localhost
    if (peer.sin_addr.s_addr != htonl(INADDR_LOOPBACK)) {
        LOG("[HTTP] BLOCKED non-loopback connection");
        return false;
    }
    return true;
}

static void HandleClient(int client, std::shared_ptr<std::atomic<bool>> doneSignal) {
    std::vector<char> reqBuf(8192);
    size_t totalRead = 0;
    std::string headers;
    size_t headerEnd = std::string::npos;

    // Read headers
    while (totalRead < reqBuf.size() - 1) {
        ssize_t n = ::recv(client, reqBuf.data() + totalRead, reqBuf.size() - 1 - totalRead, 0);
        if (n <= 0) { ::close(client); doneSignal->store(true); return; }
        totalRead += n;
        reqBuf[totalRead] = '\0';
        std::string s(reqBuf.data(), totalRead);
        headerEnd = s.find("\r\n\r\n");
        if (headerEnd != std::string::npos) {
            headers = s.substr(0, headerEnd);
            break;
        }
    }

    if (headerEnd == std::string::npos) {
        ::close(client);
        doneSignal->store(true);
        return;
    }

    char method[16] = {}, path[1024] = {}, proto[16] = {};
    if (sscanf(headers.c_str(), "%15s %1023s %15s", method, path, proto) != 3) {
        ::close(client);
        doneSignal->store(true);
        return;
    }

    if (strcmp(method, "PUT") == 0) {
        size_t contentLength = 0;
        const char* cl = stristr(headers.c_str(), "Content-Length:");
        if (cl) contentLength = strtoul(cl + 15, nullptr, 10);

        int64_t cap = g_maxUploadBytes.load();
        if (cap > 0 && static_cast<int64_t>(contentLength) > cap) {
            const char resp[] = "HTTP/1.1 413 Payload Too Large\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            ::send(client, resp, sizeof(resp) - 1, 0);
            ::close(client);
            doneSignal->store(true);
            return;
        }

        std::vector<uint8_t> body;
        body.reserve(contentLength);
        size_t bodyStart = headerEnd + 4;
        if (totalRead > bodyStart) {
            body.insert(body.end(), reqBuf.data() + bodyStart, reqBuf.data() + totalRead);
        }

        while (body.size() < contentLength) {
            char chunk[65536];
            size_t want = std::min(sizeof(chunk), contentLength - body.size());
            ssize_t n = ::recv(client, chunk, want, 0);
            if (n <= 0) break;
            body.insert(body.end(), chunk, chunk + n);
        }

        uint32_t accountId = 0, appId = 0;
        std::string filename;
        if (!ParseBlobPath(path, "/upload/", accountId, appId, filename)) {
            const char resp[] = "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            ::send(client, resp, sizeof(resp) - 1, 0);
            ::close(client);
            doneSignal->store(true);
            return;
        }

        std::string dest = BlobPath(accountId, appId, filename);
        bool diskOk = false;
        try {
            std::filesystem::create_directories(std::filesystem::path(dest).parent_path());
            std::ofstream f(dest, std::ios::binary);
            if (f) {
                f.write(reinterpret_cast<const char*>(body.data()), body.size());
                diskOk = f.good();
            }
        } catch (...) {
            diskOk = false;
        }

        if (!diskOk) {
            std::lock_guard<std::mutex> lk(g_memBlobMtx);
            g_memBlobs[{accountId, appId, filename}] = body;
        }

        const char resp[] = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        ::send(client, resp, sizeof(resp) - 1, 0);

    } else if (strcmp(method, "GET") == 0) {
        uint32_t accountId = 0, appId = 0;
        std::string filename;
        if (!ParseBlobPath(path, "/download/", accountId, appId, filename)) {
            const char resp[] = "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            ::send(client, resp, sizeof(resp) - 1, 0);
            ::close(client);
            doneSignal->store(true);
            return;
        }

        std::vector<uint8_t> body;
        bool found = false;
        {
            std::lock_guard<std::mutex> lk(g_memBlobMtx);
            auto it = g_memBlobs.find({accountId, appId, filename});
            if (it != g_memBlobs.end()) {
                body = it->second;
                found = true;
            }
        }

        if (!found) {
            std::string src = BlobPath(accountId, appId, filename);
            std::ifstream f(src, std::ios::binary);
            if (f) {
                body.assign(std::istreambuf_iterator<char>(f), std::istreambuf_iterator<char>());
                found = true;
            }
        }

        if (!found) {
            const char resp[] = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            ::send(client, resp, sizeof(resp) - 1, 0);
            ::close(client);
            doneSignal->store(true);
            return;
        }

        std::string hdr = "HTTP/1.1 200 OK\r\nContent-Length: " + std::to_string(body.size()) +
                          "\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n";
        ::send(client, hdr.data(), hdr.size(), 0);
        ::send(client, reinterpret_cast<const char*>(body.data()), body.size(), 0);
    } else {
        const char resp[] = "HTTP/1.1 405 Method Not Allowed\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        ::send(client, resp, sizeof(resp) - 1, 0);
    }

    ::close(client);
    doneSignal->store(true);
}

static void AcceptLoop() {
    LOG("[HTTP] Accept loop started on port %u", g_port.load());
    while (g_running.load()) {
        fd_set readfds;
        FD_ZERO(&readfds);
        FD_SET(g_listenSock, &readfds);

        struct timeval tv{};
        tv.tv_sec = 0;
        tv.tv_usec = 250000; // 250ms

        int r = select(g_listenSock + 1, &readfds, nullptr, nullptr, &tv);
        if (r <= 0) continue;

        sockaddr_in clientAddr{};
        socklen_t clientLen = sizeof(clientAddr);
        int client = ::accept(g_listenSock, (sockaddr*)&clientAddr, &clientLen);
        if (client < 0) continue;

        if (!IsConnectionFromSteam(client)) {
            ::close(client);
            continue;
        }

        auto done = std::make_shared<std::atomic<bool>>(false);
        std::lock_guard<std::mutex> lk(g_clientMtx);
        PruneClientThreads();
        g_clientSlots.push_back({std::thread(HandleClient, client, done), done});
    }
}

bool Start(const std::string& blobRoot, uint32_t accountId) {
    if (g_running.load()) return true;

    g_blobRoot = blobRoot;
    if (!g_blobRoot.empty() && g_blobRoot.back() != '/') {
        g_blobRoot += '/';
    }
    if (accountId != 0) g_accountId.store(accountId);

    g_listenSock = ::socket(AF_INET, SOCK_STREAM, 0);
    if (g_listenSock < 0) {
        LOG("[HTTP] socket() failed: %s", strerror(errno));
        return false;
    }

    int opt = 1;
    setsockopt(g_listenSock, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    addr.sin_port = 0; // OS assigned port

    if (::bind(g_listenSock, (sockaddr*)&addr, sizeof(addr)) < 0) {
        LOG("[HTTP] bind() failed: %s", strerror(errno));
        ::close(g_listenSock);
        g_listenSock = -1;
        return false;
    }

    socklen_t len = sizeof(addr);
    if (::getsockname(g_listenSock, (sockaddr*)&addr, &len) == 0) {
        g_port.store(ntohs(addr.sin_port));
    }

    if (::listen(g_listenSock, SOMAXCONN) < 0) {
        LOG("[HTTP] listen() failed: %s", strerror(errno));
        ::close(g_listenSock);
        g_listenSock = -1;
        return false;
    }

    g_running.store(true);
    g_acceptThread = std::thread(AcceptLoop);
    LOG("[HTTP] Server started on 127.0.0.1:%u", g_port.load());
    return true;
}

void Stop() {
    if (!g_running.load()) return;
    g_running.store(false);

    if (g_listenSock >= 0) {
        ::close(g_listenSock);
        g_listenSock = -1;
    }

    if (g_acceptThread.joinable()) {
        g_acceptThread.join();
    }

    std::lock_guard<std::mutex> lk(g_clientMtx);
    for (auto& slot : g_clientSlots) {
        if (slot.thread.joinable()) slot.thread.join();
    }
    g_clientSlots.clear();
    LOG("[HTTP] Server stopped");
}

void SetAccountId(uint32_t accountId) {
    g_accountId.store(accountId);
    LOG("[HTTP] Account ID set to %u", g_accountId.load());
}

void SetMaxUploadMB(int mb) {
    g_maxUploadBytes.store(mb > 0 ? static_cast<int64_t>(mb) * 1024LL * 1024LL : 0);
}

uint16_t GetPort() {
    return g_port.load();
}

static bool ValidateBlobPath(const std::string& blobPath) {
    return FileUtil::IsPathWithin(g_blobRoot, blobPath);
}

bool HasBlob(uint32_t accountId, uint32_t appId, const std::string& filename) {
    {
        std::lock_guard<std::mutex> lk(g_memBlobMtx);
        if (g_memBlobs.count({accountId, appId, filename})) return true;
    }
    std::string path = BlobPath(accountId, appId, filename);
    if (!ValidateBlobPath(path)) return false;
    std::error_code ec;
    return std::filesystem::exists(path, ec);
}

uint64_t GetBlobSize(uint32_t accountId, uint32_t appId, const std::string& filename) {
    {
        std::lock_guard<std::mutex> lk(g_memBlobMtx);
        auto it = g_memBlobs.find({accountId, appId, filename});
        if (it != g_memBlobs.end()) return it->second.size();
    }
    std::string path = BlobPath(accountId, appId, filename);
    if (!ValidateBlobPath(path)) return 0;
    std::error_code ec;
    auto sz = std::filesystem::file_size(path, ec);
    return ec ? 0 : sz;
}

std::vector<uint8_t> ReadBlob(uint32_t accountId, uint32_t appId, const std::string& filename) {
    {
        std::lock_guard<std::mutex> lk(g_memBlobMtx);
        auto it = g_memBlobs.find({accountId, appId, filename});
        if (it != g_memBlobs.end()) return it->second;
    }
    std::string path = BlobPath(accountId, appId, filename);
    if (!ValidateBlobPath(path)) return {};
    std::ifstream f(path, std::ios::binary);
    if (!f) return {};
    return std::vector<uint8_t>((std::istreambuf_iterator<char>(f)),
                                std::istreambuf_iterator<char>());
}

bool DeleteBlob(uint32_t accountId, uint32_t appId, const std::string& filename) {
    bool removed = false;
    {
        std::lock_guard<std::mutex> lk(g_memBlobMtx);
        removed = g_memBlobs.erase({accountId, appId, filename}) > 0;
    }
    std::string path = BlobPath(accountId, appId, filename);
    if (ValidateBlobPath(path)) {
        std::error_code ec;
        if (std::filesystem::remove(path, ec)) removed = true;
    }
    return removed;
}

} // namespace HttpServer
