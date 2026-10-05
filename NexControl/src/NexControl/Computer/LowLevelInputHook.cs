using System.Runtime.InteropServices;
using static NexControl.Native.NativeMethods;

namespace NexControl.Computer;

public enum RealInputKind { MouseMove, MouseButton, MouseWheel, Key }

/// <summary>
/// Notices input from the PHYSICAL mouse and keyboard (input injected by Nex Control or other software is ignored).
///
/// Privacy: this hook is only installed while an automation runs (to detect that you took control) or while you
/// explicitly turn on click capture in the Inspector. It never records which keys you press: keyboard events only
/// report "a key was pressed" plus whether a modifier (Ctrl/Alt/Win) was involved, so shortcuts can be ignored.
/// </summary>
public sealed class LowLevelInputHook : IDisposable
{
    public sealed record RealInput(RealInputKind Kind, int X, int Y, bool IsDown, bool IsRightButton, bool WithModifier);

    /// <summary>Raised on the hook thread. Handlers must return quickly.</summary>
    public event Action<RealInput>? Input;

    private readonly bool _mouse;
    private readonly bool _keyboard;
    private Thread? _thread;
    private uint _threadId;
    private nint _mouseHook, _keyboardHook;
    private LowLevelProc? _mouseProc, _keyboardProc; // kept alive so the GC does not collect them
    private readonly ManualResetEventSlim _started = new();

    public LowLevelInputHook(bool mouse, bool keyboard)
    {
        _mouse = mouse;
        _keyboard = keyboard;
    }

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Nex input watcher" };
        _thread.Start();
        _started.Wait(2000);
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        nint module = GetModuleHandle(null);
        if (_mouse)
        {
            _mouseProc = MouseProc;
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        }
        if (_keyboard)
        {
            _keyboardProc = KeyboardProc;
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        }
        _started.Set();

        // Low-level hooks are delivered through this thread's message loop.
        while (GetMessage(out MSG msg, 0, 0, 0) > 0) { }

        if (_mouseHook != 0) UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != 0) UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = _keyboardHook = 0;
    }

    private nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            bool injected = (data.flags & LLMHF_INJECTED) != 0 || data.dwExtraInfo == NexInputSignature;
            if (!injected)
            {
                int msg = (int)wParam;
                RealInput? input = msg switch
                {
                    WM_MOUSEMOVE => new RealInput(RealInputKind.MouseMove, data.pt.X, data.pt.Y, false, false, false),
                    WM_LBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN => new RealInput(RealInputKind.MouseButton, data.pt.X, data.pt.Y, true, false, false),
                    WM_RBUTTONDOWN => new RealInput(RealInputKind.MouseButton, data.pt.X, data.pt.Y, true, true, false),
                    WM_LBUTTONUP => new RealInput(RealInputKind.MouseButton, data.pt.X, data.pt.Y, false, false, false),
                    WM_RBUTTONUP => new RealInput(RealInputKind.MouseButton, data.pt.X, data.pt.Y, false, true, false),
                    WM_MOUSEWHEEL or WM_MOUSEHWHEEL => new RealInput(RealInputKind.MouseWheel, data.pt.X, data.pt.Y, false, false, false),
                    _ => null,
                };
                if (input != null) Raise(input);
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool injected = (data.flags & LLKHF_INJECTED) != 0 || data.dwExtraInfo == NexInputSignature;
                if (!injected)
                {
                    bool isModifierKey = Core.Input.KeyNames.IsModifier((int)data.vkCode);
                    bool modifierHeld = GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x12) < 0
                                        || GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;
                    // Only "a key was pressed" leaves this method — never which key.
                    Raise(new RealInput(RealInputKind.Key, 0, 0, true, false, isModifierKey || modifierHeld));
                }
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void Raise(RealInput input)
    {
        try { Input?.Invoke(input); }
        catch { /* never let a handler break system input */ }
    }

    public void Dispose()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(1000);
        _thread = null;
    }
}
