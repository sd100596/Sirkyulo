using Sirkyulo.App.Services;
using Sirkyulo.Shell.Windows;

namespace Sirkyulo.Core.Tests;

public sealed class GroupServiceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "sirkyulo-tests", Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly string _desktop;
    private readonly GroupStore _store;
    private readonly GroupService _service;

    public GroupServiceTests()
    {
        _root = Path.Combine(_sandbox, "AppData");
        _desktop = Path.Combine(_sandbox, "Desktop");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_desktop);
        _store = new GroupStore(Path.Combine(_root, "groups.json"));
        _service = CreateService(_store);
    }

    private GroupService CreateService(GroupStore store) => new(store, _root, _desktop, () => { }, _ => { });
    private string GroupDir(Group group) => Path.Combine(_root, "Groups", group.Id.ToString("N"));

    private Group AddGroup()
    {
        var group = new Group { Name = "Games", DesktopLinkPath = Path.Combine(_desktop, "Games.lnk") };
        Directory.CreateDirectory(GroupDir(group));
        _store.Add(group);
        _store.Save();
        return group;
    }

    private GroupItem AddItem(Group group, bool directory = false)
    {
        var item = new GroupItem
        {
            StoredPath = Path.Combine(GroupDir(group), directory ? "Folder" : "Document.txt"),
            OriginalDesktopPath = Path.Combine(_desktop, directory ? "Folder" : "Document.txt"),
            WasMoved = true,
            IsDirectory = directory,
        };
        if (directory)
        {
            Directory.CreateDirectory(item.StoredPath);
            File.WriteAllText(Path.Combine(item.StoredPath, "Nested.txt"), "precious contents");
        }
        else File.WriteAllText(item.StoredPath, "precious contents");
        group.Items.Add(item);
        _store.Save();
        return item;
    }

    private GroupStore Reload()
    {
        var store = new GroupStore(Path.Combine(_root, "groups.json"));
        store.Load();
        return store;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedRemovalKeepsEntryAndContentsAndCanBeRetried(bool directory)
    {
        var group = AddGroup();
        var item = AddItem(group, directory);
        Directory.Delete(_desktop);

        Assert.False(_service.RemoveItem(group.Id, item.Id));
        Assert.Single(Reload().Find(group.Id)!.Items);
        Assert.True(Path.Exists(item.StoredPath));

        Directory.CreateDirectory(_desktop);
        Assert.True(_service.RemoveItem(group.Id, item.Id));
        Assert.Empty(Reload().Find(group.Id)!.Items);
        var restored = directory ? Path.Combine(item.OriginalDesktopPath!, "Nested.txt") : item.OriginalDesktopPath!;
        Assert.Equal("precious contents", File.ReadAllText(restored));
    }

    [Fact]
    public void FailedGroupDeletionPreservesDataAcrossRestart()
    {
        var group = AddGroup();
        var item = AddItem(group);
        Directory.Delete(_desktop);

        Assert.Equal(0, _service.DeleteGroup(group.Id));
        Assert.Equal("precious contents", File.ReadAllText(item.StoredPath));
        var reloaded = Reload();
        Assert.Single(reloaded.Find(group.Id)!.Items);

        Directory.CreateDirectory(_desktop);
        Assert.Equal(1, CreateService(reloaded).DeleteGroup(group.Id));
        Assert.Null(Reload().Find(group.Id));
        Assert.Equal("precious contents", File.ReadAllText(item.OriginalDesktopPath!));
    }

    [Fact]
    public void OrphanRecoveryPreservesFilesWhenDestinationIsUnavailable()
    {
        var group = AddGroup();
        var item = AddItem(group);
        _store.Remove(group.Id);
        _store.Save();
        Directory.Delete(_desktop);

        Assert.Equal(0, _service.RecoverOrphans().RestoredItems);
        Assert.Equal("precious contents", File.ReadAllText(item.StoredPath));

        Directory.CreateDirectory(_desktop);
        Assert.Equal(1, _service.RecoverOrphans().RestoredItems);
        Assert.Equal("precious contents", File.ReadAllText(item.OriginalDesktopPath!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UninstallPreservesFailedRestoresAndTheirIndex(bool orphan)
    {
        var group = AddGroup();
        var item = AddItem(group);
        if (orphan) { _store.Remove(group.Id); _store.Save(); }
        Directory.Delete(_desktop);

        Assert.Equal(0, _service.UninstallEverything());
        Assert.Equal("precious contents", File.ReadAllText(item.StoredPath));
        if (!orphan) Assert.Single(Reload().Find(group.Id)!.Items);

        Directory.CreateDirectory(_desktop);
        Assert.Equal(1, CreateService(Reload()).UninstallEverything());
        Assert.Equal("precious contents", File.ReadAllText(item.OriginalDesktopPath!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryFindsUnindexedContentsInsideKnownGroup(bool directory)
    {
        var group = AddGroup();
        var indexed = AddItem(group);
        var unindexed = Path.Combine(GroupDir(group), "Unindexed");
        if (directory)
        {
            Directory.CreateDirectory(unindexed);
            File.WriteAllText(Path.Combine(unindexed, "Nested.txt"), "unindexed contents");
        }
        else File.WriteAllText(unindexed, "unindexed contents");

        var service = CreateService(Reload());
        Assert.Equal(1, service.RecoverOrphans().RestoredItems);
        Assert.True(File.Exists(indexed.StoredPath));
        Assert.Single(service.Store.Find(group.Id)!.Items);
        var restored = Path.Combine(_desktop, "Unindexed");
        Assert.Equal("unindexed contents", File.ReadAllText(directory ? Path.Combine(restored, "Nested.txt") : restored));
        Assert.Equal(0, service.RecoverOrphans().RestoredItems);
    }

    [Fact]
    public void FailedIndexSaveAfterIngestionIsRecoveredOnRestart()
    {
        var group = AddGroup();
        var source = Path.Combine(_desktop, "Incoming.txt");
        File.WriteAllText(source, "incoming contents");
        // A directory at the temporary-file path makes Save fail after the move.
        Directory.CreateDirectory(Path.Combine(_root, "groups.json.tmp"));
        var error = Record.Exception(() => _service.Ingest(group.Id, [source]));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.False(File.Exists(source));

        var service = CreateService(Reload());
        Assert.Empty(service.Store.Find(group.Id)!.Items);
        Assert.Equal(1, service.RecoverOrphans().RestoredItems);
        Assert.Equal("incoming contents", File.ReadAllText(source));
    }

    [Fact]
    public void ReconcileFindsShortcutRenamedWhileAppWasClosed()
    {
        var group = AddGroup();
        var item = AddItem(group);
        ShellLink.Create(group.DesktopLinkPath, Environment.ProcessPath!, GroupArgs.Format(group.Id));
        var renamed = Path.Combine(_desktop, "Renamed.lnk");
        File.Move(group.DesktopLinkPath, renamed);

        var service = CreateService(Reload());
        service.Reconcile();
        service.RecoverOrphans();

        var saved = Reload().Find(group.Id)!;
        Assert.NotNull(saved);
        Assert.Equal(renamed, saved.DesktopLinkPath);
        Assert.Equal("Renamed", saved.Name);
        Assert.Single(saved.Items);
        Assert.True(File.Exists(item.StoredPath));
        Assert.True(File.Exists(renamed));
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
    }
}
