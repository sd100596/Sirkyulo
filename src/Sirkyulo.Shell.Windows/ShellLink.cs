using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Sirkyulo.Shell.Windows.Interop;

namespace Sirkyulo.Shell.Windows;

/// <summary>Everything a .lnk carries that Sirkyulo cares about.</summary>
public sealed record ShellLinkInfo(
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string Description,
    string IconPath,
    int IconIndex);

/// <summary>
/// Reads and writes .lnk files via IShellLinkW.
///
/// This is the backbone of the whole design: a group's desktop icon is a shortcut to
/// Sirkyulo.exe carrying <c>--group {guid}</c> in its Arguments, which is how Explorer's
/// native drag-drop, rename and per-file-icon behaviour all come for free.
/// </summary>
public static class ShellLink
{
    private const int MaxPath = 260;
    private const uint StgmRead = 0x00000000;
    private const uint SlgpRawPath = 0x0004;

    public static void Create(
        string linkPath,
        string targetPath,
        string? arguments = null,
        string? workingDirectory = null,
        string? description = null,
        string? iconPath = null,
        int iconIndex = 0)
    {
        var link = (IShellLinkW)new ShellLinkInterop.ShellLinkCoClass();
        try
        {
            link.SetPath(targetPath);
            if (!string.IsNullOrEmpty(arguments)) link.SetArguments(arguments);
            link.SetWorkingDirectory(workingDirectory ?? Path.GetDirectoryName(targetPath) ?? "");
            if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
            if (!string.IsNullOrEmpty(iconPath)) link.SetIconLocation(iconPath, iconIndex);

            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            ((IPersistFile)link).Save(linkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>
    /// Reads a .lnk without resolving it. Resolution is deliberately skipped: it can trigger
    /// a UI prompt or block for seconds chasing a dead network target, and we only ever need
    /// the raw stored fields.
    /// </summary>
    public static ShellLinkInfo? Read(string linkPath)
    {
        if (!File.Exists(linkPath)) return null;

        var link = (IShellLinkW)new ShellLinkInterop.ShellLinkCoClass();
        try
        {
            ((IPersistFile)link).Load(linkPath, StgmRead);

            var buffer = new StringBuilder(MaxPath);
            var find = default(Win32FindDataW);

            link.GetPath(buffer, buffer.Capacity, ref find, SlgpRawPath);
            var target = buffer.ToString();

            buffer.Clear(); buffer.EnsureCapacity(1024);
            link.GetArguments(buffer, buffer.Capacity);
            var args = buffer.ToString();

            buffer.Clear(); buffer.EnsureCapacity(MaxPath);
            link.GetWorkingDirectory(buffer, buffer.Capacity);
            var workDir = buffer.ToString();

            buffer.Clear();
            link.GetDescription(buffer, buffer.Capacity);
            var description = buffer.ToString();

            buffer.Clear();
            link.GetIconLocation(buffer, buffer.Capacity, out var iconIndex);
            var iconPath = buffer.ToString();

            return new ShellLinkInfo(target, args, workDir, description, iconPath, iconIndex);
        }
        catch (COMException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }
}
