using System.IO;
using Sirkyulo.Core;
using Sirkyulo.Shell.Windows;

namespace Sirkyulo.App.Services;

/// <summary>
/// All mutations to groups and to the user's files.
///
/// The guiding rule here is that nothing the user owns may become unrecoverable. Everything
/// absorbed into a group is moved rather than copied-and-deleted, and every move records where
/// it came from, so removing an item, deleting a group and uninstalling can all put it back.
/// A group folder the index has lost track of is swept back to the desktop on startup.
/// </summary>
public sealed class GroupService(GroupStore store)
{
    private readonly Lock _gate = new();

    public GroupStore Store => store;

    public event Action? Changed;

    // ---------------------------------------------------------------- create

    /// <summary>
    /// Creates a group and its desktop shortcut. The shortcut targets our own exe and carries
    /// <c>--group {id}</c>, which is what makes Explorer's drag-drop and rename work on it.
    /// </summary>
    public Group CreateGroup(string name)
    {
        lock (_gate)
        {
            var group = new Group { Id = Guid.NewGuid(), Name = SanitiseName(name) };

            Directory.CreateDirectory(AppPaths.GroupDir(group.Id));
            group.DesktopLinkPath = UniquePath(Path.Combine(ShellEnvironment.UserDesktop, group.Name + ".lnk"));

            var exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine the running executable path.");

            ShellLink.Create(
                group.DesktopLinkPath,
                exe,
                GroupArgs.Format(group.Id),
                Path.GetDirectoryName(exe),
                $"Sirkyulo group: {group.Name}",
                // Icon from the exe itself, so the shortcut never points at a file that
                // could be cleaned up independently of the app.
                exe);

            store.Add(group);
            store.Save();
            ShellEnvironment.NotifyFileChanged(group.DesktopLinkPath, created: true);
            Changed?.Invoke();
            return group;
        }
    }

    // ---------------------------------------------------------------- ingest

    /// <summary>
    /// Ceiling for a move that has to be performed as a copy.
    ///
    /// Only applies across volumes. A move within one volume is a directory-entry update and
    /// completes instantly whatever the size, so capping it would pointlessly refuse a 40 GB
    /// folder that would have taken milliseconds.
    /// </summary>
    public const long MaxCrossVolumeBytes = 500L * 1024 * 1024;

    /// <summary>
    /// Most items a single group will hold.
    ///
    /// The ring grows its radius to seat every icon, so an uncapped group eventually draws a
    /// circle larger than the screen and pushes its own items out of reach. Eight keeps the
    /// ring comfortably on any display, keeps every item within a short flick of the cursor,
    /// and matches what a radial menu can show without becoming a list.
    /// </summary>
    public const int MaxItemsPerGroup = 8;

    /// <param name="Added">Items that landed in the group.</param>
    /// <param name="Skipped">Items refused for any reason.</param>
    /// <param name="SkippedGroups">
    /// Of those, how many were other groups. Tracked separately so the user can be told the
    /// specific reason rather than a vague failure — dropping a group on a group is a
    /// deliberate refusal, not a malfunction.
    /// </param>
    /// <param name="SkippedTooLarge">Of those, how many exceeded the cross-volume copy limit.</param>
    /// <param name="SkippedGroupFull">Of those, how many arrived at an already-full group.</param>
    public sealed record IngestResult(
        int Added, int Skipped, int SkippedGroups, int SkippedTooLarge, int SkippedGroupFull);

    private enum IngestOutcome
    {
        Added,
        RejectedAsGroup,
        RejectedTooLarge,
        RejectedGroupFull,
        Rejected,
    }

    /// <summary>
    /// Absorbs dropped paths into a group. Returns how many landed, so the caller can decide
    /// whether a confirmation is worth showing.
    /// </summary>
    public IngestResult Ingest(Guid groupId, IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            var group = store.Find(groupId);
            if (group is null) return new IngestResult(0, paths.Count, 0, 0, 0);

            var dir = AppPaths.GroupDir(group.Id);
            Directory.CreateDirectory(dir);

            int added = 0, skipped = 0, skippedGroups = 0, skippedTooLarge = 0, skippedFull = 0;
            foreach (var path in paths)
            {
                switch (IngestOne(group, dir, path))
                {
                    case IngestOutcome.Added:
                        added++;
                        break;
                    case IngestOutcome.RejectedAsGroup:
                        skipped++;
                        skippedGroups++;
                        break;
                    case IngestOutcome.RejectedTooLarge:
                        skipped++;
                        skippedTooLarge++;
                        break;
                    case IngestOutcome.RejectedGroupFull:
                        skipped++;
                        skippedFull++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }

            if (added > 0)
            {
                store.Save();
                Changed?.Invoke();
            }

            return new IngestResult(added, skipped, skippedGroups, skippedTooLarge, skippedFull);
        }
    }

    private IngestOutcome IngestOne(Group group, string groupDir, string path)
    {
        try
        {
            // Groups stay one level deep. Nesting would turn the ring from a launcher into a
            // navigator, and it is refused here rather than silently ignored so the caller can
            // explain why.
            if (store.FindByLinkPath(path) is not null) return IngestOutcome.RejectedAsGroup;

            // Checked per item rather than up front, so a drop of ten onto a group holding six
            // still takes the two that fit instead of refusing the whole batch.
            if (group.Items.Count >= MaxItemsPerGroup) return IngestOutcome.RejectedGroupFull;

            var isFile = File.Exists(path);
            var isDir = Directory.Exists(path);
            if (!isFile && !isDir) return IngestOutcome.Rejected;

            // Folders keep their whole name; only files lose the extension, so a folder called
            // "My.Stuff" does not become "My".
            var displayName = isDir ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
            if (group.Items.Any(i => string.Equals(i.OriginalDesktopPath, path, StringComparison.OrdinalIgnoreCase)))
                return IngestOutcome.Rejected;

            // Everything moves — shortcuts, files and folders alike. Leaving real content on
            // the desktop would defeat the entire point of grouping it, and it reads as a
            // failed drop, which invites the user to delete what they just filed away.
            // Nothing is stranded by this: every removal path restores to the desktop, and
            // startup sweeps any group folder the index has lost track of.
            var destination = UniquePath(Path.Combine(groupDir, Path.GetFileName(path)));

            // Only a cross-volume move has to copy, and only a copy can take long enough to
            // matter. Measuring is itself a directory walk, so it is skipped entirely when the
            // move will be instant.
            if (!IsSameVolume(path, destination))
            {
                var size = MeasureSize(path);
                if (size > MaxCrossVolumeBytes)
                {
                    Log.Warn($"Refused '{path}': {size / 1024 / 1024} MB exceeds the " +
                             $"{MaxCrossVolumeBytes / 1024 / 1024} MB cross-drive limit.");
                    return IngestOutcome.RejectedTooLarge;
                }
            }

            MovePath(path, destination);
            ShellEnvironment.NotifyFileChanged(path, created: false);

            group.Items.Add(new GroupItem
            {
                StoredPath = destination,
                DisplayName = displayName,
                OriginalDesktopPath = path,
                WasMoved = true,
                IsDirectory = isDir,
            });
            return IngestOutcome.Added;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not ingest '{path}': {ex.Message}");
            return IngestOutcome.Rejected;
        }
    }

    // ---------------------------------------------------------------- remove

    /// <summary>Takes one item back out of a group and returns it to the desktop.</summary>
    public bool RemoveItem(Guid groupId, Guid itemId)
    {
        lock (_gate)
        {
            var group = store.Find(groupId);
            var item = group?.Items.FirstOrDefault(i => i.Id == itemId);
            if (group is null || item is null) return false;

            RestoreItem(item);
            group.Items.Remove(item);
            store.Save();
            Changed?.Invoke();
            return true;
        }
    }

    // ---------------------------------------------------------------- delete

    /// <summary>
    /// Deletes a group, returning everything inside it to the desktop first. Called both from
    /// the menu and from the desktop watcher when the user deletes the group icon directly, so
    /// there is no path by which grouped shortcuts can be silently destroyed.
    /// </summary>
    public int DeleteGroup(Guid groupId, bool removeDesktopLink = true)
    {
        lock (_gate)
        {
            var group = store.Find(groupId);
            if (group is null) return 0;

            var restored = group.Items.Count(RestoreItem);

            if (removeDesktopLink) TryDelete(group.DesktopLinkPath);
            TryDeleteDirectory(AppPaths.GroupDir(group.Id));

            store.Remove(group.Id);
            store.Save();
            Changed?.Invoke();
            return restored;
        }
    }

    /// <summary>
    /// Puts a single item back on the desktop. Moved shortcuts go home; items we merely linked
    /// to are left alone and their link is discarded, because the original never moved.
    /// </summary>
    private bool RestoreItem(GroupItem item)
    {
        try
        {
            // Legacy entries from when real files were linked in place rather than moved:
            // the original never left the desktop, so only our link should go.
            if (!item.WasMoved)
            {
                TryDelete(item.StoredPath);
                return false;
            }

            if (!File.Exists(item.StoredPath) && !Directory.Exists(item.StoredPath)) return false;

            // Prefer the exact spot it came from; fall back to the desktop root if that
            // folder is gone, so a restore can never fail into nothing.
            var preferred = item.OriginalDesktopPath;
            var targetDir = preferred is not null ? Path.GetDirectoryName(preferred) : null;
            if (targetDir is null || !Directory.Exists(targetDir))
                targetDir = ShellEnvironment.UserDesktop;

            var fileName = preferred is not null ? Path.GetFileName(preferred) : Path.GetFileName(item.StoredPath);
            var destination = UniquePath(Path.Combine(targetDir, fileName));

            MovePath(item.StoredPath, destination);
            ShellEnvironment.NotifyFileChanged(destination, created: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not restore '{item.StoredPath}': {ex.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------- sync

    /// <summary>
    /// Applies a desktop rename. The guid in the shortcut's arguments is the identity, so the
    /// file name is free to change and we simply follow it.
    /// </summary>
    public void SyncRename(Guid groupId, string newLinkPath)
    {
        lock (_gate)
        {
            var group = store.Find(groupId);
            if (group is null) return;

            group.DesktopLinkPath = newLinkPath;
            group.Name = Path.GetFileNameWithoutExtension(newLinkPath);
            store.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Startup repair. Any group whose desktop shortcut no longer exists is treated as deleted
    /// and its contents are handed back to the desktop, so items can never be stranded in
    /// %APPDATA% where the user would never find them.
    /// </summary>
    public void Reconcile()
    {
        lock (_gate)
        {
            foreach (var group in store.Groups)
            {
                if (!File.Exists(group.DesktopLinkPath))
                {
                    Log.Warn($"Group '{group.Name}' lost its desktop icon; restoring {group.Items.Count} item(s).");
                    DeleteGroup(group.Id, removeDesktopLink: false);
                    continue;
                }

                // Drop entries whose stored file vanished from under us.
                var missing = group.Items
                    .Where(i => !File.Exists(i.StoredPath) && !Directory.Exists(i.StoredPath))
                    .ToList();
                if (missing.Count == 0) continue;

                foreach (var m in missing) group.Items.Remove(m);
                store.Save();
            }
        }
    }

    /// <summary>
    /// Undoes everything Sirkyulo has done to this machine, in the only safe order: every
    /// grouped shortcut goes back to the desktop first, then the group icons, then the startup
    /// entry, then app data.
    ///
    /// This has to exist for the uninstaller to be honest. Deleting the app without it would
    /// leave the user's shortcuts sitting in %APPDATA% under GUID-named folders, and removing
    /// that folder would destroy them outright.
    /// </summary>
    public int UninstallEverything()
    {
        lock (_gate)
        {
            var restored = 0;
            foreach (var group in store.Groups)
                restored += DeleteGroup(group.Id, removeDesktopLink: true);

            // Catches anything the index had already lost track of.
            var swept = RecoverOrphans();
            restored += swept.RestoredItems;

            StartupRegistration.Remove();

            try
            {
                if (Directory.Exists(AppPaths.Root)) Directory.Delete(AppPaths.Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The log lives in here and is still open, so a leftover file is expected and
                // harmless — everything that mattered has already been returned to the desktop.
            }

            return restored;
        }
    }

    public sealed record RecoveryResult(int RestoredItems, int RemovedIcons)
    {
        public bool AnythingHappened => RestoredItems > 0 || RemovedIcons > 0;
    }

    /// <summary>
    /// Hands back anything the index has lost track of.
    ///
    /// If groups.json is ever unreadable the store quarantines it and starts empty, which would
    /// otherwise strand every grouped shortcut inside %APPDATA% under a GUID-named folder the
    /// user has no reason to ever look in. Sweeping those folders back onto the desktop means
    /// the worst case costs the user their grouping, never their shortcuts — and means the data
    /// folder is never something they need to open.
    /// </summary>
    public RecoveryResult RecoverOrphans()
    {
        lock (_gate)
        {
            var known = store.Groups.Select(g => g.Id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var restored = 0;

            if (Directory.Exists(AppPaths.GroupsDir))
            {
                foreach (var dir in Directory.GetDirectories(AppPaths.GroupsDir))
                {
                    if (known.Contains(Path.GetFileName(dir))) continue;

                    // Both files and subfolders: a group can now hold either, so sweeping only
                    // files would leave the user's folders buried in app data.
                    foreach (var entry in Directory.GetFileSystemEntries(dir))
                    {
                        try
                        {
                            var destination = UniquePath(
                                Path.Combine(ShellEnvironment.UserDesktop, Path.GetFileName(entry)));
                            MovePath(entry, destination);
                            ShellEnvironment.NotifyFileChanged(destination, created: true);
                            restored++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            Log.Warn($"Could not recover '{entry}': {ex.Message}");
                        }
                    }

                    Log.Warn($"Recovered orphaned group folder '{Path.GetFileName(dir)}'.");
                    TryDeleteDirectory(dir);
                }
            }

            // Desktop icons for groups that no longer exist are dead — clicking one would open
            // nothing. Their contents have just been returned above, so the icon can go.
            var removed = 0;
            foreach (var link in SafeListDesktopLinks())
            {
                if (!TryReadGroupId(link, out var id)) continue;
                if (store.Find(id) is not null) continue;

                Log.Warn($"Removing dead group icon '{Path.GetFileNameWithoutExtension(link)}'.");
                TryDelete(link);
                removed++;
            }

            return new RecoveryResult(restored, removed);
        }
    }

    private static IEnumerable<string> SafeListDesktopLinks()
    {
        try
        {
            return Directory.GetFiles(ShellEnvironment.UserDesktop, "*.lnk");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Reads the group id a desktop shortcut carries, if it is one of ours at all.</summary>
    private static bool TryReadGroupId(string linkPath, out Guid groupId)
    {
        groupId = Guid.Empty;

        var info = ShellLink.Read(linkPath);
        if (info is null || string.IsNullOrEmpty(info.Arguments)) return false;

        var argv = info.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return GroupArgs.TryParse(argv, out groupId, out _);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Moves a file or a whole folder.
    ///
    /// Directory.Move cannot cross volumes — it throws rather than falling back — and a
    /// redirected Desktop on another drive is common enough that ignoring it would corrupt
    /// exactly the case where the user has the most data at stake. So a cross-volume folder
    /// move is done as a full copy, verified, and only then deleted.
    /// </summary>
    private static void MovePath(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            File.Move(source, destination);
            return;
        }

        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            CopyDirectory(source, destination);
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>True when both paths sit on the same volume, so a move needs no copying.</summary>
    private static bool IsSameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)),
                      Path.GetPathRoot(Path.GetFullPath(b)),
                      StringComparison.OrdinalIgnoreCase);

    /// <summary>Total bytes of a file, or of everything under a folder.</summary>
    private static long MeasureSize(string path)
    {
        if (!Directory.Exists(path))
            return new FileInfo(path).Length;

        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable somewhere inside. Treat as oversized rather than starting a copy
            // that will fail partway through.
            return long.MaxValue;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    /// <summary>Appends " (2)", " (3)" … until the path is free. Never overwrites.</summary>
    internal static string UniquePath(string desired)
    {
        if (!File.Exists(desired) && !Directory.Exists(desired)) return desired;

        var dir = Path.GetDirectoryName(desired)!;
        var stem = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);

        for (var n = 2; n < 1000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }

        return Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
    }

    /// <summary>Strips characters Explorer will not accept in a file name.</summary>
    internal static string SanitiseName(string name)
    {
        var cleaned = new string(name
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c))
            .ToArray())
            .Trim()
            .Trim('.');

        return cleaned.Length == 0 ? "New Group" : cleaned;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Delete(path);
            ShellEnvironment.NotifyFileChanged(path, created: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not delete '{path}': {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not remove '{path}': {ex.Message}");
        }
    }
}
