using System.Text.Json;
namespace ArcadeGridz.App.Services;
public sealed class SettingsService
{
    public bool ShowAll {get;set;}
    /// <summary>Also list BETA and ALPHA games. Off by default: the launcher shows LIVE games only.</summary>
    public bool ShowTesting {get;set;}
    /// <summary>Game browser FILTER: source (ALL, ARCADEGRIDZ, PC, ROMS), PC store (ALL, STEAM, EPIC...) and ROM system (ALL, SNES...).</summary>
    public string FilterSource {get;set;}="ALL";
    public string FilterStore {get;set;}="ALL";
    public string FilterSystem {get;set;}="ALL";
    /// <summary>Outer bounds ("x,y,w,h") of the display the app last ran fullscreen on, so it reopens there (e.g. the TV).</summary>
    public string? Display {get;set;}
    /// <summary>Off = open on the screen the app was launched from (where the mouse was when the taskbar/Start/shortcut was clicked). On = always open on the saved Display.</summary>
    public bool PinScreen {get;set;}
    /// <summary>Windowed (normal resizable window) instead of fullscreen.</summary>
    public bool Windowed {get;set;}
    /// <summary>Launcher theme id (OPTIONS &gt; THEMES): neon, acid, brutal, happy, beige, console.</summary>
    public string Theme {get;set;}="neon";
    public bool HideScanlines {get;set;}
    public bool StillBackground {get;set;}
    /// <summary>OPTIONS &gt; GAMES: ADD GAME / ADD ALBUM card first in the browser (the player's own PC games, ROMs, music). Off by default.</summary>
    public bool ShowAddCard {get;set;}
    public bool CoversNoTextDone {get;set;} // one-time remake of generated library covers without text (v0.1.79)
    /// <summary>OPTIONS &gt; GAMES: play a game's trailer (muted, looping) in the details panel art spot. On by default.</summary>
    public bool NoTrailerPreview {get;set;}
    public bool MusicOff {get;set;}
    public double MusicVolume {get;set;}=0.1;
    /// <summary>1 = music default lowered to 10 (v0.1.31); untouched 60% volumes get moved down once.</summary>
    public int SettingsVersion {get;set;}
    public bool MusicMuted {get;set;}
    public bool MusicShuffle {get;set;}
    public bool MusicShuffleAll {get;set;} // with MusicShuffle: shuffle across every album
    public RepeatMode MusicRepeat {get;set;}=RepeatMode.All;
    public int MusicTrack {get;set;}
    public string MusicAlbum {get;set;}="game-over";
    public bool SfxOff {get;set;}
    /// <summary>Turns off GPU acceleration in the game view (troubleshooting); applies next launch.</summary>
    public bool DisableGpu {get;set;}
    /// <summary>"standard" or "hd". Passed to games as gfx=...; HD remakes will read it.</summary>
    public string Quality {get;set;}="standard";
    string FileName=>Path.Combine(AppPaths.Root,"settings.json");
    public static SettingsService Load()
    {
        AppPaths.Ensure();
        SettingsService s;
        try { s = JsonSerializer.Deserialize<SettingsService>(File.ReadAllText(Path.Combine(AppPaths.Root,"settings.json"))) ?? new(); }
        catch { s = new(); }
        if (s.SettingsVersion < 1)
        {
            if (Math.Abs(s.MusicVolume - 0.6) < 0.001) s.MusicVolume = 0.1; // still on the old default
            s.SettingsVersion = 1;
            s.Save();
        }
        return s;
    }
    public void Save(){try{File.WriteAllText(FileName,JsonSerializer.Serialize(this));}catch(Exception ex){StartupLog.Exception("Save settings",ex);}}
}
