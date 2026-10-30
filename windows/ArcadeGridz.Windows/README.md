# ArcadeGridz Windows v0.1.1

ArcadeGridz is a native C# + WinUI 3 fullscreen launcher for Windows 10/11. WebView2 is used only while a game or the Network sign-in page is open.

## Install

1. Delete or rename the previous extracted build folder. Do not copy this build over the old one.
2. Download the `ArcadeGridz-win-x64` workflow artifact.
3. Extract the entire ZIP to a permanent folder such as `C:\Games\ArcadeGridz`. Keep every extracted file together.
4. Run `ArcadeGridz.App.exe`.
5. If Windows SmartScreen appears, choose **More info**, confirm the publisher is unknown, then choose **Run anyway**. This v0.1.1 build is not code-signed.
6. Current Windows 10/11 installations normally include the Microsoft Edge WebView2 Runtime. If a game page cannot open, install Microsoft's Evergreen WebView2 Runtime and retry.

The build is self-contained; no separate .NET or Windows App SDK installation is required.

## Startup diagnostics

Every launch writes a persistent diagnostic log before WinUI/XAML initialization:

`%LOCALAPPDATA%\ArcadeGridz\logs\startup.log`

If the app does not open, run this in PowerShell and send the output:

```powershell
Get-Content "$env:LOCALAPPDATA\ArcadeGridz\logs\startup.log" -Tail 200
```

The log records startup stages, OS/runtime/architecture details, packaged runtime file checks, HRESULTs, inner exceptions, and stack traces. Fatal managed startup errors also display a message box containing the log location.

## Controller test

Connect the SNES-style USB/Bluetooth controller before starting the app. Windows must expose it as a standard gamepad.

- D-pad: move through tiles and menus
- A: choose/play
- B: back
- LB/RB: previous/next category
- SELECT + START held together for 0.6 seconds: force-exit a game to the native launcher
- Keyboard fallback: arrows, Enter, and Escape

Start with **2DISO**, return with SELECT+START, then start **PIXEL SICKNESS**. If A/B are physically reversed by the controller's Windows driver, note the controller model and button behavior for the next mapping adjustment.

## Sign-in and score test

Choose **SIGN IN** and use the Network email/password form; embedded Google sign-in is not supported. After signing in, play a short run and submit a score. Return to the launcher and confirm the game's live top-five panel refreshes.

## Catalog and offline-cache test

On first launch, the status should say `GRID ONLINE` and the default list should contain 2DISO and Pixel Sickness. The launcher reads `https://arcadegridz.com/api/launcher/catalog`.

After one successful online launch, close the app, disconnect the PC from the network, and reopen it. The status should say `OFFLINE CACHE` and the cached game list should remain available. Web games themselves still require network access.

## Build

On Windows with Visual Studio 2022 and the .NET 8 SDK:

```powershell
msbuild ArcadeGridz.App/ArcadeGridz.App.csproj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:PublishDir=publish\ArcadeGridz\
```

GitHub Actions builds the same self-contained x64 package on launcher changes and on demand.
