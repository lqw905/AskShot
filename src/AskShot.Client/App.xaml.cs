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

        if (pythonExe != null && serviceDir != null)
        {
            _pythonService = new PythonServiceManager(
                pythonExe,
                serviceDir,
                AppConfig.DataDir,
                AppConfig.LogsDir);
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

            GetCursorPos(out var cursorPt);

            if (_currentPopup is { IsPinned: false })
                _currentPopup.Close();
            _currentPopup = new ResultPopup();
            _currentPopup.FollowUpAsked += OnFollowUp;
            _currentPopup.ShowResult($"📐 ({rx},{ry}) {rw}x{rh}", new Point(cursorPt.X, cursorPt.Y));

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
                    _ = _inferenceClient.SaveHistoryAsync(
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
    }

    private async void OnFollowUp(object? sender, string question)
    {
        if (_inferenceClient != null && await _inferenceClient.IsHealthy())
        {
            var latestConfig = AppConfig.Load();
            var result = await _inferenceClient.AnalyzeAsync(
                _lastImageBase64,
                userQuestion: question,
                llmConfig: latestConfig.Llm);
            if (result != null) _currentPopup?.AppendText(result.Summary);
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
        if (pythonExe == null || serviceDir == null)
            throw new InvalidOperationException("找不到 Python 或 services/main.py。");

        _pythonService = new PythonServiceManager(
            pythonExe,
            serviceDir,
            AppConfig.DataDir,
            AppConfig.LogsDir);
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

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}
