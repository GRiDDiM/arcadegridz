using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ArcadeGridz.App;

/// <summary>
/// Makes it obvious when pad/keyboard focus moves into the details panel (A on a game tile):
/// the panel lights up with a cyan frame + control hint, the game grid dims back, and the focused
/// button (PLAY / TRAILER) pulses. All opacity/scale animation, so it stays on the compositor.
/// </summary>
public sealed partial class MainWindow
{
    bool detailsFocused;
    Storyboard? detailsFade, buttonPulse;

    void UpdateDetailsFocus(bool on)
    {
        if (on) PulseFocusedDetailsButton();
        if (on == detailsFocused) return;
        detailsFocused = on;
        try
        {
            detailsFade?.Stop();
            var sb = new Storyboard();
            void Fade(DependencyObject target, double to)
            {
                var a = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(160), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(a, target);
                Storyboard.SetTargetProperty(a, "Opacity");
                sb.Children.Add(a);
            }
            Fade(DetailsFrame, on ? 1 : 0);
            Fade(GameGrid, on ? 0.45 : 1);
            detailsFade = sb;
            sb.Begin();
            if (!on) StopButtonPulse();
        }
        catch (Exception ex) { Services.StartupLog.Exception("Details focus", ex); }
    }

    void PulseFocusedDetailsButton()
    {
        StopButtonPulse();
        var button = launcherFocus == LauncherFocus.Trailer ? TrailerButton : PlayButton;
        if (button.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform();
            button.RenderTransform = scale;
            button.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }
        var sb = new Storyboard();
        foreach (var prop in new[] { "ScaleX", "ScaleY" })
        {
            var a = new DoubleAnimation
            {
                From = 1, To = 1.07, Duration = TimeSpan.FromMilliseconds(520), AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(a, scale);
            Storyboard.SetTargetProperty(a, prop);
            sb.Children.Add(a);
        }
        buttonPulse = sb;
        sb.Begin();
    }

    void StopButtonPulse()
    {
        try { buttonPulse?.Stop(); } catch { }
        buttonPulse = null;
        foreach (var b in new[] { PlayButton, TrailerButton })
            if (b.RenderTransform is ScaleTransform s) { s.ScaleX = 1; s.ScaleY = 1; }
    }
}
