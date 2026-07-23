using Sirkyulo.Core;

namespace Sirkyulo.Core.Tests;

public class GroupArgsTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void PlainActivationParsesWithNoPaths()
    {
        Assert.True(GroupArgs.TryParse(["--group", Id.ToString("B")], out var id, out var paths));
        Assert.Equal(Id, id);
        Assert.Empty(paths);
    }

    [Fact]
    public void DroppedPathsFollowTheGuid()
    {
        string[] argv = ["--group", Id.ToString("B"), @"C:\Users\x\Desktop\Steam.lnk", @"C:\Users\x\Desktop\Doom.lnk"];

        Assert.True(GroupArgs.TryParse(argv, out var id, out var paths));
        Assert.Equal(Id, id);
        Assert.Equal(2, paths.Count);
        Assert.EndsWith("Doom.lnk", paths[1]);
    }

    [Fact]
    public void FormatRoundTrips()
    {
        var argv = GroupArgs.Format(Id).Split(' ');
        Assert.True(GroupArgs.TryParse(argv, out var id, out _));
        Assert.Equal(Id, id);
    }

    [Fact]
    public void NonGroupInvocationIsRejected()
    {
        Assert.False(GroupArgs.TryParse([], out _, out _));
        Assert.False(GroupArgs.TryParse(["--settings"], out _, out _));
        Assert.False(GroupArgs.TryParse(["--group"], out _, out _));
        Assert.False(GroupArgs.TryParse(["--group", "not-a-guid"], out _, out _));
    }
}
