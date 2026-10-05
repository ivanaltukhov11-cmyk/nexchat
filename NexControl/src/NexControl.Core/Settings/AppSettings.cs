using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexControl.Core.Settings;

public enum TypingSpeed { Instant, Fast, Normal, Slow }
public enum ThemeChoice { System, Dark, Light }

public sealed class GeneralSettings
{
    /// <summary>Seconds to count down before a test-page action runs, so you can put the cursor/focus where you want it.</summary>
    public int ActionCountdownSeconds { get; set; } = 3;
    /// <summary>Minimize Nex Control while an automation runs (the small status banner stays visible).</summary>
    public bool MinimizeDuringAutomation { get; set; } = false;
    public bool ShowAutomationBanner { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
}

public sealed class MouseSettings
{
    public bool SmoothMovement { get; set; } = true;
    /// <summary>Smooth movement speed in pixels per second.</summary>
    public int MovementSpeed { get; set; } = 2000;
    /// <summary>Time between button down and up in a click.</summary>
    public int ClickDelayMs { get; set; } = 30;
    public int DoubleClickGapMs { get; set; } = 60;
    /// <summary>Wheel notches are multiplied by this (120 = one standard notch).</summary>
    public int ScrollStep { get; set; } = 120;
}

public sealed class KeyboardSettings
{
    public TypingSpeed TypingSpeed { get; set; } = TypingSpeed.Fast;
    /// <summary>Time a key is held down when pressing single keys and hotkeys.</summary>
    public int KeyDelayMs { get; set; } = 20;

    [JsonIgnore]
    public int CharacterDelayMs => TypingSpeed switch
    {
        TypingSpeed.Instant => 0,
        TypingSpeed.Fast => 10,
        TypingSpeed.Normal => 40,
        TypingSpeed.Slow => 110,
        _ => 10,
    };
}

public sealed class ScreenSettings
{
    public bool SaveScreenshotsToHistory { get; set; } = true;
    public bool HideWindowDuringCapture { get; set; } = true;
}

public sealed class OcrSettings
{
    /// <summary>BCP-47 tag such as "en-US"; empty = the Windows user profile language.</summary>
    public string Language { get; set; } = "";
    /// <summary>Scale small captures up before OCR; improves accuracy for small text.</summary>
    public bool UpscaleSmallImages { get; set; } = true;
    public bool SaveResultsToHistory { get; set; } = true;
}

public sealed class AutomationSettings
{
    public int StepDelayMs { get; set; } = 100;
    public int MaxSteps { get; set; } = 1000;
    public int TimeoutSeconds { get; set; } = 300;
    public bool PauseOnManualInput { get; set; } = true;
    public bool SaveRunsToHistory { get; set; } = true;
}

public sealed class HotkeySettings
{
    public string OpenApp { get; set; } = "Ctrl+Shift+Space";
    public string Screenshot { get; set; } = "Ctrl+Shift+S";
    public string EmergencyStop { get; set; } = "Ctrl+Shift+X";
    public string PauseAutomation { get; set; } = "Ctrl+Shift+P";
}

public sealed class AppearanceSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
}

public sealed class PerformanceSettings
{
    public int CoordinateRefreshMs { get; set; } = 50;
    public int MaxUiElements { get; set; } = 1500;
    public int MaxUiDepth { get; set; } = 30;
}

public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public MouseSettings Mouse { get; set; } = new();
    public KeyboardSettings Keyboard { get; set; } = new();
    public ScreenSettings Screen { get; set; } = new();
    public OcrSettings Ocr { get; set; } = new();
    public AutomationSettings Automation { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static AppSettings FromJson(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

    public AppSettings Clone() => FromJson(ToJson());

    /// <summary>Loads settings, falling back to defaults when the file is missing or unreadable.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }
}
