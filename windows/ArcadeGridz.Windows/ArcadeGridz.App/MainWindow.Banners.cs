using System.Text.Json;
using System.Text.Json.Serialization;
using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ArcadeGridz.App;

public sealed class BannerManifest
{
    [JsonPropertyName("rotateSeconds")] public int RotateSeconds { get; set; } = 120;
    [JsonPropertyName("banners")] public List<BannerEntry> Banners { get; set; } = [];
}
public sealed class BannerEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("image")] public string Image { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("alt")] public string Alt { get; set; } = "";
    /// <summary>Catalog id: the app shows that game's details (or plays it) instead of opening the browser.</summary>
    [JsonPropertyName("game")] public string Game { get; set; } = "";
}

/// <summary>
/// Banner slot between the game grid and the details panel: IAB wide skyscraper (160x600). The list lives on the site
/// (arcadegridz.com/assets/banners/banners.json), so banners and promoters change without an app update.
/// Rotates every rotateSeconds (default 2 min) with a crossfade; a click opens the link in the system browser
/// (never inside the app). Not a pad stop, so it never gets in the way of navigation.
/// </summary>
public sealed partial class MainWindow
{
    const string BannerManifestUrl = "https://arcadegridz.com/assets/banners/banners.json";
    static readonly HttpClient bannerHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    List<BannerEntry> banners = [];
    int bannerIndex = -1;
    bool bannerOnA = true;
    BannerEntry? bannerShown;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? bannerTimer;
    DateTime bannerManifestAt = DateTime.MinValue;

    async void InitBanners()
    {
        GameGrid.SizeChanged += (_, _) => CenterBanner();
        BannerSlot.Loaded += (_, _) => CenterBanner();
        bannerTimer = DispatcherQueue.CreateTimer();
        bannerTimer.Tick += (_, _) => NextBanner();
        await LoadBanners();
    }

    /// <summary>
    /// Center the banner in the empty band between the last tile column and the details panel. The banner's
    /// column keeps a fixed width (margins always add up to 10), so shifting it never changes the layout around it.
    /// </summary>
    void CenterBanner()
    {
        var w = GameGrid.ActualWidth;
        if (w <= 0) return;
        // right edge of the visible tiles: grid padding + whole 224px columns, minus the tile's own 10px margin
        var tilesRight = GameGrid.Padding.Left + GridColumns() * 224 - 10;
        var free = Math.Max(0, w - tilesRight); // empty space left inside the grid's column
        const double heroInset = 24;            // the details panel's own left margin
        // band = [tiles right edge, hero left]; banner column starts at w, its 160px box at w + left
        var bandLeft = tilesRight - w;          // relative to the banner column start (<= 0)
        var bandRight = 170 + heroInset;
        var left = Math.Round((bandLeft + bandRight) / 2 - 80);
        left = Math.Clamp(left, -free, 10);
        BannerSlot.Margin = new Thickness(left, 12, 10 - left, 12);
    }

    async Task LoadBanners()
    {
        try
        {
            var raw = await bannerHttp.GetStringAsync(BannerManifestUrl + "?t=" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
            var m = JsonSerializer.Deserialize<BannerManifest>(raw);
            bannerManifestAt = DateTime.UtcNow;
            var list = m?.Banners.Where(b => Uri.TryCreate(b.Image, UriKind.Absolute, out _)).ToList() ?? [];
            if (list.Count == 0) { BannerSlot.Visibility = Visibility.Collapsed; return; }
            // start somewhere random so every launch doesn't open on the same banner
            var first = banners.Count == 0;
            banners = list;
            bannerTimer!.Interval = TimeSpan.FromSeconds(Math.Clamp(m!.RotateSeconds, 15, 3600));
            if (first) bannerIndex = Random.Shared.Next(banners.Count) - 1;
            BannerSlot.Visibility = Visibility.Visible;
            NextBanner();
            bannerTimer.Start();
            StartupLog.Write("banners", $"{banners.Count} banners, rotate {bannerTimer.Interval.TotalSeconds:0}s");
        }
        catch (Exception ex)
        {
            StartupLog.Write("banners", $"Banner list unavailable: {ex.GetType().Name}: {ex.Message}");
            if (banners.Count == 0) BannerSlot.Visibility = Visibility.Collapsed;
        }
    }

    void NextBanner()
    {
        if (banners.Count == 0) return;
        // pick up new/removed banners about every 30 minutes
        if (DateTime.UtcNow - bannerManifestAt > TimeSpan.FromMinutes(30)) { bannerManifestAt = DateTime.UtcNow; _ = LoadBanners(); return; }
        if (gameOpen) return; // nothing to see; rotate when the launcher is back
        bannerIndex = (bannerIndex + 1) % banners.Count;
        var b = banners[bannerIndex];
        if (banners.Count == 1 && bannerShown == b) return;
        bannerShown = b;
        var incoming = bannerOnA ? BannerImageB : BannerImageA;
        var outgoing = bannerOnA ? BannerImageA : BannerImageB;
        bannerOnA = !bannerOnA;
        try { incoming.Source = new BitmapImage(new Uri(b.Image)); } catch { return; }
        var inApp = !string.IsNullOrEmpty(b.Game) && all.Any(g => g.Id == b.Game);
        ToolTipService.SetToolTip(BannerHit, inApp ? $"{b.Alt} (click to check it out)"
            : string.IsNullOrWhiteSpace(b.Alt) ? "Opens in your web browser" : $"{b.Alt} (opens in your web browser)");
        var sb = new Storyboard();
        foreach (var (target, to) in new[] { (incoming, 1.0), (outgoing, 0.0) })
        {
            var a = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(700) };
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, "Opacity");
            sb.Children.Add(a);
        }
        sb.Begin();
    }

    async void Banner_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (bannerShown is not null && !string.IsNullOrEmpty(bannerShown.Game) && all.FirstOrDefault(g => g.Id == bannerShown.Game) is { } game)
        {
            StartupLog.Write("banners", $"Opened game {game.Id} from banner");
            audio?.Play("select");
            await ShowGameFromBanner(game);
            return;
        }
        if (bannerShown is null || !Uri.TryCreate(bannerShown.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        StartupLog.Write("banners", $"Opened {bannerShown.Id} -> {uri}");
        audio?.Play("select");
        try { await Windows.System.Launcher.LaunchUriAsync(uri); } catch (Exception ex) { StartupLog.Exception("Banner link", ex); }
    }

    /// <summary>Banner click: show the game's tile + details (switching to ALL GAMES, and to betas/alphas if it's
    /// hidden by LIVE ONLY); if it still isn't listed, just play it.</summary>
    async Task ShowGameFromBanner(GameEntry game)
    {
        if (Launcher.Visibility != Visibility.Visible) ReturnToLauncher();
        if (!visible.Contains(game))
        {
            storyMode = IsStoryboard(game);
            albumMode = false;
            if (!storyMode) categoryIndex = 0; // ALL GAMES
            if (!Listed(game) && !settings.ShowTesting) Testing_Click(this, new RoutedEventArgs());
            ApplyFilter();
        }
        if (visible.Contains(game))
        {
            GameGrid.SelectedItem = game;
            GameGrid.ScrollIntoView(game);
            SetLauncherFocus(LauncherFocus.Play);
        }
        else await Launch(game);
    }
}
