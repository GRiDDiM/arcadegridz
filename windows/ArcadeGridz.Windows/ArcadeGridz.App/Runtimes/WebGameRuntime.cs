using System.Text.Json;
using ArcadeGridz.App.Models;
using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ArcadeGridz.App.Runtimes;

/// <summary>The signed-in Network account as the site reports it (name, @username, avatar URL).</summary>
public sealed record NetworkAccount(string Id, string Name, string Username, string Avatar);

public sealed class WebGameRuntime(WebView2 view) : IGameRuntime
{
    TaskCompletionSource? signedOut;
    /// <summary>Raised with the account after sign-in / session check, or null after sign-out.</summary>
    public event Action<NetworkAccount?>? AccountChanged;
    bool ready;
    bool consoleMode;
    TaskCompletionSource? desktopReady;
    TaskCompletionSource? desktopAuthCompleted;
    TaskCompletionSource<bool>? sessionCheck;
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
            if (consoleMode) await InstallConsoleInputLockAsync();
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
            if (type is "hello" or "auth" && document.RootElement.TryGetProperty("account", out var acct) && acct.ValueKind == JsonValueKind.Object)
            {
                string Str(string name) => acct.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
                AccountChanged?.Invoke(new NetworkAccount(Str("id"), Str("name"), Str("username"), Str("avatar")));
            }
            else if (type == "signedout")
            {
                AccountChanged?.Invoke(null);
                signedOut?.TrySetResult();
            }
            Status?.Invoke(type);
            if (type == "hello")
            {
                var signedIn = document.RootElement.TryGetProperty("signedIn", out var signedInValue) &&
                    signedInValue.ValueKind == JsonValueKind.True;
                var hasAccount = document.RootElement.TryGetProperty("account", out var accountValue) &&
                    accountValue.ValueKind == JsonValueKind.Object;
                sessionCheck?.TrySetResult(signedIn && hasAccount);
            }
            else if (type == "desktop-ready") desktopReady?.TrySetResult();
            else if (type == "auth") desktopAuthCompleted?.TrySetResult();
            else if (type == "exited") Exited?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StartupLog.Exception("WebView2 message", ex); }
    }

    public async Task<bool> HasPersistedSessionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAsync();
        sessionCheck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            StartupLog.Write("auth", "Checking persistent WebView session");
            view.Source = new Uri("https://arcadegridz.com/?host=winapp&mode=session-check");
            var signedIn = await sessionCheck.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
            StartupLog.Write("auth", signedIn ? "Persistent session is valid" : "No valid persistent session");
            return signedIn;
        }
        catch (TimeoutException)
        {
            StartupLog.Write("auth", "Persistent session check timed out; showing welcome screen");
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StartupLog.Exception("Persistent session check", ex);
            return false;
        }
        finally
        {
            sessionCheck = null;
        }
    }

    public async Task ImportDesktopSessionAsync(string token, CancellationToken cancellationToken = default)
    {
        await EnsureAsync();
        desktopReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        desktopAuthCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.Source = new Uri("https://arcadegridz.com/?host=winapp&mode=desktop-complete");
        await desktopReady.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "desktop-auth", token }));
        await desktopAuthCompleted.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        desktopReady = null;
        desktopAuthCompleted = null;
        StartupLog.Write("auth", "Desktop session imported into persistent WebView profile");
    }

    const string ConsoleInputLockScript = """
        (() => {
          if (window.__arcadeGridzConsoleInputLock) return;
          window.__arcadeGridzConsoleInputLock = true;
          // only real keyboard/mouse input: synthetic key events from the site's gamepad mapper must reach the game
          const block = event => { if (!event.isTrusted) return; event.preventDefault(); event.stopImmediatePropagation(); };
          ['keydown','keyup','keypress','mousedown','mouseup','click','dblclick','contextmenu',
           'pointerdown','pointerup','pointermove','touchstart','touchend','wheel']
            .forEach(type => window.addEventListener(type, block, true));
        })();
        """;

    async Task InstallConsoleInputLockAsync()
    {
        try
        {
            await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ConsoleInputLockScript);
            await view.CoreWebView2.ExecuteScriptAsync(ConsoleInputLockScript);
            StartupLog.Write("runtime", "Console input lock installed in WebView");
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Install WebView console input lock", ex);
        }
    }

    public void SetConsoleMode(bool enabled)
    {
        consoleMode = enabled;
        if (ready && enabled) _ = InstallConsoleInputLockAsync();
        StartupLog.Write("runtime", $"Console mode={enabled}");
    }

    /// <summary>Signs this app's web profile out of the Network, without showing any page.</summary>
    public async Task SignOutAsync()
    {
        await EnsureAsync();
        signedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            StartupLog.Write("auth", "Signing out");
            view.Source = new Uri("https://arcadegridz.com/?host=winapp&mode=signout");
            await signedOut.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException) { StartupLog.Write("auth", "Sign-out confirmation timed out"); AccountChanged?.Invoke(null); }
        finally
        {
            signedOut = null;
            try { view.CoreWebView2.Navigate("about:blank"); } catch { }
        }
    }

    /// <summary>Opens the site's SYSTEM MENU over the running game (same as holding SELECT+START in the page).</summary>
    public void OpenSystemMenu()
    {
        if (!ready) return;
        try { _ = view.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('arcadegridz:system',{detail:{source:'app'}}))"); }
        catch (Exception ex) { StartupLog.Exception("Open system menu", ex); }
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
