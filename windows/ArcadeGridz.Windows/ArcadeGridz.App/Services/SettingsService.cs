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
    /// <summary>Turns off GPU acceleration in the game view (troubleshooting); applies next launch.</summary>
    public bool DisableGpu {get;set;}
    /// <summary>"standard" or "hd". Passed to games as gfx=...; HD remakes will read it.</summary>
    public string Quality {get;set;}="standard";
    string FileName=>Path.Combine(AppPaths.Root,"settings.json");
    public static SettingsService Load(){AppPaths.Ensure();try{return JsonSerializer.Deserialize<SettingsService>(File.ReadAllText(Path.Combine(AppPaths.Root,"settings.json")))??new();}catch{return new();}}
    public void Save(){try{File.WriteAllText(FileName,JsonSerializer.Serialize(this));}catch(Exception ex){StartupLog.Exception("Save settings",ex);}}
}
