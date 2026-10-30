# ArcadeGridz Windows v0.1

Native WinUI 3 launcher for Windows 10 1809+ / Windows 11. WebView2 is instantiated only while hosting the existing web games or the SSO page.

Build on Windows with Visual Studio 2022 (Desktop development with C++) or the .NET 8 SDK:

```powershell
dotnet restore ArcadeGridz.sln -p:Platform=x64
dotnet publish ArcadeGridz.App/ArcadeGridz.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o publish/ArcadeGridz
Compress-Archive publish/ArcadeGridz/* publish/ArcadeGridz-win-x64.zip
```

The unpackaged executable is `publish/ArcadeGridz/ArcadeGridz.App.exe`. The Microsoft Edge WebView2 Runtime must be installed (included with current Windows 10/11).
