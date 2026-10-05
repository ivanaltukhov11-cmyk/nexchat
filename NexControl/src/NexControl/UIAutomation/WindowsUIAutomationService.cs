using System.Windows.Automation;
using NexControl.Computer;
using NexControl.Core.Elements;
using NexControl.Core.Settings;

namespace NexControl.UIAutomation;

/// <summary>
/// Reads the controls of a window through Windows UI Automation (the accessibility API screen readers use):
/// buttons, text fields, checkboxes, menus, their names and exact screen rectangles.
/// Call from a background thread; UI Automation calls can be slow and must not run on the UI thread.
/// </summary>
public sealed class WindowsUIAutomationService(Func<PerformanceSettings> settings)
{
    /// <summary>Inspects the active application (or the app window behind Nex Control if Nex Control is active).</summary>
    public (WindowInfo Window, List<ScreenElement> Elements) InspectActiveWindow(CancellationToken ct = default)
    {
        var window = WindowService.GetTargetWindow() ?? throw new InvalidOperationException("No active window found.");
        return (window, InspectWindow(window.Handle, ct));
    }

    public List<ScreenElement> InspectWindow(nint hwnd, CancellationToken ct = default)
    {
        var root = AutomationElement.FromHandle(hwnd);
        return Walk(root, ct);
    }

    public List<ScreenElement> Walk(AutomationElement root, CancellationToken ct = default)
    {
        var s = settings();
        var results = new List<ScreenElement>();
        var walker = TreeWalker.ControlViewWalker;
        var stack = new Stack<(AutomationElement Element, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0 && results.Count < s.MaxUiElements)
        {
            ct.ThrowIfCancellationRequested();
            var (element, depth) = stack.Pop();
            ScreenElement? converted = Convert(element, depth);
            if (converted != null) results.Add(converted);
            // Off-screen subtrees (collapsed menus, hidden tabs, scrolled-away web content) are skipped.
            if (depth >= s.MaxUiDepth || converted == null) continue;

            var children = new List<AutomationElement>();
            try
            {
                for (var child = walker.GetFirstChild(element); child != null; child = walker.GetNextSibling(child))
                    children.Add(child);
            }
            catch (ElementNotAvailableException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            // Push in reverse so the output follows on-screen (document) order.
            for (int i = children.Count - 1; i >= 0; i--) stack.Push((children[i], depth + 1));
        }
        return results;
    }

    /// <summary>The control under a screen point, e.g. what the mouse is pointing at.</summary>
    public ScreenElement? ElementAt(int x, int y)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            return element == null ? null : Convert(element, 0);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Finds a control by AutomationId inside a window (used by the built-in self test).</summary>
    public ScreenElement? FindByAutomationId(nint hwnd, string automationId)
    {
        var root = AutomationElement.FromHandle(hwnd);
        var found = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        return found == null ? null : Convert(found, 0);
    }

    /// <summary>Reads the current text of a control: its Value (text boxes) or its Name (labels).</summary>
    public string? ReadText(nint hwnd, string automationId)
    {
        var root = AutomationElement.FromHandle(hwnd);
        var found = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        if (found == null) return null;
        if (found.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp)) return ((ValuePattern)vp).Current.Value;
        return found.Current.Name;
    }

    private static ScreenElement? Convert(AutomationElement element, int depth)
    {
        try
        {
            var info = element.Current;
            if (info.IsOffscreen && depth > 0) return null;
            var r = info.BoundingRectangle;
            if (r.IsEmpty || double.IsInfinity(r.Width)) return null;

            ControlType type = info.ControlType;
            string text = info.Name ?? "";
            if (text.Length == 0 && element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp))
            {
                try { text = ((ValuePattern)vp).Current.Value ?? ""; } catch { /* some providers throw for protected values */ }
            }
            // Never read the contents of password fields.
            if (info.IsPassword) text = "";

            return new ScreenElement
            {
                Type = MapType(type),
                Text = text.Length > 300 ? text[..300] + "…" : text,
                X = (int)Math.Round(r.X),
                Y = (int)Math.Round(r.Y),
                Width = (int)Math.Round(r.Width),
                Height = (int)Math.Round(r.Height),
                Confidence = 1.0,
                Source = ElementSource.UIAutomation,
                Role = type.ProgrammaticName.Replace("ControlType.", ""),
                AutomationId = info.AutomationId ?? "",
                IsEnabled = info.IsEnabled,
                Depth = depth,
            };
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return null;
        }
    }

    public static ElementType MapType(ControlType type)
    {
        if (type == ControlType.Button || type == ControlType.SplitButton || type == ControlType.MenuItem || type == ControlType.TabItem)
            return ElementType.Button;
        if (type == ControlType.Edit || type == ControlType.Spinner) return ElementType.Input;
        if (type == ControlType.CheckBox) return ElementType.Checkbox;
        if (type == ControlType.RadioButton) return ElementType.RadioButton;
        if (type == ControlType.ComboBox) return ElementType.Dropdown;
        if (type == ControlType.Hyperlink) return ElementType.Link;
        if (type == ControlType.Image) return ElementType.Image;
        if (type == ControlType.Text || type == ControlType.Document || type == ControlType.TitleBar || type == ControlType.Header || type == ControlType.HeaderItem)
            return ElementType.Text;
        return ElementType.Unknown;
    }
}
