using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace NexControl.Core.Automation;

public enum StepType
{
    Move,
    Click,
    DoubleClick,
    RightClick,
    MouseDown,
    MouseUp,
    Type,
    Key,
    Hotkey,
    Scroll,
    Drag,
    Wait,
    Screenshot,
}

public enum MoveMode
{
    Default,
    Smooth,
    Instant,
}

public enum ScreenshotTarget
{
    FullScreen,
    ActiveWindow,
    Region,
}

/// <summary>
/// One command in an automation sequence. Which fields are used depends on <see cref="Type"/>:
/// <list type="bullet">
/// <item>Move: X, Y, Mode</item>
/// <item>Click / DoubleClick / RightClick / MouseDown / MouseUp: optional X, Y (HasPosition)</item>
/// <item>Type: Text</item>
/// <item>Key: Key, Count</item>
/// <item>Hotkey: Key (e.g. "Ctrl+Shift+Esc")</item>
/// <item>Scroll: Amount (positive = up), optional X, Y</item>
/// <item>Drag: X, Y, X2, Y2</item>
/// <item>Wait: Milliseconds</item>
/// <item>Screenshot: Target, and X, Y, Width, Height for Region</item>
/// </list>
/// </summary>
public sealed class AutomationStep
{
    public StepType Type { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>For click and scroll steps: whether X/Y should be used (otherwise act at the current cursor position).</summary>
    public bool HasPosition { get; set; }

    public string Text { get; set; } = "";
    public string Key { get; set; } = "";
    public int Count { get; set; } = 1;
    public int Amount { get; set; }
    public int Milliseconds { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MoveMode Mode { get; set; } = MoveMode.Default;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScreenshotTarget Target { get; set; } = ScreenshotTarget.FullScreen;

    public AutomationStep Clone() => (AutomationStep)MemberwiseClone();

    public static AutomationStep MoveTo(int x, int y, MoveMode mode = MoveMode.Default) =>
        new() { Type = StepType.Move, X = x, Y = y, Mode = mode };

    public static AutomationStep ClickAt(int x, int y) =>
        new() { Type = StepType.Click, X = x, Y = y, HasPosition = true };

    public static AutomationStep TypeText(string text) => new() { Type = StepType.Type, Text = text };

    public static AutomationStep PressKey(string key, int count = 1) =>
        new() { Type = StepType.Key, Key = key, Count = count };

    public static AutomationStep WaitFor(int ms) => new() { Type = StepType.Wait, Milliseconds = ms };

    /// <summary>Human readable one-line description, used by the editor list.</summary>
    public string Describe()
    {
        string at = HasPosition ? $" at {X}, {Y}" : "";
        return Type switch
        {
            StepType.Move => $"Move mouse to {X}, {Y}" + (Mode == MoveMode.Default ? "" : $" ({Mode.ToString().ToLowerInvariant()})"),
            StepType.Click => "Left click" + at,
            StepType.DoubleClick => "Double click" + at,
            StepType.RightClick => "Right click" + at,
            StepType.MouseDown => "Mouse down" + at,
            StepType.MouseUp => "Mouse up" + at,
            StepType.Type => $"Type \"{Text}\"",
            StepType.Key => Count > 1 ? $"Press {Key} ×{Count}" : $"Press {Key}",
            StepType.Hotkey => $"Hotkey {Key}",
            StepType.Scroll => (Amount >= 0 ? $"Scroll up {Amount}" : $"Scroll down {-Amount}") + at,
            StepType.Drag => $"Drag {X}, {Y} → {X2}, {Y2}",
            StepType.Wait => $"Wait {Milliseconds} ms",
            StepType.Screenshot => Target switch
            {
                ScreenshotTarget.ActiveWindow => "Screenshot (active window)",
                ScreenshotTarget.Region => $"Screenshot region {X}, {Y} {Width}×{Height}",
                _ => "Screenshot (full screen)",
            },
            _ => Type.ToString(),
        };
    }

    /// <summary>The script-language form of this step. <see cref="ScriptParser"/> parses it back.</summary>
    public string ToScriptLine()
    {
        var ci = CultureInfo.InvariantCulture;
        string pos = HasPosition ? string.Create(ci, $" {X} {Y}") : "";
        return Type switch
        {
            StepType.Move => string.Create(ci, $"MOVE {X} {Y}") + (Mode == MoveMode.Default ? "" : " " + Mode.ToString().ToUpperInvariant()),
            StepType.Click => "CLICK" + pos,
            StepType.DoubleClick => "DOUBLECLICK" + pos,
            StepType.RightClick => "RIGHTCLICK" + pos,
            StepType.MouseDown => "MOUSEDOWN" + pos,
            StepType.MouseUp => "MOUSEUP" + pos,
            StepType.Type => "TYPE " + Quote(Text),
            StepType.Key => "KEY " + Key.ToUpperInvariant() + (Count > 1 ? string.Create(ci, $" {Count}") : ""),
            StepType.Hotkey => "HOTKEY " + Key.ToUpperInvariant(),
            StepType.Scroll => string.Create(ci, $"SCROLL {Amount}") + pos,
            StepType.Drag => string.Create(ci, $"DRAG {X} {Y} {X2} {Y2}"),
            StepType.Wait => string.Create(ci, $"WAIT {Milliseconds}"),
            StepType.Screenshot => Target switch
            {
                ScreenshotTarget.ActiveWindow => "SCREENSHOT WINDOW",
                ScreenshotTarget.Region => string.Create(ci, $"SCREENSHOT {X} {Y} {Width} {Height}"),
                _ => "SCREENSHOT",
            },
            _ => throw new InvalidOperationException($"Unknown step type {Type}"),
        };
    }

    internal static string Quote(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }
}
