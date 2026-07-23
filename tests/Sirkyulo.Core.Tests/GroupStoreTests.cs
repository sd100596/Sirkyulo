using Sirkyulo.Core;

namespace Sirkyulo.Core.Tests;

public class GroupStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sirkyulo-tests", Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_dir, "groups.json");

    public GroupStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RoundTripsGroupsAndItems()
    {
        var store = new GroupStore(StorePath);
        var group = new Group { Name = "Games", DesktopLinkPath = @"C:\Users\x\Desktop\Games.lnk" };
        group.Items.Add(new GroupItem
        {
            StoredPath = @"C:\store\Steam.lnk",
            DisplayName = "Steam",
            OriginalDesktopPath = @"C:\Users\x\Desktop\Steam.lnk",
            WasMoved = true,
        });
        store.Add(group);
        store.Save();

        var reloaded = new GroupStore(StorePath);
        reloaded.Load();

        var g = Assert.Single(reloaded.Groups);
        Assert.Equal(group.Id, g.Id);
        Assert.Equal("Games", g.Name);
        var item = Assert.Single(g.Items);
        Assert.Equal("Steam", item.DisplayName);
        Assert.True(item.WasMoved);
        Assert.Equal(@"C:\Users\x\Desktop\Steam.lnk", item.OriginalDesktopPath);
    }

    [Fact]
    public void MissingFileLoadsAsEmpty()
    {
        var store = new GroupStore(StorePath);
        store.Load();

        Assert.Empty(store.Groups);
        Assert.False(store.LastLoadWasCorrupt);
    }

    [Fact]
    public void CorruptFileDegradesToEmptyAndIsQuarantined()
    {
        File.WriteAllText(StorePath, "{ this is not json");

        var store = new GroupStore(StorePath);
        store.Load();

        Assert.Empty(store.Groups);
        Assert.True(store.LastLoadWasCorrupt);
        // The bad file is preserved rather than silently overwritten.
        Assert.NotEmpty(Directory.GetFiles(_dir, "groups.json.corrupt-*"));
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void SaveIsAtomicAndLeavesNoTempFile()
    {
        var store = new GroupStore(StorePath);
        store.Add(new Group { Name = "A" });
        store.Save();
        store.Add(new Group { Name = "B" });
        store.Save();

        Assert.False(File.Exists(StorePath + ".tmp"));

        var reloaded = new GroupStore(StorePath);
        reloaded.Load();
        Assert.Equal(2, reloaded.Groups.Count);
    }

    [Fact]
    public void FindsByIdAndByLinkPathCaseInsensitively()
    {
        var store = new GroupStore(StorePath);
        var group = new Group { Name = "Games", DesktopLinkPath = @"C:\Users\x\Desktop\Games.lnk" };
        store.Add(group);

        Assert.NotNull(store.Find(group.Id));
        Assert.NotNull(store.FindByLinkPath(@"c:\users\x\desktop\GAMES.LNK"));
        Assert.Null(store.Find(Guid.NewGuid()));
    }

    [Fact]
    public void RemoveDropsTheGroup()
    {
        var store = new GroupStore(StorePath);
        var group = new Group { Name = "Games" };
        store.Add(group);

        Assert.True(store.Remove(group.Id));
        Assert.False(store.Remove(group.Id));
        Assert.Empty(store.Groups);
    }
}
