using Windows.Media.Core;
using Windows.Media.Playback;

namespace ArcadeGridz.App.Services;

/// <summary>
/// Launcher UI sounds (all synthesized by tools/make_audio.py,
/// shipped in Assets/Audio). Music is MusicPlayerService.
/// </summary>
public sealed class AudioService : IDisposable
{
    const double MusicVolume = 0.32, SfxVolume = 0.55;
    readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher;
    readonly Dictionary<string, MediaPlayer[]> sfx = new();
    readonly Dictionary<string, int> nextVoice = new();
    long lastMove;

    public bool SfxOn { get; set; }

    public AudioService(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, bool sfxOn)
    {
        this.dispatcher = dispatcher;
        SfxOn = sfxOn;
        try
        {
            // move gets 3 voices so fast d-pad scrolling doesn't cut itself off
            foreach (var (name, voices) in new[] { ("move", 3), ("select", 2), ("back", 2), ("open", 1), ("launch", 1), ("error", 1), ("jingle", 1) })
            {
                var path = AudioPath(name + ".wav");
                if (!File.Exists(path)) { StartupLog.Write("audio", $"Missing {path}"); continue; }
                sfx[name] = Enumerable.Range(0, voices).Select(_ => NewPlayer(path, MediaPlayerAudioCategory.SoundEffects, SfxVolume)).ToArray();
            }
            StartupLog.Write("audio", $"Ready; sounds={sfx.Count}; sfxOn={sfxOn}");
        }
        catch (Exception ex) { StartupLog.Exception("Audio init", ex); }
    }

    static string AudioPath(string file) => Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", file);

    static MediaPlayer NewPlayer(string path, MediaPlayerAudioCategory category, double volume)
    {
        var p = new MediaPlayer
        {
            Source = MediaSource.CreateFromUri(new Uri(path)),
            AudioCategory = category,
            Volume = volume,
            AutoPlay = false
        };
        p.CommandManager.IsEnabled = false; // keep ArcadeGridz out of the Windows media overlay / volume flyout
        return p;
    }

    /// <summary>Plays a UI sound: move, select, back, open, launch, error, jingle.</summary>
    public void Play(string name)
    {
        if (!SfxOn || !sfx.TryGetValue(name, out var voices)) return;
        try
        {
            if (name == "move")
            {
                var now = Environment.TickCount64;
                if (now - lastMove < 45) return; // held d-pad repeat: don't machine-gun
                lastMove = now;
            }
            var i = nextVoice.GetValueOrDefault(name);
            nextVoice[name] = (i + 1) % voices.Length;
            var p = voices[i];
            p.PlaybackSession.Position = TimeSpan.Zero;
            p.Play();
        }
        catch (Exception ex) { StartupLog.Exception($"Audio play {name}", ex); }
    }

    public void Nav(PadAction action) => Play(action switch
    {
        PadAction.A => "select",
        PadAction.B => "back",
        PadAction.ExitCombo => "",
        _ => "move"
    });

    public void Dispose()
    {
        foreach (var v in sfx.Values) foreach (var p in v) p.Dispose();
    }
}
