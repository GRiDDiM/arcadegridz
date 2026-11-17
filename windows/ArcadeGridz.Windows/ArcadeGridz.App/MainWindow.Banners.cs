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
        bannerTimer = DispatcherQueue.CreateTimer();
        bannerTimer.Tick += (_, _) => NextBanner();
        await LoadBanners();
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
        ToolTipService.SetToolTip(BannerHit, string.IsNullOrWhiteSpace(b.Alt) ? "Opens in your web browser" : $"{b.Alt} (opens in your web browser)");
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
        if (bannerShown is null || !Uri.TryCreate(bannerShown.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        StartupLog.Write("banners", $"Opened {bannerShown.Id} -> {uri}");
        audio?.Play("select");
        try { await Windows.System.Launcher.LaunchUriAsync(uri); } catch (Exception ex) { StartupLog.Exception("Banner link", ex); }
    }
}
