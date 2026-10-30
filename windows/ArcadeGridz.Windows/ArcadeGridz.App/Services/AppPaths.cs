namespace ArcadeGridz.App.Services;
public static class AppPaths
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcadeGridz");
    public static string Cache => Path.Combine(Root,"cache");
    public static string Art => Path.Combine(Cache,"art");
    public static string WebView => Path.Combine(Root,"WebView2");
    public static string Games => Path.Combine(Root,"games");
    public static void Ensure() { Directory.CreateDirectory(Cache); Directory.CreateDirectory(Art); Directory.CreateDirectory(WebView); Directory.CreateDirectory(Games); }
}
