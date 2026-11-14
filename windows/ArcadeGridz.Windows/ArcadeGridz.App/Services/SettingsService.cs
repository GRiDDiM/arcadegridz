using System.Text.Json;
namespace ArcadeGridz.App.Services;
public sealed class SettingsService
{
    public bool ShowAll {get;set;}
    /// <summary>Outer bounds ("x,y,w,h") of the display the app last ran fullscreen on, so it reopens there (e.g. the TV).</summary>
    public string? Display {get;set;}
    /// <summary>Windowed (normal resizable window) instead of fullscreen.</summary>
    public bool Windowed {get;set;}
    public bool HideScanlines {get;set;}
    public bool StillBackground {get;set;}
    public bool MusicOff {get;set;}
    public double MusicVolume {get;set;}=0.1;
    /// <summary>1 = music default lowered to 10 (v0.1.31); untouched 60% volumes get moved down once.</summary>
    public int SettingsVersion {get;set;}
    public bool MusicMuted {get;set;}
    public bool MusicShuffle {get;set;}
    public RepeatMode MusicRepeat {get;set;}=RepeatMode.All;
    public int MusicTrack {get;set;}
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
