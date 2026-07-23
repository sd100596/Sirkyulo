using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using Sirkyulo.Shell.Windows;

namespace Sirkyulo.App.Services;

/// <summary>
/// In-memory icon cache, keyed by path and last-write time so a re-themed shortcut picks up
/// its new art without a restart.
///
/// Deliberately not persisted to disk: resolution costs a few milliseconds, the ring opens
/// from an already-running process, and a disk cache would just add a staleness bug and a
/// folder to clean up.
/// </summary>
public static class IconCache
{
    private static readonly ConcurrentDictionary<string, BitmapSource?> Cache = new();

    public static BitmapSource? Get(string path, int size = 128)
    {
        var key = BuildKey(path, size);
        return Cache.GetOrAdd(key, _ =>
        {
            var resolved = IconResolver.Resolve(path, size);
            if (resolved is null) Log.Warn($"No icon for '{path}'.");
            return resolved?.Image;
        });
    }

    public static void Clear() => Cache.Clear();

    private static string BuildKey(string path, int size)
    {
        try
        {
            return $"{path.ToLowerInvariant()}|{size}|{File.GetLastWriteTimeUtc(path).Ticks}";
        }
        catch (IOException)
        {
            return $"{path.ToLowerInvariant()}|{size}|0";
        }
    }
}
