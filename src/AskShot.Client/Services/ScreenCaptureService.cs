using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AskShot.Client.Services;

/// <summary>
/// Screen capture using Win32 GDI. Captures the full virtual screen (all
/// monitors) at physical pixel resolution by enumerating every display and
/// bitblt-ing them into a single bitmap. The returned BitmapSource is the
/// single source of truth for both the region-selector background and the
/// final crop — coordinates are always in the captured image's pixel space.
/// </summary>
public class ScreenCaptureService
{
    /// <summary>
    /// Returns (left, top, width, height) of the virtual screen in physical
    /// pixels. These are the same coordinates used by GDI BitBlt.
    /// </summary>
    public (int left, int top, int width, int height) GetVirtualScreenBounds()
    {
        int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        return (left, top, width, height);
    }

    /// <summary>Captures the full virtual screen (all monitors) at physical pixel resolution.</summary>
    public BitmapSource CaptureVirtualScreen()
    {
        var bounds = GetVirtualScreenBounds();
        if (bounds.width <= 0 || bounds.height <= 0)
            throw new InvalidOperationException("无法获取屏幕尺寸。");

        IntPtr screenDC = IntPtr.Zero;
        IntPtr memDC = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            // Create a DC covering the entire virtual screen. GetDC(NULL) returns
            // a DC for the entire screen; the source coordinate for BitBlt must
            // still be given in virtual-screen physical pixels (which is what
            // vsLeft/vsTop are).
            screenDC = NativeMethods.GetDC(IntPtr.Zero);
            memDC = NativeMethods.CreateCompatibleDC(screenDC);
            hBitmap = NativeMethods.CreateCompatibleBitmap(screenDC, bounds.width, bounds.height);
            oldBitmap = NativeMethods.SelectObject(memDC, hBitmap);

            // BitBlt from the virtual screen origin — vsLeft/vsTop may be
            // negative in multi-monitor setups where secondary monitors sit
            // above or to the left of the primary one.
            NativeMethods.BitBlt(memDC, 0, 0, bounds.width, bounds.height,
                screenDC, bounds.left, bounds.top, NativeMethods.SRCCOPY);

            var result = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            result.Freeze();

            return result;
        }
        finally
        {
            // 确保所有 GDI 对象都被释放，即使发生异常
            if (oldBitmap != IntPtr.Zero && memDC != IntPtr.Zero)
                NativeMethods.SelectObject(memDC, oldBitmap);
            if (hBitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(hBitmap);
            if (memDC != IntPtr.Zero)
                NativeMethods.DeleteDC(memDC);
            if (screenDC != IntPtr.Zero)
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    /// <summary>Crops a region from an existing bitmap source. Coordinates are in the source image's pixels.</summary>
    public BitmapSource Crop(BitmapSource source, int x, int y, int w, int h)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        x = Math.Clamp(x, 0, source.PixelWidth - 1);
        y = Math.Clamp(y, 0, source.PixelHeight - 1);
        w = Math.Clamp(w, 1, source.PixelWidth - x);
        h = Math.Clamp(h, 1, source.PixelHeight - y);

        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
        cropped.Freeze();
        return cropped;
    }

    /// <summary>Crops a region from an existing bitmap source.</summary>
    public BitmapSource Crop(BitmapSource source, Int32Rect region)
    {
        return Crop(source, region.X, region.Y, region.Width, region.Height);
    }

    public string ToBase64Png(BitmapSource bitmap)
    {
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }
}
