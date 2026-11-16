using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace ArcadeGridz.App.Services;

/// <summary>One launcher theme: a palette that every neon colour is remapped onto, plus fonts and backdrop flags.</summary>
public sealed record AppTheme(
    string Id, string Name, string Tagline,
    Color Ink, Color Card, Color Text, Color Muted, Color Dim,
    Color Cyan, Color Magenta, Color Violet, Color Gold, Color Green, Color Danger,
    bool Light, double Glow, bool Scanlines,
    string Display, string Ui, string Body, string Emoji, double UiScale = 1)
{
    public bool IsNeon => Id == "neon";
    public Color[] Swatches => [Ink, Cyan, Magenta, Violet, Gold, Green];
}

/// <summary>
/// Theme manager. The launcher is authored in the neon palette; instead of keeping six copies of the XAML,
/// the active theme walks the visual tree and remaps every brush colour (by role: ink, card, text, cyan, magenta...)
/// and the display/pixel fonts in place. The original neon value is remembered per brush and per element,
/// so switching themes (or back to NEON) is always computed from the source colours and never drifts.
/// </summary>
public static class ThemeService
{
    static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    const string Bungee = "ms-appx:///Assets/Fonts/Bungee-Regular.ttf#Bungee";
    const string PressStart = "ms-appx:///Assets/Fonts/PressStart2P-Regular.ttf#Press Start 2P";
    const string SegoeBody = "Segoe UI Variable Text";

    public static readonly AppTheme[] All =
    [
        new("neon", "NEON RETRO", "The original 80s arcade glow.",
            C(0xFF07051A), C(0xFF0B0820), C(0xFFE8ECFF), C(0xFF9FB0D8), C(0xFF6C7699),
            C(0xFF5EE7FF), C(0xFFFF3AA7), C(0xFF9F7CFF), C(0xFFFFC14D), C(0xFF3AFF9A), C(0xFFFF5468),
            false, 1, true, Bungee, PressStart, SegoeBody, "🕹️"),
        new("acid", "ACID ZOMBIE", "Toxic green neon from the graveyard.",
            C(0xFF030803), C(0xFF071107), C(0xFFE4FFD8), C(0xFF97C893), C(0xFF5B7A58),
            C(0xFF39FF14), C(0xFFB6FF00), C(0xFF00FFA3), C(0xFFE8FF3A), C(0xFF7CFF4F), C(0xFFC04BFF),
            false, 1, true, "ms-appx:///Assets/Fonts/Creepster-Regular.ttf#Creepster", PressStart, SegoeBody, "🧟"),
        new("brutal", "BRUTAL", "Blood red. Black metal. No mercy.",
            C(0xFF060000), C(0xFF120303), C(0xFFF3E4E4), C(0xFFB89A9A), C(0xFF745656),
            C(0xFFE00000), C(0xFF9A0000), C(0xFFFF3D1F), C(0xFFC9C9C9), C(0xFFFF5A1F), C(0xFFFF1A1A),
            false, 1, true, "ms-appx:///Assets/Fonts/MetalMania-Regular.ttf#Metal Mania", "ms-appx:///Assets/Fonts/BebasNeue-Regular.ttf#Bebas Neue", SegoeBody, "🤘", 1.6),
        new("happy", "HAPPY", "Sunny day, rainbows and unicorns.",
            C(0xFFFFF8E7), C(0xFFFFFFFF), C(0xFF3A2A5C), C(0xFF6B5B8F), C(0xFF9A8FB5),
            C(0xFF1E9BEA), C(0xFFFF4F98), C(0xFF9257F5), C(0xFFFFA800), C(0xFF22C063), C(0xFFFF4B4B),
            true, 0.55, false, "ms-appx:///Assets/Fonts/Chewy-Regular.ttf#Chewy", "ms-appx:///Assets/Fonts/Chewy-Regular.ttf#Chewy", SegoeBody, "🦄", 1.45),
        new("beige", "BEIGE", "Professionally boring. Like a tax form.",
            C(0xFFE7DECC), C(0xFFF1EBDE), C(0xFF4A4237), C(0xFF6E6557), C(0xFF938A7A),
            C(0xFF8C7B62), C(0xFF94866F), C(0xFF8F8573), C(0xFF9C8C6C), C(0xFF8A8270), C(0xFF8A6F60),
            true, 0.12, false, "Arial", "Arial", "Arial", "📎", 1.3),
        new("console", "STAR CONSOLE", "Starship bridge panels: rounded bars, black space.",
            C(0xFF000000), C(0xFF05050A), C(0xFFFFCC99), C(0xFFCC99CC), C(0xFF9999CC),
            C(0xFFFF9900), C(0xFFCC6699), C(0xFF9999FF), C(0xFFFFCC66), C(0xFF99CCFF), C(0xFFCC4444),
            false, 0.25, false, "ms-appx:///Assets/Fonts/BebasNeue-Regular.ttf#Bebas Neue", "ms-appx:///Assets/Fonts/BebasNeue-Regular.ttf#Bebas Neue", SegoeBody, "🖖", 1.7),
    ];

    public static AppTheme Get(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
    public static AppTheme Current { get; private set; } = All[0];
    public static event Action<AppTheme>? Changed;

    // The neon source value is stored ON the native object (attached properties), not in a managed table:
    // WinRT wrappers get recycled, and a lost lookup would re-theme an already themed colour.
    static readonly DependencyProperty OrigProperty = DependencyProperty.RegisterAttached("AgThemeOrig", typeof(string), typeof(ThemeService), new PropertyMetadata(null));
    static readonly DependencyProperty SizeProperty = DependencyProperty.RegisterAttached("AgThemeSize", typeof(string), typeof(ThemeService), new PropertyMetadata(null));
    static readonly DependencyProperty SkipProperty = DependencyProperty.RegisterAttached("AgThemeSkip", typeof(string), typeof(ThemeService), new PropertyMetadata(null));

    /// <summary>Elements whose subtree keeps its own colours and fonts (theme preview cards, scene art).</summary>
    public static void Exclude(DependencyObject element) => element.SetValue(SkipProperty, "1");

    static readonly Dictionary<string, FontFamily> fontCache = [];
    static bool dirtyFromNeon; // something was remapped and NEON must restore it

    static string Hex(Color c) => $"{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    static Color FromHex(string h) => C(Convert.ToUInt32(h, 16));
    static Color OrigColor(DependencyObject d, Color current)
    {
        if (d.GetValue(OrigProperty) is string h && h.Length == 8) return FromHex(h);
        d.SetValue(OrigProperty, Hex(current));
        return current;
    }

    public static void Set(AppTheme theme)
    {
        if (Current == theme) return;
        Current = theme;
        StartupLog.Write("theme", $"theme -> {theme.Id}");
        Changed?.Invoke(theme);
    }

    // ---- colour roles ----
    enum Role { Ink, Card, Text, Muted, Dim, Cyan, Magenta, Violet, Gold, Green, Danger, None }

    static readonly Dictionary<uint, Role> known = new()
    {
        [0x07051A] = Role.Ink, [0x050312] = Role.Ink,
        [0x0B0820] = Role.Card, [0x0E0B26] = Role.Card, [0x0C0A26] = Role.Card, [0x100A24] = Role.Card,
        [0x140E2E] = Role.Card, [0x1A1238] = Role.Card, [0x1E1A33] = Role.Card,
        [0xE8ECFF] = Role.Text, [0xFFFFFF] = Role.Text, [0xC9D2E2] = Role.Muted, [0x9FB0D8] = Role.Muted,
        [0x6C7699] = Role.Dim, [0x8D95AD] = Role.Dim,
        [0x5EE7FF] = Role.Cyan, [0x22E3D4] = Role.Cyan, [0xFF3AA7] = Role.Magenta, [0xFF6FB5] = Role.Magenta,
        [0x9F7CFF] = Role.Violet, [0xB26BFF] = Role.Violet, [0xFFC14D] = Role.Gold, [0xFFB070] = Role.Gold,
        [0x3AFF9A] = Role.Green, [0xFF5468] = Role.Danger, [0xFF3355] = Role.Danger, [0xFF4D6D] = Role.Danger, [0xFF3B4E] = Role.Danger,
    };

    static Color RoleColor(AppTheme t, Role r) => r switch
    {
        Role.Ink => t.Ink, Role.Card => t.Card, Role.Text => t.Text, Role.Muted => t.Muted, Role.Dim => t.Dim,
        Role.Cyan => t.Cyan, Role.Magenta => t.Magenta, Role.Violet => t.Violet, Role.Gold => t.Gold,
        Role.Green => t.Green, Role.Danger => t.Danger, _ => t.Text
    };

    static Color Lerp(Color a, Color b, double k)
    {
        k = Math.Clamp(k, 0, 1);
        return Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
    }

    static (double h, double s, double v) Hsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    /// <summary>Maps one neon-authored colour onto the theme (alpha kept, glows scaled).</summary>
    public static Color Map(Color c, AppTheme t)
    {
        if (t.IsNeon || c.A == 0) return c;
        var rgb = (uint)(c.R << 16 | c.G << 8 | c.B);
        if (rgb == 0) return c; // pure black stays black (game letterbox, shadows)
        Color outC;
        bool accent = false;
        if (known.TryGetValue(rgb, out var role))
        {
            outC = RoleColor(t, role);
            accent = role >= Role.Cyan;
        }
        else
        {
            var (h, s, v) = Hsv(c);
            if (v < 0.3)
            {
                // dark surfaces: ink -> card by brightness
                outC = Lerp(t.Ink, t.Card, v / 0.3);
            }
            else if (s < 0.22)
            {
                var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
                outC = lum >= 0.85 ? t.Text : lum >= 0.6 ? Lerp(t.Muted, t.Text, (lum - 0.6) / 0.25) : Lerp(t.Dim, t.Muted, (lum - 0.3) / 0.3);
            }
            else
            {
                accent = true;
                var baseC = h >= 165 && h < 205 ? t.Cyan
                    : h >= 205 && h < 290 ? t.Violet
                    : h >= 290 && h < 343 ? t.Magenta
                    : h >= 343 || h < 15 ? t.Danger
                    : h < 68 ? t.Gold
                    : t.Green;
                // keep pale tints pale (stars, glints) on dark themes
                var whiteness = 1 - s;
                outC = t.Light ? baseC : Lerp(baseC, Color.FromArgb(255, 255, 255, 255), whiteness * 0.85);
                if (v < 0.75) outC = Lerp(t.Ink, outC, v / 0.75);
            }
        }
        var a = c.A;
        if (accent && a < 0xF0 && t.Glow < 1) a = (byte)(a * t.Glow);
        return Color.FromArgb(a, outC.R, outC.G, outC.B);
    }

    // ---- in-place remapping ----
    static void MapBrush(Brush? brush, AppTheme t)
    {
        try
        {
            switch (brush)
            {
                case SolidColorBrush sb:
                {
                    var target = Map(OrigColor(sb, sb.Color), t);
                    if (sb.Color != target) sb.Color = target;
                    break;
                }
                case GradientBrush gb:
                    foreach (var stop in gb.GradientStops)
                    {
                        var target = Map(OrigColor(stop, stop.Color), t);
                        if (stop.Color != target) stop.Color = target;
                    }
                    break;
            }
        }
        catch { /* a few system brushes are read-only; leave them */ }
    }

    static FontFamily Font(string source)
    {
        if (!fontCache.TryGetValue(source, out var f)) { f = new FontFamily(source); fontCache[source] = f; }
        return f;
    }

    static void MapFont(DependencyObject d, DependencyProperty prop, DependencyProperty sizeProp, AppTheme t)
    {
        if (d.GetValue(prop) is not FontFamily current) return;
        var origSrc = d.GetValue(OrigProperty) as string;
        if (origSrc is null)
        {
            var src = current.Source ?? "";
            // only the launcher's own fonts are themed (icons, emoji and system defaults are left alone)
            if (src != Bungee && src != PressStart && src != SegoeBody) return;
            d.SetValue(OrigProperty, src);
            origSrc = src;
        }
        var want = t.IsNeon ? origSrc : origSrc == Bungee ? t.Display : origSrc == PressStart ? t.Ui : t.Body;
        if (current.Source != want) d.SetValue(prop, Font(want));
        // the pixel font is huge per point; condensed/rounded replacements get scaled up to match
        if (origSrc == PressStart)
        {
            if (d.GetValue(sizeProp) is not double size) return;
            var stored = d.GetValue(SizeProperty) as string;
            double origSize;
            if (stored is null) { origSize = size; d.SetValue(SizeProperty, size.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            else origSize = double.Parse(stored, System.Globalization.CultureInfo.InvariantCulture);
            var wantSize = Math.Round(origSize * (t.IsNeon ? 1 : t.UiScale), 1);
            if (Math.Abs(size - wantSize) > 0.05) d.SetValue(sizeProp, wantSize);
        }
    }

    /// <summary>Remaps the whole tree under <paramref name="root"/> (and open popups) to the current theme.</summary>
    public static void Apply(UIElement root)
    {
        var t = Current;
        if (t.IsNeon && !dirtyFromNeon) return;
        dirtyFromNeon = !t.IsNeon;
        Walk(root, t);
        try
        {
            if (root.XamlRoot is { } xr)
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xr))
                    if (popup.Child is { } child) Walk(child, t);
        }
        catch { }
    }

    static void Walk(DependencyObject d, AppTheme t)
    {
        if (d.GetValue(SkipProperty) is string) return;
        switch (d)
        {
            case Control c:
                MapBrush(c.Background, t); MapBrush(c.Foreground, t); MapBrush(c.BorderBrush, t);
                MapFont(c, Control.FontFamilyProperty, Control.FontSizeProperty, t);
                break;
            case TextBlock tb:
                MapBrush(tb.Foreground, t);
                MapFont(tb, TextBlock.FontFamilyProperty, TextBlock.FontSizeProperty, t);
                break;
            case ContentPresenter cp:
                MapBrush(cp.Background, t); MapBrush(cp.Foreground, t); MapBrush(cp.BorderBrush, t);
                MapFont(cp, ContentPresenter.FontFamilyProperty, ContentPresenter.FontSizeProperty, t);
                break;
            case Border b:
                MapBrush(b.Background, t); MapBrush(b.BorderBrush, t);
                break;
            case Panel p:
                MapBrush(p.Background, t);
                break;
            case Shape s:
                MapBrush(s.Fill, t); MapBrush(s.Stroke, t);
                break;
            case IconElement ic:
                MapBrush(ic.Foreground, t);
                break;
        }
        var n = VisualTreeHelper.GetChildrenCount(d);
        for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i), t);
    }

    /// <summary>Remaps brushes held in a resource dictionary (styles' setter brushes included).</summary>
    public static void ApplyResources(ResourceDictionary dict)
    {
        var t = Current;
        foreach (var value in dict.Values)
        {
            try
            {
                if (value is Brush b) MapBrush(b, t);
                else if (value is Style st)
                    foreach (var sb in st.Setters.OfType<Setter>()) if (sb.Value is Brush sbb) MapBrush(sbb, t);
            }
            catch { }
        }
    }
}
