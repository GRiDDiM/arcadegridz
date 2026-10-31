using System.Text.Json;
using ArcadeGridz.App.Models;
using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ArcadeGridz.App.Runtimes;

public sealed class WebGameRuntime(WebView2 view) : IGameRuntime
{
    bool ready;
    bool consoleMode;
    public event EventHandler? Exited;
    public event Action<string>? Status;

    public bool CanRun(GameEntry game) =>
        game.Runtime.Type.Equals("web", StringComparison.OrdinalIgnoreCase) &&
        Uri.TryCreate(game.Runtime.Url, UriKind.Absolute, out _);

    async Task EnsureAsync()
    {
        if (ready) return;
        AppPaths.Ensure();
        try
        {
            string browserVersion;
            try { browserVersion = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Exception ex) { browserVersion = $"unavailable ({ex.Message})"; }
            StartupLog.Write("webview", $"Initializing; browser={browserVersion}; profile={AppPaths.WebView}");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, AppPaths.WebView, null);
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.Settings.AreDevToolsEnabled = false;
            view.CoreWebView2.Settings.IsStatusBarEnabled = false;
            view.CoreWebView2Controller.IsInputEnabled = !consoleMode;
            view.CoreWebView2.WebMessageReceived += OnMessage;
            ready = true;
            StartupLog.Write("webview", "CoreWebView2 initialized");
        }
        catch (Exception ex)
        {
            StartupLog.Exception("WebView2 initialization", ex);
            throw;
        }
    }

    public async Task LaunchAsync(GameEntry game, CancellationToken ct = default)
    {
        await EnsureAsync();
        StartupLog.Write("webview", $"Navigate game={game.Id}; url={game.Runtime.Url}");
        view.Source = new Uri(game.Runtime.Url!);
    }

    public async Task NavigateAsync(string url)
    {
        await EnsureAsync();
        StartupLog.Write("webview", $"Navigate url={url}");
        view.Source = new Uri(url);
    }

    void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var type = document.RootElement.GetProperty("type").GetString() ?? "";
            Status?.Invoke(type);
            if (type == "exited") Exited?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StartupLog.Exception("WebView2 message", ex); }
    }

    public void SetConsoleMode(bool enabled)
    {
        consoleMode = enabled;
        if (ready)
            view.CoreWebView2Controller.IsInputEnabled = !enabled;
        StartupLog.Write("runtime", $"Web input enabled={!enabled}");
    }

    public async Task ExitAsync()
    {
        if (!ready) { Exited?.Invoke(this, EventArgs.Empty); return; }
        view.CoreWebView2.PostWebMessageAsJson("{\"type\":\"exit\"}");
        await Task.Delay(120);
        view.CoreWebView2.Navigate("about:blank");
        Exited?.Invoke(this, EventArgs.Empty);
    }
}
