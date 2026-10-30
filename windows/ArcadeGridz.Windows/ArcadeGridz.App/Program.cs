using ArcadeGridz.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace ArcadeGridz.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupLog.Initialize();
        try
        {
            StartupLog.Write("main", "Initializing COM wrappers");
            ComWrappersSupport.InitializeComWrappers();
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
