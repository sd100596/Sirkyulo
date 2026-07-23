using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Sirkyulo.Shell.Windows;

namespace Sirkyulo.App;

public partial class NamePromptWindow : Window
{
    private NamePromptWindow(string initial)
    {
        InitializeComponent();
        NameBox.Text = initial;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Same foreground lock that bit the ring. This dialog is opened by the resident tray
        // process while the shell still owns the foreground, so Windows refuses activation and
        // Focus() quietly does nothing — the caret never lands in the box. It appeared to work
        // on the second attempt only because by then our process was already foreground.
        ForegroundActivation.ForceForeground(new WindowInteropHelper(this).Handle);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // Focus after the window really is active, and set keyboard focus explicitly —
        // logical focus alone does not put the caret in the box.
        Activate();
        NameBox.Focus();
        Keyboard.Focus(NameBox);
        NameBox.SelectAll();
    }

    public string? Result { get; private set; }

    /// <summary>Returns the chosen name, or null if the user backed out.</summary>
    public static string? Ask(string initial = "New Group")
    {
        var window = new NamePromptWindow(initial);
        return window.ShowDialog() == true ? window.Result : null;
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0) return;

        Result = name;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    /// <summary>The window has no title bar, so the card itself is the drag handle.</summary>
    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
