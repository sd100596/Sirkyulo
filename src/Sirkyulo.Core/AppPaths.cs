namespace Sirkyulo.Core;

/// <summary>
/// Every location Sirkyulo writes to. Nothing else in the codebase builds an app-data path by hand.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Sirkyulo");

    /// <summary>The group index. Small, human-readable, hand-editable in a pinch.</summary>
    public static string GroupsFile => Path.Combine(Root, "groups.json");

    /// <summary>Parent of the per-group folders that hold moved shortcuts.</summary>
    public static string GroupsDir => Path.Combine(Root, "Groups");

    public static string IconCacheDir => Path.Combine(Root, "iconcache");

    public static string LogFile => Path.Combine(Root, "sirkyulo.log");

    public static string GroupDir(Guid id) => Path.Combine(GroupsDir, id.ToString("N"));

    /// <summary>Creates the whole tree. Safe to call repeatedly.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(GroupsDir);
        Directory.CreateDirectory(IconCacheDir);
    }
}
