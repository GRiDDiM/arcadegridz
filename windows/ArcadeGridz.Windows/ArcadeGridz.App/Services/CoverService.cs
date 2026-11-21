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

    /// <summary>A 3:4 neon cover: the icon (or initials) glowing over a retro grid, with the name underneath.</summary>
    public static string Generate(string name, string label, Bitmap? icon)
    {
        const int W = 448, H = 600;
        name = string.IsNullOrWhiteSpace(name) ? "MY GAME" : name.Trim();
        var accent = Palette[(uint)Norm(name).Aggregate(17, (h, ch) => h * 31 + ch) % Palette.Length];
        var dark = Color.FromArgb(10, 6, 22);
        using var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), dark, Mix(dark, accent, 0.28f), 90f)) g.FillRectangle(bg, 0, 0, W, H);
        // synthwave floor
        const float horizon = 372;
        using (var grid = new Pen(Color.FromArgb(70, accent), 1.5f))
        {
            for (var i = 1; i <= 10; i++) { var t = i / 10f; var y = horizon + (H - horizon) * t * t; g.DrawLine(grid, 0, y, W, y); }
            for (var i = -9; i <= 9; i++) g.DrawLine(grid, W / 2f + i * 12, horizon, W / 2f + i * 80, H);
        }
        using (var hz = new Pen(Color.FromArgb(160, accent), 2f)) g.DrawLine(hz, 0, horizon, W, horizon);
        // glow behind the icon
        const float cy = 205;
        using (var glow = new GraphicsPath())
        {
            glow.AddEllipse(W / 2f - 175, cy - 175, 350, 350);
            using var pg = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(150, accent), SurroundColors = [Color.FromArgb(0, accent)] };
            g.FillPath(pg, glow);
        }
        if (icon is not null)
        {
            const int s = 196;
            g.InterpolationMode = icon.Width < 96 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(icon, new Rectangle(W / 2 - s / 2, (int)cy - s / 2, s, s));
        }
        else
        {
            var initials = string.Concat(Regex.Split(name, @"[\s\-_:]+").Where(w => w.Length > 0).Take(2).Select(w => char.ToUpperInvariant(w[0])));
            using var f = new Font(PixelFont(), 64, FontStyle.Regular, GraphicsUnit.Pixel);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var sh = new SolidBrush(Color.FromArgb(200, accent))) g.DrawString(initials, f, sh, new RectangleF(5, cy - 95, W, 200), fmt);
            g.DrawString(initials, f, Brushes.White, new RectangleF(0, cy - 100, W, 200), fmt);
        }
        // the name, as big as fits in two or three lines
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
        {
            var rect = new RectangleF(26, 400, W - 52, 150);
            var title = name.ToUpperInvariant();
            Font? font = null;
            for (var size = 30f; size >= 12; size -= 2)
            {
                font?.Dispose();
                font = new Font(PixelFont(), size, FontStyle.Regular, GraphicsUnit.Pixel);
                var m = g.MeasureString(title, font, (int)rect.Width);
                if (m.Height <= rect.Height && m.Width <= rect.Width + 1) break;
            }
            using (var plate = new SolidBrush(Color.FromArgb(150, dark))) g.FillRectangle(plate, 0, rect.Y - 8, W, rect.Height + 16);
            using (var sh = new SolidBrush(accent)) g.DrawString(title, font!, sh, new RectangleF(rect.X + 3, rect.Y + 3, rect.Width, rect.Height), fmt);
            g.DrawString(title, font!, Brushes.White, rect, fmt);
            font!.Dispose();
        }
        using (var lf = new Font(PixelFont(), 11, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var lb = new SolidBrush(accent))
            g.DrawString(label.ToUpperInvariant(), lf, lb, 24, 24);
        using (var frame = new Pen(Color.FromArgb(200, accent), 4)) g.DrawRectangle(frame, 2, 2, W - 5, H - 5);

        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, $"gen-{Guid.NewGuid():N}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }
}
