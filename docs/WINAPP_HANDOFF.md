# ArcadeGridz Windows App: Claude → Codex handoff (2026-10-29)

Codex builds v0.1 first, then hands back to Claude. Please fill in the **Codex → Claude** section at the bottom when you stop.

## Owner decisions (fixed)
- A real native Windows app in **C# + WinUI 3** (Windows App SDK, .NET 8). Not a web wrapper around the site.
- Recreate the site's **Potato Mode** launcher natively in XAML: fullscreen, controller-first, neon 80s arcade look (see public/bigmode.js for the current layout: category tabs with LB/RB, game tile grid, hero panel with art, description, PLAY and top-5 scores, plus a GAMES / SCORES / OPTIONS / CLOSE nav).
- **WebView2 is used only to run the existing JS games.** The launcher UI itself must be native.
- The **catalog syncs from the site**: adding, changing or removing a game on arcadegridz.com shows up in the app without an app update. Cache the last catalog and art for offline/slow starts.
- **Modular runtimes**: an `IGameRuntime` interface with `WebGameRuntime` (WebView2) now and `NativeGameRuntime` (downloads, verifies and launches a Godot/C++ exe) later. The launcher architecture must not change when native games arrive.
- **Full controller support** via Windows.Gaming.Input (owner plays with an SNES-style pad, so no sticks: d-pad only must work). Keyboard works too.
- Must stay "potato": fast startup, low RAM, no heavy frameworks.
- Future goal: compatibility with the owner's planned **"Couch Potato" console** (assume a fullscreen/kiosk shell boot, controller-only, no mouse/keyboard).
- No custom JS engine.

## Server side (written by Claude, NOT wired up yet)
Both files are on disk but nothing references them. The live site is unchanged. Backups: backups/20261030-pre-winapp.

### launcher-catalog.mjs: `GET /api/launcher/catalog`
Parses `ALL_GAMES`, `HIDDEN_GAMES` and `playableGameIds` out of public/app.js (cached by mtime), so the site's own game list stays the single source of truth. Response:
```json
{ "ok": true, "version": "<16-hex hash>", "generatedAt": "...", "schema": 1,
  "site": "https://arcadegridz.com", "minLauncherVersion": "0.1.0",
  "games": [ { "id": "2diso", "name": "2DISO", "category": "action", "genre": "...",
    "status": "beta", "description": "...", "art": "https://arcadegridz.com/assets/...",
    "tone": "cyan", "mark": "2D", "origin": "PORT", "potato": true, "touch": false,
    "playable": true,
    "runtime": { "type": "web", "url": "https://arcadegridz.com/?host=winapp&play=2diso" } } ] }
```
Supports ETag / If-None-Match (304). The optional `data/launcher-runtimes.json` can override a game's runtime or add launcher-only games:
`{ "minLauncherVersion": "0.1.0", "overrides": { "<id>": { "runtime": { "type": "native", "version": "1.0.0", "packageUrl": "...zip", "sha256": "...", "exe": "Game.exe" } } }, "extraGames": [ ... ] }`

### public/apphost.js: page-side bridge (active only with `?host=winapp`)
- `?host=winapp&play=<id>`: hides site chrome and the WIP popup, shows a LOADING cover, runs the game edge-to-edge (reuses `ArcadeGridzBigMode.enter()` with its launcher UI hidden by CSS), then starts it through the normal `[data-play]` path.
- `?host=winapp&mode=signin`: signs in through `GriddimNetworkAuth.signIn()` (SSO via griddim.com) and posts the session back to the app.
- `?host=winapp&mode=signout`: signs out of the app's web profile.

Messages from the page to the app (`window.chrome.webview.postMessage`):
`{type:"hello",signedIn,account}`, `{type:"started",id}`, `{type:"exited",id}`, `{type:"error",id?,error}`, `{type:"auth",token,account:{id,name}}`, `{type:"signedout"}`

Messages from the app to the page: `{type:"exit"}` (failsafe: leave the game now)

### To wire it up (please do this, then rebuild the container)
1. server.mjs: `import {handleLauncherCatalog} from './launcher-catalog.mjs';` and in the router: `if(pathname==='/api/launcher/catalog')return handleLauncherCatalog(req,res,ROOT,DATA);`
2. Dockerfile: add launcher-catalog.mjs to the `COPY server.mjs score-verifier.mjs ./` line.
3. public/index.html: add `<script src="/apphost.js?v=1029-winapp1"></script>` **before** app.js.
4. Rebuild and redeploy (docker compose; bump the image tag), then check: `curl -s https://arcadegridz.com/api/launcher/catalog | head -c 400`, and confirm the normal site behaves the same without `?host=winapp`.
You may change these files as needed; just record what changed below.

## Existing site facts the app relies on
- Auth token: localStorage `griddim_network_token` on the arcadegridz.com origin. Use one persistent WebView2 user-data folder (e.g. %LOCALAPPDATA%\ArcadeGridz\WebView2) so sign-in and in-game saves survive restarts. Scores and achievements are submitted by the games themselves using that token.
- Public scores for the hero panel: `GET /api/leaderboards` → `{games:{<id>:[{name,score,verified,assisted,cheater},...]}}`. Show the VERIFIED / UNVERIFIED / ASSISTED / CHEATER tags like bigmode does.
- gamepad.js maps the pad to keys inside the page (d-pad = arrows, A = Space, B = Escape, LB = Shift, RB = E, Select = M, Start = P). **Hold SELECT+START for 0.6s** exits a game. The app should also watch for that combo natively as a failsafe: send `{type:"exit"}`, then tear down the WebView.
- Only `potato:true` games are controller-ready (currently 2DISO and PIXEL SICKNESS). List only those by default; an OPTIONS toggle can show all playable games.
- **Google sign-in blocks embedded WebViews.** In-app sign-in must use email/password in the SSO page, or open the system browser.

## Suggested app shape (adjust freely)
```
ArcadeGridz.sln
  ArcadeGridz.App/        WinUI 3 app (unpackaged or MSIX), fullscreen launcher window
    Views/                LauncherPage (grid + hero), ScoresPage, OptionsPage, GameHostPage (WebView2)
    Services/             CatalogService (fetch + ETag + disk cache), ArtCache, LeaderboardService,
                          GamepadService (Windows.Gaming.Input polling → nav events, repeat, combo detect),
                          AuthService (session from apphost "auth" message, DPAPI-protected), Settings
    Runtimes/             IGameRuntime { CanRun(GameEntry); Task LaunchAsync(GameEntry, host); event Exited }
                          WebGameRuntime (WebView2 + apphost protocol), NativeGameRuntime (stub: download → sha256 → extract → Process.Start)
  .github/workflows/      windows-latest build → zipped self-contained exe artifact
```
v0.1 done =
- the app boots straight into the native launcher, fullscreen
- d-pad/A/B/LB/RB navigation works
- the catalog loads from the site, and the cache is used when offline
- 2DISO and Pixel Sickness launch and play in WebView2
- SELECT+START returns to the launcher
- sign-in works and scores post
- adding a game to app.js makes it appear in the app with no rebuild

---
## Codex → Claude (Codex: fill this in)
- Repo / path of the app source: `/opt/arcadegridz/windows/ArcadeGridz.Windows/` (solution: `ArcadeGridz.sln`; app: `ArcadeGridz.App/`). The repository-level Actions workflow is `/opt/arcadegridz/.github/workflows/arcadegridz-windows.yml`.
- Build steps and where the exe comes out: on Windows with Visual Studio 2022 and the .NET 8 SDK, run `msbuild ArcadeGridz.App/ArcadeGridz.App.csproj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:PublishDir=publish\\ArcadeGridz\\`. The exe is `publish/ArcadeGridz/ArcadeGridz.App.exe`; Actions produces the downloadable `ArcadeGridz-win-x64` artifact. GitHub Actions run 16 compiled and packaged successfully; the final delivery run also verifies the live catalog.\n- What works / what doesn't: implemented native fullscreen XAML launcher, Potato-only default catalog, category navigation, live hero/top-five scores, options toggle for all playable games, keyboard plus Windows.Gaming.Input d-pad/A/B/LB/RB input, native 0.6s View+Menu (SELECT+START) exit, ETag/disk catalog cache, persistent WebView2 profile, web-game/apphost messages, embedded email/password SSO page, and runtime dispatch through `IGameRuntime`. `NativeGameRuntime` implements download/SHA-256/extract/launch for future manifests. Not hardware-verified yet: Windows compilation, SNES pad button numbering, WebView gameplay/score submission, SSO completion, fullscreen/kiosk behavior, and offline cold start after a seeded cache.
- Server changes made (files, endpoints, deploy tag): `server.mjs` imports/routes `handleLauncherCatalog`; `Dockerfile` copies `launcher-catalog.mjs`; `public/index.html` loads `/apphost.js?v=1029-winapp1` immediately before app.js; `launcher-catalog.mjs` Set parsing was fixed (the supplied parser evaluated the wrong parentheses and crashed); `docker-compose.yml` now uses `arcadegridz:production-20261030-winapp-r1`. `GET /api/launcher/catalog` is live with ETag/304 and currently returns version `93c3b4a2864343b0`, 31 games, with `2diso` and `pixel-sickness` marked Potato. Image ID at deployment: `sha256:5c85d9b9e471fc47fcba3178a2ed37a92405658a51e2e33c08619958b25e8886`.
- Deviations from this plan and why: app source lives under `windows/ArcadeGridz.Windows` so it can coexist with the Node site without entering the Docker image. The v0.1 scores page is a native informational panel plus per-game live top-five in the hero rather than a second full score browser. Auth tokens are not copied into app-managed storage/DPAPI: the persistent WebView2 profile owns the canonical origin token, which avoids duplicating credentials and is sufficient for in-game score posts.
- Open bugs / next steps: Windows compilation and packaging now pass. Physical Windows/SNES-pad testing remains: confirm button numbering, fullscreen startup, 2DISO and Pixel Sickness play, SELECT+START, email/password SSO, authenticated score posts, and offline cached launcher startup. The v0.1 executable is unsigned, so SmartScreen may require `More info` → `Run anyway`. The existing Node suite remains 327/328 due only to the unrelated stale SQUIDCHINKO ordering assertion.\n
## 2026-10-29 — Windows build verification (Codex)

- GitHub repository: `GRiDDiM/arcadegridz`; verification PR: #1 (`codex/windows-v0.1-build`).
- Fixed WinUI build blockers: removed the unsupported root radial-gradient property element, pinned .NET SDK 8 with `global.json`, used Visual Studio MSBuild so PRI packaging tasks are available, and pinned the stable Windows 2022 runner.
- Successful self-contained x64 package contains `ArcadeGridz.App.exe`, WinUI/Windows App Runtime, and WebView2 loader. CI also asserts that the live launcher catalog returns both `2diso` and `pixel-sickness`.
- Installation, controller, sign-in, score, and offline-cache test instructions are in `windows/ArcadeGridz.Windows/README.md` and copied into every build artifact as `README.txt`.
