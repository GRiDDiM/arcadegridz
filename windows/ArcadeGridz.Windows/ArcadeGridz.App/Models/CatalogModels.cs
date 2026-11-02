using System.Text.Json.Serialization;
namespace ArcadeGridz.App.Models;
public sealed class LauncherCatalog
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("games")] public List<GameEntry> Games { get; set; } = [];
}
public sealed class GameEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("genre")] public string Genre { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("art")] public string Art { get; set; } = "";
    [JsonPropertyName("tone")] public string Tone { get; set; } = "cyan";
    [JsonPropertyName("mark")] public string Mark { get; set; } = "";
    [JsonPropertyName("potato")] public bool Potato { get; set; }
    [JsonPropertyName("playable")] public bool Playable { get; set; }
    [JsonPropertyName("runtime")] public RuntimeSpec Runtime { get; set; } = new();
}
public sealed class RuntimeSpec
{
    [JsonPropertyName("type")] public string Type { get; set; } = "web";
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("packageUrl")] public string? PackageUrl { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("exe")] public string? Exe { get; set; }
}
public sealed class LeaderboardResponse { [JsonPropertyName("games")] public Dictionary<string,List<ScoreEntry>> Games { get; set; }=[]; }
public sealed class ScoreEntry
{
    [JsonPropertyName("name")] public string Name { get; set; }="";
    [JsonPropertyName("score")] public long Score { get; set; }
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    [JsonPropertyName("assisted")] public bool Assisted { get; set; }
    [JsonPropertyName("cheater")] public bool Cheater { get; set; }
    public string Tag => Cheater ? "CHEATER" : Assisted ? "ASSISTED" : Verified ? "VERIFIED" : "UNVERIFIED";
}
public sealed record ScoreRow(string Rank, string Name, string ScoreText, string Tag);
