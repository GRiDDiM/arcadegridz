using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ArcadeGridz.App.Services;

/// <summary>A PC game a store says is installed. Path is what the library launches (a store link or Battle.net.exe).</summary>
public sealed record PcGameCandidate(string Store, string Name, string Path, string Args, string SteamAppId, string IconFrom);

/// <summary>An emulator found on the PC, set up with its usual launch arguments and file types.</summary>
public sealed record EmulatorCandidate(string Name, string Exe, string Args, string Extensions);

/// <summary>A ROM file for one of the player's emulators.</summary>
public sealed record RomCandidate(string Name, string Path, string EmulatorId, string EmulatorName, string ThumbSystem);

/// <summary>
/// Finds what's already on the PC: games installed through Steam, Epic, GOG and Battle.net (launched through their
/// stores, so sign-ins and authenticators work), emulators the player installed, and ROMs for those emulators.
/// ArcadeGridz ships no emulators or ROMs.
/// </summary>
public static class GameScanner
{
    static string Norm(string s) => Regex.Replace(s.ToLowerInvariant().Replace("&", "and"), @"[^a-z0-9]", "");

    // ======================= PC games =======================
    static readonly Regex SteamJunk = new(@"redistributable|steamworks|proton|steam linux runtime|steamvr|soundtrack|dedicated server|\bsdk\b|benchmark|wallpaper engine", RegexOptions.IgnoreCase);

    public static List<PcGameCandidate> FindPcGames(IReadOnlyCollection<string> knownNames, IReadOnlyCollection<string> knownTargets)
    {
        var found = new List<PcGameCandidate>();
        try { found.AddRange(Steam()); } catch (Exception ex) { StartupLog.Write("library", "Steam scan: " + ex.Message); }
        try { found.AddRange(Epic()); } catch (Exception ex) { StartupLog.Write("library", "Epic scan: " + ex.Message); }
        try { found.AddRange(Gog()); } catch (Exception ex) { StartupLog.Write("library", "GOG scan: " + ex.Message); }
        try { found.AddRange(BattleNet()); } catch (Exception ex) { StartupLog.Write("library", "Battle.net scan: " + ex.Message); }
        var names = new HashSet<string>(knownNames.Select(Norm));
        var targets = new HashSet<string>(knownTargets.Select(t => t.Trim().ToLowerInvariant()));
        var result = new List<PcGameCandidate>();
        foreach (var g in found)
        {
            var key = (g.Path + " " + g.Args).Trim().ToLowerInvariant();
            if (targets.Contains(key) || targets.Contains(g.Path.ToLowerInvariant()) && g.Args.Length == 0) continue; // already added
            if (!names.Add(Norm(g.Name))) continue; // same game from two stores / already in the library by name
            result.Add(g);
        }
        return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static IEnumerable<PcGameCandidate> Steam()
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                    ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (string.IsNullOrWhiteSpace(steam)) yield break;
        steam = steam.Replace('/', '\\');
        var libs = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        var seen = new HashSet<string>();
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string text;
                try { text = File.ReadAllText(acf); } catch { continue; }
                var id = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                var dir = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                if (id.Length == 0 || name.Length == 0 || id == "228980" || SteamJunk.IsMatch(name) || !seen.Add(id)) continue;
                yield return new PcGameCandidate("STEAM", name, $"steam://rungameid/{id}", "", id, Path.Combine(apps, "common", dir));
            }
        }
    }

    static IEnumerable<PcGameCandidate> Epic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir, "*.item"))
        {
            JsonElement r;
            try { using var doc = JsonDocument.Parse(File.ReadAllText(f)); r = doc.RootElement.Clone(); } catch { continue; }
            string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) continue;
            var app = S("AppName");
            var name = S("DisplayName");
            if (app.Length == 0 || name.Length == 0 || app.StartsWith("UE_", StringComparison.OrdinalIgnoreCase)) continue;
            var link = $"com.epicgames.launcher://apps/{S("CatalogNamespace")}%3A{S("CatalogItemId")}%3A{app}?action=launch&silent=true";
            var exe = S("LaunchExecutable");
            yield return new PcGameCandidate("EPIC", name, link, "", "", exe.Length > 0 ? Path.Combine(S("InstallLocation"), exe) : S("InstallLocation"));
        }
    }

    static IEnumerable<PcGameCandidate> Gog()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (root is null) yield break;
        foreach (var sub in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(sub);
            var name = k?.GetValue("gameName") as string ?? "";
            var exe = k?.GetValue("exe") as string ?? "";
            if (name.Length == 0 || !File.Exists(exe)) continue;
            yield return new PcGameCandidate("GOG", name, exe, k?.GetValue("launchParam") as string ?? "", "", exe);
        }
    }

    // Battle.net games: launched through Battle.net with --exec="launch CODE" (the login / authenticator push works)
    static readonly (string Match, string Code, string Name)[] Blizzard =
    [
        ("Diablo IV", "Fen", "Diablo IV"), ("Overwatch", "Pro", "Overwatch 2"), ("Hearthstone", "WTCG", "Hearthstone"),
        ("StarCraft II", "S2", "StarCraft II"), ("Heroes of the Storm", "Hero", "Heroes of the Storm"), ("Diablo III", "D3", "Diablo III"),
        ("Diablo II Resurrected", "OSI", "Diablo II: Resurrected"), ("Warcraft III", "W3", "Warcraft III: Reforged")
    ];

    static IEnumerable<PcGameCandidate> BattleNet()
    {
        var bnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net.exe");
        var entries = new List<(string Name, string Location)>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            foreach (var path in new[] { @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using var root = hive.OpenSubKey(path);
                if (root is null) continue;
                foreach (var sub in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(sub);
                    if ((k?.GetValue("Publisher") as string ?? "").IndexOf("Blizzard", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var name = k?.GetValue("DisplayName") as string ?? "";
                    var loc = (k?.GetValue("InstallLocation") as string ?? "").Trim('"');
                    if (name.Equals("Battle.net", StringComparison.OrdinalIgnoreCase)) { if (File.Exists(Path.Combine(loc, "Battle.net.exe"))) bnet = Path.Combine(loc, "Battle.net.exe"); continue; }
                    entries.Add((name, loc));
                }
            }
        if (!File.Exists(bnet)) yield break;
        var seen = new HashSet<string>();
        foreach (var (name, loc) in entries)
        {
            var plain = Regex.Replace(name, @"[^\w\s]", "");
            if (plain.Contains("World of Warcraft", StringComparison.OrdinalIgnoreCase))
            {
                // one install folder, several versions: _retail_ is WoW, a *forever* folder is WoW Forever
                if (Directory.Exists(Path.Combine(loc, "_retail_")) && seen.Add("WoW"))
                    yield return new PcGameCandidate("BATTLE.NET", "World of Warcraft", bnet, "--exec=\"launch WoW\"", "", Path.Combine(loc, "_retail_", "Wow.exe"));
                IEnumerable<string> dirs;
                try { dirs = Directory.Exists(loc) ? Directory.EnumerateDirectories(loc).ToList() : []; } catch { dirs = []; }
                var forever = dirs.FirstOrDefault(d => Path.GetFileName(d).Contains("forever", StringComparison.OrdinalIgnoreCase));
                if (forever is not null && seen.Add("WoWF"))
                    yield return new PcGameCandidate("BATTLE.NET", "World of Warcraft Forever", bnet, "--exec=\"launch WoWF\"", "", forever);
                continue;
            }
            var hit = Blizzard.FirstOrDefault(b => plain.StartsWith(Regex.Replace(b.Match, @"[^\w\s]", ""), StringComparison.OrdinalIgnoreCase));
            if (hit.Code is null || !seen.Add(hit.Code)) continue;
            yield return new PcGameCandidate("BATTLE.NET", hit.Name, bnet, $"--exec=\"launch {hit.Code}\"", "", loc);
        }
    }

    /// <summary>The biggest .exe in a game folder (its icon makes the cover when no art is found online).</summary>
    public static string? MainExe(string pathOrDir)
    {
        try
        {
            if (File.Exists(pathOrDir)) return pathOrDir;
            if (!Directory.Exists(pathOrDir)) return null;
            return Directory.EnumerateFiles(pathOrDir, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true })
                .Where(f => !Regex.IsMatch(Path.GetFileName(f), @"unins|setup|crash|redist|vc_|dxsetup|launcher|helper|report", RegexOptions.IgnoreCase))
                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        }
        catch { return null; }
    }

    // ======================= emulators =======================
    sealed record EmuDef(string Name, string ExePattern, string Args, string Extensions);

    // exe name (regex, whole file name) -> how to launch a ROM and which file types it plays
    static readonly EmuDef[] Emus =
    [
        new("Dolphin", @"dolphin\.exe", "-b -e \"{rom}\"", ".iso .gcm .gcz .rvz .wbfs .ciso .wad .dol .elf"),
        new("PCSX2", @"pcsx2(-qt)?(-avx2)?\.exe", "-batch -fullscreen \"{rom}\"", ".iso .cso .chd .gz"),
        new("DuckStation", @"duckstation-qt.*\.exe", "-batch -fullscreen \"{rom}\"", ".cue .chd .pbp .m3u .ecm"),
        new("PPSSPP", @"ppssppwindows(64)?\.exe", "\"{rom}\"", ".cso .pbp .iso"),
        new("Cemu", @"cemu\.exe", "-f -g \"{rom}\"", ".wud .wux .wua .rpx"),
        new("Ryujinx", @"ryujinx\.exe", "\"{rom}\"", ".nsp .xci .nca"),
        new("Switch emulator", @"(yuzu|suyu|sudachi|citron|eden)\.exe", "-f -g \"{rom}\"", ".nsp .xci"),
        new("Azahar / Citra", @"(azahar|citra-qt|citra|lime3ds|lime3ds-gui)\.exe", "\"{rom}\"", ".3ds .cci .cxi .3dsx"),
        new("melonDS", @"melonds\.exe", "\"{rom}\"", ".nds .dsi"),
        new("DeSmuME", @"desmume.*\.exe", "\"{rom}\"", ".nds"),
        new("mGBA", @"mgba\.exe", "-f \"{rom}\"", ".gba .gb .gbc"),
        new("VisualBoyAdvance-M", @"visualboyadvance-m\.exe", "\"{rom}\"", ".gba .gb .gbc"),
        new("Snes9x", @"snes9x[\w.\- ]*\.exe", "-fullscreen \"{rom}\"", ".sfc .smc"),
        new("bsnes", @"bsnes[\w.\- ]*\.exe", "--fullscreen \"{rom}\"", ".sfc .smc"),
        new("Mesen", @"mesen\.exe", "--fullscreen \"{rom}\"", ".nes .fds"),
        new("Project64", @"project64\.exe", "\"{rom}\"", ".n64 .z64 .v64"),
        new("simple64", @"simple64-gui\.exe", "\"{rom}\"", ".n64 .z64 .v64"),
        new("Kega Fusion", @"fusion\.exe", "-fullscreen \"{rom}\"", ".md .gen .smd .sms .gg"),
        new("Flycast", @"flycast\.exe", "\"{rom}\"", ".cdi .gdi"),
        new("RPCS3", @"rpcs3\.exe", "--no-gui \"{rom}\"", ""),
        new("xemu", @"xemu\.exe", "-dvd_path \"{rom}\"", ".xiso"),
        new("Xenia", @"xenia(_canary)?\.exe", "\"{rom}\"", ".xex"),
        new("MAME", @"mame(64)?\.exe", "-rompath \"{romdir}\" {romname}", ".zip"),
    ];

    // RetroArch: one emulator entry per installed core that a system is known for
    static readonly (string Core, string Name, string Extensions)[] RetroCores =
    [
        ("snes9x_libretro", "RetroArch · SNES", ".sfc .smc"), ("bsnes_libretro", "RetroArch · SNES", ".sfc .smc"),
        ("mesen_libretro", "RetroArch · NES", ".nes .fds"), ("nestopia_libretro", "RetroArch · NES", ".nes .fds"), ("fceumm_libretro", "RetroArch · NES", ".nes .fds"),
        ("mgba_libretro", "RetroArch · GBA / GB", ".gba .gb .gbc"), ("gambatte_libretro", "RetroArch · Game Boy", ".gb .gbc"),
        ("genesis_plus_gx_libretro", "RetroArch · Genesis", ".md .gen .smd .sms .gg"), ("picodrive_libretro", "RetroArch · Genesis", ".md .gen .smd .32x"),
        ("mupen64plus_next_libretro", "RetroArch · N64", ".n64 .z64 .v64"), ("parallel_n64_libretro", "RetroArch · N64", ".n64 .z64 .v64"),
        ("swanstation_libretro", "RetroArch · PlayStation", ".cue .chd .pbp .m3u"), ("mednafen_psx_hw_libretro", "RetroArch · PlayStation", ".cue .chd .pbp .m3u"),
        ("pcsx_rearmed_libretro", "RetroArch · PlayStation", ".cue .chd .pbp .m3u"), ("melonds_libretro", "RetroArch · DS", ".nds"),
        ("desmume_libretro", "RetroArch · DS", ".nds"), ("mednafen_pce_fast_libretro", "RetroArch · PC Engine", ".pce"),
        ("stella_libretro", "RetroArch · Atari 2600", ".a26"), ("flycast_libretro", "RetroArch · Dreamcast", ".cdi .gdi"),
    ];

    static readonly HashSet<string> EmuSkip = new(StringComparer.OrdinalIgnoreCase)
    { "Windows", "$Recycle.Bin", "System Volume Information", "WindowsApps", "node_modules", ".git", "ProgramData", "Microsoft", "Common Files", "WinSxS", "Temp", "cores", "shaders", "saves", "states" };

    static IEnumerable<string> EmuRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var list = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), home
        };
        foreach (var d in DriveInfo.GetDrives())
            try { if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable) list.Add(d.RootDirectory.FullName); } catch { }
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steam is not null) list.Add(Path.Combine(steam.Replace('/', '\\'), "steamapps", "common"));
        return list.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Usual launch arguments and file types for a known emulator exe (by file name), e.g. bsnes -> .sfc .smc.</summary>
    public static (string Args, string Extensions)? KnownSetup(string exePath)
    {
        var file = Path.GetFileName(exePath ?? "");
        if (file.Length == 0) return null;
        foreach (var e in Emus)
            if (Regex.IsMatch(file, "^" + e.ExePattern + "$", RegexOptions.IgnoreCase)) return (e.Args, e.Extensions);
        return null;
    }

    public static async Task<List<EmulatorCandidate>> FindEmulatorsAsync(IReadOnlyCollection<string> knownExes, IProgress<string>? progress, CancellationToken ct)
    {
        var known = new HashSet<string>(knownExes.Select(AlbumScanner.NormPath));
        var found = new List<EmulatorCandidate>();
        var exesSeen = new HashSet<string>();
        var regexes = Emus.Select(e => (Def: e, Rx: new Regex("^" + e.ExePattern + "$", RegexOptions.IgnoreCase))).ToList();
        var scanned = 0;
        await Task.Run(() =>
        {
            void Walk(DirectoryInfo dir, int depth)
            {
                ct.ThrowIfCancellationRequested();
                if (depth > 5) return;
                if (++scanned % 300 == 0) progress?.Report($"Looking for emulators... {scanned} folders, {found.Count} found");
                try
                {
                    foreach (var f in dir.EnumerateFiles("*.exe"))
                    {
                        var p = AlbumScanner.NormPath(f.FullName);
                        if (known.Contains(p) || !exesSeen.Add(p)) continue;
                        if (f.Name.Equals("retroarch.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            var cores = Path.Combine(dir.FullName, "cores");
                            var namesUsed = new HashSet<string>();
                            foreach (var (core, name, ext) in RetroCores)
                            {
                                var dll = Path.Combine(cores, core + ".dll");
                                if (File.Exists(dll) && namesUsed.Add(name))
                                    found.Add(new EmulatorCandidate(name, f.FullName, $"-f -L \"{dll}\" \"{{rom}}\"", ext));
                            }
                            if (namesUsed.Count == 0) found.Add(new EmulatorCandidate("RetroArch", f.FullName, "\"{rom}\"", ""));
                            continue;
                        }
                        var hit = regexes.FirstOrDefault(r => r.Rx.IsMatch(f.Name));
                        if (hit.Def is not null) found.Add(new EmulatorCandidate(hit.Def.Name, f.FullName, hit.Def.Args, hit.Def.Extensions));
                    }
                    foreach (var sub in dir.EnumerateDirectories())
                    {
                        if (EmuSkip.Contains(sub.Name) || sub.Name.StartsWith('.') || sub.Name.StartsWith('$') ||
                            (sub.Attributes & System.IO.FileAttributes.System) != 0 || IsLink(sub)) continue;
                        Walk(sub, depth + 1);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            foreach (var r in EmuRoots()) Walk(new DirectoryInfo(r), 0);
        }, ct);
        return found.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // junctions / symlinks loop or double-count; OneDrive folders also carry the reparse-point flag but are real folders
    static bool IsLink(DirectoryInfo d)
    {
        if ((d.Attributes & System.IO.FileAttributes.ReparsePoint) == 0) return false;
        try { return d.LinkTarget is not null; } catch { return true; }
    }

    // ======================= ROMs =======================
    // libretro-thumbnails system folders (box art by the ROM's own No-Intro file name)
    static readonly Dictionary<string, string> ThumbByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".sfc"] = "Nintendo_-_Super_Nintendo_Entertainment_System", [".smc"] = "Nintendo_-_Super_Nintendo_Entertainment_System",
        [".nes"] = "Nintendo_-_Nintendo_Entertainment_System", [".fds"] = "Nintendo_-_Family_Computer_Disk_System",
        [".gba"] = "Nintendo_-_Game_Boy_Advance", [".gb"] = "Nintendo_-_Game_Boy", [".gbc"] = "Nintendo_-_Game_Boy_Color",
        [".n64"] = "Nintendo_-_Nintendo_64", [".z64"] = "Nintendo_-_Nintendo_64", [".v64"] = "Nintendo_-_Nintendo_64",
        [".nds"] = "Nintendo_-_Nintendo_DS", [".3ds"] = "Nintendo_-_Nintendo_3DS", [".cci"] = "Nintendo_-_Nintendo_3DS",
        [".md"] = "Sega_-_Mega_Drive_-_Genesis", [".gen"] = "Sega_-_Mega_Drive_-_Genesis", [".smd"] = "Sega_-_Mega_Drive_-_Genesis",
        [".sms"] = "Sega_-_Master_System_-_Mark_III", [".gg"] = "Sega_-_Game_Gear", [".32x"] = "Sega_-_32X",
        [".cdi"] = "Sega_-_Dreamcast", [".gdi"] = "Sega_-_Dreamcast", [".pce"] = "NEC_-_PC_Engine_-_TurboGrafx_16", [".a26"] = "Atari_-_2600",
        [".gcm"] = "Nintendo_-_GameCube", [".rvz"] = "Nintendo_-_GameCube", [".wbfs"] = "Nintendo_-_Wii", [".cso"] = "Sony_-_PlayStation_Portable",
    };

    static string ThumbSystem(string ext, EmulatorConfig emu)
    {
        if (ThumbByExt.TryGetValue(ext, out var s)) return s;
        var n = emu.Name.ToLowerInvariant();
        if (n.Contains("duckstation") || n.Contains("playstation") && !n.Contains("2")) return "Sony_-_PlayStation";
        if (n.Contains("pcsx2")) return "Sony_-_PlayStation_2";
        if (n.Contains("ppsspp")) return "Sony_-_PlayStation_Portable";
        if (n.Contains("dolphin")) return "Nintendo_-_GameCube";
        if (n.Contains("mame")) return "MAME";
        return "";
    }

    static readonly Regex RomSkipFolder = new(@"\b(bios|firmware|saves?|states?|savestates|screenshots|shaders|cheats|patches|system|keys)\b", RegexOptions.IgnoreCase);

    /// <summary>The name a ROM file shows as: "Super Metroid (Japan, USA) (En,Ja).sfc" -> "Super Metroid".</summary>
    public static string CleanRomName(string file)
    {
        var n = Path.GetFileNameWithoutExtension(file);
        n = Regex.Replace(n, @"\s*[\(\[][^\)\]]*[\)\]]", "").Replace('_', ' ').Trim();
        return n.Length > 0 ? n : Path.GetFileNameWithoutExtension(file);
    }

    public static async Task<List<RomCandidate>> FindRomsAsync(IReadOnlyList<EmulatorConfig> emulators, IReadOnlyCollection<string> knownPaths,
        IProgress<string>? progress, CancellationToken ct)
    {
        // extension -> first emulator that plays it
        var byExt = new Dictionary<string, EmulatorConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in emulators)
            foreach (var x in e.Extensions.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
                byExt.TryAdd(x.StartsWith('.') ? x : "." + x, e);
        if (byExt.Count == 0) return [];
        var emuDirs = emulators.Select(e => AlbumScanner.NormPath(Path.GetDirectoryName(e.Exe) ?? "")).Where(d => d.Length > 3).ToHashSet();
        var known = new HashSet<string>(knownPaths.Select(AlbumScanner.NormPath));
        var found = new List<RomCandidate>();
        var seen = new HashSet<string>();
        var scanned = 0;
        await Task.Run(() =>
        {
            void Walk(DirectoryInfo dir, int depth)
            {
                ct.ThrowIfCancellationRequested();
                if (depth > 8) return;
                if (++scanned % 300 == 0) progress?.Report($"Looking for ROMs... {scanned} folders, {found.Count} found");
                try
                {
                    var dirNorm = AlbumScanner.NormPath(dir.FullName);
                    // files right next to an emulator are its own (bios, sample files), not ROMs
                    var files = emuDirs.Contains(dirNorm) ? new List<FileInfo>() : dir.EnumerateFiles().ToList();
                    // a playlist / cue sheet stands for its disc files
                    var hasM3u = files.Any(f => f.Extension.Equals(".m3u", StringComparison.OrdinalIgnoreCase) && byExt.ContainsKey(".m3u"));
                    foreach (var f in files)
                    {
                        if (!byExt.TryGetValue(f.Extension, out var emu) || f.Length == 0) continue;
                        if (hasM3u && !f.Extension.Equals(".m3u", StringComparison.OrdinalIgnoreCase)) continue;
                        var ext = f.Extension.ToLowerInvariant();
                        // ambiguous types only count in a folder that looks like a ROM folder
                        if (ext is ".zip" or ".iso" or ".gz" && !Regex.IsMatch(dir.FullName, @"rom|mame|arcade|iso|ps2|psp|gamecube|wii|emu", RegexOptions.IgnoreCase)) continue;
                        var p = AlbumScanner.NormPath(f.FullName);
                        if (known.Contains(p)) continue;
                        var name = CleanRomName(f.Name);
                        if (!seen.Add(emu.Id + "|" + Norm(name))) continue; // same game twice (other region / copy): first one kept
                        found.Add(new RomCandidate(name, f.FullName, emu.Id, emu.Name, ThumbSystem(ext, emu)));
                    }
                    foreach (var sub in dir.EnumerateDirectories())
                    {
                        if (EmuSkip.Contains(sub.Name) || RomSkipFolder.IsMatch(sub.Name) || sub.Name.StartsWith('.') || sub.Name.StartsWith('$') ||
                            sub.Name is "Program Files" or "Program Files (x86)" or "AppData" or "steamapps" ||
                            (sub.Attributes & System.IO.FileAttributes.System) != 0 || IsLink(sub)) continue;
                        Walk(sub, depth + 1);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            var roots = AlbumScanner.DefaultRoots().ToList();
            // ROM folders often sit next to the emulator too (RetroArch\roms, Emulators\ROMs)
            foreach (var e in emulators)
            {
                var d = Path.GetDirectoryName(e.Exe);
                if (string.IsNullOrEmpty(d)) continue;
                var cand = Path.Combine(d, "roms");
                if (Directory.Exists(cand) && !roots.Any(r => AlbumScanner.NormPath(cand).StartsWith(r))) roots.Add(AlbumScanner.NormPath(cand));
            }
            foreach (var r in roots.Distinct()) Walk(new DirectoryInfo(r), 0);
        }, ct);
        return found.OrderBy(r => r.EmulatorName).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Box art for a ROM from the libretro thumbnail sets (exact No-Intro file names).</summary>
    public static async Task<string?> RomBoxArtAsync(RomCandidate rom, HttpClient http, CancellationToken ct)
    {
        if (rom.ThumbSystem.Length == 0) return null;
        var name = Regex.Replace(Path.GetFileNameWithoutExtension(rom.Path), @"[&*/:`<>?\\|""]", "_");
        var url = $"https://raw.githubusercontent.com/libretro-thumbnails/{rom.ThumbSystem}/master/Named_Boxarts/{Uri.EscapeDataString(name)}.png";
        try
        {
            var bytes = await http.GetByteArrayAsync(url, ct);
            if (bytes.Length < 1000) return null;
            var p = Path.Combine(Path.GetTempPath(), "ArcadeGridz-covers", $"rom-{Guid.NewGuid():N}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            await File.WriteAllBytesAsync(p, bytes, ct);
            return p;
        }
        catch (HttpRequestException) { return null; }
    }
}
