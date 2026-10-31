using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ArcadeGridz.App.Services;

public static class StartupLog
{
    static readonly object Gate = new();
    static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArcadeGridz", "logs");
    public static readonly string LogFile = Path.Combine(LogDirectory, "startup.log");

    public static void Initialize()
    {
        Directory.CreateDirectory(LogDirectory);
        Write("startup", $"ArcadeGridz starting; version={Assembly.GetExecutingAssembly().GetName().Version}");
        Write("environment", $"os={Environment.OSVersion}; framework={RuntimeInformation.FrameworkDescription}; arch={RuntimeInformation.ProcessArchitecture}; base={AppContext.BaseDirectory}");
        Write("deployment", $"WindowsAppRuntime={File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.dll"))}; App.xbf={File.Exists(Path.Combine(AppContext.BaseDirectory, "App.xbf"))}; MainWindow.xbf={File.Exists(Path.Combine(AppContext.BaseDirectory, "MainWindow.xbf"))}; resources.pri={File.Exists(Path.Combine(AppContext.BaseDirectory, "resources.pri"))}; WebView2Loader={File.Exists(Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll"))}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Exception("AppDomain.UnhandledException", e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)), e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Exception("TaskScheduler.UnobservedTaskException", e.Exception, false);
            e.SetObserved();
        };
    }

    public static void Write(string stage, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogFile, $"{DateTimeOffset.Now:O} [{Environment.ProcessId}:{Environment.CurrentManagedThreadId}] {stage}: {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { }
    }

    public static void Exception(string stage, Exception? error, bool terminating = false)
    {
        var current = error;
        var depth = 0;
        while (current is not null && depth++ < 8)
        {
            Write(stage, $"terminating={terminating}; type={current.GetType().FullName}; hresult=0x{current.HResult:X8}; message={current.Message}{Environment.NewLine}{current.StackTrace}");
            current = current.InnerException;
            stage += ".inner";
        }
    }

    public static void Fatal(string stage, Exception error)
    {
        Exception(stage, error, true);
        ShowFatal($"ArcadeGridz could not start.\n\n{error.GetType().Name}: {error.Message}\n\nCrash log:\n{LogFile}");
    }

    public static void ShowFatal(string message)
    {
        try { MessageBoxW(IntPtr.Zero, message, "ArcadeGridz startup error", 0x10); }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
