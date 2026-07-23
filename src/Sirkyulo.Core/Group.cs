namespace Sirkyulo.Core;

/// <summary>
/// One desktop group. Its identity lives in the <c>--group {Id}</c> argument baked into
/// the desktop shortcut at <see cref="DesktopLinkPath"/>, so the shortcut can be renamed
/// or moved freely without losing the association.
/// </summary>
public sealed class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name. Mirrors the desktop shortcut's file name, minus the extension.</summary>
    public string Name { get; set; } = "New Group";

    /// <summary>Absolute path of the .lnk on the desktop. Re-synced when the user renames it.</summary>
    public string DesktopLinkPath { get; set; } = "";

    public List<GroupItem> Items { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One entry inside a group.
/// </summary>
public sealed class GroupItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Absolute path of the .lnk/.url now living in the group folder.</summary>
    public string StoredPath { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Where this file sat before it was absorbed. Used to put it back on ungroup.
    /// Null when <see cref="WasMoved"/> is false.
    /// </summary>
    public string? OriginalDesktopPath { get; set; }

    /// <summary>
    /// True when the original was physically relocated into the group folder, which is now
    /// every case. Retained because older stores contain entries written when real files and
    /// folders were linked in place instead of moved; those must not be "restored".
    /// </summary>
    public bool WasMoved { get; set; }

    /// <summary>
    /// True when this item is a folder. Directories need different move and restore calls
    /// than files, and the distinction is not always recoverable from the path once the
    /// original has gone.
    /// </summary>
    public bool IsDirectory { get; set; }

    public DateTimeOffset AddedUtc { get; set; } = DateTimeOffset.UtcNow;
}
