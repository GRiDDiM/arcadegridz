using System.Net.Http.Headers;
using System.Text.Json;
using ArcadeGridz.App.Models;
using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ArcadeGridz.App;

/// <summary>
/// SHOP (native, never the website): two tabs.
///  SPARKZ STORE — Network items bought with Sparkz (app themes, name colors, titles, frames; game cosmetics later).
///  DEALS        — third-party game deals (Humble / Fanatical / Green Man Gaming feeds later); BUY opens the system browser.
/// Items live in arcadegridz.com/assets/shop/shop.json. Purchasing isn't wired yet: the Sparkz Store runs as a preview
/// and sample deals are marked SAMPLE. The header Sparkz chip shows the Network balance (griddim.com/account/sparkz).
/// Pad: LB/RB switch tabs, arrows browse, A buys, B goes back.
/// </summary>
public sealed partial class MainWindow
{
    const string ShopManifestUrl = "https://arcadegridz.com/assets/shop/shop.json";
    const string SparkzBalanceUrl = "https://griddim.com/account/sparkz";
    static readonly HttpClient shopHttp = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly JsonSerializerOptions ShopJson = new() { PropertyNameCaseInsensitive = true };
    ShopManifest? shop;
    DateTime shopLoadedAt = DateTime.MinValue;
    bool shopDeals;
    long? sparkzBalance;

    // ---------- Sparkz balance (header chip + shop) ----------

    async void RefreshSparkz()
    {
        if (account is null || web?.Token is not { Length: > 0 } token) { SetSparkz(null); return; }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SparkzBalanceUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            using var response = await shopHttp.SendAsync(request);
            if (!response.IsSuccessStatusCode) { StartupLog.Write("sparkz", $"HTTP {(int)response.StatusCode}"); return; }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("balance", out var b) && b.TryGetInt64(out var balance) && account is not null)
                SetSparkz(balance);
        }
        catch (Exception ex) { StartupLog.Exception("Sparkz balance", ex); }
    }

    void SetSparkz(long? balance)
    {
        sparkzBalance = balance;
        var signedIn = account is not null;
        SparkzChip.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        SparkzChipText.Text = balance is { } v ? v.ToString("N0") : "—";
        ShopBalance.Text = !signedIn ? "SIGN IN" : balance is { } s ? s.ToString("N0") : "—";
        if (ShopLayer.Visibility == Visibility.Visible) ShowShopItem();
    }

    void SparkzChip_Click(object sender, RoutedEventArgs e) => _ = OpenShop(deals: false);

    // ---------- open / close ----------

    void Shop_Click(object sender, RoutedEventArgs e) => _ = OpenShop(shopDeals);

    async Task OpenShop(bool deals)
    {
        audio?.Play("open");
        Panel.Visibility = Visibility.Collapsed;
        Launcher.Visibility = Visibility.Collapsed;
        ScoresLayer.Visibility = Visibility.Collapsed;
        ShopLayer.Visibility = Visibility.Visible;
        SetSparkz(sparkzBalance);
        RefreshSparkz();
        shopDeals = deals;
        if (shop is null || DateTime.UtcNow - shopLoadedAt > TimeSpan.FromMinutes(10))
        {
            ShopNote.Text = "LOADING THE SHOP...";
            ShopGrid.ItemsSource = null;
            ShowShopItem();
            try
            {
                var json = await shopHttp.GetStringAsync($"{ShopManifestUrl}?t={DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute}");
                shop = JsonSerializer.Deserialize<ShopManifest>(json, ShopJson) ?? new();
                foreach (var d in shop.Deals) d.IsDeal = true;
                shopLoadedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                StartupLog.Exception("Shop manifest", ex);
                if (shop is null)
                {
                    ShopNote.Text = "THE SHOP IS OFFLINE RIGHT NOW. TRY AGAIN IN A MOMENT.";
                    return;
                }
            }
            if (ShopLayer.Visibility != Visibility.Visible) return;
        }
        ShowShopTab(deals);
        PadFocus(ShopGrid);
    }

    void CloseShop()
    {
        if (ShopLayer.Visibility != Visibility.Visible) return;
        ShopLayer.Visibility = Visibility.Collapsed;
        ReturnToLauncher();
        SetLauncherFocus(LauncherFocus.Shop);
    }

    void ShopBack_Click(object sender, RoutedEventArgs e) { audio?.Play("back"); CloseShop(); }
    void ShopTabSparkz_Click(object sender, RoutedEventArgs e) { audio?.Play("move"); ShowShopTab(false); }
    void ShopTabDeals_Click(object sender, RoutedEventArgs e) { audio?.Play("move"); ShowShopTab(true); }

    void ShowShopTab(bool deals)
    {
        shopDeals = deals;
        var on = (Brush)Application.Current.Resources["AgMagenta"];
        if (deals) { ShopTabDeals.BorderBrush = on; ShopTabSparkz.ClearValue(Control.BorderBrushProperty); }
        else { ShopTabSparkz.BorderBrush = on; ShopTabDeals.ClearValue(Control.BorderBrushProperty); }
        var items = (deals ? shop?.Deals : shop?.Sparkz) ?? [];
        ShopNote.Text = (deals ? shop?.DealsNote : shop?.SparkzNote) ?? "";
        ShopNote.Visibility = string.IsNullOrWhiteSpace(ShopNote.Text) ? Visibility.Collapsed : Visibility.Visible;
        ShopDisclosure.Text = deals ? shop?.Disclosure ?? "" : "Sparkz are earned across the GRiDDiM Network (daily rewards, beta invites, events). They can't be bought or traded.";
        ShopGrid.ItemsSource = items;
        ShopGrid.SelectedIndex = items.Count > 0 ? 0 : -1;
        if (ShopGrid.SelectedItem is { } first) ShopGrid.ScrollIntoView(first);
        ShowShopItem();
    }

    // ---------- selection + detail ----------

    void ShopGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowShopItem();

    void ShopGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ShopItem item && !ReferenceEquals(ShopGrid.SelectedItem, item)) audio?.Play("move");
    }

    void ShowShopItem()
    {
        ShopStatus.Text = "";
        if (ShopGrid.SelectedItem is not ShopItem item)
        {
            ShopDetailTile.Background = null;
            ShopDetailGlyph.Glyph = "";
            ShopDetailArt.Source = null;
            ShopDetailBadge.Visibility = Visibility.Collapsed;
            ShopDetailKind.Text = ShopDetailName.Text = ShopDetailDesc.Text = ShopDetailPrice.Text = ShopDetailWas.Text = ShopDetailEnds.Text = "";
            ShopBuyButton.IsEnabled = false;
            ShopBuyLabel.Text = "BUY";
            return;
        }
        ShopDetailTile.Background = item.TileBrush;
        ShopDetailGlyph.Glyph = item.ArtSource is null ? item.GlyphText : "";
        ShopDetailGlyph.Foreground = item.AccentBrush;
        ShopDetailArt.Source = item.ArtSource;
        ShopDetailBadge.Visibility = item.BadgeVisibility;
        ShopDetailBadgeText.Text = item.BadgeText;
        ShopDetailKind.Text = item.IsDeal ? $"{item.Store.ToUpperInvariant()}  ·  {item.Kind.ToUpperInvariant()}" : item.Kind.ToUpperInvariant();
        ShopDetailKind.Foreground = item.AccentBrush;
        ShopDetailName.Text = item.Name;
        ShopDetailDesc.Text = item.Description;
        ShopDetailPrice.Text = item.PriceText;
        ShopDetailWas.Text = item.WasText;
        ShopDetailEnds.Text = item.Sample ? "SAMPLE LISTING  ·  REAL DEALS ARRIVE WITH THE PARTNER FEEDS"
            : string.IsNullOrWhiteSpace(item.Ends) ? "" : $"ENDS {item.Ends.ToUpperInvariant()}";

        if (item.Soon) { ShopBuyButton.IsEnabled = false; ShopBuyLabel.Text = "COMING SOON"; ShopBuyIcon.Glyph = ""; }
        else if (item.IsDeal)
        {
            ShopBuyButton.IsEnabled = true;
            ShopBuyLabel.Text = item.Sample ? "PREVIEW ONLY" : $"GET IT ON {item.Store.ToUpperInvariant()}";
            ShopBuyIcon.Glyph = ""; // open in new window
        }
        else
        {
            ShopBuyButton.IsEnabled = true;
            ShopBuyIcon.Glyph = "";
            ShopBuyLabel.Text = account is null ? "SIGN IN TO BUY" : $"BUY  ·  ϟ {item.Sparkz:N0}";
        }
        ShopAHint.Text = item.IsDeal ? "OPEN DEAL" : "BUY";
    }

    // ---------- buy ----------

    void ShopBuy_Click(object sender, RoutedEventArgs e) => ShopBuy();

    async void ShopBuy()
    {
        if (ShopGrid.SelectedItem is not ShopItem item || item.Soon) { audio?.Play("error"); return; }
        if (item.IsDeal)
        {
            if (item.Sample || !Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                audio?.Play("error");
                ShopStatus.Text = "THIS IS A SAMPLE DEAL. LIVE DEALS OPEN THE STORE IN YOUR BROWSER.";
                return;
            }
            audio?.Play("select");
            ShopStatus.Text = $"OPENING {item.Store.ToUpperInvariant()} IN YOUR BROWSER...";
            try { await Windows.System.Launcher.LaunchUriAsync(uri); } catch (Exception ex) { StartupLog.Exception("Shop deal link", ex); }
            return;
        }
        if (account is null) { audio?.Play("select"); CloseShop(); SignIn_Click(this, new RoutedEventArgs()); return; }
        if (sparkzBalance is { } bal && bal < item.Sparkz)
        {
            audio?.Play("error");
            ShopStatus.Text = $"YOU NEED ϟ {item.Sparkz - bal:N0} MORE SPARKZ.";
            return;
        }
        if (shop?.SparkzOpen != true)
        {
            audio?.Play("error");
            ShopStatus.Text = sparkzBalance is { } have
                ? $"PREVIEW: PURCHASES OPEN SOON. YOU'D HAVE ϟ {have - item.Sparkz:N0} LEFT."
                : "PREVIEW: PURCHASES OPEN SOON.";
            return;
        }
        // purchase endpoint arrives with the Network store; until then sparkzOpen stays false in shop.json
        ShopStatus.Text = "PURCHASES OPEN SOON.";
    }

    // ---------- pad ----------

    int ShopColumns() => Math.Max(1, (int)((ShopGrid.ActualWidth - 8) / 222));

    void HandleShopAction(PadAction action)
    {
        var n = ShopGrid.Items.Count;
        var i = ShopGrid.SelectedIndex;
        var cols = ShopColumns();
        switch (action)
        {
            case PadAction.B: CloseShop(); return;
            case PadAction.A: ShopBuy(); return;
            case PadAction.LB: if (shopDeals) ShowShopTab(false); return;
            case PadAction.RB: if (!shopDeals) ShowShopTab(true); return;
            case PadAction.Left: if (i % cols > 0) i--; break;
            case PadAction.Right: if (i % cols < cols - 1 && i < n - 1) i++; break;
            case PadAction.Up: if (i - cols >= 0) i -= cols; break;
            case PadAction.Down: if (i + cols < n) i += cols; else if (i / cols < (n - 1) / cols) i = n - 1; break;
            default: return;
        }
        if (n > 0 && i != ShopGrid.SelectedIndex)
        {
            ShopGrid.SelectedIndex = Math.Clamp(i, 0, n - 1);
            ShopGrid.ScrollIntoView(ShopGrid.SelectedItem);
        }
    }
}
