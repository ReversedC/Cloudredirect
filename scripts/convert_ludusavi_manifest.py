"""
Script to download and convert mtkennerly/ludusavi-manifest data/manifest.yaml
into CloudRedirect's community database format (cloudredirect-community-saves-v1).
"""

import sys
import os
import re
import json
import urllib.request
import time
from datetime import datetime, timezone

LUDUSAVI_MANIFEST_URL = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml"

# Directories that are too broad to redirect as a save folder
TOO_BROAD_PATHS = {
    "%USERPROFILE%",
    "%USERPROFILE%\\DOCUMENTS",
    "%USERPROFILE%\\SAVED GAMES",
    "%USERPROFILE%\\APPDATA",
    "%USERPROFILE%\\APPDATA\\LOCAL",
    "%USERPROFILE%\\APPDATA\\LOCAL\\PACKAGES",
    "%USERPROFILE%\\APPDATA\\LOCALLOW",
    "%USERPROFILE%\\APPDATA\\ROAMING",
    "%APPDATA%",
    "%LOCALAPPDATA%",
    "%PUBLIC%",
    "%PROGRAMDATA%",
    "DOCUMENTS",
    "DOCUMENTS\\MY GAMES",
    "C:\\PROGRAM FILES",
    "C:\\PROGRAM FILES (X86)",
    "C:\\XBOXGAMES"
}

def clean_game_title(title_line: str) -> str:
    raw = title_line.strip()
    # Match double quoted key: "Title with : inside":
    if raw.startswith('"'):
        # Find closing quote that is followed by optional whitespace and colon
        end_idx = raw.rfind('":')
        if end_idx != -1:
            return raw[1:end_idx].replace('\\"', '"').strip()
    # Match single quoted key: 'Title with : inside':
    if raw.startswith("'"):
        end_idx = raw.rfind("':")
        if end_idx != -1:
            return raw[1:end_idx].replace("\\'", "'").strip()
    # Unquoted: strip trailing colon
    if raw.endswith(':'):
        return raw[:-1].strip()
    colon_idx = raw.rfind(':')
    if colon_idx != -1:
        return raw[:colon_idx].strip()
    return raw.strip()

def normalize_windows_path(raw_path: str) -> str | None:
    if not raw_path:
        return None
    
    p = raw_path.strip().strip('"\'')
    p = p.replace('/', '\\')

    # Exclude obvious non-Windows paths
    if p.startswith('~') or p.startswith('/'):
        return None
    if '<xdgData>' in p or '<xdgConfig>' in p:
        return None
    if '<home>\\Library' in p or '<home>\\.config' in p or '<home>\\.local' in p:
        return None

    # Replace placeholders
    p = p.replace('<winLocalAppDataLow>', '%USERPROFILE%\\AppData\\LocalLow')
    p = p.replace('<winLocalAppData>', '%LOCALAPPDATA%')
    p = p.replace('<winAppData>', '%APPDATA%')
    p = p.replace('<winDocuments>', 'Documents')
    p = p.replace('<winSavedGames>', '%USERPROFILE%\\Saved Games')
    p = p.replace('<winProgramData>', '%PROGRAMDATA%')
    p = p.replace('<winPublic>', '%PUBLIC%')
    p = p.replace('<winDir>', '%WINDIR%')
    p = p.replace('<home>\\AppData\\LocalLow', '%USERPROFILE%\\AppData\\LocalLow')
    p = p.replace('<home>\\Saved Games', '%USERPROFILE%\\Saved Games')
    p = p.replace('<home>\\Documents', 'Documents')
    p = p.replace('<home>', '%USERPROFILE%')
    p = p.replace('<storeUserId>', '*')
    p = p.replace('<osUserName>', '%USERNAME%')

    # If it still contains unknown <...>, discard or inspect
    if re.search(r'<[a-zA-Z0-9_]+>', p):
        # Ignore paths with unsupported placeholders like <base>, <root> for universal saves unless specific
        return None

    # Ludusavi paths often end with files or globs, e.g.
    # \\Saved\\SaveGames\\**\\*.sav or \\settings.json or \\*
    # CloudRedirect operates on save FOLDERS.
    segments = p.split('\\')
    folder_segments = []
    for seg in segments:
        if '*' in seg or '?' in seg:
            break
        folder_segments.append(seg)
    
    folder = '\\'.join(folder_segments)

    # If the last segment looks like a file (has an extension like .sav, .ini, .json, .dat, .xml, .bin, .cfg, .txt)
    # strip it to get the containing directory
    if '\\' in folder:
        last_seg = folder.split('\\')[-1]
        if '.' in last_seg and not last_seg.startswith('.'):
            # Check common file extensions
            ext = last_seg.split('.')[-1].lower()
            if ext in {'sav', 'save', 'ini', 'json', 'dat', 'xml', 'bin', 'cfg', 'config', 'txt', 'db', 'sqlite', 'log', 'ron', 'profile', 'dat0', 'sl2', 'sl3', 'chr'}:
                folder = folder.rsplit('\\', 1)[0]

    # Clean double slashes or trailing slashes
    folder = re.sub(r'\\{2,}', '\\\\', folder).rstrip('\\')

    if not folder:
        return None

    # Check if too broad
    if folder.upper() in TOO_BROAD_PATHS:
        return None

    # Ensure it starts with an environment variable or Documents or drive
    if not (folder.startswith('%') or folder.startswith('Documents') or (len(folder) >= 2 and folder[1] == ':')):
        return None

    return folder

def parse_manifest_content(text: str) -> dict:
    lines = text.splitlines()
    games = {}
    
    current_game = None
    in_files = False
    in_launch = False
    in_steam = False
    current_path_raw = None
    current_path_os_list = []
    current_path_tags = []
    
    paths_for_current_game = []
    steam_ids_for_current_game = []

    def commit_path():
        nonlocal current_path_raw, current_path_os_list, current_path_tags
        if not current_path_raw:
            return
        # If os list is specified and doesn't contain windows, skip
        if current_path_os_list and 'windows' not in current_path_os_list:
            current_path_raw = None
            current_path_os_list = []
            current_path_tags = []
            return
        
        normalized = normalize_windows_path(current_path_raw)
        if normalized and normalized not in paths_for_current_game:
            paths_for_current_game.append(normalized)

        current_path_raw = None
        current_path_os_list = []
        current_path_tags = []

    def commit_game():
        nonlocal current_game, paths_for_current_game, steam_ids_for_current_game
        commit_path()
        if current_game and paths_for_current_game:
            games[current_game] = {
                "paths": list(paths_for_current_game),
                "steamIds": list(steam_ids_for_current_game)
            }
        current_game = None
        paths_for_current_game = []
        steam_ids_for_current_game = []

    for line in lines:
        if not line or line.startswith('---'):
            continue
        
        # Check indent
        indent = len(line) - len(line.lstrip(' '))
        stripped = line.strip()

        # Top-level: Game title
        if indent == 0:
            commit_game()
            current_game = clean_game_title(stripped)
            in_files = False
            in_launch = False
            in_steam = False
            continue

        if not current_game:
            continue

        if indent == 2:
            commit_path()
            in_files = stripped.startswith('files:')
            in_launch = stripped.startswith('launch:')
            in_steam = stripped.startswith('steam:')
            
            # Check steam id directly under steam:
            # Sometimes: steam:\n  id: 12345
            continue

        if in_files:
            if indent == 4:
                commit_path()
                # Line is a file path: e.g. `"<winLocalAppData>/game":`
                colon_idx = stripped.find(':')
                if colon_idx != -1:
                    current_path_raw = stripped[:colon_idx].strip()
                else:
                    current_path_raw = stripped
                current_path_os_list = []
                current_path_tags = []
            elif indent >= 6:
                if stripped.startswith('os:'):
                    val = stripped[3:].strip().lower()
                    current_path_os_list.append(val)
                elif stripped.startswith('- os:'):
                    val = stripped[5:].strip().lower()
                    current_path_os_list.append(val)
                elif stripped.startswith('- save') or stripped.startswith('- config'):
                    val = stripped[1:].strip().lower()
                    current_path_tags.append(val)

        if in_steam:
            if stripped.startswith('id:'):
                try:
                    s_id = int(stripped[3:].strip())
                    if s_id > 0 and s_id not in steam_ids_for_current_game:
                        steam_ids_for_current_game.append(s_id)
                except ValueError:
                    pass

    commit_game()
    return games

def convert_to_cloudredirect_format(games_data: dict) -> dict:
    cr_games = {}
    for game_name, data in games_data.items():
        paths = data["paths"]
        if not paths:
            continue
        
        # Primary key: Game Title
        cr_games[game_name] = paths
        
        # Secondary key: Steam AppId (if available)
        # Allows instant O(1) detection when scanning Steam games by AppId!
        for s_id in data.get("steamIds", []):
            cr_games[f"steam:{s_id}"] = paths

    result = {
        "schema": "cloudredirect-community-saves-v1",
        "updatedAt": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "description": "CloudRedirect Community Game Save Location Database (Auto-generated from Ludusavi Manifest)",
        "source": "https://github.com/mtkennerly/ludusavi-manifest",
        "totalGames": len(games_data),
        "totalSignatures": len(cr_games),
        "games": cr_games
    }
    return result

def main():
    start_time = time.time()
    input_file = None
    output_file = "community_saves_ludusavi.json"

    if len(sys.argv) > 1:
        input_file = sys.argv[1]
    if len(sys.argv) > 2:
        output_file = sys.argv[2]

    if input_file and os.path.exists(input_file):
        print(f"Reading local manifest from {input_file}...")
        with open(input_file, "r", encoding="utf-8", errors="ignore") as f:
            content = f.read()
    else:
        print(f"Downloading manifest from {LUDUSAVI_MANIFEST_URL}...")
        req = urllib.request.Request(LUDUSAVI_MANIFEST_URL, headers={"User-Agent": "CloudRedirect-Converter/1.0"})
        with urllib.request.urlopen(req) as resp:
            content = resp.read().decode("utf-8", errors="ignore")
        print(f"Downloaded {len(content)/(1024*1024):.2f} MB.")

    print("Parsing manifest content...")
    t_parse = time.time()
    games_data = parse_manifest_content(content)
    print(f"Parsed {len(games_data)} games with Windows saves in {time.time() - t_parse:.2f}s.")

    print("Formatting into CloudRedirect schema...")
    cr_format = convert_to_cloudredirect_format(games_data)

    print(f"Saving to {output_file}...")
    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(cr_format, f, indent=2)

    size_mb = os.path.getsize(output_file) / (1024 * 1024)
    print(f"Done! Created {output_file} ({size_mb:.2f} MB, {cr_format['totalSignatures']} signatures) in {time.time() - start_time:.2f}s.")

if __name__ == "__main__":
    main()
