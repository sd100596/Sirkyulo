using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Sirkyulo.Core;

namespace Sirkyulo.Shell.Windows;

/// <summary>Where a resolved icon came from. Useful in logs when an icon looks wrong.</summary>
public enum IconSource
{
    None,
    SteamLibraryArt,
    IconFile,
    Shell,
}

public sealed record ResolvedIcon(BitmapSource Image, IconSource Source);

/// <summary>
/// Picks the best available artwork for a shortcut.
///
/// The shell alone is not good enough. Asked for a 96px icon it will happily return a 32px
/// asset padded onto a white document card, which is what makes some Steam games look broken
/// on the desktop. So the chain prefers, in order:
///
///   1. Steam library cover art  - always full size, for steam://rungameid shortcuts
///   2. The shortcut's own .ico  - read directly at its largest frame, no shell padding
///   3. The shell                - correct for everything that is not an internet shortcut
/// </summary>
public static class IconResolver
{
    public static ResolvedIcon? Resolve(string path, int size = 128)
    {
        if (UrlShortcut.IsUrlShortcut(path))
        {
            var info = UrlShortcut.Read(path);
            if (info is not null)
            {
                if (UrlShortcut.TryGetSteamAppId(info.Url, out var appId) && SteamPaths.Root is { } root)
                {
                    var art = SteamLibrary.FindCoverArt(root, appId);
                    if (art is not null && LoadSquareCrop(art) is { } cover)
                        return new ResolvedIcon(cover, IconSource.SteamLibraryArt);
                }

                if (info.IconFile is not null && LoadLargestIcoFrame(info.IconFile) is { } ico)
                    return new ResolvedIcon(ico, IconSource.IconFile);
            }
        }

        return ShellIcon.Extract(path, size) is { } shell
            ? new ResolvedIcon(shell, IconSource.Shell)
            : null;
    }

    /// <summary>
    /// Reads the biggest frame out of an .ico. Going direct avoids the shell's habit of
    /// centring an undersized icon on a white page graphic.
    /// </summary>
    private static BitmapSource? LoadLargestIcoFrame(string icoPath)
    {
        if (!File.Exists(icoPath)) return null;
        try
        {
            var decoder = new IconBitmapDecoder(
                new Uri(icoPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            var best = decoder.Frames.MaxBy(f => f.PixelWidth);
            best?.Freeze();
            return best;
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException or IOException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads cover art and centre-crops it square. Steam's portrait art is 2:3, and the middle
    /// square is reliably the key art rather than the title bar or the publisher logo.
    /// </summary>
    private static BitmapSource? LoadSquareCrop(string imagePath)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(imagePath);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();

            var side = Math.Min(img.PixelWidth, img.PixelHeight);
            if (side <= 0) return null;

            var crop = new CroppedBitmap(img, new Int32Rect(
                (img.PixelWidth - side) / 2, (img.PixelHeight - side) / 2, side, side));
            crop.Freeze();
            return crop;
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException or IOException or UriFormatException)
        {
            return null;
        }
    }
}
