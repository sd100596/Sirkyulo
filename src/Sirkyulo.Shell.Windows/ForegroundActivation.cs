using System.Runtime.InteropServices;

namespace Sirkyulo.Shell.Windows;

/// <summary>
/// Forces a window to the foreground past Windows' foreground lock.
///
/// A plain SetForegroundWindow silently fails when the calling process is not already the
/// foreground process. Sirkyulo hits this every single time: the ring is created by the
/// resident tray process while Explorer owns the foreground, so the menu appears on top but
/// never actually receives activation — which kills both keyboard focus and any
/// dismiss-on-focus-loss behaviour.
///
/// Briefly attaching our input queue to the foreground thread's makes the system treat the
/// call as coming from the active app, which is the documented way through this.
/// </summary>
public static class ForegroundActivation
{
    public static void ForceForeground(nint hwnd)
    {
        if (hwnd == nint.Zero) return;

        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
        {
            SetFocus(hwnd);
            return;
        }

        var ourThread = GetCurrentThreadId();
        var theirThread = foreground == nint.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);

        var attached = theirThread != 0 && theirThread != ourThread
                       && AttachThreadInput(ourThread, theirThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            SetActiveWindow(hwnd);
            SetFocus(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(ourThread, theirThread, false);
        }
    }

    /// <summary>True when the window at this screen point belongs to the current process.</summary>
    public static bool IsOwnWindowAt(int x, int y)
    {
        var hwnd = WindowFromPoint(new Point { X = x, Y = y });
        if (hwnd == nint.Zero) return false;

        GetWindowThreadProcessId(hwnd, out var processId);
        return processId == GetCurrentProcessId();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern nint SetActiveWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BringWindowToTop(nint hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
}
