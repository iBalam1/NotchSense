using Microsoft.Win32;

namespace NotchSense;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NotchSense";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enabled && Environment.ProcessPath is { } executable)
            key.SetValue(ValueName, $"\"{executable}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
