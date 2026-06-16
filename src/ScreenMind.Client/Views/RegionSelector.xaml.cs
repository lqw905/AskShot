using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ScreenMind.Client.Services;

namespace ScreenMind.Client.Views;

/// <summary>
/// Full-screen screenshot region selector.
///
/// Coordinate model:
///   - Window covers the full virtual-screen rectangle in DIPs (WPF units)
///   - Mouse position via e.GetPosition(this) returns DIPs relative to the window
///   - Final returned Int32Rect is in screenshot pixel coordinates
/// </summary>
public partial class RegionSelector : Window
{
    private Point _startDip;
    private bool _isSelecting;
    private double _dpiX = 1.0;
    private double _dpiY = 1.0;
    private readonly int _pixelW;
    private readonly int _pixelH;

    private readonly TaskCompletionSource<Int32Rect?> _tcs = new();
    public Task<Int32Rect?> Result => _tcs.Task;

    public RegionSelector(BitmapSource screenshot)
    {
        InitializeComponent();
        _pixelW = screenshot.PixelWidth;
        _pixelH = screenshot.PixelHeight;
        BackgroundImage.Source = screenshot;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // DPI scale: M11=1.0 at 100%, 1.5 at 150%, etc.
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            _dpiX = source.CompositionTarget.TransformToDevice.M11;
            _dpiY = source.CompositionTarget.TransformToDevice.M22;
        }

        // Virtual-screen bounds (physical pixels).
        int vsLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vsTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vsW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vsH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        // Convert physical pixels → WPF DIPs for Window placement.
        Left = vsLeft / _dpiX;
        Top = vsTop / _dpiY;
        Width = vsW / _dpiX;
        Height = vsH / _dpiY;

        // Image fills the whole window.
        BackgroundImage.Width = Width;
        BackgroundImage.Height = Height;
        RootCanvas.Width = Width;
        RootCanvas.Height = Height;
    }

    private int DipPx(double wpfX) => (int)Math.Round(wpfX * _dpiX);
    private int DipPy(double wpfY) => (int)Math.Round(wpfY * _dpiY);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _startDip = e.GetPosition(this);
        _isSelecting = true;

        Canvas.SetLeft(SelectionBorder, _startDip.X);
        Canvas.SetTop(SelectionBorder, _startDip.Y);
        SelectionBorder.Width = 0;
        SelectionBorder.Height = 0;
        SelectionBorder.Visibility = Visibility.Visible;
        SizeLabel.Visibility = Visibility.Visible;
        SizeText.Text = string.Empty;

        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isSelecting) return;

        Point pos = e.GetPosition(this);

        double left = Math.Min(_startDip.X, pos.X);
        double top = Math.Min(_startDip.Y, pos.Y);
        double width = Math.Abs(pos.X - _startDip.X);
        double height = Math.Abs(pos.Y - _startDip.Y);

        Canvas.SetLeft(SelectionBorder, left);
        Canvas.SetTop(SelectionBorder, top);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;

        Canvas.SetLeft(SizeLabel, left + width + 5);
        Canvas.SetTop(SizeLabel, top + height + 5);
        SizeText.Text = $"{DipPx(width)} × {DipPy(height)}";
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;
        _isSelecting = false;
        ReleaseMouseCapture();

        Point endPos = e.GetPosition(this);

        int startPxX = DipPx(Math.Min(_startDip.X, endPos.X));
        int startPxY = DipPy(Math.Min(_startDip.Y, endPos.Y));
        int endPxX = DipPx(Math.Max(_startDip.X, endPos.X));
        int endPxY = DipPy(Math.Max(_startDip.Y, endPos.Y));

        int rx = Math.Min(startPxX, endPxX);
        int ry = Math.Min(startPxY, endPxY);
        int rw = Math.Abs(endPxX - startPxX);
        int rh = Math.Abs(endPxY - startPxY);

        Close();

        if (rw < 10 || rh < 10)
            _tcs.SetResult(null);
        else
            _tcs.SetResult(new Int32Rect(rx, ry, rw, rh));
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _tcs.SetResult(null);
            Close();
        }
    }
}
