using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using NexControl.Computer;
using static NexControl.Native.NativeMethods;

namespace NexControl.Screen;

/// <summary>A captured image plus where on the screen it came from (physical pixels).</summary>
public sealed class CaptureResult(BitmapSource image, Int32Rect bounds, string source)
{
    public BitmapSource Image { get; } = image;
    public Int32Rect Bounds { get; } = bounds;
    public string Source { get; } = source;
    public DateTime Timestamp { get; } = DateTime.Now;

    public void SavePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Image));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    public byte[] ToPngBytes()
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>Real screen capture using GDI BitBlt from the desktop. Works across multiple monitors.</summary>
public sealed class ScreenCaptureService
{
    /// <summary>Captures every monitor (the whole virtual desktop).</summary>
    public CaptureResult CaptureFullScreen() => CaptureRegion(ScreenInfo.VirtualScreen, "Full screen");

    /// <summary>Captures the current foreground window, or the app window behind Nex Control if Nex Control is in front.</summary>
    public CaptureResult CaptureActiveWindow()
    {
        var window = WindowService.GetTargetWindow() ?? throw new InvalidOperationException("No window to capture.");
        var b = window.Bounds;
        var v = ScreenInfo.VirtualScreen;
        // Clip to the visible desktop (maximized windows hang a few pixels off-screen).
        int x1 = Math.Max(b.X, v.X), y1 = Math.Max(b.Y, v.Y);
        int x2 = Math.Min(b.X + b.Width, v.X + v.Width), y2 = Math.Min(b.Y + b.Height, v.Y + v.Height);
        if (x2 <= x1 || y2 <= y1) throw new InvalidOperationException("The active window is off-screen.");
        return CaptureRegion(new Int32Rect(x1, y1, x2 - x1, y2 - y1), $"Window: {window}");
    }

    public CaptureResult CaptureRegion(Int32Rect r, string source = "Region")
    {
        if (r.Width <= 0 || r.Height <= 0) throw new ArgumentException("The region is empty.");
        nint screenDc = GetDC(0);
        nint memDc = CreateCompatibleDC(screenDc);
        nint bitmap = CreateCompatibleBitmap(screenDc, r.Width, r.Height);
        nint old = SelectObject(memDc, bitmap);
        try
        {
            if (!BitBlt(memDc, 0, 0, r.Width, r.Height, screenDc, r.X, r.Y, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("Screen capture failed.");
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // Convert to a plain 32-bit image and freeze it so any thread can use it.
            var converted = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgr32, null, 0);
            var frozen = new WriteableBitmap(converted);
            frozen.Freeze();
            return new CaptureResult(frozen, r, source);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteDC(memDc);
            DeleteObject(bitmap);
            ReleaseDC(0, screenDc);
        }
    }
}
