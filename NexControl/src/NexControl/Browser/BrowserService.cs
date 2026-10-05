using System.Windows.Automation;
using NexControl.Computer;
using NexControl.Core.Elements;
using NexControl.UIAutomation;

namespace NexControl.Browser;

public enum BrowserKind { None, Chrome, Edge, Firefox, Brave, Opera, Other }

public sealed record BrowserState(
    BrowserKind Kind,
    WindowInfo Window,
    string Title,
    string? Url,
    IReadOnlyList<ScreenElement> PageControls);

/// <summary>
/// A source of browser information. Today: <see cref="UiaBrowserIntegration"/> (no extension needed).
/// Later a browser extension or DevTools-protocol integration can implement this to give exact DOM data.
/// </summary>
public interface IBrowserIntegration
{
    string Name { get; }
    bool CanHandle(WindowInfo window);
    BrowserState Inspect(WindowInfo window, bool includeControls, CancellationToken ct);
}

/// <summary>Reads Chrome/Edge state through UI Automation: title, address bar URL, and visible page controls.</summary>
public sealed class UiaBrowserIntegration(WindowsUIAutomationService uia) : IBrowserIntegration
{
    public string Name => "UI Automation (no extension)";

    public bool CanHandle(WindowInfo window) => BrowserService.Detect(window) != BrowserKind.None;

    public BrowserState Inspect(WindowInfo window, bool includeControls, CancellationToken ct)
    {
        var kind = BrowserService.Detect(window);
        var root = AutomationElement.FromHandle(window.Handle);
        string? url = ReadAddressBar(root);

        IReadOnlyList<ScreenElement> controls = [];
        if (includeControls)
        {
            // The web page itself is exposed as a Document element; only walk that, not the browser toolbar.
            var document = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            controls = document != null ? uia.Walk(document, ct) : [];
        }
        return new BrowserState(kind, window, StripBrowserSuffix(window.Title), url, controls);
    }

    private static string? ReadAddressBar(AutomationElement root)
    {
        try
        {
            // Chrome/Edge/Brave: the omnibox is the first Edit control in the toolbar.
            var edits = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            foreach (AutomationElement edit in edits)
            {
                string name = edit.Current.Name ?? "";
                bool looksLikeAddress = name.Contains("address", StringComparison.OrdinalIgnoreCase)
                                        || name.Contains("search", StringComparison.OrdinalIgnoreCase)
                                        || edit.Current.AutomationId == "urlbar-input";
                if (!looksLikeAddress) continue;
                if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp))
                {
                    string value = ((ValuePattern)vp).Current.Value;
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    // Chrome hides "https://" in the omnibox.
                    if (!value.Contains("://") && !value.StartsWith("about:") && value.Contains('.')) value = "https://" + value;
                    return value;
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException) { }
        return null;
    }

    private static string StripBrowserSuffix(string title)
    {
        foreach (string suffix in new[] { " - Google Chrome", " - Microsoft​ Edge", " - Microsoft Edge", " - Brave", " — Mozilla Firefox", " - Mozilla Firefox", " - Opera" })
        {
            int i = title.LastIndexOf(suffix, StringComparison.Ordinal);
            if (i > 0) return title[..i];
        }
        // Edge appends " - Personal - Microsoft Edge" etc.
        int edge = title.IndexOf(" - Microsoft", StringComparison.Ordinal);
        return edge > 0 ? title[..edge] : title;
    }
}

public sealed class BrowserService(IEnumerable<IBrowserIntegration> integrations)
{
    private readonly List<IBrowserIntegration> _integrations = integrations.ToList();

    /// <summary>Integrations are tried in order; register better ones (e.g. an extension bridge) first.</summary>
    public void Register(IBrowserIntegration integration, bool preferred = true)
    {
        if (preferred) _integrations.Insert(0, integration); else _integrations.Add(integration);
    }

    public static BrowserKind Detect(WindowInfo window) => window.ProcessName.ToLowerInvariant() switch
    {
        "chrome" => BrowserKind.Chrome,
        "msedge" => BrowserKind.Edge,
        "firefox" => BrowserKind.Firefox,
        "brave" => BrowserKind.Brave,
        "opera" or "opera_gx" => BrowserKind.Opera,
        _ => window.ClassName == "Chrome_WidgetWin_1" && window.ProcessName.Length > 0 ? BrowserKind.Other : BrowserKind.None,
    };

    /// <summary>Inspects the active (or most recent) browser window. Returns null when the active window is not a browser.</summary>
    public BrowserState? InspectActive(bool includeControls, CancellationToken ct = default)
    {
        var window = WindowService.GetTargetWindow();
        if (window == null) return null;
        var integration = _integrations.FirstOrDefault(i => i.CanHandle(window));
        return integration?.Inspect(window, includeControls, ct);
    }
}
