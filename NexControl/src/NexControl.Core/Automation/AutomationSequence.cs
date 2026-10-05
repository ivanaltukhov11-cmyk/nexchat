using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexControl.Core.Automation;

/// <summary>A named, saveable list of automation steps (stored as *.nexauto JSON files).</summary>
public sealed class AutomationSequence
{
    public const string FileExtension = ".nexauto";

    public string Name { get; set; } = "Untitled automation";
    public int FormatVersion { get; set; } = 1;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public List<AutomationStep> Steps { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static AutomationSequence FromJson(string json) =>
        JsonSerializer.Deserialize<AutomationSequence>(json, JsonOptions)
        ?? throw new InvalidDataException("The automation file is empty.");

    public void Save(string path)
    {
        ModifiedUtc = DateTime.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToJson());
    }

    /// <summary>Loads a *.nexauto JSON file, or a plain-text script file (any other extension).</summary>
    public static AutomationSequence Load(string path)
    {
        string content = File.ReadAllText(path);
        if (content.TrimStart().StartsWith('{')) return FromJson(content);
        return new AutomationSequence
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Steps = ScriptParser.Parse(content),
        };
    }
}
