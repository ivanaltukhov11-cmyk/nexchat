namespace NexControl.Core.Elements;

/// <summary>
/// Picks the best element for a request like "the button called Next". Pure matching logic,
/// no Windows calls, so it can be used by any later controller (scripts, plugins, an assistant).
/// </summary>
public static class ElementFinder
{
    public static ScreenElement? Find(IEnumerable<ScreenElement> elements, string text, ElementType? type = null)
    {
        return Rank(elements, text, type).FirstOrDefault().Element;
    }

    public static IEnumerable<(ScreenElement Element, double Score)> Rank(
        IEnumerable<ScreenElement> elements, string text, ElementType? type = null)
    {
        string wanted = NormalizeText(text);
        return elements
            .Where(e => e.HasArea && e.IsEnabled)
            .Select(e => (Element: e, Score: Score(e, wanted, type)))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Element.Width * r.Element.Height);
    }

    private static double Score(ScreenElement e, string wanted, ElementType? type)
    {
        if (type != null && e.Type != type) return 0;
        string have = NormalizeText(e.Text);
        if (wanted.Length == 0) return type != null ? 0.5 * e.Confidence : 0;
        if (have.Length == 0) return 0;

        double match;
        if (have == wanted) match = 1.0;
        else if (have.StartsWith(wanted, StringComparison.Ordinal) || have.EndsWith(wanted, StringComparison.Ordinal)) match = 0.8;
        else if (have.Contains(wanted, StringComparison.Ordinal)) match = 0.6;
        else
        {
            int distance = Levenshtein(have, wanted);
            double similarity = 1.0 - (double)distance / Math.Max(have.Length, wanted.Length);
            match = similarity >= 0.75 ? similarity * 0.7 : 0; // tolerate small OCR mistakes
        }
        return match * (0.5 + 0.5 * e.Confidence);
    }

    public static string NormalizeText(string s) =>
        new string(s.Trim().ToLowerInvariant().Where(c => !char.IsPunctuation(c) || c == '-').ToArray()).Trim();

    public static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
