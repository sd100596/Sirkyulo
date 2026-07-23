using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Sirkyulo.Shell.Windows;

/// <summary>Launching, desktop locations, and telling Explorer to repaint.</summary>
public static class ShellEnvironment
{
    /// <summary>The current user's desktop. Where group shortcuts are created.</summary>
    public static string UserDesktop =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>
    /// The all-users desktop. Icons here also appear on screen, so drops can originate from it —
    /// but it is usually not writable without elevation, so we never restore items into it.
    /// </summary>
    public static string PublicDesktop =>
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    /// <summary>
    /// Opens a stored shortcut through the shell so its own target, arguments, working
    /// directory and elevation manifest are all honoured. Starting the target directly would
    /// discard all of that.
    /// </summary>
    public static bool Open(string path)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Selects a file in a new Explorer window.</summary>
    public static void RevealInExplorer(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
    }

    /// <summary>
    /// Nudges Explorer to notice a file we created or deleted behind its back. Without this the
    /// desktop can sit stale until the user presses F5, which reads as the app being broken.
    /// </summary>
    public static void NotifyFileChanged(string path, bool created)
    {
        var native = Marshal.StringToHGlobalUni(path);
        try
        {
            SHChangeNotify(created ? ShcneCreate : ShcneDelete, ShcnfPathw, native, nint.Zero);
            SHChangeNotify(ShcneUpdateDir, ShcnfPathw,
                Marshal.StringToHGlobalUni(Path.GetDirectoryName(path) ?? UserDesktop), nint.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    private const uint ShcneCreate = 0x00000002;
    private const uint ShcneDelete = 0x00000004;
    private const uint ShcneUpdateDir = 0x00001000;
    private const uint ShcnfPathw = 0x0005;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, nint dwItem1, nint dwItem2);
}
