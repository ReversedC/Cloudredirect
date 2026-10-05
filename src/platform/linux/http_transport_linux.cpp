// Linux HTTP transport adapter using libcurl
#include "cloud_provider_base.h"
#include "http_util.h"
#include "log.h"

#include <curl/curl.h>
#include <cctype>
#include <map>
#include <memory>
#include <string>
#include <algorithm>

namespace {

static size_t WriteCallback(void* contents, size_t size, size_t nmemb, void* userp) {
    size_t total = size * nmemb;
    auto* s = static_cast<std::string*>(userp);
    s->append(static_cast<const char*>(contents), total);
    return total;
}

static size_t HeaderCallback(char* buffer, size_t size, size_t nitems, void* userdata) {
    size_t total = size * nitems;
    auto* resp = static_cast<HttpUtil::HttpResp*>(userdata);
    std::string header(buffer, total);

    // Strip trailing \r\n
    while (!header.empty() && (header.back() == '\r' || header.back() == '\n')) {
        header.pop_back();
    }

    size_t colon = header.find(':');
    if (colon != std::string::npos) {
        std::string key = header.substr(0, colon);
        std::string val = header.substr(colon + 1);

        // Trim leading spaces
        size_t first = val.find_first_not_of(" \t");
        if (first != std::string::npos) val = val.substr(first);

        // Lowercase key
        std::transform(key.begin(), key.end(), key.begin(),
                       [](unsigned char c) { return std::tolower(c); });

        if (key == "location") {
            resp->location = val;
        }
        resp->headers[key] = val;
    }
    return total;
}

} // namespace

class CurlHttpTransport : public IHttpTransport {
public:
    explicit CurlHttpTransport(const char* logTag) : m_logTag(logTag ? logTag : "Curl") {}
    ~CurlHttpTransport() override { Shutdown(); }

    bool Init() override {
        if (m_initialized) return true;
        CURLcode res = curl_global_init(CURL_GLOBAL_DEFAULT);
        if (res != CURLE_OK) {
            LOG("%s curl_global_init failed: %s", m_logTag, curl_easy_strerror(res));
            return false;
        }
        m_initialized = true;
        return true;
    }

    void Shutdown() override {
        if (m_initialized) {
            curl_global_cleanup();
            m_initialized = false;
        }
    }

    bool IsReady() const override { return m_initialized; }
    void SetOptions(const TransportOptions& opts) override { m_opts = opts; }

    HttpUtil::HttpResp Request(const char* method, const char* host,
                               const std::string& path,
                               const std::string& body,
                               const std::vector<std::string>& headers) override {
        std::string fullUrl = "https://" + std::string(host) + path;
        return RequestUrl(method, fullUrl, body, headers);
    }

    HttpUtil::HttpResp RequestUrl(const char* method, const std::string& fullUrl,
                                  const std::string& body,
                                  const std::vector<std::string>& headers) override {
        HttpUtil::HttpResp resp;
        if (!m_initialized) {
            if (!Init()) return resp;
        }

        bool isHttps = (fullUrl.rfind("https://", 0) == 0);
        bool isHttp  = (fullUrl.rfind("http://", 0) == 0);

        if (!isHttps && !(m_opts.allowInsecureHttp && isHttp)) {
            LOG("%s BLOCKED non-HTTPS request to %s", m_logTag, fullUrl.c_str());
            return resp;
        }

        CURL* curl = curl_easy_init();
        if (!curl) {
            LOG("%s curl_easy_init failed", m_logTag);
            return resp;
        }

        struct curl_slist* chunk = nullptr;
        for (const auto& h : headers) {
            chunk = curl_slist_append(chunk, h.c_str());
        }

        curl_easy_setopt(curl, CURLOPT_URL, fullUrl.c_str());
        curl_easy_setopt(curl, CURLOPT_CUSTOMREQUEST, method);
        curl_easy_setopt(curl, CURLOPT_HTTPHEADER, chunk);

        if (!body.empty()) {
            curl_easy_setopt(curl, CURLOPT_POSTFIELDS, body.data());
            curl_easy_setopt(curl, CURLOPT_POSTFIELDSIZE_LARGE, static_cast<curl_off_t>(body.size()));
        } else if (strcmp(method, "POST") == 0 || strcmp(method, "PUT") == 0) {
            curl_easy_setopt(curl, CURLOPT_POSTFIELDSIZE, 0L);
        }

        curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, WriteCallback);
        curl_easy_setopt(curl, CURLOPT_WRITEDATA, &resp.body);

        curl_easy_setopt(curl, CURLOPT_HEADERFUNCTION, HeaderCallback);
        curl_easy_setopt(curl, CURLOPT_HEADERDATA, &resp);

        // Timeouts
        curl_easy_setopt(curl, CURLOPT_CONNECTTIMEOUT, 10L);
        if (body.size() > 256 * 1024) {
            curl_easy_setopt(curl, CURLOPT_TIMEOUT, 60L);
        } else {
            curl_easy_setopt(curl, CURLOPT_TIMEOUT, 20L);
        }

        // SSL options
        if (m_opts.allowInsecureTls) {
            curl_easy_setopt(curl, CURLOPT_SSL_VERIFYPEER, 0L);
            curl_easy_setopt(curl, CURLOPT_SSL_VERIFYHOST, 0L);
        } else {
            curl_easy_setopt(curl, CURLOPT_SSL_VERIFYPEER, 1L);
            curl_easy_setopt(curl, CURLOPT_SSL_VERIFYHOST, 2L);
        }

        if (!m_opts.caCertPath.empty()) {
            curl_easy_setopt(curl, CURLOPT_CAINFO, m_opts.caCertPath.c_str());
        }

        // Follow up to 5 redirects for general GET requests (not upload sessions)
        CURLcode res = curl_easy_perform(curl);
        if (res == CURLE_OK) {
            long http_code = 0;
            curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &http_code);
            resp.status = static_cast<int>(http_code);
        } else {
            LOG("%s curl_easy_perform failed: %s (%s)", m_logTag, curl_easy_strerror(res), fullUrl.c_str());
        }

        if (chunk) curl_slist_free_all(chunk);
        curl_easy_cleanup(curl);
        return resp;
    }

    HttpUtil::HttpResp AuthenticatedGetWithRedirect(const std::string& host,
                                                    const std::string& path,
                                                    const std::string& authHeader) override {
        std::vector<std::string> hdrs;
        if (!authHeader.empty()) {
            hdrs.push_back("Authorization: " + authHeader);
        }

        std::string fullUrl = "https://" + host + path;
        HttpUtil::HttpResp resp = RequestUrl("GET", fullUrl, "", hdrs);

        // If redirected (OneDrive CDN token stripping requirement)
        if ((resp.status == 301 || resp.status == 302 || resp.status == 303 ||
             resp.status == 307 || resp.status == 308) && !resp.location.empty()) {
            // Follow redirect with NO Authorization header
            std::vector<std::string> cleanHdrs;
            return RequestUrl("GET", resp.location, "", cleanHdrs);
        }

        return resp;
    }

private:
    const char* m_logTag;
    bool m_initialized = false;
    TransportOptions m_opts;
};

std::unique_ptr<IHttpTransport> CreateHttpTransport(const char* logTag) {
    return std::make_unique<CurlHttpTransport>(logTag);
}
