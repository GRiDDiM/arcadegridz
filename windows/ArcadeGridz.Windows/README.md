# ArcadeGridz Windows v0.1

ArcadeGridz is a native C# + WinUI 3 fullscreen launcher for Windows 10/11. WebView2 is used only while a game or the Network sign-in page is open.

## Install

1. Download the `ArcadeGridz-win-x64` workflow artifact.
2. Extract the ZIP to a permanent folder such as `C:\Games\ArcadeGridz`. Keep every extracted file together.
3. Run `ArcadeGridz.App.exe`.
4. If Windows SmartScreen appears, choose **More info**, confirm the publisher is unknown, then choose **Run anyway**. This v0.1 build is not code-signed.
5. Current Windows 10/11 installations already include the Microsoft Edge WebView2 Runtime. If a game page cannot open, install Microsoft's Evergreen WebView2 Runtime and retry.

The build is self-contained; no separate .NET installation is required.

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
