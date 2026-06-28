using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using AskShot.Client.Models;
using AskShot.Client.Services;
using AskShot.Client.Views;

namespace AskShot.Client;

public partial class App : Application
{
    private PythonServiceManager? _pythonService;
    private HotkeyService? _hotkeyService;
    private ScreenCaptureService _captureService = null!;
    private InferenceClient _inferenceClient = null!;
    private TrayIconService? _trayIcon;
    private AppConfig _config = null!;
    private ResultPopup? _currentPopup;
    private MainWindow? _mainWindow;
    private HistoryWindow? _historyWindow;
    private Window? _hostWindow;
    private string _lastImageBase64 = "";
    private bool _isCapturing;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers — write to a log file so crashes are visible
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            WriteLog($"[AppDomain] {args.ExceptionObject}");
            if (args.IsTerminating) Environment.Exit(1);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            WriteLog($"[Dispatcher] {args.Exception}");
            args.Handled = true;
            MessageBox.Show($"发生了错误:\n{args.Exception}", "AskShot 错误");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteLog($"[Task] {args.Exception}");
            args.SetObserved();
        };

        _config = AppConfig.Load();
        _captureService = new ScreenCaptureService();
        _inferenceClient = new InferenceClient();

        _hostWindow = new Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
        };
        _hostWindow.Show();
        _hostWindow.Hide();

        var baseDir = AppContext.BaseDirectory;
        var serviceDir = FindServiceDir(baseDir);
        var pythonExe = FindPython(baseDir, serviceDir);
        var serviceExe = FindServiceExecutable(baseDir);

        if (serviceExe != null || (pythonExe != null && serviceDir != null))
        {
            _pythonService = new PythonServiceManager(
                pythonExe ?? "",
                serviceDir ?? baseDir,
                AppConfig.DataDir,
                AppConfig.LogsDir,
                serviceExe);
            try { await _pythonService.StartAsync(); }
            catch (Exception ex) { WriteLog($"[Python] {ex}"); }
        }

        _hotkeyService = new HotkeyService();
        _hotkeyService.Attach(_hostWindow);
        RegisterHotkey();

        _trayIcon = new TrayIconService(_hostWindow);
        _trayIcon.DoubleClick += OnCaptureHotkey;
        _trayIcon.OpenConsole += OpenConsole;
        _trayIcon.OpenSearch += OpenHistory;
        _trayIcon.Exit += ExitApplication;
        _trayIcon.Show("AskShot");
    }

    private static void WriteLog(string msg)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.LogsDir);
            var logPath = Path.Combine(AppConfig.LogsDir, "AskShot_crash.log");
            File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
            Trace.WriteLine(msg);
        }
        catch { }
    }

    private async void OnCaptureHotkey()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        try
        {
            _config = AppConfig.Load();

            // Single capture: one screenshot is the source of truth.
            var fullScreen = _captureService.CaptureVirtualScreen();

            var selector = new RegionSelector(fullScreen);
            selector.Show();
            var region = await selector.Result;

            if (region == null)
                return;

            int rx = region.Value.X, ry = region.Value.Y;
            int rw = region.Value.Width, rh = region.Value.Height;

            // Crop directly from the screenshot — no second capture,
            // so no coordinate-system mismatch.
            var captured = _captureService.Crop(fullScreen, rx, ry, rw, rh);
            var base64 = _captureService.ToBase64Png(captured);
            _lastImageBase64 = base64;

            // 自动复制截图到剪贴板，方便用户直接粘贴
            try
            {
                WriteLog($"[Clipboard] === 开始复制截图到剪贴板 === captured={captured.PixelWidth}x{captured.PixelHeight} frozen={captured.IsFrozen}");

                // WPF BitmapSource → PNG 字节 → System.Drawing.Bitmap →
                // 以 CF_DIB 格式写入原生剪贴板（QQ/微信只认 CF_DIB）
                var pngEncoder = new PngBitmapEncoder();
                pngEncoder.Frames.Add(BitmapFrame.Create(captured));
                using var ms = new MemoryStream();
                pngEncoder.Save(ms);
                WriteLog($"[Clipboard] PNG 编码完成, 字节数={ms.Length}");
                ms.Seek(0, SeekOrigin.Begin);
                using var gdiBitmap = new System.Drawing.Bitmap(ms);
                WriteLog($"[Clipboard] System.Drawing.Bitmap 构造成功, 尺寸={gdiBitmap.Width}x{gdiBitmap.Height}, 格式={gdiBitmap.PixelFormat}");
                CopyBitmapToClipboardDib(gdiBitmap);
            }
            catch (Exception ex) { WriteLog($"[Clipboard] 剪贴板复制失败: {ex}"); }

            GetCursorPos(out var cursorPt);

            if (_currentPopup is { IsPinned: false })
                _currentPopup.Close();
            _currentPopup = new ResultPopup { ImageBase64 = base64 };
            _currentPopup.FollowUpAsked += OnFollowUp;
            _currentPopup.ShowLoading(new Point(cursorPt.X, cursorPt.Y));

            if (await _inferenceClient.IsHealthy())
            {
                var result = await _inferenceClient.AnalyzeAsync(base64, llmConfig: _config.Llm);
                if (result != null)
                {
                    _currentPopup.ShowResult(result.Summary, new Point(cursorPt.X, cursorPt.Y));
                    var hash = ComputeHash(base64[..Math.Min(100, base64.Length)]);
                    string? screenshotPath = null;
                    if (_config.Data.SaveScreenshots)
                        screenshotPath = await SaveScreenshot(captured, hash);
                    await _inferenceClient.SaveHistoryAsync(
                        result.Summary,
                        screenshotPath: screenshotPath,
                        imageHash: hash);
                }
            }
            else
            {
                _currentPopup.ShowResult(
                    "Python 服务未运行。",
                    new Point(cursorPt.X, cursorPt.Y));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"截图失败:\n{ex}", "AskShot 错误");
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async void OnFollowUp(object? sender, string question)
    {
        if (_currentPopup == null) return;
        var imageBase64 = _currentPopup.ImageBase64;
        if (imageBase64 == null) return;
        if (_inferenceClient != null && await _inferenceClient.IsHealthy())
        {
            var latestConfig = AppConfig.Load();
            _currentPopup.ShowLoadingForQuestion(question);
            var result = await _inferenceClient.AnalyzeAsync(
                imageBase64,
                userQuestion: question,
                previousAnswer: _currentPopup.CurrentAnswer,
                llmConfig: latestConfig.Llm);
            if (result != null) _currentPopup.AppendFollowUp(result.Summary, question);
        }
    }

    private async Task<string> SaveScreenshot(BitmapSource bitmap, string hash)
    {
        var dir = string.IsNullOrEmpty(_config.Data.ScreenshotPath)
            ? Path.Combine(AppConfig.DataDir, "screenshots")
            : _config.Data.ScreenshotPath;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{hash}.png");
        await Task.Run(() =>
        {
            using var ms = new FileStream(path, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(ms);
        });
        return path;
    }

    private void OpenConsole()
    {
        try
        {
            if (_mainWindow == null || !_mainWindow.IsLoaded)
            {
                _mainWindow = new MainWindow(_inferenceClient);
                _mainWindow.ConfigSaved += ReloadSettings;
                _mainWindow.RestartRequested += RestartPythonServiceAsync;
                _mainWindow.Closed += (_, _) => _mainWindow = null;
            }
            _mainWindow.Show();
            _mainWindow.Activate();
        }
        catch (Exception ex)
        {
            WriteLog($"[OpenConsole] {ex}");
        }
    }

    private async void OpenHistory()
    {
        try
        {
            await EnsurePythonServiceAsync();
            if (_historyWindow == null || !_historyWindow.IsLoaded)
            {
                _historyWindow = new HistoryWindow(_inferenceClient);
                _historyWindow.Closed += (_, _) => _historyWindow = null;
            }
            _historyWindow.Show();
            _historyWindow.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开历史失败:\n{ex.Message}", "AskShot");
        }
    }

    private void ReloadSettings()
    {
        _config = AppConfig.Load();
        RegisterHotkey();
    }

    private void RegisterHotkey()
    {
        if (_hotkeyService == null) return;
        try
        {
            _hotkeyService.UnregisterAll();
            _hotkeyService.Register(_config.Hotkeys.CaptureAndAnalyze, OnCaptureHotkey);
        }
        catch (Exception ex)
        {
            WriteLog($"[Hotkey] {ex}");
            MessageBox.Show($"快捷键注册失败:\n{ex.Message}", "AskShot");
        }
    }

    private async Task EnsurePythonServiceAsync()
    {
        if (_pythonService == null)
            await RestartPythonServiceAsync();
        else
            await _pythonService.StartAsync();
    }

    private async Task RestartPythonServiceAsync()
    {
        _pythonService?.Dispose();
        _pythonService = null;

        var baseDir = AppContext.BaseDirectory;
        var serviceDir = FindServiceDir(baseDir);
        var pythonExe = FindPython(baseDir, serviceDir);
        var serviceExe = FindServiceExecutable(baseDir);
        if (serviceExe == null && (pythonExe == null || serviceDir == null))
            throw new InvalidOperationException("找不到 Python 或 services/main.py。");

        _pythonService = new PythonServiceManager(
            pythonExe ?? "",
            serviceDir ?? baseDir,
            AppConfig.DataDir,
            AppConfig.LogsDir,
            serviceExe);
        await _pythonService.StartAsync();
    }

    private static string ComputeHash(string input)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private void ExitApplication()
    {
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _pythonService?.Dispose();
        _inferenceClient?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _pythonService?.Dispose();
        _inferenceClient?.Dispose();
        base.OnExit(e);
    }

    private static string? FindPython(string baseDir, string? serviceDir)
    {
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "python", "python.exe"),
            Path.Combine(baseDir, ".venv", "Scripts", "python.exe"),
            Path.Combine(baseDir, "..", "..", "python", "python.exe"),
            Path.Combine(baseDir, "..", "..", ".venv", "Scripts", "python.exe"),
            Path.Combine(baseDir, "..", "..", "venv", "Scripts", "python.exe"),
            Path.Combine(baseDir, "..", "..", "services", "venv", "Scripts", "python.exe"),
            Path.Combine(baseDir, "..", "..", "services", ".venv", "Scripts", "python.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", ".venv", "Scripts", "python.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "venv", "Scripts", "python.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "services", "venv", "Scripts", "python.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "services", ".venv", "Scripts", "python.exe"),
        };

        if (!string.IsNullOrEmpty(serviceDir))
        {
            candidates.Insert(0, Path.Combine(serviceDir, "venv", "Scripts", "python.exe"));
            candidates.Insert(0, Path.Combine(serviceDir, ".venv", "Scripts", "python.exe"));
        }

        foreach (var p in candidates.Select(Path.GetFullPath))
            if (File.Exists(p))
                return p;

        try
        {
            using var proc = Process.Start(new ProcessStartInfo("python", "--version")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            proc?.WaitForExit(2000);
            if (proc?.ExitCode == 0) return "python";
        }
        catch { }
        return null;
    }

    private static string? FindServiceDir(string baseDir)
    {
        foreach (var p in new[] {
            Path.Combine(baseDir, "services"),
            Path.Combine(baseDir, "..", "..", "services"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "services"),
        }.Select(Path.GetFullPath))
            if (Directory.Exists(p) && File.Exists(Path.Combine(p, "main.py")))
                return p;

        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "services");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "main.py")))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static string? FindServiceExecutable(string baseDir)
    {
        foreach (var p in new[] {
            Path.Combine(baseDir, "askshot-service.exe"),
            Path.Combine(baseDir, "service", "askshot-service.exe"),
            Path.Combine(baseDir, "services", "askshot-service.exe"),
            Path.Combine(baseDir, "..", "service", "askshot-service.exe"),
        }.Select(Path.GetFullPath))
            if (File.Exists(p))
                return p;

        return null;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    // Win32 原生剪贴板 API
    private const uint CF_DIB = 8;
    private const uint CF_BITMAP = 2;

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern uint RegisterClipboardFormatA(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, IntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    /// <summary>
    /// 将 System.Drawing.Bitmap 以 CF_DIB 格式写入原生剪贴板。
    /// QQ/微信等 Win32 应用只能识别 CF_DIB，不认识 WPF 的剪贴板序列化方式。
    /// </summary>
    private static void CopyBitmapToClipboardDib(System.Drawing.Bitmap bitmap)
    {
        WriteLog($"[Clipboard] CopyBitmapToClipboardDib 开始, 尺寸={bitmap.Width}x{bitmap.Height}, 像素格式={bitmap.PixelFormat}");

        // 统一转为 32bpp ARGB 确保 DIB 格式一致
        using var bmp32 = new System.Drawing.Bitmap(
            bitmap.Width, bitmap.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp32))
            g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);

        var rect = new System.Drawing.Rectangle(0, 0, bmp32.Width, bmp32.Height);
        var bmpData = bmp32.LockBits(rect,
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            bmp32.PixelFormat);

        try
        {
            int stride = bmpData.Stride;
            int pixelDataSize = stride * bmp32.Height;
            int dibSize = 40 + pixelDataSize;
            WriteLog($"[Clipboard] DIB 参数: stride={stride}, pixelDataSize={pixelDataSize}, dibSize={dibSize}");
            byte[] dib = new byte[dibSize];

            // 写入 BITMAPINFOHEADER（40 字节）
            using (var bw = new BinaryWriter(new MemoryStream(dib, 0, 40, true)))
            {
                bw.Write(40);                // biSize
                bw.Write(bmp32.Width);       // biWidth
                bw.Write(-bmp32.Height);     // biHeight（负数 = top-down，与 LockBits 数据方向一致）
                bw.Write((short)1);          // biPlanes
                bw.Write((short)32);         // biBitCount
                bw.Write(0);                 // biCompression = BI_RGB
                bw.Write(pixelDataSize);     // biSizeImage
                bw.Write(0);                 // biXPelsPerMeter
                bw.Write(0);                 // biYPelsPerMeter
                bw.Write(0);                 // biClrUsed
                bw.Write(0);                 // biClrImportant
            }

            // 拷贝像素数据
            Marshal.Copy(bmpData.Scan0, dib, 40, pixelDataSize);
            WriteLog($"[Clipboard] DIB 字节构造完成, 前16字节={BitConverter.ToString(dib, 0, 16)}");

            // ⚠️ 关键修复：Win32 剪贴板要求 GlobalAlloc(GMEM_MOVEABLE)，
            // 而 Marshal.AllocHGlobal 使用的是 LocalAlloc，SetClipboardData 会失败！
            IntPtr hMem = GlobalAlloc(0x0042, (IntPtr)dibSize); // GMEM_MOVEABLE | GMEM_ZEROINIT
            if (hMem == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                WriteLog($"[Clipboard] ❌ GlobalAlloc 失败! GetLastError={err} (0x{err:X8})");
                return;
            }
            WriteLog($"[Clipboard] GlobalAlloc 成功, hMem=0x{hMem.ToInt64():X16}");

            try
            {
                IntPtr pMem = GlobalLock(hMem);
                if (pMem == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    WriteLog($"[Clipboard] ❌ GlobalLock 失败! GetLastError={err} (0x{err:X8})");
                    return;
                }
                Marshal.Copy(dib, 0, pMem, dibSize);
                GlobalUnlock(hMem);
                WriteLog($"[Clipboard] 像素数据已写入全局内存");

                if (OpenClipboard(IntPtr.Zero))
                {
                    WriteLog($"[Clipboard] 剪贴板已打开");
                    EmptyClipboard();

                    // 1) CF_DIB — 微信/QQ 需要
                    IntPtr hResult = SetClipboardData(CF_DIB, hMem);
                    if (hResult == IntPtr.Zero)
                    {
                        int err = Marshal.GetLastWin32Error();
                        WriteLog($"[Clipboard] ❌ SetClipboardData(CF_DIB) 失败! GetLastError={err} (0x{err:X8})");
                    }
                    else
                    {
                        WriteLog($"[Clipboard] ✅ SetClipboardData(CF_DIB) 成功");
                        hMem = IntPtr.Zero; // 所有权已移交剪贴板
                    }

                    // 2) CF_BITMAP — Electron/Web 应用（AI对话框）需要
                    IntPtr hBmp = bmp32.GetHbitmap();
                    IntPtr hBmpResult = SetClipboardData(CF_BITMAP, hBmp);
                    if (hBmpResult == IntPtr.Zero)
                    {
                        int err = Marshal.GetLastWin32Error();
                        WriteLog($"[Clipboard] ❌ SetClipboardData(CF_BITMAP) 失败! GetLastError={err} (0x{err:X8})");
                        NativeMethods.DeleteObject(hBmp);
                    }
                    else
                    {
                        WriteLog($"[Clipboard] ✅ SetClipboardData(CF_BITMAP) 成功, hBmp=0x{hBmp.ToInt64():X16}");
                    }

                    // 3) PNG — 现代 Web API (navigator.clipboard.read) 需要
                    using var pngMs = new MemoryStream();
                    bmp32.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
                    byte[] pngBytes = pngMs.ToArray();
                    uint cfPng = RegisterClipboardFormatA("PNG");
                    IntPtr hPng = GlobalAlloc(0x0042, (IntPtr)pngBytes.Length);
                    if (hPng != IntPtr.Zero)
                    {
                        IntPtr pPng = GlobalLock(hPng);
                        Marshal.Copy(pngBytes, 0, pPng, pngBytes.Length);
                        GlobalUnlock(hPng);
                        IntPtr hPngResult = SetClipboardData(cfPng, hPng);
                        if (hPngResult == IntPtr.Zero)
                        {
                            int err = Marshal.GetLastWin32Error();
                            WriteLog($"[Clipboard] ❌ SetClipboardData(PNG) 失败! GetLastError={err} (0x{err:X8})");
                            GlobalFree(hPng);
                        }
                        else
                        {
                            WriteLog($"[Clipboard] ✅ SetClipboardData(PNG) 成功");
                        }
                    }
                    else
                    {
                        int err = Marshal.GetLastWin32Error();
                        WriteLog($"[Clipboard] ❌ GlobalAlloc(PNG) 失败! GetLastError={err} (0x{err:X8})");
                    }

                    CloseClipboard();
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    WriteLog($"[Clipboard] ❌ OpenClipboard 失败! GetLastError={err} (0x{err:X8})");
                }
            }
            finally
            {
                if (hMem != IntPtr.Zero)
                    GlobalFree(hMem);
            }
        }
        finally
        {
            bmp32.UnlockBits(bmpData);
        }
        WriteLog($"[Clipboard] CopyBitmapToClipboardDib 结束");
    }
}
