// Token store implementation for Linux / Android (DroidDeck).
// Implements ITokenStore using POSIX secure file permissions (0600: user-only read/write).

#include "cloud_provider_base.h"
#include <memory>
#include <string>
#include <fstream>
#include <filesystem>
#include <fcntl.h>
#include <unistd.h>
#include <sys/stat.h>

class LinuxTokenStore : public ITokenStore {
public:
    std::string Read(const std::string& path) override {
        if (path.empty()) return {};
        std::ifstream f(path, std::ios::binary);
        if (!f) return {};
        return std::string((std::istreambuf_iterator<char>(f)),
                            std::istreambuf_iterator<char>());
    }

    bool Write(const std::string& path, const std::string& json) override {
        if (path.empty()) return false;

        // Ensure parent directory exists with 0700 permissions
        std::error_code ec;
        auto parent = std::filesystem::path(path).parent_path();
        if (!parent.empty()) {
            std::filesystem::create_directories(parent, ec);
            ::chmod(parent.c_str(), 0700);
        }

        // Write directly with 0600 permissions
        int fd = ::open(path.c_str(), O_WRONLY | O_CREAT | O_TRUNC, 0600);
        if (fd < 0) return false;

        const char* ptr = json.data();
        size_t remaining = json.size();
        while (remaining > 0) {
            ssize_t written = ::write(fd, ptr, remaining);
            if (written < 0) {
                if (errno == EINTR) continue;
                ::close(fd);
                return false;
            }
            ptr += written;
            remaining -= written;
        }

        ::fdatasync(fd);
        ::close(fd);
        ::chmod(path.c_str(), 0600);
        return true;
    }

    bool IsEncryptionAvailable() const override {
        // Linux uses user-isolated file permission security (0600)
        return false;
    }
};

std::unique_ptr<ITokenStore> CreateTokenStore() {
    return std::make_unique<LinuxTokenStore>();
}
