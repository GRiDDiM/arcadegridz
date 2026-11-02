using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ArcadeGridz.App;

/// <summary>x:Bind helper functions for the neon launcher theme.</summary>
public static class UiTheme
{
    static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));

    public static Brush StatusBrush(string? status) => (status ?? "").ToLowerInvariant() switch
    {
        "beta" => Brush(0x5E, 0xE7, 0xFF),
        "alpha" => Brush(0xFF, 0xB0, 0x70),
        "release" or "live" or "stable" => Brush(0x3A, 0xFF, 0x9A),
        _ => Brush(0x9F, 0x7C, 0xFF)
    };

    public static Brush TagBrush(string? tag) => (tag ?? "").ToUpperInvariant() switch
    {
        "VERIFIED" => Brush(0x3A, 0xFF, 0x9A),
        "ASSISTED" => Brush(0xFF, 0xB0, 0x70),
        "CHEATER" => Brush(0xFF, 0x4D, 0x6D),
        _ => Brush(0x8D, 0x95, 0xAD)
    };

    public static string Upper(string? value) => (value ?? "").ToUpperInvariant();
    public static double PlayableOpacity(bool playable) => playable ? 1.0 : 0.55;
    public static Visibility LockedVisibility(bool playable) => playable ? Visibility.Collapsed : Visibility.Visible;
}
