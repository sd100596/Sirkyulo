using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sirkyulo.Core;

/// <summary>
/// The group index, persisted to <see cref="AppPaths.GroupsFile"/>.
/// Writes are atomic so a crash mid-save can never leave a truncated index, and a corrupt
/// or missing file degrades to an empty store rather than a startup crash — the desktop
/// shortcuts are the real source of truth and reconcile can rebuild from them.
/// </summary>
public sealed class GroupStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private List<Group> _groups = [];

    public GroupStore(string? path = null) => _path = path ?? AppPaths.GroupsFile;

    public IReadOnlyList<Group> Groups
    {
        get { lock (_gate) return _groups.ToList(); }
    }

    /// <summary>Set when the last <see cref="Load"/> found a file it could not parse.</summary>
    public bool LastLoadWasCorrupt { get; private set; }

    public void Load()
    {
        lock (_gate)
        {
            LastLoadWasCorrupt = false;
            if (!File.Exists(_path))
            {
                _groups = [];
                return;
            }

            try
            {
                var json = File.ReadAllText(_path);
                _groups = JsonSerializer.Deserialize<List<Group>>(json, Json) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Keep the bad file for forensics rather than overwriting it on next save.
                LastLoadWasCorrupt = true;
                TryQuarantine();
                _groups = [];
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_groups, Json));

            if (File.Exists(_path))
                File.Replace(tmp, _path, null);
            else
                File.Move(tmp, _path);
        }
    }

    public Group? Find(Guid id)
    {
        lock (_gate) return _groups.FirstOrDefault(g => g.Id == id);
    }

    public Group? FindByLinkPath(string linkPath)
    {
        lock (_gate)
            return _groups.FirstOrDefault(
                g => string.Equals(g.DesktopLinkPath, linkPath, StringComparison.OrdinalIgnoreCase));
    }

    public void Add(Group group)
    {
        lock (_gate) _groups.Add(group);
    }

    public bool Remove(Guid id)
    {
        lock (_gate) return _groups.RemoveAll(g => g.Id == id) > 0;
    }

    private void TryQuarantine()
    {
        try
        {
            File.Move(_path, $"{_path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
        }
        catch (IOException)
        {
            // Nothing useful to do; the empty store still lets the app start.
        }
    }
}
