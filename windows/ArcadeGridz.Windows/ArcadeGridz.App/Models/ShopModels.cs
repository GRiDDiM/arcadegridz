using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace ArcadeGridz.App.Models;

/// <summary>
/// SHOP manifest, served from arcadegridz.com/assets/shop/shop.json so items, prices and deals change without an app update.
/// "sparkz" = the Network's Sparkz Store (rewards currency); "deals" = third-party store deals (affiliate feeds later).
/// </summary>
public sealed class ShopManifest
{
    [JsonPropertyName("sparkzNote")] public string SparkzNote { get; set; } = "";
    [JsonPropertyName("dealsNote")] public string DealsNote { get; set; } = "";
    [JsonPropertyName("disclosure")] public string Disclosure { get; set; } = "";
    [JsonPropertyName("sparkzOpen")] public bool SparkzOpen { get; set; }
    [JsonPropertyName("sparkz")] public List<ShopItem> Sparkz { get; set; } = [];
    [JsonPropertyName("deals")] public List<ShopItem> Deals { get; set; } = [];
}

public sealed class ShopItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>Small kicker above the name: APP THEME, NAME COLOR, BUNDLE...</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>Sparkz price (Sparkz Store items).</summary>
    [JsonPropertyName("sparkz")] public long Sparkz { get; set; }
    /// <summary>Money price text for deals, e.g. "$9.99".</summary>
    [JsonPropertyName("price")] public string Price { get; set; } = "";
    [JsonPropertyName("was")] public string Was { get; set; } = "";
    [JsonPropertyName("discount")] public int Discount { get; set; }
    /// <summary>Third-party store name for deals (HUMBLE, FANATICAL, GREEN MAN GAMING...).</summary>
    [JsonPropertyName("store")] public string Store { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("art")] public string Art { get; set; } = "";
    /// <summary>Segoe Fluent icon code point (hex, e.g. "E790"), used when there's no art.</summary>
    [JsonPropertyName("glyph")] public string Glyph { get; set; } = "E719";
    [JsonPropertyName("accent")] public string Accent { get; set; } = "#5EE7FF";
    [JsonPropertyName("ends")] public string Ends { get; set; } = "";
    [JsonPropertyName("sample")] public bool Sample { get; set; }
    [JsonPropertyName("soon")] public bool Soon { get; set; }

    [JsonIgnore] public bool IsDeal { get; set; }

    [JsonIgnore] public string GlyphText => int.TryParse(Glyph, System.Globalization.NumberStyles.HexNumber, null, out var cp) && cp > 0 ? char.ConvertFromUtf32(cp) : "";
    [JsonIgnore] public Color AccentColor => ParseColor(Accent);
    [JsonIgnore] public Brush AccentBrush => new SolidColorBrush(AccentColor);
    [JsonIgnore] public Brush TileBrush
    {
        get
        {
            var c = AccentColor;
            var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x88, c.R, c.G, c.B), Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x22, c.R, c.G, c.B), Offset = 0.55 });
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xFF, 0x0B, 0x08, 0x20), Offset = 1 });
            return brush;
        }
    }
    ImageSource? art;
    [JsonIgnore] public ImageSource? ArtSource => art ??= Uri.TryCreate(Art, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? new BitmapImage(u) : null;
    [JsonIgnore] public Visibility ArtVisibility => ArtSource is null ? Visibility.Collapsed : Visibility.Visible;
    [JsonIgnore] public Visibility GlyphVisibility => ArtSource is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Big price on the card: "ϟ 2,500" for Sparkz items, "$9.99" for deals.</summary>
    [JsonIgnore] public string PriceText => Soon ? "SOON" : IsDeal ? Price : $"ϟ {Sparkz:N0}";
    [JsonIgnore] public string WasText => IsDeal && !string.IsNullOrWhiteSpace(Was) ? Was : "";
    [JsonIgnore] public Visibility WasVisibility => WasText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    [JsonIgnore] public string BadgeText => Soon ? "COMING SOON" : IsDeal ? (Discount > 0 ? $"-{Discount}%" : "DEAL") : "";
    [JsonIgnore] public Visibility BadgeVisibility => BadgeText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    [JsonIgnore] public string StoreText => IsDeal ? Store.ToUpperInvariant() : Kind.ToUpperInvariant();
    [JsonIgnore] public Visibility SampleVisibility => Sample ? Visibility.Visible : Visibility.Collapsed;
    [JsonIgnore] public double CardOpacity => Soon ? 0.6 : 1.0;

    static Color ParseColor(string hex)
    {
        var h = (hex ?? "").Trim().TrimStart('#');
        if (h.Length == 6 && uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return Color.FromArgb(255, 0x5E, 0xE7, 0xFF);
    }
}
