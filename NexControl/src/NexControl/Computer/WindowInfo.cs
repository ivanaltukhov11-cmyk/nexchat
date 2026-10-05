using System.Diagnostics;
using static NexControl.Native.NativeMethods;

namespace NexControl.Computer;

/// <summary>Basic facts about a top-level window.</summary>
public sealed record WindowInfo(nint Handle, string Title, string ClassName, int ProcessId, string ProcessName, System.Windows.Int32Rect Bounds)
{
    public static WindowInfo FromHandle(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        string processName = "";
        try { processName = Process.GetProcessById((int)pid).ProcessName; } catch { /* process exited or access denied */ }
        var r = GetVisibleWindowRect(hwnd);
        return new WindowInfo(hwnd, GetWindowTitle(hwnd), GetWindowClass(hwnd), (int)pid, processName,
            new System.Windows.Int32Rect(r.Left, r.Top, Math.Max(0, r.Width), Math.Max(0, r.Height)));
    }

    public override string ToString() => string.IsNullOrEmpty(Title) ? $"[{ProcessName}]" : $"{Title} ({ProcessName})";
}

public static class WindowService
{
    private static readonly int OwnProcessId = Environment.ProcessId;

    public static WindowInfo? GetForeground()
    {
        nint hwnd = GetForegroundWindow();
        return hwnd == 0 ? null : WindowInfo.FromHandle(hwnd);
    }

    /// <summary>
    /// The window you were using before you clicked into Nex Control: the topmost visible app window
    /// that does not belong to Nex Control. Lets buttons like "Active Window" work without a countdown.
    /// </summary>
    public static WindowInfo? GetTargetWindow()
    {
        nint fg = GetForegroundWindow();
        if (fg != 0 && !IsOwn(fg)) return WindowInfo.FromHandle(fg);

        for (nint h = fg == 0 ? 0 : GetWindow(fg, GW_HWNDNEXT); h != 0; h = GetWindow(h, GW_HWNDNEXT))
        {
            if (IsCandidate(h)) return WindowInfo.FromHandle(h);
        }
        // Foreground unknown: walk all windows in z-order.
        WindowInfo? found = null;
        EnumWindows((h, _) =>
        {
            if (IsCandidate(h)) { found = WindowInfo.FromHandle(h); return false; }
            return true;
        }, 0);
        return found;
    }

    public static bool IsOwn(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == OwnProcessId;
    }

    private static bool IsCandidate(nint h)
    {
        if (!IsWindowVisible(h) || IsIconic(h) || IsOwn(h)) return false;
        if (DwmGetWindowAttributeInt(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        if ((GetWindowLongW(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return false;
        var r = GetVisibleWindowRect(h);
        if (r.Width < 50 || r.Height < 50) return false;
        string cls = GetWindowClass(h);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        return GetWindowTextLength(h) > 0;
    }

    public static void Activate(nint hwnd) => SetForegroundWindow(hwnd);
}
