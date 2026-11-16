using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ArcadeGridz.App;

/// <summary>
/// Vector backdrops for the non-neon themes (NEON keeps the 80s arcade hall art).
/// Drawn on a 1600x900 canvas that is cropped to fill the screen; all motion is opacity/transform
/// animation so it runs on the compositor thread, and it can be frozen with ANIMATED BACKGROUND off.
/// </summary>
public static class ThemeScenes
{
    const double W = 1600, H = 900;
    static readonly Random rng = new(1982);

    public sealed class Scene(FrameworkElement root, Storyboard story)
    {
        public FrameworkElement Root { get; } = root;
        public Storyboard Story { get; } = story;
    }

    static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    static SolidColorBrush B(uint argb) => new(C(argb));

    static LinearGradientBrush V(params (uint c, double o)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (c, o) in stops) g.GradientStops.Add(new GradientStop { Color = C(c), Offset = o });
        return g;
    }

    static RadialGradientBrush Glow(uint inner, uint outer = 0)
    {
        var g = new RadialGradientBrush();
        g.GradientStops.Add(new GradientStop { Color = C(inner), Offset = 0 });
        g.GradientStops.Add(new GradientStop { Color = outer == 0 ? Color.FromArgb(0, C(inner).R, C(inner).G, C(inner).B) : C(outer), Offset = 1 });
        return g;
    }

    static T At<T>(T e, double x, double y) where T : UIElement { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); return e; }

    static Microsoft.UI.Xaml.Shapes.Path P(string data, Brush? fill, Brush? stroke = null, double thick = 0)
    {
        var path = new Microsoft.UI.Xaml.Shapes.Path { Fill = fill, Stroke = stroke, StrokeThickness = thick };
        path.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Geometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" + data + "</Geometry>");
        return path;
    }

    // ---- animation helpers (all compositor-friendly) ----
    static void Anim(Storyboard sb, DependencyObject target, string prop, double from, double to, double seconds, double begin = 0, bool reverse = true, bool ease = true)
    {
        var a = new DoubleAnimation
        {
            From = from, To = to, Duration = TimeSpan.FromSeconds(seconds), BeginTime = TimeSpan.FromSeconds(begin),
            AutoReverse = reverse, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease ? new SineEase { EasingMode = EasingMode.EaseInOut } : null
        };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, prop);
        sb.Children.Add(a);
    }

    static CompositeTransform Xf(UIElement e, double ox = 0.5, double oy = 0.5)
    {
        var t = new CompositeTransform();
        e.RenderTransform = t;
        e.RenderTransformOrigin = new Point(ox, oy);
        return t;
    }

    public static Scene? Build(AppTheme theme) => theme.Id switch
    {
        "acid" => Acid(),
        "brutal" => Brutal(),
        "happy" => Happy(),
        "beige" => Beige(),
        "console" => Bridge(),
        _ => null
    };

    static (Canvas canvas, Grid root, Storyboard sb) Frame(Brush background)
    {
        var canvas = new Canvas { Width = W, Height = H, Background = background };
        var root = new Grid { IsHitTestVisible = false };
        root.Children.Add(new Viewbox { Stretch = Stretch.UniformToFill, Child = canvas });
        ThemeService.Exclude(root);
        return (canvas, root, new Storyboard());
    }

    /// <summary>Slime/blood drips hanging from the top edge, with drops that fall from some of them.</summary>
    static void Drips(Canvas c, Storyboard sb, Brush fill, Brush drop, int count, double maxLen, uint shine)
    {
        // a wavy band across the top with long tongues
        var d = new System.Text.StringBuilder("M0,0 L0,40 ");
        var tips = new List<(double x, double y)>();
        double x = 0;
        while (x < W)
        {
            var w = 30 + rng.NextDouble() * 70;
            var len = rng.NextDouble() < 0.45 ? 60 + rng.NextDouble() * maxLen : 30 + rng.NextDouble() * 25;
            var mid = x + w / 2;
            d.Append($"C{x + w * 0.2:0},{40 + 4:0} {mid - 10:0},{len * 0.55:0} {mid - 9:0},{len - 8:0} ");
            d.Append($"C{mid - 8:0},{len + 9:0} {mid + 8:0},{len + 9:0} {mid + 9:0},{len - 8:0} ");
            d.Append($"C{mid + 10:0},{len * 0.55:0} {x + w * 0.8:0},{44:0} {x + w:0},40 ");
            if (len > 90) tips.Add((mid, len));
            x += w;
        }
        d.Append($"L{W},0 Z");
        c.Children.Add(P(d.ToString(), fill));
        c.Children.Add(At(new Rectangle { Width = W, Height = 3, Fill = B(shine) }, 0, 10));
        // falling drops
        foreach (var (tx, ty) in tips.OrderBy(_ => rng.Next()).Take(count))
        {
            var e = At(new Ellipse { Width = 12, Height = 16, Fill = drop }, tx - 6, ty - 4);
            var t = Xf(e);
            c.Children.Add(e);
            var dur = 2.6 + rng.NextDouble() * 2.5;
            var begin = rng.NextDouble() * 4;
            Anim(sb, t, "TranslateY", 0, H - ty + 40, dur, begin, reverse: false, ease: false);
            Anim(sb, e, "Opacity", 1, 0.2, dur, begin, reverse: false, ease: false);
        }
    }

    // ===== ACID ZOMBIE: toxic graveyard under a green moon, slime dripping from the top =====
    static Scene Acid()
    {
        var (c, root, sb) = Frame(V((0xFF020702, 0), (0xFF061A06, 0.6), (0xFF0B2A08, 1)));
        c.Children.Add(At(new Ellipse { Width = 1300, Height = 900, Fill = Glow(0x5539FF14) }, 150, 120));
        // moon
        var moonGlow = At(new Ellipse { Width = 420, Height = 420, Fill = Glow(0x887CFF4F) }, 1040, -40);
        c.Children.Add(moonGlow);
        Anim(sb, moonGlow, "Opacity", 0.6, 1, 4);
        c.Children.Add(At(new Ellipse { Width = 190, Height = 190, Fill = Glow(0xFFD8FF9A, 0xFF8BE83C) }, 1155, 75));
        c.Children.Add(At(new Ellipse { Width = 38, Height = 30, Fill = B(0x5539A01A) }, 1200, 120));
        c.Children.Add(At(new Ellipse { Width = 24, Height = 22, Fill = B(0x5539A01A) }, 1260, 190));
        // dead trees
        c.Children.Add(P("M160,740 L172,520 L130,450 L176,500 L182,410 L196,505 L250,430 L204,540 L214,740 Z", B(0xFF010401)));
        c.Children.Add(P("M1380,760 L1392,560 L1340,500 L1398,540 L1410,470 L1420,548 L1470,500 L1426,590 L1436,760 Z", B(0xFF010401)));
        // toxic fog
        for (int i = 0; i < 4; i++)
        {
            var fog = At(new Ellipse { Width = 900, Height = 220, Fill = Glow(0x4439FF14) }, -200 + i * 450, 620 + (i % 2) * 40);
            var t = Xf(fog);
            c.Children.Add(fog);
            Anim(sb, t, "TranslateX", -80, 80, 9 + i * 2, i);
            Anim(sb, fog, "Opacity", 0.5, 1, 5 + i, i * 0.7);
        }
        // graveyard ground + tombstones with a green rim
        c.Children.Add(P("M0,760 C300,720 600,780 900,750 C1150,725 1400,770 1600,740 L1600,900 L0,900 Z", B(0xFF020802)));
        foreach (var (gx, gh, cross) in new (double x, double h, bool cross)[] { (260, 110, false), (420, 80, true), (610, 130, false), (980, 95, true), (1120, 120, false), (1260, 85, false) })
        {
            var top = 762 - gh;
            if (cross)
            {
                c.Children.Add(At(new Rectangle { Width = 18, Height = gh, Fill = B(0xFF041004), Stroke = B(0x8839FF14), StrokeThickness = 2 }, gx + 21, top));
                c.Children.Add(At(new Rectangle { Width = 60, Height = 16, Fill = B(0xFF041004), Stroke = B(0x8839FF14), StrokeThickness = 2 }, gx, top + 22));
            }
            else
                c.Children.Add(At(new Rectangle { Width = 64, Height = gh, RadiusX = 32, RadiusY = 30, Fill = B(0xFF041004), Stroke = B(0x8839FF14), StrokeThickness = 2 }, gx, top));
        }
        // a hand clawing out of the dirt
        var hand = P("M760,770 L766,700 L758,660 L768,656 L776,694 L780,640 L790,640 L790,690 L798,646 L808,650 L802,700 L812,668 L820,674 L810,720 L804,770 Z", B(0xFF0A1F06), B(0xAA39FF14), 2);
        var ht = Xf(hand, 0.5, 1);
        c.Children.Add(hand);
        Anim(sb, ht, "Rotation", -4, 5, 2.2);
        // rising bubbles
        for (int i = 0; i < 26; i++)
        {
            var s = 6 + rng.NextDouble() * 18;
            var e = At(new Ellipse { Width = s, Height = s, Stroke = B(0xCC7CFF4F), StrokeThickness = 2, Fill = B(0x2239FF14) }, rng.NextDouble() * W, H - 40);
            var t = Xf(e);
            c.Children.Add(e);
            var dur = 6 + rng.NextDouble() * 8;
            var begin = rng.NextDouble() * 8;
            Anim(sb, t, "TranslateY", 0, -(300 + rng.NextDouble() * 500), dur, begin, reverse: false, ease: false);
            Anim(sb, t, "TranslateX", -12, 12, 1.4 + rng.NextDouble(), begin);
            Anim(sb, e, "Opacity", 0.9, 0, dur, begin, reverse: false, ease: false);
        }
        Drips(c, sb, V((0xFF2BD10F, 0), (0xFF39FF14, 0.7), (0xFF9BFF5C, 1)), B(0xFF5CFF2E), 9, 170, 0x88D8FFB0);
        return new Scene(root, sb);
    }

    // ===== BRUTAL: black and blood, iron spikes, a skull, rising embers =====
    static Scene Brutal()
    {
        var (c, root, sb) = Frame(V((0xFF000000, 0), (0xFF120000, 0.55), (0xFF2A0000, 1)));
        var hell = At(new Ellipse { Width = 1800, Height = 700, Fill = Glow(0x99B00000) }, -100, 560);
        c.Children.Add(hell);
        Anim(sb, hell, "Opacity", 0.55, 1, 2.8);
        // skull
        var skull = new Canvas { Width = 420, Height = 460, Opacity = 0.2 };
        var bone = B(0xFFE00000);
        skull.Children.Add(At(new Ellipse { Width = 420, Height = 360, Fill = bone }, 0, 0));
        skull.Children.Add(At(new Rectangle { Width = 250, Height = 150, RadiusX = 30, RadiusY = 30, Fill = bone }, 85, 290));
        skull.Children.Add(At(new Ellipse { Width = 120, Height = 110, Fill = B(0xFF000000) }, 70, 160));
        skull.Children.Add(At(new Ellipse { Width = 120, Height = 110, Fill = B(0xFF000000) }, 230, 160));
        skull.Children.Add(P("M210,280 L182,340 L238,340 Z", B(0xFF000000)));
        for (int i = 0; i < 6; i++) skull.Children.Add(At(new Rectangle { Width = 6, Height = 70, Fill = B(0xFF000000) }, 112 + i * 38, 360));
        At(skull, (W - 420) / 2, 170);
        c.Children.Add(skull);
        Anim(sb, skull, "Opacity", 0.12, 0.26, 3.5);
        // iron spikes and hanging chains
        var spikes = new System.Text.StringBuilder("M0,900 L0,800 ");
        for (double x = 0; x < W; x += 50)
        {
            var hgt = 650 + rng.NextDouble() * 120 - (Math.Abs(x - W / 2) < 300 ? 0 : 40);
            spikes.Append($"L{x + 25:0},{hgt:0} L{x + 50:0},800 ");
        }
        spikes.Append("L1600,900 Z");
        c.Children.Add(P(spikes.ToString(), B(0xFF050000), B(0xAA700000), 2));
        foreach (var cx in new[] { 120.0, 1470 })
            for (int i = 0; i < 9; i++)
                c.Children.Add(At(new Ellipse { Width = 26, Height = 40, Stroke = B(0xFF3A3A3A), StrokeThickness = 6 }, cx + (i % 2) * 3, 80 + i * 34));
        // embers
        for (int i = 0; i < 40; i++)
        {
            var s = 3 + rng.NextDouble() * 5;
            var e = At(new Ellipse { Width = s, Height = s, Fill = B(rng.NextDouble() < 0.5 ? 0xFFFF5A1F : 0xFFFF2A00) }, rng.NextDouble() * W, H - 60);
            var t = Xf(e);
            c.Children.Add(e);
            var dur = 4 + rng.NextDouble() * 6;
            var begin = rng.NextDouble() * 6;
            Anim(sb, t, "TranslateY", 0, -(400 + rng.NextDouble() * 500), dur, begin, reverse: false, ease: false);
            Anim(sb, t, "TranslateX", -30, 30, 1 + rng.NextDouble() * 2, begin);
            Anim(sb, e, "Opacity", 1, 0, dur, begin, reverse: false, ease: false);
        }
        Drips(c, sb, V((0xFF4A0000, 0), (0xFF9A0000, 0.6), (0xFFD00000, 1)), B(0xFFC00000), 12, 260, 0x55FF6060);
        return new Scene(root, sb);
    }

    // ===== HAPPY: sunny sky, smiling sun, rainbow, clouds, hills, unicorns =====
    static Scene Happy()
    {
        var (c, root, sb) = Frame(V((0xFF6CC8FF, 0), (0xFFB6E6FF, 0.55), (0xFFFFF4D2, 1)));
        // rainbow
        uint[] bow = [0xFFFF4B4B, 0xFFFF9A2E, 0xFFFFE03A, 0xFF4CD964, 0xFF35B6FF, 0xFF5A6BFF, 0xFFB35CFF];
        for (int i = 0; i < bow.Length; i++)
        {
            var r = 560 - i * 34;
            c.Children.Add(P($"M{220 - r},{760} A{r},{r} 0 0 1 {220 + r},{760}", null, B(bow[i] & 0xDDFFFFFF), 34));
        }
        // sun with spinning rays and a face
        var rays = new Canvas { Width = 420, Height = 420 };
        for (int i = 0; i < 16; i++)
        {
            var ray = At(new Rectangle { Width = 26, Height = 90, RadiusX = 13, RadiusY = 13, Fill = B(0xFFFFC21A) }, 197, 0);
            ray.RenderTransform = new RotateTransform { Angle = i * 22.5, CenterX = 13, CenterY = 210 };
            rays.Children.Add(ray);
        }
        At(rays, 1130, -70);
        var rt = Xf(rays);
        c.Children.Add(rays);
        Anim(sb, rt, "Rotation", 0, 360, 40, reverse: false, ease: false);
        var sunGlow = At(new Ellipse { Width = 500, Height = 500, Fill = Glow(0x88FFE680) }, 1090, -110);
        c.Children.Add(sunGlow);
        Anim(sb, sunGlow, "Opacity", 0.6, 1, 3);
        var face = new Canvas { Width = 240, Height = 240 };
        face.Children.Add(At(new Ellipse { Width = 240, Height = 240, Fill = Glow(0xFFFFF27A, 0xFFFFC21A) }, 0, 0));
        face.Children.Add(At(new Ellipse { Width = 26, Height = 36, Fill = B(0xFF3A2A5C) }, 70, 80));
        face.Children.Add(At(new Ellipse { Width = 26, Height = 36, Fill = B(0xFF3A2A5C) }, 144, 80));
        face.Children.Add(At(new Ellipse { Width = 10, Height = 10, Fill = B(0xFFFFFFFF) }, 78, 86));
        face.Children.Add(At(new Ellipse { Width = 10, Height = 10, Fill = B(0xFFFFFFFF) }, 152, 86));
        face.Children.Add(At(new Ellipse { Width = 34, Height = 20, Fill = B(0x66FF6FA0) }, 36, 132));
        face.Children.Add(At(new Ellipse { Width = 34, Height = 20, Fill = B(0x66FF6FA0) }, 170, 132));
        face.Children.Add(P("M72,146 Q120,206 168,146", null, B(0xFF3A2A5C), 9));
        At(face, 1220, 20);
        var ft = Xf(face);
        c.Children.Add(face);
        Anim(sb, ft, "Rotation", -5, 5, 3);
        // clouds
        for (int i = 0; i < 5; i++)
        {
            var cloud = new Canvas { Width = 260, Height = 120 };
            cloud.Children.Add(At(new Ellipse { Width = 140, Height = 90, Fill = B(0xF2FFFFFF) }, 0, 30));
            cloud.Children.Add(At(new Ellipse { Width = 130, Height = 120, Fill = B(0xF2FFFFFF) }, 70, 0));
            cloud.Children.Add(At(new Ellipse { Width = 130, Height = 80, Fill = B(0xF2FFFFFF) }, 130, 40));
            At(cloud, -300, 60 + i * 70 + rng.NextDouble() * 40);
            var t = Xf(cloud);
            t.ScaleX = t.ScaleY = 0.6 + rng.NextDouble() * 0.6;
            c.Children.Add(cloud);
            Anim(sb, t, "TranslateX", 0, W + 600, 40 + i * 9, i * 6, reverse: false, ease: false);
        }
        // rolling hills + flowers
        c.Children.Add(P("M0,760 C250,660 520,680 800,760 C1050,690 1350,660 1600,740 L1600,900 L0,900 Z", B(0xFF7EDB6A)));
        c.Children.Add(P("M0,820 C320,760 700,800 1000,840 C1250,800 1450,790 1600,820 L1600,900 L0,900 Z", B(0xFF4CC75A)));
        for (int i = 0; i < 18; i++)
        {
            var fx = 30 + i * 88 + rng.NextDouble() * 30;
            var fy = 800 + rng.NextDouble() * 70;
            var col = new[] { 0xFFFF6FB5u, 0xFFFFE03Au, 0xFFFFFFFFu, 0xFFB35CFFu }[i % 4];
            for (int p = 0; p < 5; p++)
            {
                var a = p * Math.PI * 2 / 5;
                c.Children.Add(At(new Ellipse { Width = 12, Height = 12, Fill = B(col) }, fx + Math.Cos(a) * 8, fy + Math.Sin(a) * 8));
            }
            c.Children.Add(At(new Ellipse { Width = 9, Height = 9, Fill = B(0xFFFFB800) }, fx + 1.5, fy + 1.5));
        }
        // unicorns, hearts and sparkles floating about
        string[] critters = ["🦄", "🦄", "🦄", "🌈", "💖", "✨", "🌸", "⭐", "🦋", "💕", "✨", "🍭"];
        for (int i = 0; i < critters.Length; i++)
        {
            var big = critters[i] == "🦄";
            var tb = new TextBlock { Text = critters[i], FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = big ? 96 : 40 + rng.NextDouble() * 20, IsColorFontEnabled = true };
            At(tb, 80 + i * 125 + rng.NextDouble() * 40, big ? 520 + rng.NextDouble() * 120 : 120 + rng.NextDouble() * 420);
            var t = Xf(tb);
            if (big && i % 2 == 1) t.ScaleX = -1;
            c.Children.Add(tb);
            Anim(sb, t, "TranslateY", -18, 18, 1.6 + rng.NextDouble() * 1.4, rng.NextDouble() * 2);
            if (big) Anim(sb, t, "Rotation", -6, 6, 0.9 + rng.NextDouble() * 0.5, rng.NextDouble());
            else Anim(sb, tb, "Opacity", 0.55, 1, 1 + rng.NextDouble() * 1.5, rng.NextDouble() * 2);
        }
        return new Scene(root, sb);
    }

    // ===== BEIGE: nothing happens here. Ever. =====
    static Scene Beige()
    {
        var (c, root, sb) = Frame(B(0xFFE7DECC));
        // drop ceiling tiles, the most exciting part of any office
        for (double x = 0; x <= W; x += 160) c.Children.Add(At(new Rectangle { Width = 1, Height = H, Fill = B(0x14000000) }, x, 0));
        for (double y = 0; y <= H; y += 160) c.Children.Add(At(new Rectangle { Width = W, Height = 1, Fill = B(0x14000000) }, 0, y));
        return new Scene(root, sb);
    }

    // ===== STAR CONSOLE: black void framed by rounded bridge-panel bars and numbered blocks =====
    static Scene Bridge()
    {
        var (c, root, sb) = Frame(B(0xFF000000));
        uint orange = 0xFFFF9900, peach = 0xFFFFCC99, lilac = 0xFFCC99CC, blue = 0xFF9999FF, mauve = 0xFFCC6699, tan = 0xFFFFCC66, sky = 0xFF99CCFF;
        // left frame with elbows into the top and bottom bars
        c.Children.Add(P("M0,0 L560,0 L560,46 L190,46 Q150,46 150,86 L150,330 L0,330 Z", B(orange)));
        c.Children.Add(P("M0,560 L150,560 L150,814 Q150,854 190,854 L560,854 L560,900 L0,900 Z", B(lilac)));
        // left column blocks
        uint[] col = [peach, blue, mauve, tan];
        for (int i = 0; i < 4; i++) c.Children.Add(At(new Rectangle { Width = 150, Height = 50, Fill = B(col[i]) }, 0, 338 + i * 56));
        // top + bottom bar segments
        double tx = 572;
        foreach (var (w, colr) in new (double w, uint colr)[] { (180, peach), (90, mauve), (340, blue), (120, tan), (220, orange) })
        {
            c.Children.Add(At(new Rectangle { Width = w, Height = 46, Fill = B(colr) }, tx, 0));
            c.Children.Add(At(new Rectangle { Width = w, Height = 46, Fill = B(colr == orange ? sky : colr) }, tx, 854));
            tx += w + 12;
        }
        c.Children.Add(At(new Rectangle { Width = W - tx, Height = 46, RadiusX = 23, RadiusY = 23, Fill = B(lilac) }, tx, 0));
        c.Children.Add(At(new Rectangle { Width = W - tx, Height = 46, RadiusX = 23, RadiusY = 23, Fill = B(orange) }, tx, 854));
        // block numbers
        var labels = new[] { "01-4471", "02-0815", "03-1982", "04-2600" };
        for (int i = 0; i < 4; i++)
            c.Children.Add(At(new TextBlock { Text = labels[i], FontFamily = new FontFamily("ms-appx:///Assets/Fonts/BebasNeue-Regular.ttf#Bebas Neue"), FontSize = 22, Foreground = B(0xFF000000) }, 70, 360 + i * 56));
        c.Children.Add(At(new TextBlock { Text = "ARCADE OPS  ·  GRID STATUS NOMINAL", FontFamily = new FontFamily("ms-appx:///Assets/Fonts/BebasNeue-Regular.ttf#Bebas Neue"), FontSize = 30, Foreground = B(orange) }, 180, 54));
        // blinking indicator blocks along the right edge
        for (int i = 0; i < 10; i++)
        {
            var blk = At(new Rectangle { Width = 46, Height = 22, RadiusX = 11, RadiusY = 11, Fill = B(new[] { orange, blue, mauve, tan, sky }[i % 5]) }, W - 66, 160 + i * 52);
            c.Children.Add(blk);
            Anim(sb, blk, "Opacity", 1, 0.15, 0.6 + rng.NextDouble() * 1.8, rng.NextDouble() * 2);
        }
        // a slow scanning sweep line
        var sweep = At(new Rectangle { Width = 1300, Height = 2, Fill = B(0x55FF9900) }, 170, 70);
        var st = Xf(sweep);
        c.Children.Add(sweep);
        Anim(sb, st, "TranslateY", 0, 760, 6, ease: false);
        // starfield
        for (int i = 0; i < 70; i++)
        {
            var s = rng.NextDouble() * 2.5 + 1;
            var star = At(new Ellipse { Width = s, Height = s, Fill = B(0xFFFFFFFF), Opacity = 0.3 + rng.NextDouble() * 0.6 }, 170 + rng.NextDouble() * 1330, 60 + rng.NextDouble() * 780);
            c.Children.Add(star);
            if (i % 4 == 0) Anim(sb, star, "Opacity", 0.9, 0.15, 1 + rng.NextDouble() * 3, rng.NextDouble() * 3);
        }
        return new Scene(root, sb);
    }
}
