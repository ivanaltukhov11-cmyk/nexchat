using System.Runtime.InteropServices;
using NexControl.Computer;
using NexControl.Core.Settings;
using NexControl.Screen;
using static NexControl.Native.NativeMethods;

namespace NexControl.Mouse;

public enum MouseButton { Left, Right, Middle }

/// <summary>
/// Moves and clicks the REAL Windows cursor with SendInput. Coordinates are physical screen pixels
/// (the app is per-monitor DPI aware, so they match what screenshots and UI Automation report).
/// </summary>
public sealed class MouseController(Func<MouseSettings> settings, StopController stop)
{
    public (int X, int Y) GetPosition()
    {
        GetCursorPos(out POINT p);
        return (p.X, p.Y);
    }

    /// <summary>Jumps the cursor straight to (x, y).</summary>
    public void MoveMouse(int x, int y)
    {
        stop.Token.ThrowIfCancellationRequested();
        (x, y) = ScreenInfo.Clamp(x, y);
        var v = ScreenInfo.VirtualScreen;
        // Absolute coordinates are normalised to 0..65535 across the whole virtual desktop (all monitors).
        int nx = (int)Math.Round((x - v.X) * 65535.0 / Math.Max(1, v.Width - 1));
        int ny = (int)Math.Round((y - v.Y) * 65535.0 / Math.Max(1, v.Height - 1));
        Send(MouseInput(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, nx, ny));
    }

    /// <summary>Glides the cursor to (x, y) at the configured speed. Cancelled instantly by the emergency stop.</summary>
    public async Task MoveMouseSmoothAsync(int x, int y, CancellationToken ct = default)
    {
        using var linked = stop.Link(ct);
        var token = linked.Token;
        (x, y) = ScreenInfo.Clamp(x, y);
        var (sx, sy) = GetPosition();
        double distance = Math.Sqrt(Math.Pow(x - sx, 2) + Math.Pow(y - sy, 2));
        if (distance < 2) { MoveMouse(x, y); return; }

        double speed = Math.Max(100, settings().MovementSpeed);
        double durationMs = Math.Clamp(distance / speed * 1000, 60, 3000);
        const double frameMs = 8;
        int frames = Math.Max(2, (int)(durationMs / frameMs));
        var start = DateTime.UtcNow;
        for (int i = 1; i <= frames; i++)
        {
            token.ThrowIfCancellationRequested();
            double t = (double)i / frames;
            double eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; // ease-in-out cubic
            MoveMouse((int)Math.Round(sx + (x - sx) * eased), (int)Math.Round(sy + (y - sy) * eased));
            double targetElapsed = i * frameMs;
            double actual = (DateTime.UtcNow - start).TotalMilliseconds;
            if (targetElapsed > actual) await Task.Delay(TimeSpan.FromMilliseconds(targetElapsed - actual), token);
        }
        MoveMouse(x, y);
    }

    /// <summary>Moves using the user's Smooth/Instant setting (or an explicit override).</summary>
    public Task MoveAsync(int x, int y, bool? smooth = null, CancellationToken ct = default)
    {
        if (smooth ?? settings().SmoothMovement) return MoveMouseSmoothAsync(x, y, ct);
        MoveMouse(x, y);
        return Task.CompletedTask;
    }

    public void MouseDown(MouseButton button = MouseButton.Left)
    {
        stop.Token.ThrowIfCancellationRequested();
        Send(MouseInput(button switch
        {
            MouseButton.Right => MOUSEEVENTF_RIGHTDOWN,
            MouseButton.Middle => MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSEEVENTF_LEFTDOWN,
        }));
    }

    /// <summary>Always allowed, even after a stop, so a button is never left held down.</summary>
    public void MouseUp(MouseButton button = MouseButton.Left)
    {
        Send(MouseInput(button switch
        {
            MouseButton.Right => MOUSEEVENTF_RIGHTUP,
            MouseButton.Middle => MOUSEEVENTF_MIDDLEUP,
            _ => MOUSEEVENTF_LEFTUP,
        }));
    }

    public async Task ClickAsync(MouseButton button = MouseButton.Left, CancellationToken ct = default)
    {
        using var linked = stop.Link(ct);
        MouseDown(button);
        try
        {
            int delay = settings().ClickDelayMs;
            if (delay > 0) await Task.Delay(delay, linked.Token);
        }
        finally
        {
            MouseUp(button);
        }
    }

    public Task LeftClickAsync(CancellationToken ct = default) => ClickAsync(MouseButton.Left, ct);
    public Task RightClickAsync(CancellationToken ct = default) => ClickAsync(MouseButton.Right, ct);

    public async Task DoubleClickAsync(CancellationToken ct = default)
    {
        using var linked = stop.Link(ct);
        await ClickAsync(MouseButton.Left, linked.Token);
        await Task.Delay(Math.Max(1, settings().DoubleClickGapMs), linked.Token);
        await ClickAsync(MouseButton.Left, linked.Token);
    }

    /// <summary>Scrolls the wheel. Positive = up/away from you, negative = down. One unit = one notch.</summary>
    public async Task ScrollAsync(int notches, CancellationToken ct = default)
    {
        using var linked = stop.Link(ct);
        int step = Math.Max(1, settings().ScrollStep);
        int direction = Math.Sign(notches);
        for (int i = 0; i < Math.Abs(notches); i++)
        {
            linked.Token.ThrowIfCancellationRequested();
            Send(MouseInput(MOUSEEVENTF_WHEEL, 0, 0, direction * step));
            await Task.Delay(30, linked.Token);
        }
    }

    public async Task ScrollHorizontalAsync(int notches, CancellationToken ct = default)
    {
        using var linked = stop.Link(ct);
        int step = Math.Max(1, settings().ScrollStep);
        for (int i = 0; i < Math.Abs(notches); i++)
        {
            linked.Token.ThrowIfCancellationRequested();
            Send(MouseInput(MOUSEEVENTF_HWHEEL, 0, 0, Math.Sign(notches) * step));
            await Task.Delay(30, linked.Token);
        }
    }

    /// <summary>Presses the left button at the start point, glides to the end point and releases.</summary>
    public async Task DragAsync(int startX, int startY, int endX, int endY, CancellationToken ct = default)
    {
        using var linked = stop.Link(ct);
        var token = linked.Token;
        await MoveAsync(startX, startY, ct: token);
        await Task.Delay(50, token);
        MouseDown(MouseButton.Left);
        try
        {
            await Task.Delay(80, token);
            // Always glide during a drag: many apps ignore a drag that jumps in a single event.
            await MoveMouseSmoothAsync(endX, endY, token);
            await Task.Delay(80, token);
        }
        finally
        {
            MouseUp(MouseButton.Left);
        }
    }

    /// <summary>Releases any mouse button that is currently held down. Called by the emergency stop.</summary>
    public void ReleaseAllButtons()
    {
        if (GetAsyncKeyState(VK_LBUTTON) < 0) MouseUp(MouseButton.Left);
        if (GetAsyncKeyState(VK_RBUTTON) < 0) MouseUp(MouseButton.Right);
        if (GetAsyncKeyState(VK_MBUTTON) < 0) MouseUp(MouseButton.Middle);
    }

    public static bool IsMousePresent => GetSystemMetrics(SM_MOUSEPRESENT) != 0;

    private static INPUT MouseInput(uint flags, int dx = 0, int dy = 0, int data = 0) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion
        {
            mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags, dwExtraInfo = NexInputSignature },
        },
    };

    private static void Send(params INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new InputBlockedException(Marshal.GetLastWin32Error());
    }
}

/// <summary>
/// Windows refused the input, e.g. while the PC is locked or a UAC prompt is on screen.
/// (Input sent to a window of a program running as administrator is silently dropped by Windows instead;
/// run Nex Control as administrator to control such windows.)
/// </summary>
public sealed class InputBlockedException(int win32Error)
    : Exception($"Windows rejected the input (error {win32Error}). This happens while the PC is locked or a secure prompt such as UAC is showing.");
