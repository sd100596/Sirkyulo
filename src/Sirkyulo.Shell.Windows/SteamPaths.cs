using System.IO;
using Microsoft.Win32;

namespace Sirkyulo.Shell.Windows;

/// <summary>Finds the Steam installation. Resolved once — it does not move while we run.</summary>
public static class SteamPaths
{
    private static readonly Lazy<string?> Resolved = new(Locate);

    /// <summary>Steam's install root, or null when Steam is not installed.</summary>
    public static string? Root => Resolved.Value;

    private static string? Locate()
    {
        // The per-user key is authoritative and survives Steam being installed off the
        // default drive; it stores the path with forward slashes.
        var userPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (Exists(userPath)) return Normalize(userPath!);

        var machinePath =
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string;
        if (Exists(machinePath)) return Normalize(machinePath!);

        var guess = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(guess) ? guess : null;
    }

    private static bool Exists(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    private static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');
}
