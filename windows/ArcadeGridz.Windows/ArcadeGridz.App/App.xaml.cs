using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml;

namespace ArcadeGridz.App;

public partial class App : Application
{
    public static MainWindow Window { get; private set; } = null!;

    public App()
    {
        StartupLog.Write("app", "Constructor entered");
        UnhandledException += OnUnhandledException;
        try
        {
            InitializeComponent();
            StartupLog.Write("app", "InitializeComponent completed");
        }
        catch (Exception ex)
        {
            StartupLog.Fatal("App.InitializeComponent", ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupLog.Write("app", "OnLaunched entered");
        try
        {
            Window = new MainWindow();
            StartupLog.Write("app", "MainWindow constructed");
            Window.Activate();
            StartupLog.Write("app", "MainWindow activated");
        }
        catch (Exception ex)
        {
            StartupLog.Fatal("App.OnLaunched", ex);
            Environment.Exit(-1);
        }
    }

    void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        StartupLog.Exception("App.UnhandledException", e.Exception, true);
        StartupLog.ShowFatal($"ArcadeGridz encountered a fatal UI error.\n\n{e.Message}\n\nCrash log:\n{StartupLog.LogFile}");
    }
}
