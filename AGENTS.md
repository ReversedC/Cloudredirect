# Agent Guidelines for CloudRedirect

## Development, Build & Release Directive
- **Local Testing & Debug Mode (DEFAULT)**:
  - When making iterative changes or when working on tasks, build and test **locally only**.
  - **Do NOT push to GitHub or trigger releases** until the user explicitly says to push to GitHub.
  - While testing locally, append a debug indicator or pre-release suffix (e.g. `<ReleasePrerelease>-DEBUG</ReleasePrerelease>` in `Version.props`) and use a debug display name so it never conflicts with production or triggers premature auto-updates for existing users.
  - For local builds:
    ```powershell
    dotnet build ui/CloudRedirect.csproj -c Debug
    ```
    or for testing the published bundle locally:
    ```powershell
    dotnet publish ui/CloudRedirect.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ui/bin/publish
    & "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /platform:x64 /win32icon:ui\steam_logo3.ico /res:ui\bin\publish\CloudRedirect.Core.exe,MainAppPayload /out:ui\bin\publish\CloudRedirect.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll src\launcher\CloudRedirectLauncher.cs
    ```

- **Mandatory Version Increment Rule**:
  - **ALWAYS increase the version number when pushing to GitHub**.
  - Every single push to GitHub (`git push`) MUST be accompanied by incrementing `<ReleaseVersion>` in `Version.props` (e.g. `2.9.115` -> `2.9.116`).
  - Never push commits to GitHub without bumping `<ReleaseVersion>`.

- **Production Build & Release Directive (ONLY WHEN USER EXPLICITLY INSTRUCTS TO PUSH)**:
  - Once the user explicitly instructs to publish/push to GitHub:
    1. **MANDATORY**: Bump `<ReleaseVersion>` in `Version.props` (e.g. `2.9.115` -> `2.9.116`) and clear `<ReleasePrerelease>`.
    2. Build and publish the Windows Executable (`.exe`):
       ```powershell
       dotnet publish ui/CloudRedirect.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ui/bin/publish
       & "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /platform:x64 /win32icon:ui\steam_logo3.ico /res:ui\bin\publish\CloudRedirect.Core.exe,MainAppPayload /out:ui\bin\publish\CloudRedirect.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll src\launcher\CloudRedirectLauncher.cs
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
- **Styling Guidelines**:
  - The UI is styled to match the modern Steam desktop client aesthetic (dark navy/charcoal backgrounds, Steam cyan/blue highlights, and iconic Steam "Play" green action buttons).
  - Do not introduce OS Light Mode watchers; the app should consistently maintain its Steam dark theme.
