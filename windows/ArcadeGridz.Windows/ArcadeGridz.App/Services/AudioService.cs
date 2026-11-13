using Windows.Media.Core;
using Windows.Media.Playback;

namespace ArcadeGridz.App.Services;

/// <summary>
/// Launcher audio: an ambient chillstep loop plus short UI sounds (all synthesized by tools/make_audio.py,
/// shipped in Assets/Audio). Music fades out while a game runs or the window is in the background.
/// </summary>
public sealed class AudioService : IDisposable
{
    const double MusicVolume = 0.32, SfxVolume = 0.55;
    readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher;
    readonly Dictionary<string, MediaPlayer[]> sfx = new();
    readonly Dictionary<string, int> nextVoice = new();
    MediaPlayer? music;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? fadeTimer;
    double musicTarget;
    bool musicWanted;
    long lastMove;

    public bool MusicOn { get; private set; }
    public bool SfxOn { get; set; }

    public AudioService(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, bool musicOn, bool sfxOn)
    {
        this.dispatcher = dispatcher;
        MusicOn = musicOn;
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
            var musicPath = AudioPath("chillstep.wav");
            if (File.Exists(musicPath))
            {
                music = NewPlayer(musicPath, MediaPlayerAudioCategory.GameMedia, 0);
                music.IsLoopingEnabled = true;
            }
            StartupLog.Write("audio", $"Ready; sounds={sfx.Count}; music={(music is not null)}; musicOn={musicOn}; sfxOn={sfxOn}");
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

    public void SetMusicOn(bool on)
    {
        MusicOn = on;
        if (on && musicWanted) StartMusic(); else FadeMusic(0, pauseAtEnd: true);
    }

    /// <summary>The launcher wants music (true) or silence for a game / background (false).</summary>
    public void MusicWanted(bool wanted)
    {
        musicWanted = wanted;
        if (wanted && MusicOn) StartMusic(); else FadeMusic(0, pauseAtEnd: true);
    }

    void StartMusic()
    {
        if (music is null) return;
        try
        {
            if (music.PlaybackSession.PlaybackState != MediaPlaybackState.Playing) music.Play();
            FadeMusic(MusicVolume, pauseAtEnd: false);
        }
        catch (Exception ex) { StartupLog.Exception("Music start", ex); }
    }

    void FadeMusic(double target, bool pauseAtEnd)
    {
        if (music is null) return;
        musicTarget = target;
        fadeTimer ??= CreateFadeTimer();
        fadeTimer.Tick -= FadeTick;
        fadeTimer.Tick += FadeTick;
        pauseWhenSilent = pauseAtEnd;
        fadeTimer.Start();
    }

    bool pauseWhenSilent;

    Microsoft.UI.Dispatching.DispatcherQueueTimer CreateFadeTimer()
    {
        var t = dispatcher.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(40);
        t.IsRepeating = true;
        return t;
    }

    void FadeTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (music is null) { sender.Stop(); return; }
        var v = music.Volume;
        var step = musicTarget > v ? 0.012 : 0.03; // ~1 s fade in, ~0.4 s fade out
        v = Math.Abs(musicTarget - v) <= step ? musicTarget : v + Math.Sign(musicTarget - v) * step;
        music.Volume = v;
        if (Math.Abs(v - musicTarget) < 0.0001)
        {
            sender.Stop();
            if (pauseWhenSilent && v <= 0.0001) { try { music.Pause(); } catch { } }
        }
    }

    public void Dispose()
    {
        fadeTimer?.Stop();
        music?.Dispose();
        foreach (var v in sfx.Values) foreach (var p in v) p.Dispose();
    }
}
