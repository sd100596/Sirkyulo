using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Win32;
using Sirkyulo.App.Services;
using Sirkyulo.Core;

namespace Sirkyulo.App;

/// <summary>
/// The notification-area presence. This is Sirkyulo's entire chrome — there is no main window,
/// because the product is the desktop icons themselves.
/// </summary>
public sealed class TrayMenu : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Sirkyulo";

    private readonly GroupService _service;
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _startupItem;

    public TrayMenu(GroupService service)
    {
        _service = service;

        _startupItem = new MenuItem
        {
            Header = "Run at startup",
            IsCheckable = true,
            IsChecked = IsRunAtStartupEnabled(),
        };
        _startupItem.Click += (_, _) =>
        {
            SetRunAtStartup(_startupItem.IsChecked);

            // Re-read rather than trusting the toggle: the registry is the truth, and if the
            // write failed the tick must not claim otherwise.
            _startupItem.IsChecked = IsRunAtStartupEnabled();
        };

        var newGroup = new MenuItem { Header = "New group…" };
        newGroup.Click += (_, _) => CreateGroup();

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Application.Current.Shutdown();

        var menu = new ContextMenu();

        // The entry can be removed behind our back — by a startup manager, a cleanup tool, or
        // the user editing it directly — so re-check every time the menu is shown.
        menu.Opened += (_, _) => _startupItem.IsChecked = IsRunAtStartupEnabled();

        menu.Items.Add(newGroup);
        menu.Items.Add(new Separator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(
                new Uri("pack://application:,,,/Assets/tray.ico", UriKind.Absolute)),
            ToolTipText = "Sirkyulo",
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
        };

        // Left click is the fastest route to the only action that matters.
        _icon.TrayLeftMouseUp += (_, _) => CreateGroup();

        _icon.ForceCreate();
    }

    private void CreateGroup()
    {
        var name = NamePromptWindow.Ask();
        if (name is null) return;

        try
        {
            var group = _service.CreateGroup(name);
            Log.Info($"Created group '{group.Name}' at {group.DesktopLinkPath}.");
            Notify("Group created", $"'{group.Name}' is on your desktop. Drag shortcuts onto it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error("Could not create the group.", ex);
            Notify("Could not create the group", ex.Message);
        }
    }

    /// <summary>
    /// Shows a message. Routed through our own overlay rather than a tray balloon: Windows
    /// silently collapses balloons that repeat the same text, so a user who triggers the same
    /// condition twice sees nothing the second time.
    /// </summary>
    public static void Notify(string title, string message) => ToastWindow.Show(title, message);

    // ---------------------------------------------------------------- startup

    private static bool IsRunAtStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    private static void SetRunAtStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (exe is null) return;
                key.SetValue(RunValue, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(RunValue, throwOnMissingValue: false);
            }

            Log.Info($"Run at startup {(enabled ? "enabled" : "disabled")}.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Log.Error("Could not update the startup entry.", ex);
        }
    }

    public void Dispose() => _icon.Dispose();
}
