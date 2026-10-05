using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexControl.Core.Elements;
using NexControl.Core.Settings;
using NexControl.Screen;
using Windows.Globalization;
using Windows.Media.Ocr;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using WinSoftwareBitmap = Windows.Graphics.Imaging.SoftwareBitmap;

namespace NexControl.OCR;

public sealed record OcrWordResult(string Text, Rect ScreenBounds);

public sealed record OcrLineResult(string Text, Rect ScreenBounds, IReadOnlyList<OcrWordResult> Words);

public sealed class OcrScanResult
{
    public required IReadOnlyList<OcrLineResult> Lines { get; init; }
    public required string Language { get; init; }
    /// <summary>Screen area the image came from; all bounds are in screen pixels.</summary>
    public required Int32Rect SourceBounds { get; init; }
    public double TextAngle { get; init; }

    public string FullText => string.Join(Environment.NewLine, Lines.Select(l => l.Text));

    /// <summary>Each OCR line as a screen element. Short lines that look like controls are tagged as probable buttons/links.</summary>
    public IEnumerable<ScreenElement> ToElements() => Lines.Select(l => new ScreenElement
    {
        Type = ElementType.Text,
        Text = l.Text,
        X = (int)l.ScreenBounds.X,
        Y = (int)l.ScreenBounds.Y,
        Width = (int)Math.Ceiling(l.ScreenBounds.Width),
        Height = (int)Math.Ceiling(l.ScreenBounds.Height),
        Confidence = 0.7,
        Source = ElementSource.Ocr,
    });
}

/// <summary>
/// Offline OCR using the OCR engine built into Windows 10/11 (Windows.Media.Ocr). No internet, no API, no keys.
/// It uses the text-recognition language packs installed in Windows (Settings → Time &amp; language → Language).
/// </summary>
public sealed class OcrService(Func<OcrSettings> settings)
{
    public static IReadOnlyList<string> AvailableLanguages =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    public static bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    public static string DefaultLanguageTag
    {
        get
        {
            try { return OcrEngine.TryCreateFromUserProfileLanguages()?.RecognizerLanguage.LanguageTag ?? ""; }
            catch { return ""; }
        }
    }

    public async Task<OcrScanResult> RecognizeAsync(CaptureResult capture, CancellationToken ct = default)
    {
        OcrEngine engine = CreateEngine();

        // Small text is recognised much better when enlarged. Scale so the image is reasonably large,
        // without exceeding the engine's maximum dimension.
        BitmapSource image = capture.Image;
        double scale = 1;
        int maxDim = (int)OcrEngine.MaxImageDimension;
        int largest = Math.Max(image.PixelWidth, image.PixelHeight);
        if (settings().UpscaleSmallImages && largest < 1600)
            scale = Math.Min(3.0, 1600.0 / largest);
        if (largest * scale > maxDim)
            scale = (double)maxDim / largest;
        if (Math.Abs(scale - 1) > 0.01)
        {
            var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            scaled.Freeze();
            image = scaled;
        }

        WinSoftwareBitmap bitmap = await ToSoftwareBitmapAsync(image);
        ct.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(bitmap);
        ct.ThrowIfCancellationRequested();

        Rect ToScreen(Windows.Foundation.Rect r) => new(
            capture.Bounds.X + r.X / scale, capture.Bounds.Y + r.Y / scale, r.Width / scale, r.Height / scale);

        var lines = new List<OcrLineResult>();
        foreach (var line in result.Lines)
        {
            var words = line.Words.Select(w => new OcrWordResult(w.Text, ToScreen(w.BoundingRect))).ToList();
            if (words.Count == 0) continue;
            var bounds = words[0].ScreenBounds;
            foreach (var w in words.Skip(1)) bounds.Union(w.ScreenBounds);
            lines.Add(new OcrLineResult(line.Text, bounds, words));
        }

        return new OcrScanResult
        {
            Lines = lines,
            Language = engine.RecognizerLanguage.LanguageTag,
            SourceBounds = capture.Bounds,
            TextAngle = result.TextAngle ?? 0,
        };
    }

    private OcrEngine CreateEngine()
    {
        string tag = settings().Language;
        OcrEngine? engine = null;
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var language = new Language(tag);
            if (OcrEngine.IsLanguageSupported(language)) engine = OcrEngine.TryCreateFromLanguage(language);
        }
        engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null && OcrEngine.AvailableRecognizerLanguages.Count > 0)
            engine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
        return engine ?? throw new InvalidOperationException(
            "No OCR language is installed. Open Windows Settings → Time & language → Language & region, add a language " +
            "(e.g. English) and make sure its optional feature \"Optical character recognition\" is installed.");
    }

    private static async Task<WinSoftwareBitmap> ToSoftwareBitmapAsync(BitmapSource image)
    {
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        var decoder = await WinBitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        return await decoder.GetSoftwareBitmapAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
    }
}
