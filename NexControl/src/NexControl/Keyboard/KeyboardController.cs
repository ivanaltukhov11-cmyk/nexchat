using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NexControl.Computer;
using NexControl.Core.Input;
using NexControl.Core.Settings;
using NexControl.Mouse;
using static NexControl.Native.NativeMethods;

namespace NexControl.Keyboard;

/// <summary>
/// Sends REAL keyboard input with SendInput. Keys go to whichever window has keyboard focus,
/// exactly as if they were typed on the physical keyboard.
/// </summary>
public sealed class KeyboardController(Func<KeyboardSettings> settings, StopController stop)
{
    // Keys we have pressed but not yet released, so the emergency stop can release them.
    private readonly ConcurrentDictionary<int, byte> _held = new();

    /// <summary>
    /// Types text into the focused window. Uses Unicode input, so any character works regardless of keyboard
    /// layout: letters, numbers, symbols, accents and emoji. "\n" presses Enter and "\t" presses Tab.
    /// </summary>
    /// <param name="checkpoint">Optional callback awaited before each character (the automation engine uses it to pause mid-text).</param>
    public async Task TypeTextAsync(string text, CancellationToken ct = default, TypingSpeed? speed = null,
        Func<CancellationToken, Task>? checkpoint = null)
    {
        using var linked = stop.Link(ct);
        var token = linked.Token;
        var s = settings();
        int delay = speed is { } sp ? new KeyboardSettings { TypingSpeed = sp }.CharacterDelayMs : s.CharacterDelayMs;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        if (delay == 0)
        {
            // Instant: one SendInput call per chunk (Windows accepts a few thousand events per call).
            var batch = new List<INPUT>();
            foreach (char c in text) batch.AddRange(CharInputs(c));
            foreach (var chunk in batch.Chunk(1000))
            {
                token.ThrowIfCancellationRequested();
                Send(chunk);
            }
            return;
        }

        for (int i = 0; i < text.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (checkpoint != null) await checkpoint(token);
            char c = text[i];
            // Keep surrogate pairs (emoji) together.
            if (char.IsHighSurrogate(c) && i + 1 < text.Length)
            {
                Send([.. CharInputs(c), .. CharInputs(text[++i])]);
            }
            else
            {
                Send([.. CharInputs(c)]);
            }
            await Task.Delay(delay, token);
        }
    }

    /// <summary>Presses and releases a key by name: "Enter", "Tab", "F5", "A", "Left", ...</summary>
    public Task PressKeyAsync(string key, CancellationToken ct = default) => PressKeyAsync(KeyNames.GetVirtualKey(key), ct);

    public async Task PressKeyAsync(int vk, CancellationToken ct = default)
    {
        using var linked = stop.Link(ct);
        KeyDown(vk);
        try
        {
            int hold = settings().KeyDelayMs;
            if (hold > 0) await Task.Delay(hold, linked.Token);
        }
        finally
        {
            KeyUp(vk);
        }
    }

    public Task PressEnterAsync(CancellationToken ct = default) => PressKeyAsync(0x0D, ct);
    public Task PressTabAsync(CancellationToken ct = default) => PressKeyAsync(0x09, ct);
    public Task PressBackspaceAsync(CancellationToken ct = default) => PressKeyAsync(0x08, ct);
    public Task PressEscapeAsync(CancellationToken ct = default) => PressKeyAsync(0x1B, ct);

    /// <summary>Presses a shortcut such as Hotkey("Ctrl", "Shift", "Esc") or Hotkey("Ctrl+C").</summary>
    public Task HotkeyAsync(params string[] keys) => HotkeyAsync(KeyCombo.Parse(string.Join("+", keys)));

    public async Task HotkeyAsync(KeyCombo combo, CancellationToken ct = default)
    {
        using var linked = stop.Link(ct);
        var keys = combo.KeysInOrder();
        var pressed = new List<int>();
        try
        {
            foreach (int vk in keys)
            {
                KeyDown(vk);
                pressed.Add(vk);
                await Task.Delay(Math.Max(10, settings().KeyDelayMs / 2), linked.Token);
            }
            await Task.Delay(Math.Max(10, settings().KeyDelayMs), linked.Token);
        }
        finally
        {
            // Release in reverse order, even when stopped, so no modifier stays stuck.
            for (int i = pressed.Count - 1; i >= 0; i--) KeyUp(pressed[i]);
        }
    }

    public void KeyDown(int vk)
    {
        stop.Token.ThrowIfCancellationRequested();
        Send([VkInput(vk, keyUp: false)]);
        _held[vk] = 0;
    }

    public void KeyUp(int vk)
    {
        Send([VkInput(vk, keyUp: true)]);
        _held.TryRemove(vk, out _);
    }

    /// <summary>Releases every key Nex Control pressed. Called by the emergency stop.</summary>
    public void ReleaseAllKeys()
    {
        foreach (int vk in _held.Keys) KeyUp(vk);
    }

    private static IEnumerable<INPUT> CharInputs(char c)
    {
        if (c == '\n') return [VkInput(0x0D, false), VkInput(0x0D, true)];
        if (c == '\t') return [VkInput(0x09, false), VkInput(0x09, true)];
        return [UnicodeInput(c, false), UnicodeInput(c, true)];
    }

    private static INPUT UnicodeInput(char c, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                dwExtraInfo = NexInputSignature,
            },
        },
    };

    private static INPUT VkInput(int vk, bool keyUp)
    {
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
        if (KeyNames.IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    // Include the scan code too: some apps and games read it instead of the virtual key.
                    wScan = (ushort)MapVirtualKeyW((uint)vk, MAPVK_VK_TO_VSC),
                    dwFlags = flags,
                    dwExtraInfo = NexInputSignature,
                },
            },
        };
    }

    private static void Send(INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length) throw new InputBlockedException(Marshal.GetLastWin32Error());
    }
}
