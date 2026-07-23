using System.IO;
using Microsoft.Win32;

namespace Sirkyulo.App.Services;

/// <summary>
/// The "run when I sign in" registry entry.
///
/// Pulled out of the tray menu because the uninstaller needs to remove it too, and a stale
/// Run entry pointing at a deleted exe means Windows fails to launch something at every
/// sign-in.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Sirkyulo";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (exe is null) return;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            Log.Info($"Run at startup {(enabled ? "enabled" : "disabled")}.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Log.Error("Could not update the startup entry.", ex);
        }
    }

    public static void Remove() => Set(false);
}
