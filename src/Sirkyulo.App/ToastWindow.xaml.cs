using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Sirkyulo.Shell.Windows;

namespace Sirkyulo.App;

/// <summary>
/// A transient message shown next to the cursor.
///
/// Replaces the tray balloon, which proved unusable for this: Windows collapses repeated
/// balloons carrying the same text, so telling the user the same thing twice in a row
/// silently shows nothing the second time. It also appears in the far corner of the screen,
/// nowhere near the desktop icon the user just dropped onto.
///
/// This window owns its own presentation, so it always appears, always in the same place, and
/// cannot be suppressed by notification settings or Focus Assist.
/// </summary>
public partial class ToastWindow : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(3.4);
    private static readonly Duration FadeIn = TimeSpan.FromMilliseconds(140);
    private static readonly Duration FadeOut = TimeSpan.FromMilliseconds(220);

    /// <summary>Gap between the cursor and the card, so the pointer never covers the text.</summary>
    private const double CursorGap = 26;

    private static ToastWindow? _current;

    private DispatcherTimer? _timer;
    private bool _closing;

    private ToastWindow(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = message;
        BodyText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        Opacity = 0;
    }

    /// <summary>
    /// Shows a message near the cursor. Replaces any message already on screen, so a burst of
    /// events reads as one current status rather than a stack of stale cards.
    /// </summary>
    public static void Show(string title, string message)
    {
        _current?.Dismiss();

        var toast = new ToastWindow(title, message);
        _current = toast;
        toast.Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Click-through and never-activate: this must not steal focus from the ring or eat a
        // click aimed at whatever is underneath it.
        ScreenGeometry.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        Reposition();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeIn));

        _timer = new DispatcherTimer { Interval = Lifetime };
        _timer.Tick += (_, _) => Dismiss();
        _timer.Start();
    }

    private void Reposition()
    {
        // Height is only known once the text has wrapped, so placement happens after render.
        var ctx = ScreenGeometry.GetCursorContext();
        var widthPx = (int)Math.Ceiling(ActualWidth * ctx.Scale);
        var heightPx = (int)Math.Ceiling(ActualHeight * ctx.Scale);
        var gap = (int)(CursorGap * ctx.Scale);

        var x = ctx.X - widthPx / 2;
        var y = ctx.Y + gap;

        // Flip above the cursor when there is no room below, then clamp horizontally.
        if (y + heightPx > ctx.WorkArea.Bottom) y = ctx.Y - gap - heightPx;

        x = Math.Clamp(x, ctx.WorkArea.Left + 8, Math.Max(ctx.WorkArea.Left + 8, ctx.WorkArea.Right - widthPx - 8));
        y = Math.Clamp(y, ctx.WorkArea.Top + 8, Math.Max(ctx.WorkArea.Top + 8, ctx.WorkArea.Bottom - heightPx - 8));

        ScreenGeometry.PlaceWindow(new WindowInteropHelper(this).Handle, x, y, widthPx, heightPx, activate: false);
    }

    private void Dismiss()
    {
        if (_closing) return;
        _closing = true;

        _timer?.Stop();
        if (ReferenceEquals(_current, this)) _current = null;

        var fade = new DoubleAnimation(Opacity, 0, FadeOut);
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
