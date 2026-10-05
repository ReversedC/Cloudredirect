#include "http_util.h"
#include <cstdio>
#include <ctime>
#include <cstring>
#include <cstdint>

namespace HttpUtil {

std::wstring Widen(const std::string& s) {
    if (s.empty()) return {};
    std::wstring out;
    out.reserve(s.size());
    for (size_t i = 0; i < s.size();) {
        unsigned char c = static_cast<unsigned char>(s[i]);
        if (c <= 0x7F) {
            out.push_back(static_cast<wchar_t>(c));
            ++i;
        } else if ((c & 0xE0) == 0xC0 && i + 1 < s.size()) {
            wchar_t val = ((c & 0x1F) << 6) | (static_cast<unsigned char>(s[i + 1]) & 0x3F);
            out.push_back(val);
            i += 2;
        } else if ((c & 0xF0) == 0xE0 && i + 2 < s.size()) {
            wchar_t val = ((c & 0x0F) << 12) |
                          ((static_cast<unsigned char>(s[i + 1]) & 0x3F) << 6) |
                          (static_cast<unsigned char>(s[i + 2]) & 0x3F);
            out.push_back(val);
            i += 3;
        } else if ((c & 0xF8) == 0xF0 && i + 3 < s.size()) {
            uint32_t val = ((c & 0x07) << 18) |
                           ((static_cast<unsigned char>(s[i + 1]) & 0x3F) << 12) |
                           ((static_cast<unsigned char>(s[i + 2]) & 0x3F) << 6) |
                           (static_cast<unsigned char>(s[i + 3]) & 0x3F);
            if (sizeof(wchar_t) == 2 && val >= 0x10000) {
                val -= 0x10000;
                out.push_back(static_cast<wchar_t>(0xD800 + (val >> 10)));
                out.push_back(static_cast<wchar_t>(0xDC00 + (val & 0x3FF)));
            } else {
                out.push_back(static_cast<wchar_t>(val));
            }
            i += 4;
        } else {
            out.push_back(static_cast<wchar_t>(c));
            ++i;
        }
    }
    return out;
}

std::string UrlEncode(const std::string& s, bool preserveSlash) {
    std::string out;
    for (unsigned char c : s) {
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
            (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~' ||
            (preserveSlash && c == '/')) {
            out += (char)c;
        } else {
            char hex[4];
            snprintf(hex, sizeof(hex), "%%%02X", c);
            out += hex;
        }
    }
    return out;
}

std::string UrlDecode(const std::string& s) {
    std::string out;
    for (size_t i = 0; i < s.size(); ++i) {
        if (s[i] == '%' && i + 2 < s.size()) {
            char hi = s[i + 1], lo = s[i + 2];
            auto hexVal = [](char c) -> int {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                return -1;
            };
            int h = hexVal(hi), l = hexVal(lo);
            if (h >= 0 && l >= 0) {
                out += (char)((h << 4) | l);
                i += 2;
                continue;
            }
        }
        out += s[i];
    }
    return out;
}

int64_t Iso8601ToUnix(const std::string& iso) {
    if (iso.size() < 19) return 0;
    struct tm tm = {};
    int matched = sscanf(iso.c_str(), "%d-%d-%dT%d:%d:%d",
           &tm.tm_year, &tm.tm_mon, &tm.tm_mday,
           &tm.tm_hour, &tm.tm_min, &tm.tm_sec);
    if (matched != 6) return 0;
    tm.tm_year -= 1900;
    tm.tm_mon -= 1;
    time_t t = timegm(&tm);
    if (t == static_cast<time_t>(-1)) return 0;
    return static_cast<int64_t>(t);
}

std::string UnixToIso8601(int64_t ts) {
    time_t t = (time_t)ts;
    struct tm tm{};
    gmtime_r(&t, &tm);
    char buf[32];
    snprintf(buf, sizeof(buf), "%04d-%02d-%02dT%02d:%02d:%02dZ",
             tm.tm_year + 1900, tm.tm_mon + 1, tm.tm_mday,
             tm.tm_hour, tm.tm_min, tm.tm_sec);
    return buf;
}

} // namespace HttpUtil
