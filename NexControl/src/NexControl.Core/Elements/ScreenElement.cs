using System.Text.Json.Serialization;

namespace NexControl.Core.Elements;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ElementType
{
    Text,
    Button,
    Input,
    Checkbox,
    RadioButton,
    Dropdown,
    Link,
    Image,
    Unknown,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ElementSource
{
    /// <summary>Read from the Windows UI Automation tree (exact control type and bounds).</summary>
    UIAutomation,
    /// <summary>Found by OCR on a screenshot (text only; type is inferred).</summary>
    Ocr,
}

/// <summary>
/// Something visible on screen that can be targeted, e.g. "the button called Next".
/// Coordinates are physical screen pixels (the same coordinates the mouse uses).
/// </summary>
public sealed class ScreenElement
{
    public ElementType Type { get; set; }
    public string Text { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>0..1. UI Automation elements are 1.0; OCR elements are lower because the type is a guess.</summary>
    public double Confidence { get; set; }

    public ElementSource Source { get; set; }

    /// <summary>The raw control type name (e.g. "MenuItem", "TabItem") when it came from UI Automation.</summary>
    public string Role { get; set; } = "";
    public string AutomationId { get; set; } = "";
    public bool IsEnabled { get; set; } = true;

    /// <summary>Nesting depth in the UI Automation tree (0 for the window itself; 0 for OCR results).</summary>
    public int Depth { get; set; }

    [JsonIgnore] public int CenterX => X + Width / 2;
    [JsonIgnore] public int CenterY => Y + Height / 2;
    [JsonIgnore] public bool HasArea => Width > 0 && Height > 0;

    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    public override string ToString() =>
        $"{Type} \"{Text}\" at {X},{Y} {Width}x{Height} ({Confidence:P0}, {Source})";
}
