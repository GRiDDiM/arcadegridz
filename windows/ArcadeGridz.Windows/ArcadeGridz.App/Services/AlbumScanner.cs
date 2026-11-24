using Windows.Storage;

namespace ArcadeGridz.App.Services;

/// <summary>One music folder the scan found.</summary>
public sealed record AlbumCandidate(string Path, string Name, string Artist, string Genre, string Signature);

/// <summary>
/// Looks through the player's own folders (Music, Downloads, Desktop, Documents, OneDrive) and other fixed drives for
/// album folders: 3+ real songs (1 MB+, so game sound effects don't count). Skips system / game-store folders, folders
/// already in the library, and copies of the same album found twice (same name and same song sizes).
/// </summary>
public static class AlbumScanner
{
    const long MinSongBytes = 1_000_000;
    const int MinSongs = 3, MaxDepth = 9;

    static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "AppData", "$Recycle.Bin", "System Volume Information",
        "Recovery", "PerfLogs", "$WinREAgent", "node_modules", ".git", ".vs", "bin", "obj", "steamapps", "SteamLibrary", "Steam",
        "Epic Games", "XboxGames", "WindowsApps", "Battle.net", "GOG Games", "EA Games", "Ubisoft", "Riot Games", ".cache", ".nuget", ".dotnet"
    };

    // music-making and spoken-word folders: not albums (the whole folder tree is skipped)
    static readonly System.Text.RegularExpressions.Regex SkipFolder = new(
        // sample libraries, game assets, DAW/app folders, backups (the whole folder tree is skipped). Plain "projects" is NOT
        // skipped: artists keep real albums under folders like "LASERBEAST projects".
        @"\b(samples?|sample ?(banks?|packs?|libraries|library)|loops?|stems?|presets?|renders?|bounces?|bounced|backups?|drum ?kits?|kits|one ?shots?|" +
        @"construction ?kits?|multitracks?|sfx|sound ?effects|sound ?banks?|soundfonts?|cymatics|splice|podcasts?|audiobooks?|voice ?memos?|voicemails?|" +
        @"games?|game ?engines?|html5game|assets|bgm|serato|imported|(fl|ableton|daw|logic|reason|bitwig|cubase) ?(studio)? ?(mobile|projects?)|" +
        @"my (samples|recordings)|recordings|writing|screenplays?|temp|tmp|cache)\b|^(sounds?|audio|wav|zips?|archive|old)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Song-name fingerprint: the same album as MP3s and as WAVs, or in differently named folders, matches.</summary>
    static string? TrackSignature(List<FileInfo> songs)
    {
        var keys = TrackKeys(songs).OrderBy(k => k).ToList();
        return keys.Count < MinSongs ? null : "t|" + string.Join("|", keys);
    }

    static HashSet<string> TrackKeys(List<FileInfo> songs) =>
        songs.Select(f => NormName(System.Text.RegularExpressions.Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(f.Name), @"^[\d\s._\-]+", "")))
            .Where(k => k.Length > 0).ToHashSet();

    /// <summary>Song names of a folder already in the library (for the overlap check).</summary>
    public static HashSet<string> KnownTrackKeys(string folder) { try { return TrackKeys(Songs(folder)); } catch { return []; } }

    /// <summary>Most of its songs (80%+) are already in a bigger album: a reused-songs project, a partial copy, an EP inside the LP.</summary>
    static bool MostlyCovered(HashSet<string> keys, IEnumerable<HashSet<string>> others) =>
        keys.Count > 0 && others.Any(o => o.Count >= keys.Count && keys.Count(o.Contains) >= Math.Ceiling(keys.Count * 0.8));

    static readonly System.Text.RegularExpressions.Regex StemName = new(
        @"\b(kick|snare|hi ?hats?|hats?|clap|perc|drums?|bass|808|sub|vox|vocals?|lead|pad|synth|keys|piano|guitar|fx|stem|dry|wet|acapella|instrumental|master)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static string NormPath(string p)
    {
        try { return System.IO.Path.GetFullPath(p).TrimEnd('\\', '/').ToLowerInvariant(); } catch { return p.TrimEnd('\\', '/').ToLowerInvariant(); }
    }

    static string NormName(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static IEnumerable<string> DefaultRoots()
    {
        var list = new List<string>();
        void Add(string? p) { if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) list.Add(p); }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Add(System.IO.Path.Combine(home, "Music"));
        Add(System.IO.Path.Combine(home, "Downloads"));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add(Environment.GetEnvironmentVariable("OneDrive"));
        var systemDrive = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable &&
                    !string.Equals(d.RootDirectory.FullName, systemDrive, StringComparison.OrdinalIgnoreCase))
                    Add(d.RootDirectory.FullName);
            }
            catch { }
        }
        // a root inside another root gets scanned once
        var norm = list.Select(NormPath).Distinct().OrderBy(p => p.Length).ToList();
        return norm.Where((p, i) => !norm.Take(i).Any(q => p.StartsWith(q + "\\")));
    }

    /// <summary>Fingerprints of an album folder: name + song sizes (an exact copy) and its song names (another format / folder name).</summary>
    public static IEnumerable<string> Signatures(string folder)
    {
        List<FileInfo> songs;
        try { songs = Songs(folder); } catch { yield break; }
        if (songs.Count < MinSongs) yield break;
        yield return NormName(System.IO.Path.GetFileName(folder)) + "|" + string.Join(",", songs.Select(f => f.Length).OrderBy(x => x));
        if (TrackSignature(songs) is { } t) yield return t;
    }

    static List<FileInfo> Songs(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles()
            .Where(f => LocalLibraryService.AudioExtensions.Contains(f.Extension.ToLowerInvariant()) && f.Length >= MinSongBytes)
            .ToList();

    public static async Task<List<AlbumCandidate>> ScanAsync(IEnumerable<string> roots, IReadOnlyCollection<string> knownPaths,
        IReadOnlyCollection<string> knownSignatures, IReadOnlyCollection<HashSet<string>> knownKeys, IProgress<string>? progress, CancellationToken ct)
    {
        var known = new HashSet<string>(knownPaths.Select(NormPath));
        var sigs = new HashSet<string>(knownSignatures);
        var found = new List<(string Path, string Sig, HashSet<string> Keys)>();
        var scanned = 0;
        await Task.Run(() =>
        {
            void Walk(DirectoryInfo dir, int depth)
            {
                ct.ThrowIfCancellationRequested();
                if (depth > MaxDepth) return;
                if (++scanned % 200 == 0) progress?.Report($"Scanning... {scanned} folders, {found.Count} albums");
                try
                {
                    var path = NormPath(dir.FullName);
                    if (!known.Contains(path))
                    {
                        var songs = Songs(dir.FullName);
                        if (songs.Count >= MinSongs)
                        {
                            var sig = NormName(dir.Name) + "|" + string.Join(",", songs.Select(f => f.Length).OrderBy(x => x));
                            var tracks = TrackSignature(songs);
                            // a copy (exact, other format or other folder name) already in the library or already found: skipped
                            if (!sigs.Contains(sig) && (tracks is null || !sigs.Contains(tracks)))
                            {
                                sigs.Add(sig);
                                if (tracks is not null) sigs.Add(tracks);
                                found.Add((dir.FullName, sig, TrackKeys(songs)));
                            }
                        }
                    }
                    foreach (var sub in dir.EnumerateDirectories())
                    {
                        if (SkipNames.Contains(sub.Name) || SkipFolder.IsMatch(sub.Name) || sub.Name.StartsWith('.') || (sub.Attributes & (System.IO.FileAttributes.ReparsePoint | System.IO.FileAttributes.System)) != 0) continue;
                        Walk(sub, depth + 1);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* no access: skip */ }
            }
            foreach (var r in roots) Walk(new DirectoryInfo(r), 0);
        }, ct);

        // songs reused between folders: biggest first, then skip any folder whose songs are mostly in one already kept
        var kept = new List<HashSet<string>>(knownKeys);
        var unique = new List<(string Path, string Sig)>();
        foreach (var f in found.OrderByDescending(f => f.Keys.Count))
        {
            if (MostlyCovered(f.Keys, kept)) continue;
            kept.Add(f.Keys);
            unique.Add((f.Path, f.Sig));
        }
        // names from the songs' own tags (album / artist / genre), else the folder names
        var result = new List<AlbumCandidate>();
        var namesSeen = new HashSet<string>();
        foreach (var (p, sig) in unique.OrderBy(u => u.Path, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            string name = System.IO.Path.GetFileName(p), artist = "", genre = "";
            var songs = Songs(p);
            // stems: several files named like instrument parts
            if (songs.Count(f => StemName.IsMatch(System.IO.Path.GetFileNameWithoutExtension(f.Name))) >= 2) continue;
            var tagged = false;
            try
            {
                var first = songs.OrderBy(f => f.Name).First();
                var file = await StorageFile.GetFileFromPathAsync(first.FullName);
                var m = await file.Properties.GetMusicPropertiesAsync();
                if (!string.IsNullOrWhiteSpace(m.Album)) { name = m.Album.Trim(); tagged = true; }
                artist = (!string.IsNullOrWhiteSpace(m.AlbumArtist) ? m.AlbumArtist : m.Artist ?? "").Trim();
                genre = m.Genre?.FirstOrDefault()?.Trim() ?? "";
            }
            catch { }
            // untagged folders of only WAV/AIFF are renders, bounces or sample packs, not albums
            if (!tagged && songs.All(f => f.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) || f.Extension.StartsWith(".aif", StringComparison.OrdinalIgnoreCase))) continue;
            if (artist.Length == 0)
            {
                var parent = Directory.GetParent(p)?.Name ?? "";
                if (parent.Length > 0 && !roots.Any(r => NormPath(r) == NormPath(Directory.GetParent(p)!.FullName))) artist = parent;
            }
            // the same album (tags) in two different-looking folders: keep the first
            var key = NormName(artist) + "|" + NormName(name);
            if (NormName(name).Length > 0 && !namesSeen.Add(key)) continue;
            result.Add(new AlbumCandidate(p, name, artist, genre, sig));
        }
        progress?.Report($"Scanned {scanned} folders");
        return result;
    }
}
