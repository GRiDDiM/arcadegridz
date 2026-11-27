using ArcadeGridz.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace ArcadeGridz.App;

public static class Program
{
    /// <summary>Mouse position at process start (physical pixels); the screen the user launched from.</summary>
    public static (int X, int Y)? LaunchPoint;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct POINT { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);

    [STAThread]
    public static void Main(string[] args)
    {
        StartupLog.Initialize();
        try
        {
            StartupLog.Write("main", "Initializing COM wrappers");
            ComWrappersSupport.InitializeComWrappers();
            // where the mouse is right now = the screen it was launched from (taskbar icon, Start, a shortcut): splash and
            // launcher both open there (this capture was missing, so everything fell back to the primary screen / saved TV)
            if (GetCursorPos(out var cursor)) { LaunchPoint = (cursor.X, cursor.Y); StartupLog.Write("main", $"Launched with the mouse at {cursor.X},{cursor.Y}"); }
            // logo overlay pops up immediately, over whatever is on screen, while WinUI + the launcher load
            SplashWindow.Show();
            StartupLog.Write("main", "Entering WinUI Application.Start");
            Application.Start(initialization =>
            {
                try
                {
                    var queue = DispatcherQueue.GetForCurrentThread();
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
                    StartupLog.Write("main", "Constructing App");
                    new App();
                }
                catch (Exception ex)
                {
                    StartupLog.Fatal("Application.Start callback", ex);
                    throw;
                }
            });
            StartupLog.Write("main", "Application.Start returned");
        }
        catch (Exception ex)
        {
            StartupLog.Fatal("Program.Main", ex);
        }
    }
}
