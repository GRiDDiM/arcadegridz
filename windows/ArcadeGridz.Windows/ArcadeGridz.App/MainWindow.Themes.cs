using System.Diagnostics;
using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace ArcadeGridz.App;

/// <summary>OPTIONS &gt; THEMES: the theme manager (six launcher themes, applied live and saved).</summary>
public sealed partial class MainWindow
{
    const int ThemesTab = 2;
    Button[] themeCards = [];
    readonly Dictionary<Button, TextBlock> themeActiveTags = [];
    ThemeScenes.Scene? themeScene;
    bool themeSceneRunning;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? themeTimer;
    readonly Stopwatch themeSinceWalk = Stopwatch.StartNew();
    bool themeDirty;
    int themeWalkLogs;

    void InitThemes()
    {
        BuildThemeCards();
        // newly realized UI (game tiles, popups, code-built rows) is authored in neon: re-theme it as it appears
        Root.LayoutUpdated += (_, _) => themeDirty = true;
        CompositionTarget.Rendering += (_, _) =>
        {
            if (!themeDirty || themeSinceWalk.ElapsedMilliseconds < 120) return;
            ThemeWalk();
        };
        // safety net for state changes that don't re-layout (hover / focus brushes)
        themeTimer = DispatcherQueue.CreateTimer();
        themeTimer.Interval = TimeSpan.FromSeconds(1.5);
        themeTimer.Tick += (_, _) => { if (!ThemeService.Current.IsNeon) themeDirty = true; };
        themeTimer.Start();
        ApplyTheme(ThemeService.Get(settings.Theme), save: false);
    }

    void ThemeWalk()
    {
        themeDirty = false;
        themeSinceWalk.Restart();
        if (gameOpen) return; // the launcher is hidden; catch up when the game closes
        var sw = Stopwatch.StartNew();
        try { ThemeService.Apply(Root); }
        catch (Exception ex) { StartupLog.Exception("Theme walk", ex); }
        themeSinceWalk.Restart();
        if (themeWalkLogs < 3 && !ThemeService.Current.IsNeon)
        {
            themeWalkLogs++;
            StartupLog.Write("theme", $"walk {ThemeService.Current.Id} took {sw.ElapsedMilliseconds} ms");
        }
    }

    void ApplyTheme(AppTheme theme, bool save = true)
    {
        ThemeService.Set(theme);
        if (save) { settings.Theme = theme.Id; settings.Save(); }
        try
        {
            ThemeService.ApplyResources(Application.Current.Resources);
            ThemeService.ApplyResources(Root.Resources);
        }
        catch (Exception ex) { StartupLog.Exception("Theme resources", ex); }

        // backdrop: NEON keeps the arcade hall; every other theme draws its own scene
        StopThemeScene();
        ThemeBackdrop.Children.Clear();
        themeScene = null;
        NeonBackdrop.Visibility = theme.IsNeon ? Visibility.Visible : Visibility.Collapsed;
        if (!theme.IsNeon)
        {
            try
            {
                themeScene = ThemeScenes.Build(theme);
                if (themeScene is not null) ThemeBackdrop.Children.Add(themeScene.Root);
                // a wash of the theme's ink keeps the cards readable over busy art
                var wash = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(theme.Light ? (byte)0x40 : (byte)0x66, theme.Ink.R, theme.Ink.G, theme.Ink.B)),
                    IsHitTestVisible = false
                };
                ThemeService.Exclude(wash);
                ThemeBackdrop.Children.Add(wash);
            }
            catch (Exception ex) { StartupLog.Exception("Theme scene", ex); }
        }
        ApplyScanlines();
        ApplyCityLife();
        UpdateThemeCards();
        ThemeWalk();
        StartupLog.Write("theme", $"applied {theme.Id}");
    }

    void StopThemeScene()
    {
        try { themeScene?.Story.Stop(); } catch { }
        themeSceneRunning = false;
    }

    /// <summary>Runs or freezes the theme scene with ANIMATED BACKGROUND and while a game is up.</summary>
    void ApplyThemeSceneMotion()
    {
        if (themeScene is null) return;
        try
        {
            if (settings.StillBackground) { StopThemeScene(); return; }
            if (gameOpen) { if (themeSceneRunning) themeScene.Story.Pause(); return; }
            if (!themeSceneRunning) { themeScene.Story.Begin(); themeSceneRunning = true; }
            else themeScene.Story.Resume();
        }
        catch (Exception ex) { StartupLog.Exception("Theme scene motion", ex); }
    }

    void BuildThemeCards()
    {
        ThemeCards.Children.Clear();
        themeActiveTags.Clear();
        var cards = new List<Button>();
        for (int i = 0; i < ThemeService.All.Length; i++)
        {
            var t = ThemeService.All[i];
            var preview = new Grid { Height = 66, CornerRadius = new CornerRadius(10), Background = PreviewBackground(t), BorderBrush = new SolidColorBrush(t.Cyan), BorderThickness = new Thickness(t.IsNeon ? 2 : t.Light ? 1 : 2) };
            ThemeService.Exclude(preview);
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 8) };
            foreach (var c in t.Swatches.Skip(1))
                swatches.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(c), Stroke = new SolidColorBrush(Color.FromArgb(0x55, t.Text.R, t.Text.G, t.Text.B)), StrokeThickness = 1 });
            preview.Children.Add(swatches);
            preview.Children.Add(new TextBlock
            {
                Text = t.Emoji, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 26, IsColorFontEnabled = true,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 6)
            });
            preview.Children.Add(new TextBlock
            {
                Text = t.Name, FontFamily = new FontFamily(t.Display), FontSize = t.Id is "console" or "brutal" ? 26 : t.Id == "beige" ? 18 : 20,
                Foreground = t.IsNeon ? NeonPreviewGradient() : new SolidColorBrush(t.Id == "beige" ? t.Text : t.Cyan),
                Margin = new Thickness(12, 10, 10, 0), VerticalAlignment = VerticalAlignment.Top, TextTrimming = TextTrimming.CharacterEllipsis
            });

            var tagline = new TextBlock { Text = t.Tagline, FontFamily = (FontFamily)Application.Current.Resources["AgBody"], FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Height = 32, Margin = new Thickness(2, 6, 2, 0) };
            var active = new TextBlock { Text = "", Style = (Style)Application.Current.Resources["PixelLabel"], FontSize = 8, Margin = new Thickness(2, 4, 2, 0) };
            var body = new StackPanel();
            body.Children.Add(preview);
            body.Children.Add(tagline);
            body.Children.Add(active);
            var card = new Button
            {
                Tag = t.Id, Content = body, Padding = new Thickness(8), Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top
            };
            ToolTipService.SetToolTip(card, $"{t.Name}: {t.Tagline}");
            card.Click += ThemeCard_Click;
            themeActiveTags[card] = active;
            Grid.SetColumn(card, i % 3);
            Grid.SetRow(card, i / 3);
            ThemeCards.Children.Add(card);
            cards.Add(card);
        }
        themeCards = [.. cards];
    }

    static Brush NeonPreviewGradient()
    {
        var g = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0) };
        g.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 0x5E, 0xE7, 0xFF), Offset = 0 });
        g.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 0x9F, 0x7C, 0xFF), Offset = 0.5 });
        g.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 0xFF, 0x6F, 0xB5), Offset = 1 });
        return g;
    }

    static Brush PreviewBackground(AppTheme t)
    {
        Color Top() => t.Id switch
        {
            "happy" => Color.FromArgb(255, 0x6C, 0xC8, 0xFF),
            "acid" => Color.FromArgb(255, 0x06, 0x1A, 0x06),
            "brutal" => Color.FromArgb(255, 0x00, 0x00, 0x00),
            _ => t.Ink
        };
        Color Bottom() => t.Id switch
        {
            "happy" => Color.FromArgb(255, 0xFF, 0xF4, 0xD2),
            "acid" => Color.FromArgb(255, 0x0B, 0x2A, 0x08),
            "brutal" => Color.FromArgb(255, 0x3A, 0x00, 0x00),
            "neon" => Color.FromArgb(255, 0x1A, 0x12, 0x38),
            _ => t.Card
        };
        var g = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        g.GradientStops.Add(new GradientStop { Color = Top(), Offset = 0 });
        g.GradientStops.Add(new GradientStop { Color = Bottom(), Offset = 1 });
        return g;
    }

    void UpdateThemeCards()
    {
        var current = ThemeService.Current;
        var pink = (Brush)Application.Current.Resources["AgMagenta"];
        foreach (var card in themeCards)
        {
            var on = (card.Tag as string) == current.Id;
            if (on) card.BorderBrush = pink; else card.ClearValue(Control.BorderBrushProperty);
            if (themeActiveTags.TryGetValue(card, out var tag))
            {
                // only the active card carries a status line, so the grid stays short enough for 720p screens
                tag.Text = on ? "✓ ACTIVE THEME" : "";
                tag.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        ThemeStatus.Text = $"ACTIVE: {current.Name}  ·  SAVED FOR NEXT TIME";
    }

    void ThemeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } card) return;
        SelectTheme(id);
        var i = Array.IndexOf(themeCards, card);
        if (i >= 0) panelFocus = 1 + i;
    }

    void SelectTheme(string id)
    {
        var theme = ThemeService.Get(id);
        if (theme == ThemeService.Current) { audio?.Play("select"); return; }
        ApplyTheme(theme);
        audio?.Play("launch");
    }

    /// <summary>Pad/keys on the theme grid: d-pad moves in 2D (3 columns), A applies.</summary>
    bool HandleThemeGrid(PadAction action, Control current, List<Control> controls)
    {
        var i = current is Button b ? Array.IndexOf(themeCards, b) : -1;
        if (i < 0) return false;
        if (action == PadAction.A) { SelectTheme((string)themeCards[i].Tag); return true; }
        int next = action switch
        {
            PadAction.Left => i % 3 == 0 ? i : i - 1,
            PadAction.Right => i % 3 == 2 || i + 1 >= themeCards.Length ? i : i + 1,
            PadAction.Up => i - 3,
            PadAction.Down => i + 3,
            _ => int.MinValue
        };
        if (next == int.MinValue) return false;
        if (next < 0) panelFocus = 0; // up to the tab row
        else if (next >= themeCards.Length) panelFocus = controls.Count - 1; // down to BACK
        else panelFocus = controls.IndexOf(themeCards[next]);
        FocusPanelControl(controls[Math.Clamp(panelFocus, 0, controls.Count - 1)]);
        audio?.Play("move");
        return true;
    }
}
