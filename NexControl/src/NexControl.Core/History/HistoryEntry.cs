using System.Text.Json.Serialization;

namespace NexControl.Core.History;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HistoryKind
{
    Screenshot,
    OcrScan,
    Automation,
}

/// <summary>One item on the History page. Files referenced here live next to the index on disk.</summary>
public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public HistoryKind Kind { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";

    /// <summary>Files belonging to this entry, relative to the history folder.</summary>
    public List<string> Files { get; set; } = [];

    public static string DayLabel(DateTime timestamp, DateTime now)
    {
        var day = timestamp.Date;
        if (day == now.Date) return "Today";
        if (day == now.Date.AddDays(-1)) return "Yesterday";
        return day.ToString("dddd, MMMM d, yyyy");
    }
}
