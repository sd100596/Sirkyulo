using System.IO;
using System.Windows.Threading;
using Sirkyulo.Core;
using Sirkyulo.Shell.Windows;

namespace Sirkyulo.App.Services;

/// <summary>
/// Watches the desktop so the app follows what the user does in Explorer rather than fighting it.
///
/// Renaming a group icon is a rename of a .lnk; deleting a group is a delete of a .lnk. Both
/// must be honoured, and a delete in particular has to hand the group's contents back before
/// the association is lost.
/// </summary>
public sealed class DesktopWatcher : IDisposable
{
    /// <summary>
    /// Explorer emits delete/create pairs during its own housekeeping, and our own writes echo
    /// back here too. Settling briefly before acting keeps a transient event from destroying a
    /// group.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    private readonly GroupService _service;
    private readonly Dispatcher _dispatcher;
    private readonly FileSystemWatcher _watcher;

    public DesktopWatcher(GroupService service, Dispatcher dispatcher)
    {
        _service = service;
        _dispatcher = dispatcher;

        _watcher = new FileSystemWatcher(ShellEnvironment.UserDesktop, "*.lnk")
        {
            NotifyFilter = NotifyFilters.FileName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };

        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnDeleted;
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            // Identity lives in the shortcut's arguments, so read it back rather than
            // trusting the old path — the user may have renamed it more than once.
            if (!TryReadGroupId(e.FullPath, out var groupId)) return;

            Log.Info($"Group renamed to '{Path.GetFileNameWithoutExtension(e.FullPath)}'.");
            _service.SyncRename(groupId, e.FullPath);
        });
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        _ = _dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(Settle);

            // If it came back, or we deleted it ourselves and already dropped the entry,
            // there is nothing to do.
            if (File.Exists(e.FullPath)) return;
            if (_service.Store.FindByLinkPath(e.FullPath) is not { } group) return;

            var restored = _service.DeleteGroup(group.Id, removeDesktopLink: false);
            Log.Info($"Group '{group.Name}' deleted from the desktop; {restored} shortcut(s) returned.");
            GroupDeletedExternally?.Invoke(group.Name, restored);
        });
    }

    /// <summary>Raised after the user deletes a group icon, so the tray can say what happened.</summary>
    public event Action<string, int>? GroupDeletedExternally;

    private static bool TryReadGroupId(string linkPath, out Guid groupId)
    {
        groupId = Guid.Empty;

        var info = ShellLink.Read(linkPath);
        if (info is null) return false;

        var argv = info.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return GroupArgs.TryParse(argv, out groupId, out _);
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Renamed -= OnRenamed;
        _watcher.Deleted -= OnDeleted;
        _watcher.Dispose();
    }
}
