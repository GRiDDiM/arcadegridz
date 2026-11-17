using System.Text.Json;
using ArcadeGridz.App.Models;
namespace ArcadeGridz.App.Services;
public sealed class LeaderboardService
{
    readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(6)};
    /// <summary>Every game's leaderboard in one call (id -> entries, best first); empty on failure.</summary>
    public async Task<Dictionary<string, List<ScoreEntry>>> AllAsync(){try{var raw=await http.GetStringAsync("https://arcadegridz.com/api/leaderboards");var x=JsonSerializer.Deserialize<LeaderboardResponse>(raw,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});return x?.Games??[];}catch{return[];}}
    public async Task<IReadOnlyList<ScoreEntry>> TopAsync(string id){try{var raw=await http.GetStringAsync("https://arcadegridz.com/api/leaderboards");var x=JsonSerializer.Deserialize<LeaderboardResponse>(raw,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});return x?.Games.GetValueOrDefault(id)?.Take(5).ToList()??[];}catch{return[];}}
}
