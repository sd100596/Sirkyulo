using System.Runtime.InteropServices;

namespace Sirkyulo.Shell.Windows;

/// <summary>
/// Reports mouse-button presses anywhere on screen, including in other applications.
///
/// This is the backstop for dismissing the ring. Relying on the window losing focus is not
/// enough on its own — activation can be refused, and a click on the desktop or on an
/// already-active window may not change focus at all. A low-level hook sees the press
/// regardless.
///
/// The hook never swallows the event, so the click still reaches whatever the user aimed at.
/// </summary>
public sealed class GlobalMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmNcLButtonDown = 0x00A1;

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    // Held in a field on purpose: if the delegate is collected the hook callback becomes a
    // dangling function pointer and the process dies on the next mouse move.
    private readonly HookProc _callback;
    private nint _hook;

    /// <summary>Raised on any button press, with the screen position in physical pixels.</summary>
    public event Action<int, int>? ButtonPressed;

    public GlobalMouseHook()
    {
        _callback = OnMouseEvent;
        _hook = SetWindowsHookEx(WhMouseLl, _callback, nint.Zero, 0);
    }

    private nint OnMouseEvent(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var message = (int)wParam;
            if (message is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmNcLButtonDown)
            {
                var data = Marshal.PtrToStructure<MouseHookStruct>(lParam);
                ButtonPressed?.Invoke(data.pt.X, data.pt.Y);
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookStruct
    {
        public Point pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    public void Dispose()
    {
        if (_hook == nint.Zero) return;

        UnhookWindowsHookEx(_hook);
        _hook = nint.Zero;
    }
}
