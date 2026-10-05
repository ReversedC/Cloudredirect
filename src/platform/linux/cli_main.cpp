// cloud_redirect_cli - Linux CLI wrapper for libcloud_redirect.so
// Loads libcloud_redirect.so and executes the CLI entry point.

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <dlfcn.h>
#include <unistd.h>
#include <string>
#include <vector>
#include <filesystem>

typedef int (*CliMainFn)(int argc, char** argv);

static std::string GetExecutableDir() {
    char buf[1024] = {};
    ssize_t len = readlink("/proc/self/exe", buf, sizeof(buf) - 1);
    if (len > 0) {
        buf[len] = '\0';
        return std::filesystem::path(buf).parent_path().string();
    }
    return ".";
}

int main(int argc, char** argv) {
    std::string exeDir = GetExecutableDir();
    std::vector<std::string> candidates = {
        exeDir + "/libcloud_redirect.so",
        exeDir + "/cloud_redirect.so",
        "./libcloud_redirect.so",
        "./cloud_redirect.so",
        "libcloud_redirect.so"
    };

    void* handle = nullptr;
    for (const auto& path : candidates) {
        handle = dlopen(path.c_str(), RTLD_NOW | RTLD_GLOBAL);
        if (handle) break;
    }

    if (!handle) {
        fprintf(stderr, "Error: Cannot load libcloud_redirect.so: %s\n", dlerror());
        return 1;
    }

    CliMainFn cliMain = reinterpret_cast<CliMainFn>(dlsym(handle, "CloudRedirect_CliMain"));
    if (!cliMain) {
        fprintf(stderr, "Error: Cannot find CloudRedirect_CliMain symbol in library: %s\n", dlerror());
        dlclose(handle);
        return 1;
    }

    int exitCode = 0;
    if (argc >= 2 && strcmp(argv[1], "--cli") == 0) {
        exitCode = cliMain(argc, argv);
    } else {
        std::vector<char*> newArgv;
        newArgv.reserve(argc + 2);
        newArgv.push_back(argv[0]);
        char cliFlag[] = "--cli";
        newArgv.push_back(cliFlag);
        for (int i = 1; i < argc; ++i) {
            newArgv.push_back(argv[i]);
        }
        newArgv.push_back(nullptr);
        exitCode = cliMain(static_cast<int>(newArgv.size() - 1), newArgv.data());
    }

    dlclose(handle);
    return exitCode;
}
