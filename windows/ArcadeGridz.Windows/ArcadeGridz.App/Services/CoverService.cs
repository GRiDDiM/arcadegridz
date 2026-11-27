using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace ArcadeGridz.App.Services;

/// <summary>
/// Covers for the player's library when they don't pick one: look one up online (Steam's store art for games,
/// iTunes album art for music; no keys needed) or make one from the program's own icon.
/// </summary>
public static class CoverService
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };
    public static HttpClient Http => http;

    /// <summary>Steam's tall library art for a known app id.</summary>
    public static async Task<string?> SteamArtAsync(string appId, CancellationToken ct = default)
    {
        foreach (var art in new[] { "library_600x900_2x.jpg", "library_600x900.jpg" })
            if (await Download($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/{art}", $"steam-{appId}.jpg", ct) is { } p) return p;
        return null;
    }

    static string TempDir => Path.Combine(Path.GetTempPath(), "ArcadeGridz-covers");

    static string Norm(string s) => Regex.Replace(s.ToLowerInvariant().Replace("&", "and"), @"[^a-z0-9]", "");

    static bool Matches(string want, string title, bool strict)
    {
        var a = Norm(want); var b = Norm(title);
        if (a.Length == 0 || b.Length == 0) return false;
        return strict ? a == b : a == b || (a.Length >= 4 && b.Contains(a)) || (b.Length >= 4 && a.Contains(b));
    }

    static async Task<string?> Download(string url, string file, CancellationToken ct)
    {
        try
        {
            var bytes = await http.GetByteArrayAsync(url, ct);
            if (bytes.Length < 2000) return null;
            Directory.CreateDirectory(TempDir);
            var p = Path.Combine(TempDir, file);
            await File.WriteAllBytesAsync(p, bytes, ct);
            return p;
        }
        catch (HttpRequestException) { return null; }
    }

    /// <summary>A game's tall library art from Steam's store (works for anything sold on Steam).</summary>
    public static async Task<(string Path, string Title)?> FindGameAsync(string name, bool strict, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var json = await http.GetStringAsync("https://store.steampowered.com/api/storesearch/?cc=us&l=english&term=" + Uri.EscapeDataString(name), ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items)) return null;
        foreach (var it in items.EnumerateArray())
        {
            var title = it.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!Matches(name, title, strict) || !it.TryGetProperty("id", out var idEl)) continue;
            var id = idEl.GetInt64();
            foreach (var art in new[] { "library_600x900_2x.jpg", "library_600x900.jpg" })
                if (await Download($"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/{art}", $"steam-{id}.jpg", ct) is { } p) return (p, title);
        }
        return null;
    }

    /// <summary>Album art from the iTunes catalog.</summary>
    public static async Task<(string Path, string Title)?> FindAlbumAsync(string album, string artist, bool strict, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(album)) return null;
        var term = string.IsNullOrWhiteSpace(artist) ? album : artist + " " + album;
        var json = await http.GetStringAsync("https://itunes.apple.com/search?media=music&entity=album&limit=10&term=" + Uri.EscapeDataString(term), ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results)) return null;
        foreach (var r in results.EnumerateArray())
        {
            var title = r.TryGetProperty("collectionName", out var c) ? c.GetString() ?? "" : "";
            if (!Matches(album, title, strict) || !r.TryGetProperty("artworkUrl100", out var art)) continue;
            var url = (art.GetString() ?? "").Replace("100x100bb", "600x600bb");
            if (url.Length > 0 && await Download(url, $"album-{Norm(title)}.jpg", ct) is { } p) return (p, title);
        }
        return null;
    }

    /// <summary>The biggest icon Windows has for a program, shortcut or file (null for links like steam://).</summary>
    public static async Task<Bitmap?> IconAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            try { using var ic = Icon.ExtractIcon(path, 0, 256); if (ic is not null) return ic.ToBitmap(); } catch { }
        }
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 256, ThumbnailOptions.UseCurrentScale);
            if (thumb is not null && thumb.Size > 0)
            {
                using var s = thumb.AsStreamForRead();
                using var b = new Bitmap(s);
                return new Bitmap(b);
            }
        }
        catch { }
        try { using var ic = Icon.ExtractAssociatedIcon(path); if (ic is not null) return ic.ToBitmap(); } catch { }
        return null;
    }

    static readonly Color[] Palette =
    [
        Color.FromArgb(0x5E, 0xE7, 0xFF), Color.FromArgb(0xFF, 0x3E, 0xA5), Color.FromArgb(0xFF, 0xC9, 0x4A),
        Color.FromArgb(0x7C, 0xFF, 0x6B), Color.FromArgb(0xA6, 0x6B, 0xFF), Color.FromArgb(0xFF, 0x8A, 0x3D)
    ];

    static FontFamily? pixel;
    static readonly PrivateFontCollection fonts = new();
    static FontFamily PixelFont()
    {
        if (pixel is not null) return pixel;
        try
        {
            var f = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "PressStart2P-Regular.ttf");
            if (File.Exists(f)) { fonts.AddFontFile(f); pixel = fonts.Families[0]; return pixel; }
        }
        catch { }
        return pixel = new FontFamily("Segoe UI");
    }

    static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    static int StableHash(string s) => Norm(s).Aggregate(17, (h, ch) => unchecked(h * 31 + ch));

    /// <summary>
    /// A 3:4 neon cover with NO text (the browser tile already prints the name, genre and badges over it): the program's
    /// icon, or a vinyl record for albums / a pixel sprite for games, glowing over a retro grid. All the art sits in the top
    /// part; the bottom fades dark so the tile's own text stays readable. Colours and shapes come from the name, so every
    /// cover is different but always the same for the same name.
    /// </summary>
    public static string Generate(string name, string label, Bitmap? icon)
    {
        const int W = 448, H = 600;
        name = string.IsNullOrWhiteSpace(name) ? "MY GAME" : name.Trim();
        var album = label.Contains("ALBUM", StringComparison.OrdinalIgnoreCase);
        var hash = StableHash(name);
        var rng = new Random(hash);
        var accent = Palette[(uint)hash % Palette.Length];
        var second = Palette[((uint)hash / 7 + 1 + (uint)rng.Next(Palette.Length - 1)) % Palette.Length];
        if (second == accent) second = Palette[((uint)hash + 2) % Palette.Length];
        var dark = Color.FromArgb(10, 6, 22);
        using var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Mix(dark, accent, 0.30f), dark, 90f)) g.FillRectangle(bg, 0, 0, W, H);
        // a few stars
        using (var star = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
            for (var i = 0; i < 28; i++) { var x = rng.Next(W); var y = rng.Next(300); var r = rng.Next(1, 3); g.FillEllipse(star, x, y, r, r); }
        // synthwave floor
        const float horizon = 352;
        using (var grid = new Pen(Color.FromArgb(55, accent), 1.5f))
        {
            for (var i = 1; i <= 10; i++) { var t = i / 10f; var y = horizon + (H - horizon) * t * t; g.DrawLine(grid, 0, y, W, y); }
            for (var i = -9; i <= 9; i++) g.DrawLine(grid, W / 2f + i * 12, horizon, W / 2f + i * 80, H);
        }
        // glow behind the art
        const float cy = 200;
        using (var glow = new GraphicsPath())
        {
            glow.AddEllipse(W / 2f - 190, cy - 190, 380, 380);
            using var pg = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(150, accent), SurroundColors = [Color.FromArgb(0, accent)] };
            g.FillPath(pg, glow);
        }

        if (icon is not null)
        {
            const int s = 200;
            g.InterpolationMode = icon.Width < 96 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(icon, new Rectangle(W / 2 - s / 2, (int)cy - s / 2, s, s));
        }
        else if (album) DrawRecord(g, W / 2f, cy, accent, second, rng);
        else DrawSprite(g, W / 2f, cy, accent, second, rng);

        // the bottom 45% fades to dark: the tile's name / genre text sits there
        using (var fade = new LinearGradientBrush(new RectangleF(0, 330, W, H - 330), Color.FromArgb(0, dark), Color.FromArgb(235, dark), 90f))
            g.FillRectangle(fade, 0, 330, W, H - 330);
        using (var frame = new Pen(Color.FromArgb(170, accent), 4)) g.DrawRectangle(frame, 2, 2, W - 5, H - 5);

        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, $"gen-{Guid.NewGuid():N}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>A vinyl record: grooves, a two-colour label and a little shine.</summary>
    static void DrawRecord(Graphics g, float cx, float cy, Color a, Color b, Random rng)
    {
        const float r = 150;
        using (var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillEllipse(shadow, cx - r + 8, cy - r + 10, r * 2, r * 2);
        using (var disc = new SolidBrush(Color.FromArgb(18, 16, 24))) g.FillEllipse(disc, cx - r, cy - r, r * 2, r * 2);
        using (var groove = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
            for (var gr = r - 8; gr > 58; gr -= 5) g.DrawEllipse(groove, cx - gr, cy - gr, gr * 2, gr * 2);
        using (var rim = new Pen(Color.FromArgb(150, a), 3)) g.DrawEllipse(rim, cx - r, cy - r, r * 2, r * 2);
        // shine
        using (var shine = new Pen(Color.FromArgb(70, 255, 255, 255), 10)) { g.DrawArc(shine, cx - r + 20, cy - r + 20, (r - 20) * 2, (r - 20) * 2, 200, 50); g.DrawArc(shine, cx - r + 20, cy - r + 20, (r - 20) * 2, (r - 20) * 2, 20, 50); }
        // label: split in two colours at a random angle
        const float lr = 54;
        using (var la = new SolidBrush(a)) g.FillEllipse(la, cx - lr, cy - lr, lr * 2, lr * 2);
        using (var lb = new SolidBrush(b)) g.FillPie(lb, cx - lr, cy - lr, lr * 2, lr * 2, rng.Next(360), 180);
        using (var ring = new Pen(Color.FromArgb(160, 255, 255, 255), 2)) g.DrawEllipse(ring, cx - lr * 0.62f, cy - lr * 0.62f, lr * 1.24f, lr * 1.24f);
        using (var hole = new SolidBrush(Color.FromArgb(18, 16, 24))) g.FillEllipse(hole, cx - 7, cy - 7, 14, 14);
    }

    /// <summary>A symmetrical pixel sprite (like an arcade invader), different for every name.</summary>
    static void DrawSprite(Graphics g, float cx, float cy, Color a, Color b, Random rng)
    {
        const int cols = 9, rows = 9, cell = 24;
        var half = (cols + 1) / 2;
        var on = new int[rows, cols];
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < half; x++)
            {
                var edge = y == 0 || y == rows - 1 ? 0.35 : 0.55;
                var v = rng.NextDouble() < edge ? (rng.NextDouble() < 0.25 ? 2 : 1) : 0;
                on[y, x] = v;
                on[y, cols - 1 - x] = v;
            }
        // make sure it has a solid middle so it never comes out empty
        for (var y = 2; y < rows - 2; y++) if (on[y, half - 1] == 0) on[y, half - 1] = 1;
        var ox = cx - cols * cell / 2f; var oy = cy - rows * cell / 2f;
        using var shadow = new SolidBrush(Color.FromArgb(130, 0, 0, 0));
        using var ba = new SolidBrush(a);
        using var bb = new SolidBrush(b);
        using var hi = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < cols; x++)
                if (on[y, x] > 0) g.FillRectangle(shadow, ox + x * cell + 6, oy + y * cell + 8, cell, cell);
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < cols; x++)
            {
                if (on[y, x] == 0) continue;
                var rx = ox + x * cell; var ry = oy + y * cell;
                g.FillRectangle(on[y, x] == 2 ? bb : ba, rx, ry, cell, cell);
                g.FillRectangle(hi, rx, ry, cell, 5);
            }
    }

    /// <summary>True for a cover this class made before (448x600 PNG), so it can be remade in the current style.</summary>
    public static bool LooksGenerated(string path)
    {
        try
        {
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
            using var f = File.OpenRead(path);
            var h = new byte[24];
            if (f.Read(h, 0, 24) < 24) return false;
            int w = h[16] << 24 | h[17] << 16 | h[18] << 8 | h[19], ht = h[20] << 24 | h[21] << 16 | h[22] << 8 | h[23];
            return w == 448 && ht == 600;
        }
        catch { return false; }
    }
}
