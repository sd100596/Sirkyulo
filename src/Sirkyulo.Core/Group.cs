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
    /// True when the user's original file was physically relocated into the group folder
    /// (shortcuts only). False when we merely created a new link pointing at content that
    /// stayed where it was — real files and folders are never moved.
    /// </summary>
    public bool WasMoved { get; set; }

    public DateTimeOffset AddedUtc { get; set; } = DateTimeOffset.UtcNow;
}
