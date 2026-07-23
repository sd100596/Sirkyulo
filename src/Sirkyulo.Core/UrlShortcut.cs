namespace Sirkyulo.Core;

/// <summary>The fields Sirkyulo reads out of a .url internet shortcut.</summary>
public sealed record UrlShortcutInfo(string Url, string? IconFile, int IconIndex);

/// <summary>
/// Reads .url shortcuts. These are plain INI text, not COM shell links — IShellLinkW cannot
/// open them, and Steam writes every desktop game shortcut in this format, so they are the
/// common case rather than an edge case.
/// </summary>
public static class UrlShortcut
{
    private const string SteamRunGamePrefix = "steam://rungameid/";

    public static bool IsUrlShortcut(string path) =>
        Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase);

    public static UrlShortcutInfo? Read(string path)
    {
        try
        {
            return Parse(File.ReadAllLines(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static UrlShortcutInfo? Parse(IEnumerable<string> lines)
    {
        string? url = null, iconFile = null;
        var iconIndex = 0;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('[')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (key.Equals("URL", StringComparison.OrdinalIgnoreCase)) url = value;
            else if (key.Equals("IconFile", StringComparison.OrdinalIgnoreCase)) iconFile = value;
            else if (key.Equals("IconIndex", StringComparison.OrdinalIgnoreCase))
                int.TryParse(value, out iconIndex);
        }

        return url is null ? null : new UrlShortcutInfo(url, string.IsNullOrWhiteSpace(iconFile) ? null : iconFile, iconIndex);
    }

    /// <summary>
    /// Pulls the Steam application id out of a <c>steam://rungameid/&lt;id&gt;</c> URL,
    /// which is the hook for finding proper cover art instead of Steam's undersized .ico cache.
    /// </summary>
    public static bool TryGetSteamAppId(string? url, out string appId)
    {
        appId = "";
        if (url is null || !url.StartsWith(SteamRunGamePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var tail = url[SteamRunGamePrefix.Length..].Split('/', '?', '#')[0];
        if (tail.Length == 0 || !tail.All(char.IsAsciiDigit)) return false;

        appId = tail;
        return true;
    }
}
