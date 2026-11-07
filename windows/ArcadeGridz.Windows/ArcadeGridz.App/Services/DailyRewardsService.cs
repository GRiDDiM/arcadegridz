using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArcadeGridz.App.Services;

public sealed record DailyReward(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("amount")] long Amount);

public sealed record DailyQuest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("reward")] long Reward,
    [property: JsonPropertyName("completed")] bool Completed);

public sealed record DailyState(
    [property: JsonPropertyName("streak")] int Streak,
    [property: JsonPropertyName("streakBonus")] long StreakBonus,
    [property: JsonPropertyName("balance")] JsonElement Balance,
    [property: JsonPropertyName("rewards")] List<DailyReward>? Rewards,
    [property: JsonPropertyName("quests")] List<DailyQuest>? Quests)
{
    /// <summary>Seal 1..7 of the current seven-day cycle (0 = no streak yet).</summary>
    public int Stage => Streak > 0 ? (Streak - 1) % 7 + 1 : 0;
    public int Cycle => Streak > 0 ? (Streak - 1) / 7 + 1 : 1;
    public long NewSparkz => Rewards?.Where(r => r.Amount > 0).Sum(r => r.Amount) ?? 0;
    public string BalanceText => Balance.ValueKind == JsonValueKind.Number && Balance.TryGetInt64(out var b) ? b.ToString("N0") : "—";
}

/// <summary>
/// The Network's daily check-in, called natively (no web page). Same endpoints the websites use:
/// POST /account/daily-quests/visit (records today's visit, returns any new rewards) and
/// GET /account/daily-quests (read-only state).
/// </summary>
public sealed class DailyRewardsService
{
    const string Base = "https://griddim.com";
    const string Site = "arcadegridz";
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    string? lastUser;
    DateTime lastVisit = DateTime.MinValue;

    /// <summary>Checks in for today. Returns the state only when the call is not throttled; null when skipped or failed.</summary>
    public async Task<DailyState?> VisitAsync(string token, string userId, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        if (!force && userId == lastUser && DateTime.UtcNow - lastVisit < TimeSpan.FromMinutes(2)) return null;
        lastUser = userId;
        lastVisit = DateTime.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/account/daily-quests/visit")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { site = Site }), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await SendAsync(request);
        }
        catch (Exception ex)
        {
            lastVisit = DateTime.MinValue;
            StartupLog.Exception("Daily rewards check-in", ex);
            return null;
        }
    }

    public async Task<DailyState?> StateAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/account/daily-quests?site={Site}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await SendAsync(request);
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Daily rewards state", ex);
            return null;
        }
    }

    async Task<DailyState?> SendAsync(HttpRequestMessage request)
    {
        request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            StartupLog.Write("daily", $"HTTP {(int)response.StatusCode}");
            return null;
        }
        var state = JsonSerializer.Deserialize<DailyState>(body, Json);
        StartupLog.Write("daily", $"streak={state?.Streak}; new={state?.NewSparkz}");
        return state;
    }
}
