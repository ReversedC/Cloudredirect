#include "log.h"
#include "file_util.h"
#include <cstdarg>
#include <ctime>
#include <vector>
#include <mutex>
#include <cstdio>
#include <cstring>
#include <unistd.h>

#ifndef CR_RELEASE_VERSION
#define CR_RELEASE_VERSION "linux"
#endif

namespace Log {

static FILE* g_file = nullptr;
static std::mutex g_mutex;
static std::string g_logPath;

static constexpr long MAX_LOG_SIZE = 10 * 1024 * 1024;
static constexpr size_t STACK_BUF = 1024;

static FILE* OpenLog(const std::string& utf8Path) {
    if (utf8Path.empty()) return nullptr;
    // On Linux paths are directly UTF-8
    return fopen(utf8Path.c_str(), "ab");
}

static void WriteRecord(const char* data, size_t len) {
    if (!g_file || !data || len == 0) return;
    fwrite(data, 1, len, g_file);
    fflush(g_file);
}

static void TruncateIfNeeded() {
    if (!g_file) return;
    long pos = ftell(g_file);
    if (pos < 0 || pos < MAX_LOG_SIZE) return;

    fclose(g_file);
    if (!g_logPath.empty()) {
        unlink(g_logPath.c_str());
    }

    g_file = OpenLog(g_logPath);
    if (g_file) {
        const char banner[] = "=== Log truncated (size limit reached) ===\n";
        WriteRecord(banner, sizeof(banner) - 1);
    }
}

void Init(const char* path) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_logPath = path ? path : "";
    g_file = OpenLog(g_logPath);
    if (g_file) {
        time_t t = time(nullptr);
        tm lt{};
        localtime_r(&t, &lt);
        char buf[128];
        int n = snprintf(buf, sizeof(buf),
                         "\n=== CloudRedirect loaded at %04d-%02d-%02d %02d:%02d:%02d [BUILD:" CR_RELEASE_VERSION "] ===\n",
                         lt.tm_year + 1900, lt.tm_mon + 1, lt.tm_mday,
                         lt.tm_hour, lt.tm_min, lt.tm_sec);
        if (n > 0) WriteRecord(buf, (size_t)n);
    }
}

void Shutdown() {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_file) {
        const char banner[] = "=== CloudRedirect unloaded ===\n";
        WriteRecord(banner, sizeof(banner) - 1);
        fclose(g_file);
        g_file = nullptr;
    }
}

void Write(const char* fmt, ...) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_file) return;

    TruncateIfNeeded();
    if (!g_file) return;

    char stack[STACK_BUF];
    char* buf = stack;
    size_t cap = sizeof(stack);
    std::vector<char> heap;

    time_t t = time(nullptr);
    tm lt{};
    localtime_r(&t, &lt);
    int prefix = snprintf(buf, cap, "[%02d:%02d:%02d] ",
                          lt.tm_hour, lt.tm_min, lt.tm_sec);
    if (prefix < 0) return;
    if ((size_t)prefix >= cap) return;

    va_list args;
    va_start(args, fmt);
    va_list argsCopy;
    va_copy(argsCopy, args);
    int bodyLen = vsnprintf(buf + prefix, cap - prefix, fmt, args);
    va_end(args);

    if (bodyLen < 0) {
        va_end(argsCopy);
        return;
    }

    size_t need = (size_t)prefix + (size_t)bodyLen + 1;
    if (need > cap) {
        heap.resize(need + 1);
        buf = heap.data();
        cap = heap.size();
        memcpy(buf, stack, (size_t)prefix);
        bodyLen = vsnprintf(buf + prefix, cap - prefix, fmt, argsCopy);
        if (bodyLen < 0) {
            va_end(argsCopy);
            return;
        }
    }
    va_end(argsCopy);

    size_t total = (size_t)prefix + (size_t)bodyLen;
    buf[total++] = '\n';
    WriteRecord(buf, total);
}

} // namespace Log
