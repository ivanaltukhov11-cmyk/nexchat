using System.Globalization;
using System.Text;
using NexControl.Core.Input;

namespace NexControl.Core.Automation;

public sealed class ScriptParseException(int line, string message)
    : Exception($"Line {line}: {message}")
{
    public int Line { get; } = line;
}

/// <summary>
/// Parses the Nex Control automation script language. One command per line, e.g.
/// <code>
/// MOVE 500 400
/// CLICK
/// TYPE "Hello"
/// KEY ENTER
/// WAIT 1000
/// SCREENSHOT
/// </code>
/// Lines starting with # or // are comments. Command names are case-insensitive.
/// </summary>
public static class ScriptParser
{
    public const string Reference = """
        MOVE x y [SMOOTH|INSTANT]     Move the mouse cursor
        CLICK [x y]                   Left click (optionally move first)
        DOUBLECLICK [x y]             Double click
        RIGHTCLICK [x y]              Right click
        MOUSEDOWN [x y] / MOUSEUP [x y]
        TYPE "text"                   Type text (\n = Enter, \t = Tab)
        KEY name [count]              Press a key, e.g. KEY ENTER, KEY TAB 3
        HOTKEY Ctrl+Shift+Esc         Press a key combination
        SCROLL amount [x y]           Positive = up, negative = down (notches)
        DRAG x1 y1 x2 y2              Drag with the left button
        WAIT ms                       Pause for a number of milliseconds
        SCREENSHOT [WINDOW | x y w h] Full screen, active window, or region
        """;

    public static List<AutomationStep> Parse(string script)
    {
        var steps = new List<AutomationStep>();
        string[] lines = script.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var step = ParseLine(lines[i], i + 1);
            if (step != null) steps.Add(step);
        }
        return steps;
    }

    public static string Format(IEnumerable<AutomationStep> steps) =>
        string.Join(Environment.NewLine, steps.Select(s => s.ToScriptLine()));

    /// <summary>Parses a single line. Returns null for blank lines and comments.</summary>
    public static AutomationStep? ParseLine(string line, int lineNumber = 1)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith("//")) return null;

        List<string> tokens = Tokenize(trimmed, lineNumber, out bool lastWasQuoted);
        string command = tokens[0].ToUpperInvariant();
        var args = tokens.Skip(1).ToList();

        switch (command)
        {
            case "MOVE":
            {
                if (args.Count is < 2 or > 3) throw Error(lineNumber, "MOVE needs x and y, e.g. MOVE 500 400");
                var step = AutomationStep.MoveTo(Int(args[0], lineNumber), Int(args[1], lineNumber));
                if (args.Count == 3)
                {
                    step.Mode = args[2].ToUpperInvariant() switch
                    {
                        "SMOOTH" => MoveMode.Smooth,
                        "INSTANT" => MoveMode.Instant,
                        _ => throw Error(lineNumber, $"Unknown move mode '{args[2]}' (use SMOOTH or INSTANT)"),
                    };
                }
                return step;
            }
            case "CLICK":
            case "LEFTCLICK":
                return PositionStep(StepType.Click, args, lineNumber, command);
            case "DOUBLECLICK":
                return PositionStep(StepType.DoubleClick, args, lineNumber, command);
            case "RIGHTCLICK":
                return PositionStep(StepType.RightClick, args, lineNumber, command);
            case "MOUSEDOWN":
                return PositionStep(StepType.MouseDown, args, lineNumber, command);
            case "MOUSEUP":
                return PositionStep(StepType.MouseUp, args, lineNumber, command);
            case "TYPE":
            {
                if (args.Count == 0) throw Error(lineNumber, "TYPE needs text, e.g. TYPE \"Hello\"");
                // Unquoted text is allowed too: TYPE Hello World
                string text = args.Count == 1 && lastWasQuoted ? args[0] : RawRest(trimmed, lineNumber);
                return AutomationStep.TypeText(text);
            }
            case "KEY":
            case "PRESS":
            {
                if (args.Count is < 1 or > 2) throw Error(lineNumber, "KEY needs a key name, e.g. KEY ENTER");
                if (!KeyNames.TryGetVirtualKey(args[0], out _)) throw Error(lineNumber, $"Unknown key '{args[0]}'");
                int count = args.Count == 2 ? Int(args[1], lineNumber) : 1;
                if (count < 1) throw Error(lineNumber, "Key count must be at least 1");
                return AutomationStep.PressKey(KeyNames.Normalize(args[0]), count);
            }
            case "HOTKEY":
            {
                if (args.Count == 0) throw Error(lineNumber, "HOTKEY needs keys, e.g. HOTKEY Ctrl+C");
                string combo = string.Join("+", args.SelectMany(a => a.Split('+', StringSplitOptions.RemoveEmptyEntries)));
                if (!KeyCombo.TryParse(combo, out var parsed, out string? error)) throw Error(lineNumber, error!);
                return new AutomationStep { Type = StepType.Hotkey, Key = parsed!.ToString() };
            }
            case "SCROLL":
            {
                if (args.Count is not (1 or 3)) throw Error(lineNumber, "SCROLL needs an amount, e.g. SCROLL 3 or SCROLL -3");
                var step = new AutomationStep { Type = StepType.Scroll, Amount = Int(args[0], lineNumber) };
                if (args.Count == 3)
                {
                    step.HasPosition = true;
                    step.X = Int(args[1], lineNumber);
                    step.Y = Int(args[2], lineNumber);
                }
                return step;
            }
            case "DRAG":
            {
                if (args.Count != 4) throw Error(lineNumber, "DRAG needs x1 y1 x2 y2");
                return new AutomationStep
                {
                    Type = StepType.Drag,
                    X = Int(args[0], lineNumber), Y = Int(args[1], lineNumber),
                    X2 = Int(args[2], lineNumber), Y2 = Int(args[3], lineNumber),
                };
            }
            case "WAIT":
            case "SLEEP":
            {
                if (args.Count != 1) throw Error(lineNumber, "WAIT needs milliseconds, e.g. WAIT 1000");
                int ms = Int(args[0], lineNumber);
                if (ms < 0) throw Error(lineNumber, "WAIT cannot be negative");
                return AutomationStep.WaitFor(ms);
            }
            case "SCREENSHOT":
            {
                if (args.Count == 0 || (args.Count == 1 && args[0].Equals("FULL", StringComparison.OrdinalIgnoreCase)))
                    return new AutomationStep { Type = StepType.Screenshot, Target = ScreenshotTarget.FullScreen };
                if (args.Count == 1 && args[0].Equals("WINDOW", StringComparison.OrdinalIgnoreCase))
                    return new AutomationStep { Type = StepType.Screenshot, Target = ScreenshotTarget.ActiveWindow };
                if (args.Count == 4)
                {
                    var step = new AutomationStep
                    {
                        Type = StepType.Screenshot, Target = ScreenshotTarget.Region,
                        X = Int(args[0], lineNumber), Y = Int(args[1], lineNumber),
                        Width = Int(args[2], lineNumber), Height = Int(args[3], lineNumber),
                    };
                    if (step.Width <= 0 || step.Height <= 0) throw Error(lineNumber, "Screenshot width and height must be positive");
                    return step;
                }
                throw Error(lineNumber, "Use SCREENSHOT, SCREENSHOT WINDOW, or SCREENSHOT x y width height");
            }
            default:
                throw Error(lineNumber, $"Unknown command '{tokens[0]}'");
        }
    }

    private static AutomationStep PositionStep(StepType type, List<string> args, int lineNumber, string command)
    {
        if (args.Count == 0) return new AutomationStep { Type = type };
        if (args.Count != 2) throw Error(lineNumber, $"{command} takes no arguments or x y");
        return new AutomationStep { Type = type, HasPosition = true, X = Int(args[0], lineNumber), Y = Int(args[1], lineNumber) };
    }

    private static int Int(string s, int lineNumber) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : throw Error(lineNumber, $"'{s}' is not a whole number");

    private static ScriptParseException Error(int line, string message) => new(line, message);

    /// <summary>Everything after the command word, verbatim (used for unquoted TYPE text).</summary>
    private static string RawRest(string trimmed, int lineNumber)
    {
        int space = trimmed.IndexOfAny([' ', '\t']);
        if (space < 0) throw Error(lineNumber, "TYPE needs text");
        return trimmed[(space + 1)..].Trim();
    }

    private static List<string> Tokenize(string line, int lineNumber, out bool lastWasQuoted)
    {
        var tokens = new List<string>();
        lastWasQuoted = false;
        int i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i])) { i++; continue; }
            if (line[i] == '"')
            {
                var sb = new StringBuilder();
                i++;
                bool closed = false;
                while (i < line.Length)
                {
                    char c = line[i];
                    if (c == '\\' && i + 1 < line.Length)
                    {
                        char n = line[i + 1];
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', '"' => '"', '\\' => '\\', _ => n });
                        i += 2;
                        continue;
                    }
                    if (c == '"') { closed = true; i++; break; }
                    sb.Append(c);
                    i++;
                }
                if (!closed) throw Error(lineNumber, "Missing closing quote");
                tokens.Add(sb.ToString());
                lastWasQuoted = true;
            }
            else
            {
                int start = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
                tokens.Add(line[start..i]);
                lastWasQuoted = false;
            }
        }
        return tokens;
    }
}
