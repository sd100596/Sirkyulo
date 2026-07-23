using System.IO;
using Sirkyulo.Core;

namespace Sirkyulo.App.Services;

/// <summary>
/// Minimal append-only log. A tray app has nowhere to show an error, and the file operations
/// here move the user's shortcuts around, so failures need to leave a trace somewhere.
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex) =>
        Write("ERROR", $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                Trim();
                File.AppendAllText(AppPaths.LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
    }

    /// <summary>Keeps the log from growing without bound on a machine that never restarts.</summary>
    private static void Trim()
    {
        var info = new FileInfo(AppPaths.LogFile);
        if (!info.Exists || info.Length < 1024 * 512) return;

        var keep = File.ReadAllLines(AppPaths.LogFile).TakeLast(500);
        File.WriteAllLines(AppPaths.LogFile, keep);
    }
}
