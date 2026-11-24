using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArcadeGridz.App.Models;

namespace ArcadeGridz.App.Services;

/// <summary>Something the player added from their own PC: a game/app, a ROM (run through an emulator they set up), or a music folder.</summary>
public sealed class LocalItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    [JsonPropertyName("kind")] public string Kind { get; set; } = LocalLibraryService.KindApp; // app | rom | album
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("tags")] public string Tags { get; set; } = "";          // comma separated genre tags
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("cover")] public string Cover { get; set; } = "";        // copied into the library folder
    [JsonPropertyName("path")] public string Path { get; set; } = "";          // exe / shortcut / link, ROM file, or music folder
    [JsonPropertyName("args")] public string Args { get; set; } = "";          // extra launch options (apps)
    [JsonPropertyName("emulator")] public string Emulator { get; set; } = "";  // emulator id (ROMs)
    [JsonPropertyName("artist")] public string Artist { get; set; } = "";      // albums
    [JsonPropertyName("added")] public DateTime Added { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("scanned")] public bool Scanned { get; set; }               // added by SCAN THIS PC FOR ALBUMS
}

/// <summary>An emulator the player installed themselves. ArcadeGridz ships no emulators or ROMs.</summary>
public sealed class EmulatorConfig
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("exe")] public string Exe { get; set; } = "";
    [JsonPropertyName("args")] public string Args { get; set; } = "\"{rom}\"";   // {rom} = full ROM path
    [JsonPropertyName("extensions")] public string Extensions { get; set; } = ""; // ".sfc .smc" (used to suggest an emulator)
}

public sealed class LocalLibraryData
{
    [JsonPropertyName("items")] public List<LocalItem> Items { get; set; } = [];
    [JsonPropertyName("emulators")] public List<EmulatorConfig> Emulators { get; set; } = [];
}

/// <summary>
/// The player's own library (OPTIONS &gt; GAMES &gt; ADD GAME CARD): saved in %LOCALAPPDATA%\ArcadeGridz\library\library.json,
/// shown as tiles in the MY LIBRARY category (albums in the ALBUMS view), launched straight from the PC.
/// </summary>
public sealed class LocalLibraryService
{
    public const string KindApp = "app", KindRom = "rom", KindAlbum = "album";
    public const string Runtime = "local";
    public const string Category = "MY LIBRARY";
    public static readonly string[] AudioExtensions = [".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".ogg"];
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"];

    static string Dir => System.IO.Path.Combine(AppPaths.Root, "library");
    static string CoversDir => System.IO.Path.Combine(Dir, "covers");
    static string FilePath => System.IO.Path.Combine(Dir, "library.json");
    static readonly JsonSerializerOptions json = new() { WriteIndented = true };

    public LocalLibraryData Data { get; private set; } = new();
    public event Action? Changed;

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath)) Data = JsonSerializer.Deserialize<LocalLibraryData>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) { StartupLog.Exception("Library load", ex); Data = new(); }
        StartupLog.Write("library", $"{Data.Items.Count} items, {Data.Emulators.Count} emulators");
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) { StartupLog.Exception("Library save", ex); }
        Changed?.Invoke();
    }

    public LocalItem? Get(string id) => Data.Items.FirstOrDefault(i => i.Id == id);
    public EmulatorConfig? Emulator(string id) => Data.Emulators.FirstOrDefault(e => e.Id == id);

    public void Upsert(LocalItem item)
    {
        var i = Data.Items.FindIndex(x => x.Id == item.Id);
        if (i >= 0) Data.Items[i] = item; else Data.Items.Add(item);
        Save();
    }

    public void Remove(string id) => RemoveMany([id]);

    /// <summary>Removes items (and their copied covers) with one save. Never touches the player's own files.</summary>
    public void RemoveMany(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        foreach (var item in Data.Items.Where(i => set.Contains(i.Id)).ToList())
        {
            Data.Items.Remove(item);
            try { if (item.Cover.StartsWith(CoversDir, StringComparison.OrdinalIgnoreCase) && File.Exists(item.Cover)) File.Delete(item.Cover); } catch { }
        }
        Save();
    }

    /// <summary>Copies a chosen cover into the library so moving or deleting the original never breaks the tile.</summary>
    public static string ImportCover(string source, string itemId)
    {
        Directory.CreateDirectory(CoversDir);
        if (source.StartsWith(CoversDir, StringComparison.OrdinalIgnoreCase)) return source;
        var target = System.IO.Path.Combine(CoversDir, $"{itemId}-{DateTime.UtcNow.Ticks % 100000}{System.IO.Path.GetExtension(source).ToLowerInvariant()}");
        File.Copy(source, target, overwrite: true);
        return target;
    }

    /// <summary>A cover sitting next to the music (cover.jpg, folder.jpg, front.png ...), if any.</summary>
    public static string? FindFolderCover(string folder)
    {
        try
        {
            foreach (var name in new[] { "cover", "folder", "front", "album", "art" })
                foreach (var ext in ImageExtensions)
                {
                    var p = System.IO.Path.Combine(folder, name + ext);
                    if (File.Exists(p)) return p;
                }
            return Directory.EnumerateFiles(folder).FirstOrDefault(f => ImageExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()));
        }
        catch { return null; }
    }

    public static string FileUri(string path) => string.IsNullOrEmpty(path) ? "" : new Uri(path).AbsoluteUri;

    static string Fallback(string kind) => kind switch
    {
        KindRom => "ms-appx:///Assets/Library/local-rom.png",
        KindAlbum => "ms-appx:///Assets/Library/local-album.png",
        _ => "ms-appx:///Assets/Library/local-app.png"
    };

    public static string TagLine(LocalItem i)
    {
        var tags = string.Join("  ·  ", i.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return tags.Length > 0 ? tags : i.Kind == KindRom ? "ROM" : i.Kind == KindAlbum ? "ALBUM" : "PC GAME";
    }

    /// <summary>Tiles for the game browser (apps and ROMs; albums go to the music player).</summary>
    public IEnumerable<GameEntry> GameEntries() => Data.Items.Where(i => i.Kind != KindAlbum).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(i => new GameEntry
    {
        Id = "local:" + i.Id, Name = i.Name, Category = Category, Genre = TagLine(i),
        Status = i.Kind == KindRom ? "ROM" : "PC", Description = i.Description,
        Art = File.Exists(i.Cover) ? FileUri(i.Cover) : Fallback(i.Kind),
        Playable = true, Potato = true, Tone = "cyan",
        Runtime = new RuntimeSpec { Type = Runtime, Url = i.Id }
    });

    public static bool IsLocal(GameEntry g) => g.Runtime.Type == Runtime;
    public static string LocalId(GameEntry g) => g.Id.StartsWith("local:") ? g.Id[6..] : g.Runtime.Url ?? "";

    /// <summary>Suggest the emulator whose extensions cover this ROM.</summary>
    public EmulatorConfig? EmulatorFor(string romPath)
    {
        var ext = System.IO.Path.GetExtension(romPath).ToLowerInvariant();
        return Data.Emulators.FirstOrDefault(e => e.Extensions.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Any(x => (x.StartsWith('.') ? x : "." + x).Equals(ext, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Starts an app/game (exe, shortcut, URL protocol like steam://) or a ROM through its emulator.</summary>
    public Process? Launch(LocalItem item)
    {
        ProcessStartInfo psi;
        if (item.Kind == KindRom)
        {
            var emu = Emulator(item.Emulator) ?? throw new InvalidOperationException("Pick an emulator for this ROM (EDIT).");
            if (!File.Exists(emu.Exe)) throw new FileNotFoundException($"The emulator {emu.Name} isn't at {emu.Exe} any more.");
            if (!File.Exists(item.Path)) throw new FileNotFoundException($"The ROM isn't at {item.Path} any more.");
            var args = (string.IsNullOrWhiteSpace(emu.Args) ? "\"{rom}\"" : emu.Args)
                .Replace("{rom}", item.Path).Replace("{romdir}", System.IO.Path.GetDirectoryName(item.Path) ?? "")
                .Replace("{romname}", System.IO.Path.GetFileNameWithoutExtension(item.Path));
            psi = new ProcessStartInfo(emu.Exe, args) { WorkingDirectory = System.IO.Path.GetDirectoryName(emu.Exe) ?? "", UseShellExecute = false };
        }
        else
        {
            var target = item.Path.Trim();
            var isLink = target.Contains("://");
            if (!isLink && !File.Exists(target)) throw new FileNotFoundException($"{item.Name} isn't at {target} any more.");
            var isExe = !isLink && System.IO.Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase);
            psi = new ProcessStartInfo(target, isLink ? "" : item.Args) { UseShellExecute = !isExe };
            if (isExe) psi.WorkingDirectory = System.IO.Path.GetDirectoryName(target) ?? "";
        }
        StartupLog.Write("library", $"Launch {item.Kind} '{item.Name}'");
        return Process.Start(psi);
    }
}
