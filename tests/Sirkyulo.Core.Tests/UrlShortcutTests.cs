using Sirkyulo.Core;

namespace Sirkyulo.Core.Tests;

public class UrlShortcutTests
{
    // Verbatim from a real Steam-authored desktop shortcut.
    private static readonly string[] SteamShortcut =
    [
        "[{000214A0-0000-0000-C000-000000000046}]",
        "Prop3=19,0",
        "[InternetShortcut]",
        "IDList=",
        "IconIndex=0",
        "URL=steam://rungameid/12210",
        @"IconFile=C:\Program Files (x86)\Steam\steam\games\2239a011f34704ddb0561dc2c8dd312b08a0be3e.ico",
    ];

    [Fact]
    public void ParsesRealSteamShortcut()
    {
        var info = UrlShortcut.Parse(SteamShortcut);

        Assert.NotNull(info);
        Assert.Equal("steam://rungameid/12210", info.Url);
        Assert.EndsWith(".ico", info.IconFile);
        Assert.Equal(0, info.IconIndex);
    }

    [Fact]
    public void EmptyIconFileBecomesNull()
    {
        var info = UrlShortcut.Parse(["[InternetShortcut]", "URL=https://example.com", "IconFile="]);

        Assert.NotNull(info);
        Assert.Null(info.IconFile);
    }

    [Fact]
    public void SectionHeadersAndBlanksAreIgnored()
    {
        var info = UrlShortcut.Parse(["", "[InternetShortcut]", "  URL = https://example.com  "]);

        Assert.NotNull(info);
        Assert.Equal("https://example.com", info.Url);
    }

    [Fact]
    public void FileWithoutUrlIsNotAShortcut()
    {
        Assert.Null(UrlShortcut.Parse(["[InternetShortcut]", "IconIndex=0"]));
    }

    [Theory]
    [InlineData("steam://rungameid/12210", "12210")]
    [InlineData("steam://rungameid/1262540", "1262540")]
    [InlineData("STEAM://RUNGAMEID/454650", "454650")]
    [InlineData("steam://rungameid/1790600/extra", "1790600")]
    public void ExtractsSteamAppId(string url, string expected)
    {
        Assert.True(UrlShortcut.TryGetSteamAppId(url, out var appId));
        Assert.Equal(expected, appId);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("steam://openurl/https://store.steampowered.com")]
    [InlineData("steam://rungameid/")]
    [InlineData("steam://rungameid/abc")]
    [InlineData(null)]
    public void RejectsNonGameUrls(string? url)
    {
        Assert.False(UrlShortcut.TryGetSteamAppId(url, out _));
    }

    [Theory]
    [InlineData(@"C:\x\Game.url", true)]
    [InlineData(@"C:\x\Game.URL", true)]
    [InlineData(@"C:\x\Game.lnk", false)]
    public void RecognisesTheExtension(string path, bool expected)
    {
        Assert.Equal(expected, UrlShortcut.IsUrlShortcut(path));
    }
}

public class SteamLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sirkyulo-steam", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string SeedCache(string appId, params string[] files)
    {
        var dir = Path.Combine(_root, "appcache", "librarycache", appId);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "x");
        return dir;
    }

    [Fact]
    public void PrefersPortraitCoverOverHeader()
    {
        SeedCache("12210", "header.jpg", "library_header.jpg", "library_600x900.jpg");

        var art = SteamLibrary.FindCoverArt(_root, "12210");

        Assert.EndsWith("library_600x900.jpg", art);
    }

    [Fact]
    public void FallsBackThroughThePreferenceOrder()
    {
        SeedCache("12210", "header.jpg");

        Assert.EndsWith("header.jpg", SteamLibrary.FindCoverArt(_root, "12210"));
    }

    [Fact]
    public void ReturnsNullWhenNothingIsCached()
    {
        SeedCache("12210", "library_hero_blur.jpg");

        Assert.Null(SteamLibrary.FindCoverArt(_root, "12210"));
        Assert.Null(SteamLibrary.FindCoverArt(_root, "999999"));
        Assert.Null(SteamLibrary.FindCoverArt("", "12210"));
        Assert.Null(SteamLibrary.FindCoverArt(_root, ""));
    }
}
