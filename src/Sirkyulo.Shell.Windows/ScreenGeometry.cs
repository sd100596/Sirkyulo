using System.Runtime.InteropServices;

namespace Sirkyulo.Shell.Windows;

/// <summary>A rectangle in physical pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Where the cursor is and what the monitor under it looks like, in physical pixels.</summary>
public sealed record CursorContext(int X, int Y, PixelRect WorkArea, double Scale);

/// <summary>
/// Monitor and cursor geometry, always in physical pixels.
///
/// WPF's own Left/Top are device-independent units interpreted against a DPI that is
/// ambiguous the moment a second monitor has different scaling. The ring is placed from raw
/// cursor coordinates, so it works exclusively in physical pixels and lets WPF scale the
/// content inside the frame we set.
/// </summary>
public static class ScreenGeometry
{
    private const int MonitorDefaultToNearest = 0x00000002;
    private const int MdtEffectiveDpi = 0;

    /// <summary>Cursor position plus the work area and scale factor of the monitor it sits on.</summary>
    public static CursorContext GetCursorContext()
    {
        GetCursorPos(out var point);

        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        var work = GetMonitorInfo(monitor, ref info)
            ? new PixelRect(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom)
            : new PixelRect(0, 0, 1920, 1080);

        var scale = 1.0;
        if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
            scale = dpiX / 96.0;

        return new CursorContext(point.X, point.Y, work, scale);
    }

    /// <summary>
    /// Centres a square of <paramref name="sizePx"/> on the cursor, then slides it fully inside
    /// the work area. A group icon parked at a screen edge must still show its whole ring.
    /// </summary>
    public static (int X, int Y) CentreAndClamp(CursorContext ctx, int sizePx)
    {
        var x = ctx.X - sizePx / 2;
        var y = ctx.Y - sizePx / 2;

        // Clamp the far edge first, then the near edge, so an oversized ring on a small
        // screen ends up flush at the top-left rather than off the bottom-right.
        x = Math.Min(x, ctx.WorkArea.Right - sizePx);
        y = Math.Min(y, ctx.WorkArea.Bottom - sizePx);
        x = Math.Max(x, ctx.WorkArea.Left);
        y = Math.Max(y, ctx.WorkArea.Top);

        return (x, y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point pt, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = -1;

    /// <summary>Sets a window's frame directly in physical pixels.</summary>
    public static void PlaceWindow(nint hwnd, int x, int y, int widthPx, int heightPx, bool activate = true)
    {
        var flags = SwpShowWindow | (activate ? 0 : SwpNoActivate);
        SetWindowPos(hwnd, HwndTopmost, x, y, widthPx, heightPx, flags);
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    /// <summary>
    /// Makes a window ignore the mouse entirely and never take focus. For overlays that must
    /// inform without ever getting in the way — a click passes straight through to whatever is
    /// underneath.
    /// </summary>
    public static void MakeClickThrough(nint hwnd)
    {
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExTransparent | WsExNoActivate | WsExToolWindow);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
}
