using System.IO;

namespace NexControl.Storage;

/// <summary>Everything Nex Control stores lives under %LOCALAPPDATA%\NexControl. Nothing is sent anywhere.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexControl");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string HistoryFolder => Path.Combine(Root, "History");
    public static string AutomationsFolder => Path.Combine(Root, "Automations");
    public static string LogFile => Path.Combine(Root, "nexcontrol.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(HistoryFolder);
        Directory.CreateDirectory(AutomationsFolder);
    }
}
