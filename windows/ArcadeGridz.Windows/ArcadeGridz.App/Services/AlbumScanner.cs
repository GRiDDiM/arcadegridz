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

    /// <summary>Signature of an album folder's contents: its name plus its song sizes (catches the same album copied twice).</summary>
    public static string? Signature(string folder)
    {
        try
        {
            var songs = Songs(folder);
            if (songs.Count < MinSongs) return null;
            return NormName(System.IO.Path.GetFileName(folder)) + "|" + string.Join(",", songs.Select(f => f.Length).OrderBy(x => x));
        }
        catch { return null; }
    }

    static List<FileInfo> Songs(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles()
            .Where(f => LocalLibraryService.AudioExtensions.Contains(f.Extension.ToLowerInvariant()) && f.Length >= MinSongBytes)
            .ToList();

    public static async Task<List<AlbumCandidate>> ScanAsync(IEnumerable<string> roots, IReadOnlyCollection<string> knownPaths,
        IReadOnlyCollection<string> knownSignatures, IProgress<string>? progress, CancellationToken ct)
    {
        var known = new HashSet<string>(knownPaths.Select(NormPath));
        var sigs = new HashSet<string>(knownSignatures);
        var found = new List<(string Path, string Sig)>();
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
                            if (sigs.Add(sig)) found.Add((dir.FullName, sig)); // a copy already in the library or already found: skipped
                        }
                    }
                    foreach (var sub in dir.EnumerateDirectories())
                    {
                        if (SkipNames.Contains(sub.Name) || sub.Name.StartsWith('.') || (sub.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue;
                        Walk(sub, depth + 1);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* no access: skip */ }
            }
            foreach (var r in roots) Walk(new DirectoryInfo(r), 0);
        }, ct);

        // names from the songs' own tags (album / artist / genre), else the folder names
        var result = new List<AlbumCandidate>();
        var namesSeen = new HashSet<string>();
        foreach (var (p, sig) in found)
        {
            ct.ThrowIfCancellationRequested();
            string name = System.IO.Path.GetFileName(p), artist = "", genre = "";
            try
            {
                var first = Songs(p).OrderBy(f => f.Name).First();
                var file = await StorageFile.GetFileFromPathAsync(first.FullName);
                var m = await file.Properties.GetMusicPropertiesAsync();
                if (!string.IsNullOrWhiteSpace(m.Album)) name = m.Album.Trim();
                artist = (!string.IsNullOrWhiteSpace(m.AlbumArtist) ? m.AlbumArtist : m.Artist ?? "").Trim();
                genre = m.Genre?.FirstOrDefault()?.Trim() ?? "";
            }
            catch { }
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
