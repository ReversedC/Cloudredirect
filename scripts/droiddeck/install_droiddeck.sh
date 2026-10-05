#!/usr/bin/env bash
# ==============================================================================
# CloudRedirect - DroidDeck / Linux ARM64 Installation Script
# https://github.com/Droid-Deck/DroidDeck
# ==============================================================================
set -euo pipefail

echo "=================================================="
echo " CloudRedirect Installer for DroidDeck / Linux"
echo "=================================================="

# 1. Detect Architecture
ARCH=$(uname -m)
echo "[+] Detected architecture: ${ARCH}"
if [ "${ARCH}" != "aarch64" ] && [ "${ARCH}" != "x86_64" ]; then
    echo "[-] Warning: Unsupported architecture '${ARCH}'. CloudRedirect supports aarch64 and x86_64."
fi

# 2. Setup install directories
INSTALL_BIN="${HOME}/.local/bin"
INSTALL_LIB="${HOME}/.local/lib"
CONFIG_DIR="${XDG_CONFIG_HOME:-${HOME}/.config}/CloudRedirect"
DATA_DIR="${XDG_DATA_HOME:-${HOME}/.local/share}/cloudredirect"

mkdir -p "${INSTALL_BIN}" "${INSTALL_LIB}" "${CONFIG_DIR}" "${DATA_DIR}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST_DIR="${SCRIPT_DIR}/../../build"

echo "[+] Copying binaries..."
if [ -f "${DIST_DIR}/libcloud_redirect.so" ]; then
    cp -f "${DIST_DIR}/libcloud_redirect.so" "${INSTALL_LIB}/"
    chmod 755 "${INSTALL_LIB}/libcloud_redirect.so"
    echo "    Installed ${INSTALL_LIB}/libcloud_redirect.so"
elif [ -f "${DIST_DIR}/cloud_redirect.so" ]; then
    cp -f "${DIST_DIR}/cloud_redirect.so" "${INSTALL_LIB}/libcloud_redirect.so"
    chmod 755 "${INSTALL_LIB}/libcloud_redirect.so"
    echo "    Installed ${INSTALL_LIB}/libcloud_redirect.so"
else
    echo "[-] Note: libcloud_redirect.so not found in build directory. If building from source:"
    echo "    mkdir build && cd build && cmake .. -DCMAKE_BUILD_TYPE=Release && cmake --build ."
fi

if [ -f "${DIST_DIR}/cloud_redirect_cli" ]; then
    cp -f "${DIST_DIR}/cloud_redirect_cli" "${INSTALL_BIN}/"
    chmod 755 "${INSTALL_BIN}/cloud_redirect_cli"
    echo "    Installed ${INSTALL_BIN}/cloud_redirect_cli"
fi

# 3. Locate Steam directory in DroidDeck
STEAM_DIR=""
CANDIDATE_DIRS=(
    "${HOME}/.local/share/Steam"
    "${HOME}/.steam/steam"
    "${HOME}/.steam/root"
    "/data/data/com.valvesoftware.steam/rootfs/home/steamuser/.local/share/Steam"
    "${HOME}/.var/app/com.valvesoftware.Steam/.local/share/Steam"
)

for d in "${CANDIDATE_DIRS[@]}"; do
    if [ -d "${d}" ]; then
        STEAM_DIR="${d}"
        break
    fi
done

if [ -n "${STEAM_DIR}" ]; then
    echo "[+] Found Steam directory: ${STEAM_DIR}"
    # Copy shared library to steam directory for direct preload
    cp -f "${INSTALL_LIB}/libcloud_redirect.so" "${STEAM_DIR}/libcloud_redirect.so" 2>/dev/null || true
else
    echo "[!] Steam directory not detected yet. It will be auto-detected at runtime."
fi

# 4. Generate default config if not existing
DEFAULT_CONFIG="${CONFIG_DIR}/config.json"
if [ ! -f "${DEFAULT_CONFIG}" ]; then
    cat <<EOF > "${DEFAULT_CONFIG}"
{
  "provider": "gdrive",
  "all_apps": true,
  "sync_achievements": true,
  "sync_playtime": true,
  "apps": []
}
EOF
    chmod 600 "${DEFAULT_CONFIG}"
    echo "[+] Created default config at ${DEFAULT_CONFIG}"
fi

# 5. Create Steam launch wrapper helper
WRAPPER="${INSTALL_BIN}/droiddeck-steam-wrapper"
cat <<'EOF' > "${WRAPPER}"
#!/usr/bin/env bash
# Preload CloudRedirect into Steam
export LD_PRELOAD="${HOME}/.local/lib/libcloud_redirect.so:${LD_PRELOAD:-}"
export CLOUDREDIRECT_FORCE_INIT=1

# Start the cloud sync daemon in the background
if command -v cloudredirect-daemon >/dev/null 2>&1; then
    cloudredirect-daemon &
fi

exec "$@"
EOF
chmod 755 "${WRAPPER}"

# 6. Copy background daemon script
if [ -f "${SCRIPT_DIR}/cloudredirect_daemon.sh" ]; then
    cp -f "${SCRIPT_DIR}/cloudredirect_daemon.sh" "${INSTALL_BIN}/cloudredirect-daemon"
    chmod 755 "${INSTALL_BIN}/cloudredirect-daemon"
fi

echo "=================================================="
echo " CloudRedirect successfully installed for DroidDeck!"
echo ""
echo " Next Steps:"
echo " 1. Add '${INSTALL_BIN}' to your PATH if not already present:"
echo "    export PATH=\"\${HOME}/.local/bin:\${PATH}\""
echo ""
echo " 2. Authenticate your cloud provider (e.g. Google Drive / OneDrive):"
echo "    cloud_redirect_cli authenticate gdrive"
echo ""
echo " 3. Verify status:"
echo "    cloud_redirect_cli auth-status gdrive"
echo ""
echo " 4. Launch Steam with CloudRedirect enabled:"
echo "    LD_PRELOAD=${INSTALL_LIB}/libcloud_redirect.so steam"
echo "    (or use '${WRAPPER} steam')"
echo "=================================================="
