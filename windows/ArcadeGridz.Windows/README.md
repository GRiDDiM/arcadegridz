# ArcadeGridz Windows v0.1.3

ArcadeGridz is a native C# + WinUI 3 fullscreen launcher for Windows 10/11. WebView2 is used only while a game or Network sign-in page is open.

## Install

1. Download `ArcadeGridz-Setup-x64.exe` from the official ArcadeGridz GitHub Release.
2. Run the installer. It installs per-user, so administrator access is not required.
3. Keep the Start Menu shortcut selected; optionally enable the desktop shortcut.
4. If Windows SmartScreen appears, choose **More info → Run anyway**. v0.1.3 is not yet Authenticode-signed.

The installer supports upgrades over an existing installation and provides a normal Windows uninstaller. User settings, the catalog cache, WebView2 sign-in data, and game saves live under `%LOCALAPPDATA%\ArcadeGridz` and are preserved during upgrades and uninstall.

## Updates

Open **OPTIONS → CHECK FOR UPDATES**. The launcher shows the installed and available versions. Downloads come only from the official `GRiDDiM/arcadegridz` GitHub Release, require HTTPS, and must match the release manifest's SHA-256 hash before **INSTALL UPDATE** is enabled. The verified installer closes and relaunches ArcadeGridz without requiring manual uninstall or ZIP extraction.

The GitHub repository and its Releases must be public for update checks from unauthenticated installed apps. Never embed a GitHub access token in the launcher.

## Startup diagnostics

Every launch writes a persistent diagnostic log before WinUI/XAML initialization:

`%LOCALAPPDATA%\ArcadeGridz\logs\startup.log`

If the app does not open, run this in PowerShell:

```powershell
Get-Content "$env:LOCALAPPDATA\ArcadeGridz\logs\startup.log" -Tail 200
```

The self-contained installer ships the Windows App SDK runtime, WebView2 loader, and the compiled `App.xbf` / `MainWindow.xbf` resources required by unpackaged WinUI.

## Controller test

Connect the SNES-style USB/Bluetooth controller before starting ArcadeGridz. D-pad focus reaches categories, Games, Hi-Scores, Options, Exit ArcadeGridz, game cards, Play, sign-in, and settings actions.

- D-pad: move through tiles and menus
- A: choose/play
- B: back
- LB/RB: previous/next category
- SELECT + START held for 0.6 seconds: return from a game to the launcher
- Keyboard fallback: arrows, Enter, and Escape

Start **2DISO**, return with SELECT+START, then start **PIXEL SICKNESS**.

## Sign-in, scores, and catalog

Choose **SIGN IN** and use the Network email/password form; embedded Google sign-in is not supported. After signing in, submit a score and confirm the live top-five panel refreshes.

The launcher reads `https://arcadegridz.com/api/launcher/catalog`. After one online launch, reopen it offline: the status should say `OFFLINE CACHE` and the cached game list should remain visible. Web games still require a network connection.

## Build

On Windows with Visual Studio 2022, .NET 8, and Inno Setup 6:

```powershell
msbuild ArcadeGridz.App/ArcadeGridz.App.csproj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:PublishDir=publish\ArcadeGridz\
& "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe" "/DMyAppVersion=0.1.3" Installer\ArcadeGridz.iss
```

GitHub Actions also launches the published executable, waits for `Launcher ready`, validates the installer/uninstaller, verifies the live catalog, creates `update-manifest.json`, and publishes tagged builds to GitHub Releases.
