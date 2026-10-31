using Microsoft.Win32;

namespace ArcadeGridz.App.Services;

public static class WindowsStartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "ArcadeGridz";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch (Exception ex)
            {
                StartupLog.Exception("Read Windows startup setting", ex);
                return false;
            }
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled)
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(ValueName, false);
            StartupLog.Write("settings", $"Start with Windows={enabled}");
            return true;
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Write Windows startup setting", ex);
            return false;
        }
    }
}
