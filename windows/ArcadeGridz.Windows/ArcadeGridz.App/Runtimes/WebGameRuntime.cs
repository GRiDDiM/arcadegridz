using System.Text.Json;
using ArcadeGridz.App.Models;
using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
namespace ArcadeGridz.App.Runtimes;
public sealed class WebGameRuntime(WebView2 view) : IGameRuntime
{
    bool ready;public event EventHandler? Exited;public event Action<string>? Status;
    public bool CanRun(GameEntry game)=>game.Runtime.Type.Equals("web",StringComparison.OrdinalIgnoreCase)&&Uri.TryCreate(game.Runtime.Url,UriKind.Absolute,out _);
    async Task EnsureAsync(){if(ready)return;AppPaths.Ensure();var env=await CoreWebView2Environment.CreateWithOptionsAsync(null,AppPaths.WebView,null);await view.EnsureCoreWebView2Async(env);view.CoreWebView2.Settings.AreDevToolsEnabled=false;view.CoreWebView2.Settings.IsStatusBarEnabled=false;view.CoreWebView2.WebMessageReceived+=OnMessage;ready=true;}
    public async Task LaunchAsync(GameEntry game,CancellationToken ct=default){await EnsureAsync();view.Source=new Uri(game.Runtime.Url!);}
    public async Task NavigateAsync(string url){await EnsureAsync();view.Source=new Uri(url);}
    void OnMessage(CoreWebView2 sender,CoreWebView2WebMessageReceivedEventArgs e){try{using var d=JsonDocument.Parse(e.WebMessageAsJson);var type=d.RootElement.GetProperty("type").GetString()??"";Status?.Invoke(type);if(type=="exited")Exited?.Invoke(this,EventArgs.Empty);}catch{}}
    public async Task ExitAsync(){if(!ready){Exited?.Invoke(this,EventArgs.Empty);return;}view.CoreWebView2.PostWebMessageAsJson("{\"type\":\"exit\"}");await Task.Delay(120);view.CoreWebView2.Navigate("about:blank");Exited?.Invoke(this,EventArgs.Empty);}
}
