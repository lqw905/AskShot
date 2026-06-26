using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AskShot.Client.Services;

/// <summary>
/// System tray icon using Shell_NotifyIcon + a plain WPF Popup window for the menu.
/// Popups don't depend on the parent window's VisualTree, so they work even with a
/// zero-size hidden host.
/// </summary>
public class TrayIconService : IDisposable
{
    private readonly Window _owner;
    private nint _iconHandle;
    private bool _visible;
    private bool _disposed;
    private Popup? _currentPopup;

    public event Action? DoubleClick;
    public event Action? OpenConsole;
    public event Action? OpenSearch;
    public event Action? Exit;

    public TrayIconService(Window owner)
    {
        _owner = owner;
    }

    public void Show(string tooltip = "AskShot")
    {
        if (_visible) return;

        _iconHandle = CreateDefaultIcon();
        _visible = true;
        UpdateIcon(tooltip);

        if (_owner.IsLoaded)
            AttachWndProc();
        else
            _owner.Loaded += (_, _) => AttachWndProc();
    }

    private void AttachWndProc()
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(_owner).Handle);
        source?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int wmTrayCallback = 0x0401;

        if (msg == wmTrayCallback)
        {
            int l = (int)lParam;
            // Route to dispatcher to avoid cross-thread issues
            _owner.Dispatcher.BeginInvoke(() =>
            {
                if (l == 0x0203 || l == 0x0206)
                    DoubleClick?.Invoke();
                else if (l == 0x0205)
                    ShowContextMenu();
            });
            handled = true;
        }
        return nint.Zero;
    }

    private void ShowContextMenu()
    {
        try
        {
            // Close any previous popup
            _currentPopup?.SetCurrentValue(Popup.IsOpenProperty, false);

            // Use a popup that positions itself via screen coordinates.
            // We'll place it at the mouse cursor.
            GetCursorPos(out var pt);
            double dpiScale = 1.0;
            var hwndSource = PresentationSource.FromVisual(_owner);
            if (hwndSource != null)
                dpiScale = hwndSource.CompositionTarget.TransformToDevice.M11;
            if (dpiScale <= 0) dpiScale = 1.0;

            // Physical pixels → WPF units
            double screenX = pt.X / dpiScale;
            double screenY = pt.Y / dpiScale;

            var popup = new Popup
            {
                Placement = PlacementMode.AbsolutePoint,
                HorizontalOffset = screenX,
                VerticalOffset = screenY,
                StaysOpen = false,
            };

            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(CreateMenuButton("控制台", () => { popup.IsOpen = false; OpenConsole?.Invoke(); }));
            panel.Children.Add(CreateMenuButton("搜索历史", () => { popup.IsOpen = false; OpenSearch?.Invoke(); }));
            panel.Children.Add(new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xED, 0xE9, 0xDE)), // Claude MutedBrush
                Height = 1,
                Margin = new Thickness(4, 2, 4, 2),
            });
            panel.Children.Add(CreateMenuButton("退出", () => { popup.IsOpen = false; Exit?.Invoke(); }));

            popup.Child = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF5)), // Claude BackgroundBrush
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0xD9, 0xD4)), // Claude BorderBrush
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Child = panel,
            };

            _currentPopup = popup;
            popup.IsOpen = true;
        }
        catch (Exception ex)
        {
            WriteLog($"[TrayIcon] Menu error: {ex}");
        }
    }

    private static System.Windows.Controls.Button CreateMenuButton(string text, Action click)
    {
        var btn = new System.Windows.Controls.Button
        {
            Content = text,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
            Padding = new Thickness(20, 6, 30, 6),
            Margin = new Thickness(2),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 13,
        };

        btn.Click += (_, _) => click();

        // Mouse hover effect
        System.Windows.Controls.Button? b = btn;
        var hoverBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xF4, 0xEF)); // Claude CardBackgroundBrush
        b.MouseEnter += (_, _) => b.Background = hoverBrush;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;

        return btn;
    }

    // ═══════════════════════════════════════════════════════════
    //  Shell_NotifyIcon / GDI
    // ═══════════════════════════════════════════════════════════

    private void UpdateIcon(string tooltip)
    {
        if (_iconHandle == nint.Zero) return;

        var hwnd = new WindowInteropHelper(_owner).Handle;

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
            uCallbackMessage = 0x0401,
            hIcon = _iconHandle,
            szTip = tooltip,
        };

        Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private static nint CreateDefaultIcon()
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xC9, 0x64, 0x42)), null, new Rect(0, 0, 32, 32)); // Claude BrandBrush
            var text = new FormattedText("SM",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"), 14, Brushes.White, 1);
            ctx.DrawText(text, new Point(2, 6));
        }

        var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        int stride = 32 * 4;
        var pixels = new byte[stride * 32];
        rtb.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i += 4)
            pixels[i + 3] = 255;

        var screenDC = GetDC(nint.Zero);
        var hbmColor = CreateCompatibleBitmap(screenDC, 32, 32);
        SetBitmapBits(hbmColor, (uint)pixels.Length, pixels);

        var maskBits = new byte[stride / 4 * 32];
        Array.Fill<byte>(maskBits, 255);
        var hbmMask = CreateBitmap(32, 32, 1, 1, maskBits);

        var iconInfo = new ICONINFO
        {
            fIcon = true,
            xHotspot = 0,
            yHotspot = 0,
            hbmMask = hbmMask,
            hbmColor = hbmColor,
        };

        var hIcon = CreateIconIndirect(ref iconInfo);

        DeleteObject(hbmColor);
        DeleteObject(hbmMask);
        ReleaseDC(nint.Zero, screenDC);

        return hIcon;
    }

    // ── Win32 ────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIF_ICON = 2;
    private const uint NIF_TIP = 4;
    private const uint NIF_MESSAGE = 1;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("gdi32.dll")]
    private static extern nint CreateBitmap(int w, int h, uint planes, uint bitCount, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern int SetBitmapBits(nint hbmp, uint cBytes, byte[] lpBits);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern nint CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObj);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    private static void WriteLog(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "AskShot_crash.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_visible)
        {
            var hwnd = new WindowInteropHelper(_owner).Handle;
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = 1,
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _visible = false;
        }

        if (_iconHandle != nint.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = nint.Zero;
        }
    }
}
