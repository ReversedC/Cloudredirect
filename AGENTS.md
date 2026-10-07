# Agent Guidelines for CloudRedirect

## Development, Build & Release Directive
- **Local Testing & Debug Mode (DEFAULT / When user says "don't push to github" / "test locally")**:
  - Whenever the user instructs not to push to GitHub or wants local testing, always **build the app locally in debug version**.
  - Append a debug indicator or pre-release suffix (`<ReleasePrerelease>-DEBUG</ReleasePrerelease>` in `Version.props`) so it never conflicts with production or triggers premature auto-updates.
  - Build the working single standalone executable for local testing:
    ```powershell
    dotnet publish ui/CloudRedirect.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ui/bin/publish
    & "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /platform:x64 /win32icon:ui\steam_logo3.ico /res:ui\bin\publish\CloudRedirect.Core.exe,MainAppPayload /out:ui\bin\publish\CloudRedirect.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll src\launcher\CloudRedirectLauncher.cs
    Remove-Item ui\bin\publish\CloudRedirect.Core.exe, ui\bin\publish\*.pdb, ui\bin\publish\*.xml, ui\bin\publish\*Debug* -Force -ErrorAction SilentlyContinue
    Copy-Item ui\bin\publish\CloudRedirect.exe .\CloudRedirect.exe -Force
    ```
  - **Do NOT push to GitHub or trigger releases** until the user explicitly says to push to GitHub.

- **Mandatory Single Executable Rule for Publish**:
  - The `ui/bin/publish` folder MUST contain only **ONE** truly working standalone executable: `CloudRedirect.exe`.
  - Never leave intermediate files (`CloudRedirect.Core.exe`, `.pdb`, `*.xml`) or legacy/debug binaries in `ui/bin/publish`. They must always be cleaned up immediately following compilation.

- **Mandatory Version Increment Rule**:
  - **ALWAYS increase the version number when pushing to GitHub**.
  - Every single push to GitHub (`git push`) MUST be accompanied by incrementing `<ReleaseVersion>` in `Version.props` (e.g. `2.9.115` -> `2.9.116`).
  - Never push commits to GitHub without bumping `<ReleaseVersion>`.

- **Production Build & Release Directive (ONLY WHEN USER EXPLICITLY INSTRUCTS TO PUSH)**:
  - Once the user explicitly instructs to publish/push to GitHub:
    1. **MANDATORY**: Bump `<ReleaseVersion>` in `Version.props` (e.g. `2.9.115` -> `2.9.116`) and clear `<ReleasePrerelease>`.
    2. Build and publish the Windows Executable (`.exe`) with auto-cleanup:
       ```powershell
       dotnet publish ui/CloudRedirect.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ui/bin/publish
       & "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /platform:x64 /win32icon:ui\steam_logo3.ico /res:ui\bin\publish\CloudRedirect.Core.exe,MainAppPayload /out:ui\bin\publish\CloudRedirect.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll src\launcher\CloudRedirectLauncher.cs
       Remove-Item ui\bin\publish\CloudRedirect.Core.exe, ui\bin\publish\*.pdb, ui\bin\publish\*.xml, ui\bin\publish\*Debug* -Force -ErrorAction SilentlyContinue
       ```
    3. Commit & Push Code:
       ```powershell
       git add -A
       git commit -m "..."
       git push origin master
       ```
    4. Publish GitHub Release:
       ```powershell
       powershell -ExecutionPolicy Bypass -File scripts/publish-release.ps1 -ReleaseBody "<Description of changes>"
       ```

## Project Overview
- **Target Platform**: Windows x64 only.
- **Tech Stack**:
  - Companion Application: C# / WPF on .NET 8 (`ui/CloudRedirect.csproj`).
  - Native Redirection Core: C++20 (`src/`).
  - UI Library: Lepoco `WPF-UI` (4.2.0) with an authentic **Steam Theme** defined in `ui/Themes/SteamTheme.xaml`.
  - Steam Plugin: Millennium framework integration packaged as a `.star` binary archive (`CloudRedirect.star`).
- **Styling Guidelines**:
  - The UI is styled to match the modern Steam desktop client aesthetic (dark navy/charcoal backgrounds, Steam cyan/blue highlights, and iconic Steam "Play" green action buttons).
  - Do not introduce OS Light Mode watchers; the app should consistently maintain its Steam dark theme.

## Steam Millennium Plugin (.star Package)
- The native Steam integration plugin is maintained under `ui/Resources/MillenniumPlugin/`.
- It is packaged as a standard Millennium `.star` container (`CloudRedirect.star`) via `scripts/build_star_plugin.py`.
- **Automated Build**: `ui/CloudRedirect.csproj` automatically runs `BuildStarPlugin` prior to compilation, ensuring `CloudRedirect.star` is always regenerated from the latest Lua/JavaScript sources and embedded into the application.
- **Deployment**: `MillenniumPluginService.cs` automatically deploys `CloudRedirect.star` and extracted files into `C:\Program Files (x86)\Steam\millennium\plugins\` when enabled in Settings.

## Discord Release Announcements
- When a Windows release is published (`vX.Y.Z`), the `.github/workflows/discord-release-announce.yml` workflow automatically triggers.
- It uses the repository secret `DISCORD_USER_TOKEN` to:
  1. Access the target Discord channel (`1495014736515829760`).
  2. Search for and delete previous release messages posted by the user in that channel.
  3. Upload `CloudRedirect.exe` directly along with the version, changelog caption, download links, and auto-playing animated setup guides (`guide.gif` and `setup_wizard_phone_copy_paste_guide.gif`).
