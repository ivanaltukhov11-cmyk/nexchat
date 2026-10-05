using NexControl.Native;
using static NexControl.Native.NativeMethods;

namespace NexControl.Screen;

/// <summary>Screen geometry in physical pixels (the coordinate system used by the mouse, UI Automation and screenshots).</summary>
public static class ScreenInfo
{
    public static System.Windows.Int32Rect VirtualScreen => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    public static int MonitorCount => GetSystemMetrics(SM_CMONITORS);

    public static System.Windows.Int32Rect PrimaryScreen => new(0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    /// <summary>Bounds of the monitor that contains (or is nearest to) the given point.</summary>
    public static System.Windows.Int32Rect MonitorAt(int x, int y)
    {
        nint mon = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (mon != 0 && GetMonitorInfo(mon, ref info))
            return new(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Width, info.rcMonitor.Height);
        return PrimaryScreen;
    }


    public static (int X, int Y) PrimaryCenter => (GetSystemMetrics(SM_CXSCREEN) / 2, GetSystemMetrics(SM_CYSCREEN) / 2);

    public static (int X, int Y) Clamp(int x, int y)
    {
        var v = VirtualScreen;
        return (Math.Clamp(x, v.X, v.X + v.Width - 1), Math.Clamp(y, v.Y, v.Y + v.Height - 1));
    }
}
