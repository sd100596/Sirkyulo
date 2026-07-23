using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Sirkyulo.App.Services;
using Sirkyulo.Core;

namespace Sirkyulo.App;

public partial class App : Application
{
    /// <summary>
    /// How long to wait for more dropped paths before acting.
    ///
    /// Explorer normally batches a whole multi-selection into one launch, but it splits into
    /// several once the command line would exceed its limit. Coalescing means a very large
    /// drop still produces one ingest and one confirmation instead of a stutter of them.
    /// </summary>
    private static readonly TimeSpan DropCoalesceWindow = TimeSpan.FromMilliseconds(250);

    private SingleInstance? _instance;
    private GroupService? _service;
    private DesktopWatcher? _watcher;
    private TrayMenu? _tray;

    private readonly Dictionary<Guid, List<string>> _pendingDrops = [];
    private DispatcherTimer? _dropTimer;
    private bool _isForwarder;
    private GroupService.RecoveryResult? _pendingRecovery;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Uninstall runs before anything else, including the single-instance handshake: the
        // uninstaller invokes it while a tray copy may still be running, and forwarding to
        // that copy would just make it show a ring instead of cleaning up.
        if (e.Args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstall();
            Shutdown();
            return;
        }

        _instance = new SingleInstance();
        if (!_instance.IsPrimary)
        {
            // Every group activation launches a new process. Hand off and get out of the way.
            SingleInstance.Forward(e.Args);
            _isForwarder = true;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        AppPaths.EnsureCreated();
        Log.Info("Sirkyulo starting.");

        var store = new GroupStore();
        store.Load();
        if (store.LastLoadWasCorrupt)
            Log.Warn("groups.json was unreadable and has been quarantined; starting empty.");

        _service = new GroupService(store);
        _service.Reconcile();

        // Nothing may be left stranded in app data. If the index lost track of a group, its
        // shortcuts go back on the desktop where the user can see them.
        var recovery = _service.RecoverOrphans();
        if (recovery.AnythingHappened)
        {
            Log.Warn($"Recovery restored {recovery.RestoredItems} item(s) " +
                     $"and removed {recovery.RemovedIcons} dead group icon(s).");
            _pendingRecovery = recovery;
        }

        _instance.ArgumentsReceived += args => Dispatcher.InvokeAsync(() => HandleArguments(args));
        _instance.StartListening();

        _watcher = new DesktopWatcher(_service, Dispatcher);
        _watcher.GroupDeletedExternally += (name, restored) =>
            ToastWindow.Show("Group removed", $"'{name}' was deleted. {restored} shortcut(s) returned to your desktop.");

        _tray = new TrayMenu(_service);

        // Reported after the tray exists, so the message has somewhere to come from.
        if (_pendingRecovery is { } recovered)
        {
            ToastWindow.Show("Groups recovered",
                $"{Plural(recovered.RestoredItems)} returned to your desktop after a problem reading saved groups.");
            _pendingRecovery = null;
        }

        HandleArguments(e.Args);
    }

    /// <summary>
    /// The single entry point for every activation, whether it started this process or was
    /// forwarded from another. A group shortcut with no trailing paths is a click; with paths,
    /// it is a drop.
    /// </summary>
    private void HandleArguments(string[] args)
    {
        if (_service is null) return;

        // Scriptable group creation. Exists so the app can be driven end-to-end without a
        // human clicking the tray, which is the only way this is testable at all.
        var newGroupIndex = Array.FindIndex(args, a =>
            string.Equals(a, "--new-group", StringComparison.OrdinalIgnoreCase));
        if (newGroupIndex >= 0)
        {
            // With a name, create it outright. Without one, open the same prompt the tray
            // uses — which also makes the prompt's focus behaviour testable from a script.
            var name = newGroupIndex + 1 < args.Length ? args[newGroupIndex + 1] : NamePromptWindow.Ask();
            if (name is null) return;

            var group = _service.CreateGroup(name);
            Log.Info($"Created group '{group.Name}' ({group.Id:B}) via command line.");
            return;
        }

        if (!GroupArgs.TryParse(args, out var groupId, out var paths)) return;

        if (paths.Count == 0)
        {
            if (_service.Store.Find(groupId) is { } group) RingWindow.Open(group, _service);
            else Log.Warn($"Activation for unknown group {groupId:B}.");
            return;
        }

        QueueDrop(groupId, paths);
    }

    private void QueueDrop(Guid groupId, IReadOnlyList<string> paths)
    {
        if (!_pendingDrops.TryGetValue(groupId, out var queued))
            _pendingDrops[groupId] = queued = [];

        queued.AddRange(paths);

        _dropTimer ??= new DispatcherTimer { Interval = DropCoalesceWindow };
        _dropTimer.Tick -= FlushDrops;
        _dropTimer.Tick += FlushDrops;
        _dropTimer.Stop();
        _dropTimer.Start();
    }

    private void FlushDrops(object? sender, EventArgs e)
    {
        _dropTimer?.Stop();
        if (_service is null) return;

        var batches = _pendingDrops.ToList();
        _pendingDrops.Clear();

        foreach (var (groupId, paths) in batches)
        {
            var result = _service.Ingest(groupId, paths);
            Log.Info($"Ingested {result.Added} item(s) into {groupId:B} " +
                     $"({result.Skipped} skipped, {result.SkippedGroups} of them groups).");

            // Dropping a group on a group is refused on purpose, so name the reason. Silently
            // doing nothing reads as the app being broken.
            if (result.SkippedGroups > 0)
            {
                ToastWindow.Show("Groups can't be nested",
                    result.Added > 0
                        ? $"Added {Plural(result.Added)}. The group you dropped stayed on your desktop."
                        : "Groups only go one level deep, so it stayed on your desktop.");
            }
            else if (result.Added == 0)
            {
                ToastWindow.Show("Nothing added", "Those items could not be added to the group.");
            }

            if (result.Added == 0) continue;

            // Flash the ring so a drop visibly does something — otherwise icons just vanish
            // off the desktop with no feedback at all.
            if (_service.Store.Find(groupId) is { } group) RingWindow.Open(group, _service);
        }
    }

    /// <summary>
    /// Returns the machine to how it was before Sirkyulo ran, then exits. Invoked by the
    /// uninstaller before it deletes any files.
    /// </summary>
    private void RunUninstall()
    {
        _isForwarder = true;

        // Any tray copy still running holds groups.json open and would rewrite it after we
        // clean up. Stop it first and give it a moment to release its handles.
        foreach (var other in Process.GetProcessesByName("Sirkyulo"))
        {
            if (other.Id == Environment.ProcessId) continue;
            try
            {
                other.Kill();
                other.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone, or not ours to stop.
            }
            finally
            {
                other.Dispose();
            }
        }

        try
        {
            AppPaths.EnsureCreated();
            var store = new GroupStore();
            store.Load();

            var restored = new GroupService(store).UninstallEverything();
            Log.Info($"Uninstall complete; {restored} shortcut(s) returned to the desktop.");
        }
        catch (Exception ex)
        {
            // Never block an uninstall. A failure here leaves files behind, which is far
            // better than an uninstaller that refuses to finish.
            Log.Error("Uninstall cleanup failed.", ex);
        }
    }

    private static string Plural(int count) => count == 1 ? "1 item" : $"{count} items";

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception on the UI thread.", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Forwarders are short-lived by design; logging their exit just buries the real
        // lifecycle of the resident instance under noise.
        if (!_isForwarder) Log.Info("Sirkyulo exiting.");
        _tray?.Dispose();
        _watcher?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
