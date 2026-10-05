namespace NexControl.Core.Input;

/// <summary>A key combination such as Ctrl+Shift+X. Used for automation hotkeys and global shortcuts.</summary>
public sealed record KeyCombo(bool Ctrl, bool Shift, bool Alt, bool Win, int VirtualKey)
{
    public static bool TryParse(string text, out KeyCombo? combo, out string? error)
    {
        combo = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "No keys given"; return false; }

        bool ctrl = false, shift = false, alt = false, win = false;
        int? key = null;
        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            // "Ctrl++" style: an empty part after a '+' means the '+' key itself.
            string part = raw.Length == 0 ? "Plus" : raw;
            switch (part.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": ctrl = true; continue;
                case "SHIFT": shift = true; continue;
                case "ALT": alt = true; continue;
                case "WIN": case "WINDOWS": case "SUPER": case "LWIN": win = true; continue;
            }
            if (!KeyNames.TryGetVirtualKey(part, out int vk)) { error = $"Unknown key '{part}'"; return false; }
            if (key != null) { error = $"Only one non-modifier key is allowed ('{text}')"; return false; }
            key = vk;
        }

        if (key == null)
        {
            // A combination of only modifiers (e.g. "Ctrl+Shift") is allowed for automation, not for global hotkeys.
            if (!(ctrl || shift || alt || win)) { error = "No keys given"; return false; }
            key = 0;
        }
        combo = new KeyCombo(ctrl, shift, alt, win, key.Value);
        return true;
    }

    public static KeyCombo Parse(string text) =>
        TryParse(text, out var c, out string? e) ? c! : throw new FormatException(e);

    public bool HasModifier => Ctrl || Shift || Alt || Win;

    /// <summary>Virtual-key codes in press order (modifiers first).</summary>
    public IReadOnlyList<int> KeysInOrder()
    {
        var keys = new List<int>();
        if (Ctrl) keys.Add(KeyNames.VK_CONTROL);
        if (Shift) keys.Add(KeyNames.VK_SHIFT);
        if (Alt) keys.Add(KeyNames.VK_MENU);
        if (Win) keys.Add(KeyNames.VK_LWIN);
        if (VirtualKey != 0) keys.Add(VirtualKey);
        return keys;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        if (VirtualKey != 0) parts.Add(KeyNames.GetName(VirtualKey));
        return string.Join("+", parts);
    }
}
