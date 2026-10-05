// Linux / POSIX platform adapter implementation for CloudRedirect
#include "platform.h"
#include <cstring>
#include <algorithm>
#include <vector>
#include <cstdint>
#include <fcntl.h>
#include <unistd.h>
#include <sys/stat.h>
#include <sys/types.h>

namespace {

// Self-contained RFC 3174 SHA-1 implementation (zero external crypto dependencies)
struct SHA1Context {
    uint32_t state[5];
    uint32_t count[2];
    uint8_t  buffer[64];
};

static uint32_t Rol(uint32_t value, size_t bits) {
    return (value << bits) | (value >> (32 - bits));
}

static void SHA1Transform(uint32_t state[5], const uint8_t buffer[64]) {
    uint32_t a = state[0], b = state[1], c = state[2], d = state[3], e = state[4];
    uint32_t w[80];

    for (size_t i = 0; i < 16; ++i) {
        w[i] = ((uint32_t)buffer[i * 4] << 24) |
               ((uint32_t)buffer[i * 4 + 1] << 16) |
               ((uint32_t)buffer[i * 4 + 2] << 8) |
               ((uint32_t)buffer[i * 4 + 3]);
    }
    for (size_t i = 16; i < 80; ++i) {
        w[i] = Rol(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
    }

    for (size_t i = 0; i < 20; ++i) {
        uint32_t t = Rol(a, 5) + ((b & c) | (~b & d)) + e + w[i] + 0x5A827999;
        e = d; d = c; c = Rol(b, 30); b = a; a = t;
    }
    for (size_t i = 20; i < 40; ++i) {
        uint32_t t = Rol(a, 5) + (b ^ c ^ d) + e + w[i] + 0x6ED9EBA1;
        e = d; d = c; c = Rol(b, 30); b = a; a = t;
    }
    for (size_t i = 40; i < 60; ++i) {
        uint32_t t = Rol(a, 5) + ((b & c) | (b & d) | (c & d)) + e + w[i] + 0x8F1BBCDC;
        e = d; d = c; c = Rol(b, 30); b = a; a = t;
    }
    for (size_t i = 60; i < 80; ++i) {
        uint32_t t = Rol(a, 5) + (b ^ c ^ d) + e + w[i] + 0xCA62C1D6;
        e = d; d = c; c = Rol(b, 30); b = a; a = t;
    }

    state[0] += a;
    state[1] += b;
    state[2] += c;
    state[3] += d;
    state[4] += e;
}

static void SHA1Init(SHA1Context* ctx) {
    ctx->state[0] = 0x67452301;
    ctx->state[1] = 0xEFCDAB89;
    ctx->state[2] = 0x98BADCFE;
    ctx->state[3] = 0x10325476;
    ctx->state[4] = 0xC3D2E1F0;
    ctx->count[0] = ctx->count[1] = 0;
}

static void SHA1Update(SHA1Context* ctx, const uint8_t* data, size_t len) {
    size_t i = 0;
    size_t j = (ctx->count[0] >> 3) & 63;
    if ((ctx->count[0] += (uint32_t)(len << 3)) < (uint32_t)(len << 3)) {
        ctx->count[1]++;
    }
    ctx->count[1] += (uint32_t)(len >> 29);
    if ((j + len) > 63) {
        memcpy(&ctx->buffer[j], data, (i = 64 - j));
        SHA1Transform(ctx->state, ctx->buffer);
        for (; i + 63 < len; i += 64) {
            SHA1Transform(ctx->state, &data[i]);
        }
        j = 0;
    }
    memcpy(&ctx->buffer[j], &data[i], len - i);
}

static void SHA1Final(uint8_t digest[20], SHA1Context* ctx) {
    uint8_t finalCount[8];
    for (size_t i = 0; i < 8; ++i) {
        finalCount[i] = (uint8_t)((ctx->count[(i >= 4 ? 0 : 1)] >> ((3 - (i & 3)) * 8)) & 255);
    }
    uint8_t pad = 0x80;
    SHA1Update(ctx, &pad, 1);
    while ((ctx->count[0] & 504) != 448) {
        uint8_t zero = 0;
        SHA1Update(ctx, &zero, 1);
    }
    SHA1Update(ctx, finalCount, 8);
    for (size_t i = 0; i < 20; ++i) {
        digest[i] = (uint8_t)((ctx->state[i >> 2] >> ((3 - (i & 3)) * 8)) & 255);
    }
}

} // namespace

class LinuxPlatform : public IPlatform {
public:
    std::filesystem::path Utf8ToPath(const std::string& utf8) override {
        return std::filesystem::path(utf8);
    }

    std::string PathToUtf8(const std::filesystem::path& p) override {
        return p.string();
    }

    std::string WideToUtf8(const wchar_t* w) override {
        if (!w || !*w) return {};
        size_t len = wcslen(w);
        return WideToUtf8(w, len);
    }

    std::string WideToUtf8(const wchar_t* w, size_t len) override {
        if (!w || len == 0) return {};
        std::string out;
        out.reserve(len * 3);
        for (size_t i = 0; i < len; ++i) {
            uint32_t cp = static_cast<uint32_t>(w[i]);
            // Handle UTF-16 surrogate pairs if wchar_t is 16-bit
            if (sizeof(wchar_t) == 2 && cp >= 0xD800 && cp <= 0xDBFF && (i + 1) < len) {
                uint32_t low = static_cast<uint32_t>(w[i + 1]);
                if (low >= 0xDC00 && low <= 0xDFFF) {
                    cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                    ++i;
                }
            }
            if (cp <= 0x7F) {
                out.push_back(static_cast<char>(cp));
            } else if (cp <= 0x7FF) {
                out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
                out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
            } else if (cp <= 0xFFFF) {
                out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
                out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
                out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
            } else if (cp <= 0x10FFFF) {
                out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
                out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
                out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
                out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
            }
        }
        return out;
    }

    bool AtomicWriteBinary(const std::string& path, const void* data, size_t len) override {
        if (path.empty()) return false;
        std::string tmpPath = path + ".tmp";

        // Ensure parent directory exists
        std::error_code ec;
        auto p = std::filesystem::path(path).parent_path();
        if (!p.empty()) {
            std::filesystem::create_directories(p, ec);
        }

        int fd = ::open(tmpPath.c_str(), O_WRONLY | O_CREAT | O_TRUNC, 0666);
        if (fd < 0) return false;

        const char* ptr = static_cast<const char*>(data);
        size_t remaining = len;
        while (remaining > 0) {
            ssize_t written = ::write(fd, ptr, remaining);
            if (written < 0) {
                if (errno == EINTR) continue;
                ::close(fd);
                ::unlink(tmpPath.c_str());
                return false;
            }
            ptr += written;
            remaining -= written;
        }

        if (::fdatasync(fd) != 0) {
            ::close(fd);
            ::unlink(tmpPath.c_str());
            return false;
        }

        if (::close(fd) != 0) {
            ::unlink(tmpPath.c_str());
            return false;
        }

        // POSIX rename is atomic
        if (::rename(tmpPath.c_str(), path.c_str()) != 0) {
            ::unlink(tmpPath.c_str());
            return false;
        }

        return true;
    }

    bool AtomicWriteText(const std::string& path, const std::string& content) override {
        return AtomicWriteBinary(path, content.data(), content.size());
    }

    bool IsPathWithin(const std::string& root, const std::string& fullPath) override {
        std::error_code ec;
        auto canonRoot = std::filesystem::weakly_canonical(Utf8ToPath(root), ec);
        if (ec) return false;
        auto canonPath = std::filesystem::weakly_canonical(Utf8ToPath(fullPath), ec);
        if (ec) return false;

        std::string rootStr = PathToUtf8(canonRoot);
        std::string pathStr = PathToUtf8(canonPath);
        if (pathStr.size() < rootStr.size()) return false;

        // Check prefix match
        if (pathStr.compare(0, rootStr.size(), rootStr) != 0) return false;
        return pathStr.size() == rootStr.size() || pathStr[rootStr.size()] == '/';
    }

    void CleanupEmptyDirsUpTo(const std::string& startDir, const std::string& stopAt) override {
        if (startDir.empty() || stopAt.empty()) return;
        std::error_code ec;
        auto canonStop = std::filesystem::weakly_canonical(Utf8ToPath(stopAt), ec);
        if (ec) return;
        auto cur = std::filesystem::weakly_canonical(Utf8ToPath(startDir), ec);
        if (ec) return;

        const std::string stopStr = PathToUtf8(canonStop);

        for (int i = 0; i < 256; ++i) {
            const std::string curStr = PathToUtf8(cur);
            if (curStr.size() <= stopStr.size()) break;
            if (curStr.compare(0, stopStr.size(), stopStr) != 0) break;
            if (curStr[stopStr.size()] != '/') break;

            ec.clear();
            bool removed = std::filesystem::remove(cur, ec);
            if (ec || !removed) break;

            if (!cur.has_parent_path()) break;
            auto parent = cur.parent_path();
            if (parent == cur) break;
            cur = std::move(parent);
        }
    }

    bool IsPathRedirectingReparsePoint(const std::string& path) override {
        std::error_code ec;
        return std::filesystem::is_symlink(Utf8ToPath(path), ec);
    }

    char PathSeparator() const override { return '/'; }
    const char* PathSeparatorStr() const override { return "/"; }

    std::string NormalizePath(const std::string& path) const override {
        std::string result = path;
        std::replace(result.begin(), result.end(), '\\', '/');
        return result;
    }

    std::vector<uint8_t> SHA1(const void* data, size_t len) override {
        std::vector<uint8_t> digest(20, 0);
        SHA1Context ctx;
        SHA1Init(&ctx);
        SHA1Update(&ctx, static_cast<const uint8_t*>(data), len);
        SHA1Final(digest.data(), &ctx);
        return digest;
    }
};

static LinuxPlatform g_realLinuxPlatform;
static IPlatform* g_currentPlatform = &g_realLinuxPlatform;

IPlatform& Platform() {
    return *g_currentPlatform;
}
