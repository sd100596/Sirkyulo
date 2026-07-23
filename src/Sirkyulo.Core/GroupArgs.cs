namespace Sirkyulo.Core;

/// <summary>
/// The command line contract between a desktop shortcut and the running app.
///
/// A group shortcut stores <c>--group {guid}</c>. Explorer appends dropped file paths after
/// the stored arguments, so a plain activation and a drop arrive through the same entry point
/// and differ only in whether any paths followed.
/// </summary>
public static class GroupArgs
{
    public const string Switch = "--group";

    public static string Format(Guid id) => $"{Switch} {id:B}";

    /// <summary>
    /// Splits an argv into the target group and any dropped paths.
    /// Returns false when this invocation is not a group activation at all.
    /// </summary>
    public static bool TryParse(IReadOnlyList<string> argv, out Guid groupId, out IReadOnlyList<string> paths)
    {
        groupId = Guid.Empty;
        paths = [];

        for (var i = 0; i < argv.Count; i++)
        {
            if (!string.Equals(argv[i], Switch, StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= argv.Count || !Guid.TryParse(argv[i + 1], out groupId)) return false;

            // Everything after the guid is a dropped path. Explorer gives absolute paths, but
            // filter anyway so a stray switch can never be mistaken for a file.
            paths = argv.Skip(i + 2).Where(a => !a.StartsWith('-')).ToList();
            return true;
        }

        return false;
    }
}
