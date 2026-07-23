using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sirkyulo.App.Services;
using Sirkyulo.Core;
using Sirkyulo.Shell.Windows;

// WPF has its own GroupItem (a ListView grouping container), which is nothing to do with ours.
using GroupItem = Sirkyulo.Core.GroupItem;

namespace Sirkyulo.App;

/// <summary>
/// The radial menu. One is created per activation and thrown away on close — it holds a
/// snapshot of the group, so there is no state to invalidate when the group changes underneath.
/// </summary>
public partial class RingWindow : Window
{
    private const double IconSize = 64;
    private const double HoverScale = 1.12;

    private static readonly Duration OpenDuration = TimeSpan.FromMilliseconds(180);
    private static readonly Duration CloseDuration = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(16);

    private readonly Group _group;
    private readonly GroupService _service;
    private readonly RingGeometry _ring;

    private bool _closing;
    private GlobalMouseHook? _outsideClick;

    /// <summary>The single ring on screen. Opening another replaces it.</summary>
    private static RingWindow? _current;

    private RingWindow(Group group, GroupService service)
    {
        _group = group;
        _service = service;
        _ring = RingLayout.Compute(Math.Max(group.Items.Count, 1), IconSize);

        InitializeComponent();

        Width = _ring.Extent * 2;
        Height = _ring.Extent * 2;

        // Fill the hole inside the ring, stopping just short of the icons. This is what
        // covers the group's own desktop icon, which sits directly under the cursor.
        var hub = (_ring.Radius - IconSize / 2 - 4) * 2;
        Hub.Width = Hub.Height = Math.Max(96, hub);

        BuildItems();
        SetCentre(group.Name, DescribeCount(group.Items.Count));
    }

    // ---------------------------------------------------------------- lifecycle

    public static void Open(Group group, GroupService service)
    {
        CloseCurrent();

        var window = new RingWindow(group, service);
        _current = window;
        window.Show();
        window.Activate();
    }

    public static void CloseCurrent()
    {
        var existing = _current;
        _current = null;
        existing?.DismissImmediately();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Position in physical pixels against the monitor the cursor is actually on, so the
        // ring appears at the right size and fully on screen regardless of per-monitor DPI.
        var ctx = ScreenGeometry.GetCursorContext();
        var sizePx = (int)Math.Ceiling(_ring.Extent * 2 * ctx.Scale);
        var (x, y) = ScreenGeometry.CentreAndClamp(ctx, sizePx);

        var hwnd = new WindowInteropHelper(this).Handle;
        ScreenGeometry.PlaceWindow(hwnd, x, y, sizePx, sizePx);

        // Explorer owns the foreground when a group icon is double-clicked, and the ring is
        // created by our resident tray process — so plain activation is refused and the menu
        // ends up visible but never focused. Force it, or Esc and focus-loss both do nothing.
        ForegroundActivation.ForceForeground(hwnd);
    }

    protected override void OnClosed(EventArgs e)
    {
        _outsideClick?.Dispose();
        _outsideClick = null;
        base.OnClosed(e);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PlayOpenAnimation();

        // Belt and braces. Even with activation forced, a click on the desktop or on an
        // already-active window need not change focus at all, so focus-loss alone is not a
        // reliable "the user clicked away" signal. This watches the actual press.
        _outsideClick = new GlobalMouseHook();
        _outsideClick.ButtonPressed += OnGlobalButtonPressed;
    }

    private void OnGlobalButtonPressed(int x, int y)
    {
        // Ignore anything landing on one of our own windows: the ring itself handles its own
        // clicks, and an item's context menu is a separate top-level window that would
        // otherwise dismiss the ring the instant the user tried to use it.
        if (ForegroundActivation.IsOwnWindowAt(x, y)) return;

        Dispatcher.InvokeAsync(Dismiss);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Dismiss();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key is Key.Escape)
        {
            e.Handled = true;
            Dismiss();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // Anything that reaches the window itself is backdrop, since items handle their own
        // clicks. Clicking away is the fastest way out of a menu you opened by accident.
        Dismiss();
    }

    // ---------------------------------------------------------------- content

    private void BuildItems()
    {
        var centre = _ring.Extent;

        if (_group.Items.Count == 0)
        {
            SetCentre(_group.Name, "Drag shortcuts onto this icon to fill it");
            return;
        }

        for (var i = 0; i < _group.Items.Count; i++)
        {
            var item = _group.Items[i];
            var point = _ring.Points[i];

            var element = CreateItem(item);
            Canvas.SetLeft(element, centre + point.X - IconSize / 2);
            Canvas.SetTop(element, centre + point.Y - IconSize / 2);
            ItemCanvas.Children.Add(element);
        }
    }

    private FrameworkElement CreateItem(GroupItem item)
    {
        var image = new Image
        {
            Source = IconCache.Get(item.StoredPath),
            Stretch = Stretch.UniformToFill,
        };

        var tile = new Border
        {
            Width = IconSize,
            Height = IconSize,
            CornerRadius = new CornerRadius(12),
            Background = (Brush)FindResource("SurfaceElevated"),
            BorderBrush = (Brush)FindResource("BorderDefault"),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = image,
            Cursor = Cursors.Hand,
            ToolTip = null,
            Effect = (System.Windows.Media.Effects.Effect)FindResource("MenuShadow"),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup
            {
                Children = [new ScaleTransform(1, 1), new TranslateTransform()],
            },
            Opacity = 0,
            Tag = item,
        };

        tile.MouseEnter += (_, _) =>
        {
            SetCentre(item.DisplayName, "Click to open");
            Scale(tile, HoverScale, TimeSpan.FromMilliseconds(120));
        };

        tile.MouseLeave += (_, _) =>
        {
            SetCentre(_group.Name, DescribeCount(_group.Items.Count));
            Scale(tile, 1.0, TimeSpan.FromMilliseconds(140));
        };

        // Swallow the press. Without this it bubbles to the window's backdrop handler, which
        // dismisses the ring before the release can ever reach this tile — the menu closes
        // and nothing launches.
        tile.MouseLeftButtonDown += (_, e) => e.Handled = true;

        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Launch(item);
        };

        tile.ContextMenu = BuildItemMenu(item);
        return tile;
    }

    private ContextMenu BuildItemMenu(GroupItem item)
    {
        var menu = new ContextMenu();

        var remove = new MenuItem { Header = "Remove from group" };
        remove.Click += (_, _) =>
        {
            _service.RemoveItem(_group.Id, item.Id);
            DismissImmediately();
        };

        var reveal = new MenuItem { Header = "Open file location" };
        reveal.Click += (_, _) =>
        {
            ShellEnvironment.RevealInExplorer(item.StoredPath);
            DismissImmediately();
        };

        menu.Items.Add(remove);
        menu.Items.Add(reveal);
        return menu;
    }

    private void SetCentre(string title, string? subtitle)
    {
        // Constrain to the hole inside the ring so the readout can never reach the icons.
        var available = Math.Max(80, (_ring.Radius - IconSize / 2) * 2 - 16);
        Centre.MaxWidth = available;
        CentreTitle.MaxWidth = available;
        CentreSub.MaxWidth = available;

        CentreTitle.Text = title;
        CentreSub.Text = subtitle ?? "";
        CentreSub.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string DescribeCount(int count) => count switch
    {
        0 => "Empty",
        1 => "1 item",
        _ => $"{count} items",
    };

    private void Launch(GroupItem item)
    {
        if (!ShellEnvironment.Open(item.StoredPath))
            Log.Warn($"Failed to launch '{item.StoredPath}'.");

        DismissImmediately();
    }

    // ---------------------------------------------------------------- motion

    private void PlayOpenAnimation()
    {
        var ease = (IEasingFunction)FindResource("GeistEase");
        var soft = (IEasingFunction)FindResource("GeistEaseSoft");

        Scrim.Opacity = 0;
        Scrim.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, OpenDuration) { EasingFunction = soft });

        Hub.Opacity = 0;
        Hub.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, OpenDuration) { EasingFunction = soft });

        Centre.Opacity = 0;
        Centre.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, OpenDuration) { BeginTime = Stagger, EasingFunction = soft });

        for (var i = 0; i < ItemCanvas.Children.Count; i++)
        {
            if (ItemCanvas.Children[i] is not Border tile) continue;

            var group = (TransformGroup)tile.RenderTransform;
            var scale = (ScaleTransform)group.Children[0];
            var translate = (TranslateTransform)group.Children[1];
            var point = _ring.Points[i];
            var begin = TimeSpan.FromTicks(Stagger.Ticks * i);

            // Items fly out from the centre, which reads as the group opening rather than
            // a panel appearing.
            translate.X = -point.X;
            translate.Y = -point.Y;
            scale.ScaleX = scale.ScaleY = 0.6;

            translate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-point.X, 0, OpenDuration) { BeginTime = begin, EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-point.Y, 0, OpenDuration) { BeginTime = begin, EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.6, 1, OpenDuration) { BeginTime = begin, EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.6, 1, OpenDuration) { BeginTime = begin, EasingFunction = ease });
            tile.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, OpenDuration) { BeginTime = begin, EasingFunction = soft });
        }
    }

    private static void Scale(UIElement element, double to, TimeSpan duration)
    {
        if (element.RenderTransform is not TransformGroup group) return;
        if (group.Children[0] is not ScaleTransform scale) return;

        var animation = new DoubleAnimation(to, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>Fades out, then closes. Safe to call repeatedly.</summary>
    private void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        if (ReferenceEquals(_current, this)) _current = null;

        var fade = new DoubleAnimation(1, 0, CloseDuration);
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Closes without the fade, for when something else is about to take the screen.</summary>
    private void DismissImmediately()
    {
        if (_closing) return;
        _closing = true;
        if (ReferenceEquals(_current, this)) _current = null;
        Close();
    }
}
