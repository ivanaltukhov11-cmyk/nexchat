using NexControl.Core.Settings;
using NexControl.Storage;

namespace NexControl.Settings;

/// <summary>Holds the current settings and saves them to %LOCALAPPDATA%\NexControl\settings.json.</summary>
public sealed class SettingsService
{
    public AppSettings Current { get; private set; }

    public event Action<AppSettings>? Changed;

    public SettingsService()
    {
        Current = AppSettings.Load(AppPaths.SettingsFile);
    }

    public void Save(AppSettings updated)
    {
        Current = updated;
        Current.Save(AppPaths.SettingsFile);
        Changed?.Invoke(Current);
    }
}
