using System.IO;
using System.Text.Json;
using NexControl.Core.Automation;
using NexControl.Core.History;
using NexControl.OCR;
using NexControl.Screen;

namespace NexControl.Storage;

/// <summary>Local history of screenshots, OCR scans and automation runs (in %LOCALAPPDATA%\NexControl\History).</summary>
public sealed class HistoryStore
{
    private readonly string _folder;
    private readonly string _indexPath;
    private readonly object _lock = new();
    private List<HistoryEntry> _entries;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public event Action? Changed;

    public HistoryStore(string folder)
    {
        _folder = folder;
        _indexPath = Path.Combine(folder, "index.json");
        Directory.CreateDirectory(folder);
        _entries = LoadIndex();
    }

    public string Folder => _folder;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_lock) return _entries.OrderByDescending(e => e.Timestamp).ToList(); }
    }

    public string FullPath(string relative) => Path.Combine(_folder, relative);

    public HistoryEntry AddScreenshot(CaptureResult capture)
    {
        var entry = NewEntry(HistoryKind.Screenshot, "Screenshot",
            $"{capture.Source} · {capture.Image.PixelWidth}×{capture.Image.PixelHeight}");
        string png = RelativeFile(entry, "screenshot.png");
        capture.SavePng(FullPath(png));
        entry.Files.Add(png);
        return Add(entry);
    }

    public HistoryEntry AddOcr(CaptureResult capture, OcrScanResult result)
    {
        string firstLine = result.Lines.FirstOrDefault()?.Text ?? "(no text found)";
        var entry = NewEntry(HistoryKind.OcrScan, "OCR Scan",
            $"{result.Lines.Count} line(s) · \"{Truncate(firstLine, 60)}\"");
        string png = RelativeFile(entry, "source.png");
        string txt = RelativeFile(entry, "text.txt");
        string json = RelativeFile(entry, "result.json");
        capture.SavePng(FullPath(png));
        File.WriteAllText(FullPath(txt), result.FullText);
        var data = new
        {
            result.Language,
            Source = new { capture.Bounds.X, capture.Bounds.Y, capture.Bounds.Width, capture.Bounds.Height },
            Lines = result.Lines.Select(l => new
            {
                l.Text,
                X = (int)l.ScreenBounds.X, Y = (int)l.ScreenBounds.Y,
                Width = (int)l.ScreenBounds.Width, Height = (int)l.ScreenBounds.Height,
            }),
        };
        File.WriteAllText(FullPath(json), JsonSerializer.Serialize(data, JsonOptions));
        entry.Files.AddRange([png, txt, json]);
        return Add(entry);
    }

    public HistoryEntry AddAutomation(AutomationSequence sequence, string status, IEnumerable<string> log)
    {
        var entry = NewEntry(HistoryKind.Automation, "Automation", $"{sequence.Name} · {sequence.Steps.Count} steps · {status}");
        string seq = RelativeFile(entry, "sequence" + AutomationSequence.FileExtension);
        string logFile = RelativeFile(entry, "run-log.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(FullPath(seq))!);
        File.WriteAllText(FullPath(seq), sequence.ToJson());
        File.WriteAllLines(FullPath(logFile), log);
        entry.Files.AddRange([seq, logFile]);
        return Add(entry);
    }

    public void Delete(HistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Id == entry.Id);
            SaveIndex();
        }
        TryDeleteDirectory(Path.Combine(_folder, entry.Timestamp.ToString("yyyy-MM-dd"), entry.Id));
        Changed?.Invoke();
    }

    public void DeleteAll()
    {
        List<HistoryEntry> all;
        lock (_lock)
        {
            all = _entries;
            _entries = [];
            SaveIndex();
        }
        foreach (var e in all) TryDeleteDirectory(Path.Combine(_folder, e.Timestamp.ToString("yyyy-MM-dd"), e.Id));
        Changed?.Invoke();
    }

    private HistoryEntry NewEntry(HistoryKind kind, string title, string summary) =>
        new() { Kind = kind, Title = title, Summary = summary, Timestamp = DateTime.Now };

    private static string RelativeFile(HistoryEntry entry, string name) =>
        Path.Combine(entry.Timestamp.ToString("yyyy-MM-dd"), entry.Id, name);

    private HistoryEntry Add(HistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            SaveIndex();
        }
        Changed?.Invoke();
        return entry;
    }

    private List<HistoryEntry> LoadIndex()
    {
        try
        {
            if (File.Exists(_indexPath))
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_indexPath)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return [];
    }

    private void SaveIndex()
    {
        string temp = _indexPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, JsonOptions));
        File.Move(temp, _indexPath, overwrite: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
