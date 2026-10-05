namespace NexControl.Core.Input;

/// <summary>Maps friendly key names ("Enter", "Ctrl", "F5", "A") to Windows virtual-key codes.</summary>
public static class KeyNames
{
    private static readonly Dictionary<string, int> Map = Build();

    /// <summary>Canonical display name for each virtual key code.</summary>
    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x10] = "Shift", [0x11] = "Ctrl", [0x12] = "Alt",
        [0x13] = "Pause", [0x14] = "CapsLock", [0x1B] = "Escape", [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown",
        [0x23] = "End", [0x24] = "Home", [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete", [0x5B] = "Win", [0x5D] = "Menu",
        [0x90] = "NumLock", [0x91] = "ScrollLock",
        [0x6A] = "Multiply", [0x6B] = "Add", [0x6D] = "Subtract", [0x6E] = "Decimal", [0x6F] = "Divide",
        [0xAD] = "VolumeMute", [0xAE] = "VolumeDown", [0xAF] = "VolumeUp",
        [0xB0] = "MediaNext", [0xB1] = "MediaPrev", [0xB2] = "MediaStop", [0xB3] = "MediaPlayPause",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
    };

    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;

    private static Dictionary<string, int> Build()
    {
        var m = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backspace"] = 0x08, ["Back"] = 0x08, ["BS"] = 0x08,
            ["Tab"] = 0x09,
            ["Enter"] = 0x0D, ["Return"] = 0x0D,
            ["Shift"] = 0x10, ["Ctrl"] = 0x11, ["Control"] = 0x11, ["Alt"] = 0x12, ["Menu"] = 0x5D, ["Apps"] = 0x5D, ["ContextMenu"] = 0x5D,
            ["Pause"] = 0x13, ["Break"] = 0x13, ["CapsLock"] = 0x14, ["Caps"] = 0x14,
            ["Escape"] = 0x1B, ["Esc"] = 0x1B,
            ["Space"] = 0x20, ["Spacebar"] = 0x20,
            ["PageUp"] = 0x21, ["PgUp"] = 0x21, ["PageDown"] = 0x22, ["PgDn"] = 0x22,
            ["End"] = 0x23, ["Home"] = 0x24,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["ArrowLeft"] = 0x25, ["ArrowUp"] = 0x26, ["ArrowRight"] = 0x27, ["ArrowDown"] = 0x28,
            ["PrintScreen"] = 0x2C, ["PrtSc"] = 0x2C, ["Snapshot"] = 0x2C,
            ["Insert"] = 0x2D, ["Ins"] = 0x2D, ["Delete"] = 0x2E, ["Del"] = 0x2E,
            ["Win"] = 0x5B, ["Windows"] = 0x5B, ["LWin"] = 0x5B, ["RWin"] = 0x5C, ["Super"] = 0x5B,
            ["NumLock"] = 0x90, ["ScrollLock"] = 0x91,
            ["Multiply"] = 0x6A, ["Add"] = 0x6B, ["Subtract"] = 0x6D, ["Decimal"] = 0x6E, ["Divide"] = 0x6F,
            ["VolumeMute"] = 0xAD, ["VolumeDown"] = 0xAE, ["VolumeUp"] = 0xAF,
            ["MediaNext"] = 0xB0, ["MediaPrev"] = 0xB1, ["MediaStop"] = 0xB2, ["MediaPlayPause"] = 0xB3,
            [";"] = 0xBA, ["Semicolon"] = 0xBA, ["="] = 0xBB, ["Equals"] = 0xBB, ["Plus"] = 0xBB,
            [","] = 0xBC, ["Comma"] = 0xBC, ["-"] = 0xBD, ["Minus"] = 0xBD, ["."] = 0xBE, ["Period"] = 0xBE,
            ["/"] = 0xBF, ["Slash"] = 0xBF, ["`"] = 0xC0, ["Backtick"] = 0xC0, ["Grave"] = 0xC0,
            ["["] = 0xDB, ["\\"] = 0xDC, ["Backslash"] = 0xDC, ["]"] = 0xDD, ["'"] = 0xDE, ["Quote"] = 0xDE,
        };
        for (char c = 'A'; c <= 'Z'; c++) m[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++)
        {
            m[c.ToString()] = c;
            m["Num" + c] = 0x60 + (c - '0');
            m["NumPad" + c] = 0x60 + (c - '0');
        }
        for (int f = 1; f <= 24; f++) m["F" + f] = 0x6F + f;
        return m;
    }

    public static bool TryGetVirtualKey(string name, out int vk) => Map.TryGetValue(name.Trim(), out vk);

    public static int GetVirtualKey(string name) =>
        TryGetVirtualKey(name, out int vk) ? vk : throw new ArgumentException($"Unknown key '{name}'", nameof(name));

    public static string GetName(int vk)
    {
        if (Names.TryGetValue(vk, out string? n)) return n;
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x60 and <= 0x69) return "Num" + (vk - 0x60);
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        return $"0x{vk:X2}";
    }

    /// <summary>Converts any accepted alias to the canonical name ("esc" → "Escape").</summary>
    public static string Normalize(string name) => GetName(GetVirtualKey(name));

    public static bool IsModifier(int vk) => vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or 0x5C
        or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    /// <summary>Keys that need KEYEVENTF_EXTENDEDKEY when sent with SendInput.</summary>
    public static bool IsExtended(int vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2C or 0x2D or 0x2E or
        0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5 or
        0xAD or 0xAE or 0xAF or 0xB0 or 0xB1 or 0xB2 or 0xB3;

    public static IEnumerable<string> CommonKeys =>
    [
        "Enter", "Tab", "Backspace", "Escape", "Space", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
        "Up", "Down", "Left", "Right", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Win", "Menu", "PrintScreen", "CapsLock",
    ];
}
