using System.Text.Json;
namespace ArcadeGridz.App.Services;
public sealed class SettingsService
{
    public bool ShowAll {get;set;}
    string FileName=>Path.Combine(AppPaths.Root,"settings.json");
    public static SettingsService Load(){AppPaths.Ensure();try{return JsonSerializer.Deserialize<SettingsService>(File.ReadAllText(Path.Combine(AppPaths.Root,"settings.json")))??new();}catch{return new();}}
    public void Save()=>File.WriteAllText(FileName,JsonSerializer.Serialize(this));
}
