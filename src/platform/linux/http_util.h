#pragma once
#include <string>
#include <cstdint>
#include <map>

namespace HttpUtil {

std::wstring Widen(const std::string& s);
std::string UrlEncode(const std::string& s, bool preserveSlash = false);
std::string UrlDecode(const std::string& s);
int64_t Iso8601ToUnix(const std::string& iso);
std::string UnixToIso8601(int64_t ts);

struct HttpResp {
    int status = 0;
    std::string body;
    std::string location;
    std::map<std::string, std::string> headers;
};

} // namespace HttpUtil
