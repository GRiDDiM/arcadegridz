using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace ArcadeGridz.App.Services;

public enum RepeatMode { All, One, Off }

public sealed class AlbumTrack
{
    [JsonPropertyName("n")] public int Number { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("fps")] public int Fps { get; set; } = 25;
    [JsonPropertyName("peaksL")] public string PeaksL { get; set; } = "";
    [JsonPropertyName("peaksR")] public string PeaksR { get; set; } = "";
    [JsonIgnore] public byte[]? L { get; set; }
    [JsonIgnore] public byte[]? R { get; set; }
}

public sealed class Album
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("subtitle")] public string Subtitle { get; set; } = "";
    [JsonPropertyName("artist")] public string Artist { get; set; } = "";
    [JsonPropertyName("base")] public string Base { get; set; } = "";
    [JsonPropertyName("tracks")] public List<AlbumTrack> Tracks { get; set; } = [];
}

/// <summary>
/// The launcher's music player: Benjamin Campbell's album "GAME OVER - Insert Coin To Continue", served from
/// arcadegridz.com/music/game-over/ (album.json lists tracks + precomputed L/R peak levels for the meter).
/// Tracks are cached under %LOCALAPPDATA%\ArcadeGridz\music after first play. Pauses while a game runs.
/// </summary>
public sealed class MusicPlayerService : IDisposable
{
    const string AlbumUrl = "https://arcadegridz.com/music/game-over/album.json";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };

    readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher;
    readonly SettingsService settings;
    readonly MediaPlayer player = new() { AudioCategory = MediaPlayerAudioCategory.Media, AutoPlay = false };
    readonly Random rng = new();
    List<int> order = [];
    int pos;                    // index into order
    bool pausedForGame;
    readonly HashSet<string> downloading = [];

    public Album? Album { get; private set; }
    public AlbumTrack? Current => Album is null || order.Count == 0 ? null : Album.Tracks[order[pos]];
    public bool IsPlaying => player.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Opening or MediaPlaybackState.Buffering;
    public TimeSpan Position => player.PlaybackSession.Position;
    public TimeSpan Duration => player.PlaybackSession.NaturalDuration > TimeSpan.Zero ? player.PlaybackSession.NaturalDuration : TimeSpan.FromSeconds(Current?.Duration ?? 0);
    public bool Shuffle => settings.MusicShuffle;
    public RepeatMode Repeat => settings.MusicRepeat;
    public bool Muted => settings.MusicMuted;
    public double Volume => settings.MusicVolume;

    /// <summary>Raised on the UI thread whenever the track, play state or a mode changes.</summary>
    public event Action? Changed;

    public MusicPlayerService(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, SettingsService settings)
    {
        this.dispatcher = dispatcher;
        this.settings = settings;
        ApplyVolume();
        player.MediaEnded += (_, _) => dispatcher.TryEnqueue(OnEnded);
        player.MediaFailed += (_, e) => dispatcher.TryEnqueue(() =>
        {
            StartupLog.Write("music", $"Track failed: {Current?.File}: {e.Error} {e.ErrorMessage}");
            if (Album is not null && Album.Tracks.Count > 1) Next(auto: true);
        });
        player.PlaybackSession.PlaybackStateChanged += (_, _) => dispatcher.TryEnqueue(() => Changed?.Invoke());
        // Windows media keys + the volume flyout's media controls drive the player
        var cm = player.CommandManager;
        cm.IsEnabled = true;
        cm.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        cm.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        cm.NextReceived += (_, a) => { a.Handled = true; dispatcher.TryEnqueue(() => Next()); };
        cm.PreviousReceived += (_, a) => { a.Handled = true; dispatcher.TryEnqueue(Previous); };
    }

    /// <summary>Loads the album (network first, cached copy if offline). Starts playback when autoPlay.</summary>
    public async Task LoadAsync(bool autoPlay)
    {
        var cacheDir = Path.Combine(AppPaths.Root, "music", "game-over");
        Directory.CreateDirectory(cacheDir);
        var cachedJson = Path.Combine(cacheDir, "album.json");
        string? json = null;
        try
        {
            json = await http.GetStringAsync(AlbumUrl);
            await File.WriteAllTextAsync(cachedJson, json);
        }
        catch (Exception ex)
        {
            StartupLog.Write("music", $"Album list offline ({ex.GetType().Name}); using cache: {File.Exists(cachedJson)}");
            if (File.Exists(cachedJson)) json = await File.ReadAllTextAsync(cachedJson);
        }
        if (json is null) return;
        try
        {
            Album = JsonSerializer.Deserialize<Album>(json);
            if (Album is null || Album.Tracks.Count == 0) { Album = null; return; }
            Album.Tracks.Sort((a, b) => a.Number.CompareTo(b.Number));
            foreach (var t in Album.Tracks)
            {
                try { t.L = Convert.FromBase64String(t.PeaksL); t.R = Convert.FromBase64String(t.PeaksR); } catch { }
            }
            BuildOrder(Math.Clamp(settings.MusicTrack, 0, Album.Tracks.Count - 1));
            StartupLog.Write("music", $"Album '{Album.Title}' loaded: {Album.Tracks.Count} tracks; autoplay={autoPlay}");
            Load(play: autoPlay);
        }
        catch (Exception ex) { StartupLog.Exception("Album load", ex); Album = null; }
    }

    void BuildOrder(int startTrack)
    {
        if (Album is null) return;
        order = Enumerable.Range(0, Album.Tracks.Count).ToList();
        if (settings.MusicShuffle)
        {
            order.Remove(startTrack);
            for (int i = order.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            order.Insert(0, startTrack);
            pos = 0;
        }
        else pos = startTrack;
    }

    string CachePath(AlbumTrack t) => Path.Combine(AppPaths.Root, "music", "game-over", t.File);

    void Load(bool play)
    {
        var t = Current;
        if (t is null || Album is null) return;
        settings.MusicTrack = order[pos];
        settings.Save();
        var cached = CachePath(t);
        var uri = File.Exists(cached) ? new Uri(cached) : new Uri(Album.Base + t.File);
        if (!File.Exists(cached)) _ = CacheAsync(t);
        var item = new MediaPlaybackItem(MediaSource.CreateFromUri(uri));
        var props = item.GetDisplayProperties();
        props.Type = MediaPlaybackType.Music;
        props.MusicProperties.Title = t.Title;
        props.MusicProperties.Artist = Album.Artist;
        props.MusicProperties.AlbumTitle = $"{Album.Title} - {Album.Subtitle}";
        props.MusicProperties.TrackNumber = (uint)t.Number;
        item.ApplyDisplayProperties(props);
        player.Source = item;
        if (play) player.Play();
        StartupLog.Write("music", $"Track {t.Number} {t.Title} ({(uri.IsFile ? "cache" : "stream")}); play={play}");
        Changed?.Invoke();
    }

    async Task CacheAsync(AlbumTrack t)
    {
        if (Album is null || !downloading.Add(t.File)) return;
        var target = CachePath(t);
        var tmp = target + ".part";
        try
        {
            using (var s = await http.GetStreamAsync(Album.Base + t.File))
            using (var f = File.Create(tmp)) await s.CopyToAsync(f);
            File.Move(tmp, target, overwrite: true);
        }
        catch (Exception ex) { StartupLog.Write("music", $"Cache {t.File} failed: {ex.GetType().Name}"); try { File.Delete(tmp); } catch { } }
        finally { downloading.Remove(t.File); }
    }

    void OnEnded()
    {
        if (settings.MusicRepeat == RepeatMode.One) { player.PlaybackSession.Position = TimeSpan.Zero; player.Play(); return; }
        Next(auto: true);
    }

    public void TogglePlay()
    {
        if (Album is null) return;
        pausedForGame = false;
        if (IsPlaying) player.Pause();
        else
        {
            if (player.Source is null) Load(play: true); else player.Play();
        }
        Changed?.Invoke();
    }

    public void Next(bool auto = false)
    {
        if (Album is null || order.Count == 0) return;
        var wasPlaying = IsPlaying || auto;
        if (pos + 1 >= order.Count)
        {
            // end of the album: stop (repeat off) or start over (repeat all); a fresh shuffle each lap
            if (auto && settings.MusicRepeat == RepeatMode.Off) { player.Pause(); player.PlaybackSession.Position = TimeSpan.Zero; Changed?.Invoke(); return; }
            if (settings.MusicShuffle)
            {
                var last = order[pos];
                var start = Album.Tracks.Count > 1 ? (last + 1 + rng.Next(Album.Tracks.Count - 1)) % Album.Tracks.Count : 0;
                BuildOrder(start);
            }
            else pos = 0;
        }
        else pos++;
        Load(play: wasPlaying);
    }

    public void Previous()
    {
        if (Album is null || order.Count == 0) return;
        // like any player: past 3 s, "previous" restarts the track
        if (player.PlaybackSession.Position > TimeSpan.FromSeconds(3)) { player.PlaybackSession.Position = TimeSpan.Zero; return; }
        var wasPlaying = IsPlaying;
        pos = pos > 0 ? pos - 1 : order.Count - 1;
        Load(play: wasPlaying);
    }

    public void ToggleShuffle()
    {
        settings.MusicShuffle = !settings.MusicShuffle;
        settings.Save();
        if (Album is not null && order.Count > 0) BuildOrder(order[pos]);
        Changed?.Invoke();
    }

    public void CycleRepeat()
    {
        settings.MusicRepeat = settings.MusicRepeat switch { RepeatMode.All => RepeatMode.One, RepeatMode.One => RepeatMode.Off, _ => RepeatMode.All };
        settings.Save();
        Changed?.Invoke();
    }

    public void ToggleMute()
    {
        settings.MusicMuted = !settings.MusicMuted;
        settings.Save();
        ApplyVolume();
        Changed?.Invoke();
    }

    public void SetVolume(double v)
    {
        settings.MusicVolume = Math.Clamp(v, 0, 1);
        if (settings.MusicVolume > 0 && settings.MusicMuted) settings.MusicMuted = false;
        settings.Save();
        ApplyVolume();
        Changed?.Invoke();
    }

    void ApplyVolume() => player.Volume = settings.MusicMuted ? 0 : settings.MusicVolume;

    public void Seek(double fraction)
    {
        var d = Duration;
        if (d > TimeSpan.Zero) player.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(fraction, 0, 0.999) * d.TotalSeconds);
    }

    /// <summary>A game is starting: stop the music so it never plays under the game.</summary>
    public void PauseForGame()
    {
        if (IsPlaying) { pausedForGame = true; player.Pause(); Changed?.Invoke(); }
    }

    /// <summary>Back in the launcher: pick the music back up if a game paused it.</summary>
    public void ResumeAfterGame()
    {
        if (!pausedForGame) return;
        pausedForGame = false;
        player.Play();
        Changed?.Invoke();
    }

    /// <summary>Current L/R peak level 0..1 from the precomputed meter data (0 when paused or muted).</summary>
    public (double L, double R) Levels()
    {
        var t = Current;
        if (t?.L is null || t.R is null || !IsPlaying || settings.MusicMuted) return (0, 0);
        var i = (int)(player.PlaybackSession.Position.TotalSeconds * t.Fps);
        if (i < 0 || i >= t.L.Length) return (0, 0);
        var gain = Math.Sqrt(settings.MusicVolume); // follow the volume knob a little, like a post-fader meter
        return (t.L[i] / 255.0 * gain, t.R[i] / 255.0 * gain);
    }

    public void Dispose() => player.Dispose();
}
