using System.Windows.Interop;
using NexControl.Core.Input;
using static NexControl.Native.NativeMethods;

namespace NexControl.Computer;

public enum HotkeyAction { OpenApp = 1, Screenshot = 2, EmergencyStop = 3, PauseAutomation = 4 }

/// <summary>System-wide shortcuts via RegisterHotKey. They work even when Nex Control is minimized or in the background.</summary>
public sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = [];

    public event Action<HotkeyAction>? Pressed;

    public GlobalHotkeys(nint windowHandle)
    {
        _source = HwndSource.FromHwnd(windowHandle) ?? throw new InvalidOperationException("No window source.");
        _source.AddHook(WndProc);
    }

    /// <summary>Registers (or re-registers) a shortcut. Returns an error message, or null on success.</summary>
    public string? Register(HotkeyAction action, string text)
    {
        int id = (int)action;
        if (_registered.Remove(id)) UnregisterHotKey(_source.Handle, id);
        if (string.IsNullOrWhiteSpace(text)) return null; // disabled

        if (!KeyCombo.TryParse(text, out var combo, out string? error)) return error;
        if (combo!.VirtualKey == 0) return "A shortcut needs a non-modifier key";
        if (!combo.HasModifier) return "Use at least one of Ctrl, Shift, Alt or Win";

        uint mods = MOD_NOREPEAT;
        if (combo.Ctrl) mods |= MOD_CONTROL;
        if (combo.Shift) mods |= MOD_SHIFT;
        if (combo.Alt) mods |= MOD_ALT;
        if (combo.Win) mods |= MOD_WIN;
        if (!RegisterHotKey(_source.Handle, id, mods, (uint)combo.VirtualKey))
            return $"{combo} is already used by another program";
        _registered.Add(id);
        return null;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && Enum.IsDefined(typeof(HotkeyAction), (int)wParam))
        {
            handled = true;
            Pressed?.Invoke((HotkeyAction)(int)wParam);
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (int id in _registered) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _source.RemoveHook(WndProc);
    }
}
