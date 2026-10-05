# CloudRedirect for DroidDeck (Android SteamOS Environment)

[DroidDeck](https://github.com/Droid-Deck/DroidDeck) runs the official ARM64 Linux Steam client in Big Picture mode using Gamescope, FEX-Emu (for x86/x64 instruction translation), ARM64 Proton, and Turnip Vulkan drivers on Qualcomm Snapdragon Android devices.

CloudRedirect enables seamless cross-platform cloud save synchronization (Google Drive, OneDrive, Cloudflare R2, AWS S3, WebDAV, Local Disk) and playtime/achievement synchronization directly inside DroidDeck.

---

## Architecture Overview

```
                      +-----------------------------------+
                      |       Android Device (Host)       |
                      |   Qualcomm Snapdragon (Adreno)   |
                      +-----------------+-----------------+
                                        |
                 +----------------------+----------------------+
                 | DroidDeck Container / Termux Rootfs (ARM64) |
                 |                                             |
                 |   +-------------------------------------+   |
                 |   |      Gamescope Compositor           |   |
                 |   |   (SteamOS Big Picture Interface)   |   |
                 |   +------------------+------------------+   |
                 |                      |                      |
                 |   +------------------v------------------+   |
                 |   |    Linux Steam Client (ARM64)       |   |
                 |   |    LD_PRELOAD=libcloud_redirect.so  |   |
                 |   +------------------+------------------+   |
                 |                      |                      |
                 |         +------------+------------+         |
                 |         |                         |         |
                 |   +-----v-----+             +-----v-----+   |
                 |   |  Native   |             |  Proton   |   |
                 |   | Linux Game|             | (Windows) |   |
                 |   +-----------+             +-----+-----+   |
                 |                                   |         |
                 |                     +-------------v-------+ |
                 |                     | compatdata/<appid>/ | |
                 |                     | pfx/drive_c/users/  | |
                 |                     +-------------+-------+ |
                 +-----------------------------------|---------+
                                                     |
                                   +-----------------v-----------------+
                                   |      CloudRedirect Engine         |
                                   |  (AutoCloud Scanner + Cloud Sync) |
                                   +-----------------+-----------------+
                                                     |
                                        +------------+------------+
                                        |                         |
                                 +------v------+           +------v------+
                                 |Google Drive |           |  OneDrive   |
                                 +-------------+           +-------------+
                                 |  S3 / R2    |           |   WebDAV    |
                                 +-------------+           +-------------+
```

---

## Features on DroidDeck

1. **Proton Path Resolution**:
   Automatically maps Windows AutoCloud rules (`%WinAppDataLocal%`, `%WinAppDataRoaming%`, `%WinMyDocuments%`, `%WinSavedGames%`, `%WindowsHome%`) to DroidDeck's Proton prefixes:
   `~/.local/share/Steam/steamapps/compatdata/<appid>/pfx/drive_c/users/steamuser/...`
   Supports both modern Windows 10 layout (`AppData/Local`, `AppData/Roaming`, `Documents`) and legacy XP/Wine paths (`Local Settings/Application Data`, `My Documents`).

2. **Native Shared Library (`libcloud_redirect.so`)**:
   Built for ARM64 (`aarch64`) and x86_64. Preloads seamlessly via `LD_PRELOAD` into Steam to intercept cloud RPCs (`Cloud.GetAppFileChangelist`, `Cloud.ClientBeginFileUpload`, etc.) and redirect blob uploads/downloads through the local embedded HTTP server.

3. **Standalone Headless CLI (`cloud_redirect_cli`)**:
   Full CLI interface for headless operation without requiring a desktop UI:
   - Check authentication status: `cloud_redirect_cli auth-status <provider>`
   - Authenticate cloud providers: `cloud_redirect_cli authenticate <provider>`
   - Upload game saves: `cloud_redirect_cli save-upload <provider> <account_id> <app_id> <game_name> <path>`
   - Download game saves: `cloud_redirect_cli save-download <provider> <account_id> <app_id> <game_name> <dest>`
   - List remote saves: `cloud_redirect_cli save-list <provider> <account_id> <app_id> <game_name>`
   - Prune legacy metadata & sync all apps.

4. **Background Auto-Sync Daemon**:
   `scripts/droiddeck/cloudredirect_daemon.sh` monitors running game processes under Gamescope and triggers automatic cloud upload when games exit.

---

## Building from Source (Linux / ARM64)

Inside your Linux environment (Ubuntu, Debian, Arch, or Termux rootfs):

```bash
# 1. Install build dependencies
sudo apt update
sudo apt install -y build-essential cmake libcurl4-openssl-dev git

# 2. Clone the linux branch
git clone -b linux https://github.com/mirzaarsyad74-cmyk/Cloudredirect.git
cd Cloudredirect

# 3. Configure and build
mkdir build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release
cmake --build . -j$(nproc)

# 4. Verify outputs
ls -lh libcloud_redirect.so cloud_redirect_cli
```

---

## Installation & Setup on DroidDeck

Run the provided installation script:

```bash
bash scripts/droiddeck/install_droiddeck.sh
```

This installs:
- `libcloud_redirect.so` to `~/.local/lib/`
- `cloud_redirect_cli` to `~/.local/bin/`
- `cloudredirect-daemon` to `~/.local/bin/`
- `droiddeck-steam-wrapper` to `~/.local/bin/`

### 1. Authenticate with your Cloud Provider

For Google Drive:
```bash
cloud_redirect_cli authenticate gdrive
```
Open the generated OAuth link in your browser, complete authentication, and paste the authorization code.

For OneDrive:
```bash
cloud_redirect_cli authenticate onedrive
```

Check status anytime:
```bash
cloud_redirect_cli auth-status gdrive
```

### 2. Launching Steam in DroidDeck

To run Steam with CloudRedirect active:
```bash
export LD_PRELOAD="$HOME/.local/lib/libcloud_redirect.so:$LD_PRELOAD"
steam
```

Or using the launch wrapper:
```bash
droiddeck-steam-wrapper steam
```

Logs are written to:
`~/.local/share/cloudredirect/cloud_redirect.log`
