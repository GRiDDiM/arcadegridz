using System.Text.Json;
using ArcadeGridz.App.Models;
using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace ArcadeGridz.App;

/// <summary>
/// Autoplay trailers (OPTIONS &gt; GAMES, on by default): highlighting a game that has a trailer plays it,
/// muted and looping, in the details panel where the cover art goes. The trailer is the game's own engine
/// (play URL + trailer=1&amp;preview=1; the site's app host restarts it when it ends), in a second, input-free
/// WebView2 that never takes focus and is torn down while a game or the options panel is up.
/// </summary>
public sealed partial class MainWindow
{
    WebView2? previewView;
    bool previewReady;
    string? previewGameId;      // game currently loaded (or loading) in the preview
    CancellationTokenSource? previewQueue;

    const string PreviewScript = """
        (() => {
          try { window.__AG_WINAPP__ = true; document.documentElement.classList.add('ag-winapp'); } catch (e) {}
          // the launcher owns the controller: the preview must never react to pad input
          try { Object.defineProperty(navigator, 'getGamepads', { value: () => [], configurable: true }); } catch (e) {}
        })();
        """;

    bool TrailerPreviewAllowed(GameEntry? g) =>
        g is not null && !settings.NoTrailerPreview && HasTrailer(g) && !gameOpen &&
        GameLayer.Visibility != Visibility.Visible && ScoresLayer.Visibility != Visibility.Visible && Panel.Visibility != Visibility.Visible && Launcher.Visibility == Visibility.Visible;

    void InitTrailerPreview()
    {
        // follow whatever covers the launcher (a game, sign-in, options/scores panels): stop under it, resume after
        void Changed(DependencyObject _, DependencyProperty __) => DispatcherQueue.TryEnqueue(() =>
        {
            if (TrailerPreviewAllowed(GameGrid.SelectedItem as GameEntry)) ResumeTrailerPreview();
            else StopTrailerPreview();
        });
        ThemeService.Exclude(HeroPreviewHost); // the trailer is the game's own picture; themes leave it alone
        Panel.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, Changed);
        Launcher.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, Changed);
        GameLayer.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, Changed);
        ScoresLayer.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, Changed);
    }

    /// <summary>Called whenever the highlighted game changes: show its art now, start its trailer after a short pause.</summary>
    void QueueTrailerPreview(GameEntry g)
    {
        previewQueue?.Cancel();
        if (previewGameId == g.Id && TrailerPreviewAllowed(g)) return; // already playing this one
        HidePreview(unload: true);
        if (!TrailerPreviewAllowed(g)) return;
        var cts = previewQueue = new CancellationTokenSource();
        _ = StartPreviewAfterDelay(g, cts.Token);
    }

    async Task StartPreviewAfterDelay(GameEntry g, CancellationToken ct)
    {
        try
        {
            await Task.Delay(700, ct); // scrolling through the grid never starts a pile of trailers
            if (ct.IsCancellationRequested || !ReferenceEquals(GameGrid.SelectedItem, g) || !TrailerPreviewAllowed(g)) return;
            var view = await EnsurePreviewView();
            if (ct.IsCancellationRequested || view is null) return;
            var url = g.Runtime.Url!;
            url += (url.Contains('?') ? "&" : "?") + "gfx=" + Uri.EscapeDataString(settings.Quality) + "&trailer=1&preview=1";
            previewGameId = g.Id;
            StartupLog.Write("preview", $"Trailer preview {g.Id}");
            view.CoreWebView2.Navigate(url);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StartupLog.Exception("Trailer preview", ex); HidePreview(unload: true); }
    }

    async Task<WebView2?> EnsurePreviewView()
    {
        if (previewView is not null && previewReady) return previewView;
        if (previewView is null)
        {
            previewView = new WebView2
            {
                IsTabStop = false, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center
            };
            // never let the preview hold keyboard/pad focus
            previewView.GotFocus += (_, _) => SetLauncherFocus(launcherFocus);
            HeroPreviewHost.Children.Add(previewView);
            HeroPreviewHost.SizeChanged += (_, _) => FitPreview();
        }
        AppPaths.Ensure();
        // same profile + same browser arguments as the game view (WebView2 requires matching options per folder)
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = settings.DisableGpu ? "--disable-gpu" : "" };
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, AppPaths.WebView, options);
        await previewView.EnsureCoreWebView2Async(environment);
        var core = previewView.CoreWebView2;
        core.IsMuted = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(PreviewScript);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !(uri.Host.EndsWith("arcadegridz.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("griddim.com", StringComparison.OrdinalIgnoreCase)))
                e.Cancel = true;
        };
        core.WebMessageReceived += (_, e) =>
        {
            string type;
            try { using var doc = JsonDocument.Parse(e.WebMessageAsJson); type = doc.RootElement.GetProperty("type").GetString() ?? ""; }
            catch { return; }
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (type == "started" && previewGameId is not null)
                {
                    await Task.Delay(350); // let the first trailer frame paint before covering the art
                    if (previewGameId is not null && TrailerPreviewAllowed(GameGrid.SelectedItem as GameEntry))
                    {
                        FitPreview();
                        HeroPreviewHost.Visibility = Visibility.Visible;
                    }
                }
                else if (type is "error" or "exited") HidePreview(unload: true);
            });
        };
        previewReady = true;
        StartupLog.Write("preview", "Preview WebView2 ready (muted)");
        return previewView;
    }

    /// <summary>Fill the art box like the cover image does: a 16:9 view as wide as the box, cropped top and bottom.</summary>
    void FitPreview()
    {
        if (previewView is null) return;
        var w = HeroPreviewHost.ActualWidth;
        var h = HeroPreviewHost.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var vh = Math.Max(h, w * 9 / 16);
        var vw = Math.Max(w, h * 16 / 9);
        previewView.Width = vw;
        previewView.Height = vh;
        HeroPreviewHost.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, w, h) };
    }

    void HidePreview(bool unload)
    {
        HeroPreviewHost.Visibility = Visibility.Collapsed;
        if (!unload) return;
        previewGameId = null;
        try { if (previewReady) previewView?.CoreWebView2?.Navigate("about:blank"); } catch { }
    }

    /// <summary>A game, sign-in or the options panel is taking over: stop the trailer and free the engine.</summary>
    void StopTrailerPreview()
    {
        previewQueue?.Cancel();
        HidePreview(unload: true);
    }

    void ResumeTrailerPreview()
    {
        if (GameGrid.SelectedItem is GameEntry g) QueueTrailerPreview(g);
    }

    void TrailerPreviewToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.NoTrailerPreview = !TrailerPreviewToggle.IsOn;
        settings.Save();
        if (settings.NoTrailerPreview) StopTrailerPreview();
    }
}
