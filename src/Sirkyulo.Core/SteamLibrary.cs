namespace Sirkyulo.Core;

/// <summary>
/// Locates Steam's cached artwork for a game.
///
/// Steam's desktop shortcuts point at an .ico cache whose resolution is wildly inconsistent —
/// some games get 256px, others only 32px, and the shell pads the small ones onto a white
/// document backing that looks broken in a radial menu. The library cache, by contrast, always
/// holds full-size store art, so it is the preferred source whenever it exists.
///
/// Takes the Steam root as a parameter rather than reading the registry, so the lookup rules
/// stay testable. <c>Sirkyulo.Shell.Windows.SteamPaths</c> supplies the real root.
/// </summary>
public static class SteamLibrary
{
    /// <summary>
    /// Art candidates in preference order. Portrait cover first — centre-cropped to a square it
    /// keeps the game's key art, whereas the wide header crops down to a meaningless strip.
    /// </summary>
    public static readonly string[] ArtFileNames =
    [
        "library_600x900.jpg",
        "library_600x900_2x.jpg",
        "library_header.jpg",
        "header.jpg",
    ];

    /// <summary>
    /// Returns the best available art file for <paramref name="appId"/>, or null when Steam has
    /// cached nothing for it (never-launched games, or a moved library).
    /// </summary>
    public static string? FindCoverArt(string steamRoot, string appId)
    {
        if (string.IsNullOrWhiteSpace(steamRoot) || string.IsNullOrWhiteSpace(appId)) return null;

        var dir = Path.Combine(steamRoot, "appcache", "librarycache", appId);
        if (!Directory.Exists(dir)) return null;

        foreach (var name in ArtFileNames)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
