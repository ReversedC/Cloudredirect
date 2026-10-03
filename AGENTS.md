# Agent Guidelines for CloudRedirect

## Post-Task Build & Release Directive
**MANDATORY**: Whenever you make any changes or complete any task in this codebase, you **MUST** automatically:
1. **Bump Version Number**: Increment `<ReleaseVersion>` in `Version.props` (e.g. `2.6.5` -> `2.6.6`) so that running instances of CloudRedirect can auto-detect the newer version and prompt/auto-install it.
2. **Build and Publish the Windows Executable (`.exe`)**:
   - For **master** branch:
   ```powershell
   dotnet publish ui/CloudRedirect.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:AssemblyName=CloudRedirect.Core -o ui/bin/publish
   & "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /platform:x64 /win32icon:ui\steam_logo3.ico /res:ui\bin\publish\CloudRedirect.Core.exe,MainAppPayload /out:ui\bin\publish\CloudRedirect.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll src\launcher\CloudRedirectLauncher.cs
   ```
   - For **beta** branch:
   The executable name ends with `B` (`CloudRedirectB.exe`) so it will never conflict with the main branch app:
   ```powershell
   dotnet publish ui/CloudRedirect.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:AssemblyName=CloudRedirectB.Core -o ui/bin/publish
   & "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /platform:x64 /win32icon:ui\steam_logo3.ico /res:ui\bin\publish\CloudRedirectB.Core.exe,MainAppPayload /out:ui\bin\publish\CloudRedirectB.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll src\launcher\CloudRedirectLauncher.cs
   Copy-Item ui\bin\publish\CloudRedirectB.exe ui\bin\publish\CloudRedirect.exe -Force
   ```
   The published executable will be at `ui/bin/publish/CloudRedirectB.exe` (~10.2 MB). It combines the main app with the built-in Steam-styled .NET 8 auto-downloader & progress bar into a single file.
3. **Commit & Push Code**:
   ```powershell
   git add -A
   git commit -m "..."
   git push origin beta  # (or master depending on branch)
   ```
4. **Publish GitHub Release**:
   Run the automated release script to create/update the GitHub Release for the new version and upload `CloudRedirectB.exe` (or `CloudRedirect.exe`) and `.sha256`:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/publish-release.ps1 -ReleaseBody "<Description of changes>"
   ```
   This ensures the in-app `AppUpdater` can detect the new release on GitHub, download the binary, and perform auto-update seamlessly.

## Project Overview
- **Target Platform**: Windows x64 only.
- **Tech Stack**:
  - Companion Application: C# / WPF on .NET 8 (`ui/CloudRedirect.csproj`).
  - Native Redirection Core: C++20 (`src/`).
  - UI Library: Lepoco `WPF-UI` (4.2.0) with an authentic **Steam Theme** defined in `ui/Themes/SteamTheme.xaml`.
- **Styling Guidelines**:
  - The UI is styled to match the modern Steam desktop client aesthetic (dark navy/charcoal backgrounds, Steam cyan/blue highlights, and iconic Steam "Play" green action buttons).
  - Do not introduce OS Light Mode watchers; the app should consistently maintain its Steam dark theme.
