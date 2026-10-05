#!/usr/bin/env bash
# ==============================================================================
# CloudRedirect - Background Sync Daemon for DroidDeck / Gamescope
# Monitors active game processes in DroidDeck and syncs saves automatically.
# ==============================================================================
set -euo pipefail

LOG_FILE="${XDG_DATA_HOME:-${HOME}/.local/share}/cloudredirect/daemon.log"
mkdir -p "$(dirname "${LOG_FILE}")"

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "${LOG_FILE}"
}

log "CloudRedirect DroidDeck sync daemon started (PID=$$)"

CLI_BIN="$(command -v cloud_redirect_cli || echo "${HOME}/.local/bin/cloud_redirect_cli")"
if [ ! -x "${CLI_BIN}" ]; then
    log "[-] Error: cloud_redirect_cli not found or not executable at ${CLI_BIN}"
    exit 1
fi

PROVIDER="gdrive"
CONFIG_FILE="${XDG_CONFIG_HOME:-${HOME}/.config}/CloudRedirect/config.json"
if [ -f "${CONFIG_FILE}" ]; then
    DETECTED_PROVIDER=$(grep -o '"provider": *"[^"]*"' "${CONFIG_FILE}" | cut -d'"' -f4 || echo "")
    if [ -n "${DETECTED_PROVIDER}" ]; then
        PROVIDER="${DETECTED_PROVIDER}"
    fi
fi

log "[+] Using Cloud Provider: ${PROVIDER}"

# State tracker for running games: AppId -> PID
declare -A ACTIVE_GAMES

check_steam_games() {
    # On Linux/DroidDeck, game processes under Proton or native Linux run with SteamAppId or SteamGameId in env
    for pid in $(pgrep -f "steamapps/common" 2>/dev/null || true); do
        if [ -r "/proc/${pid}/environ" ]; then
            APPID=$(tr '\0' '\n' < "/proc/${pid}/environ" 2>/dev/null | grep -E '^SteamAppId=' | cut -d= -f2 || true)
            if [ -n "${APPID}" ] && [[ "${APPID}" =~ ^[0-9]+$ ]]; then
                if [ -z "${ACTIVE_GAMES[${APPID}]:-}" ]; then
                    ACTIVE_GAMES[${APPID}]="${pid}"
                    log "[+] Game launch detected: AppId ${APPID} (PID ${pid})"
                    # Trigger pre-launch cloud sync / download if desired
                    "${CLI_BIN}" save-download "${PROVIDER}" "0" "${APPID}" "" "" >/dev/null 2>&1 || true
                fi
            fi
        fi
    done

    # Check for exited games
    for app in "${!ACTIVE_GAMES[@]}"; do
        pid="${ACTIVE_GAMES[${app}]}"
        if ! kill -0 "${pid}" 2>/dev/null; then
            log "[+] Game exit detected: AppId ${app}"
            unset "ACTIVE_GAMES[${app}]"
            # Trigger post-exit cloud sync / upload
            log "    Syncing saves for AppId ${app} to ${PROVIDER}..."
            "${CLI_BIN}" save-upload "${PROVIDER}" "0" "${app}" "" "" >> "${LOG_FILE}" 2>&1 || true
        fi
    done
}

# Main polling loop (low overhead, checks every 3 seconds)
while true; do
    check_steam_games
    sleep 3
done
