using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AskShot.Client.Models;
using AskShot.Client.Services;

namespace AskShot.Client;

public partial class MainWindow : Window
{
    private readonly InferenceClient _client;
    private readonly AppConfig _config;

    public event Action? ConfigSaved;
    public event Func<Task>? RestartRequested;

    public MainWindow(InferenceClient client)
    {
        InitializeComponent();
        _client = client;
        _config = AppConfig.Load();
        LoadConfigToUi();
        Loaded += async (_, _) => await RefreshServiceStatusAsync();
    }

    private void LoadConfigToUi()
    {
        TxtHotkey.Text = _config.Hotkeys.CaptureAndAnalyze;
        TxtEndpoint.Text = _config.Llm.Endpoint;
        TxtApiKey.Text = _config.Llm.ApiKey;
        TxtModel.Text = _config.Llm.Model;
        SldTemperature.Value = _config.Llm.Temperature;
        LblTemperature.Text = _config.Llm.Temperature.ToString("F1");
        TxtMaxTokens.Text = _config.Llm.MaxTokens.ToString();

        // 先设置所有输入字段，再设置 checkbox（避免 Checked/Unchecked 事件触发 SaveConfigFromUi
        // 时读到未初始化的 TextBox 值，导致覆盖已保存的配置）
        TxtScreenshotPath.Text = string.IsNullOrEmpty(_config.Data.ScreenshotPath)
            ? Path.Combine(AppConfig.DataDir, "screenshots")
            : _config.Data.ScreenshotPath;
        TxtScreenshotPath.IsEnabled = _config.Data.SaveScreenshots;
        TxtRetentionDays.Text = _config.Data.HistoryRetentionDays.ToString();

        ChkSaveScreenshots.IsChecked = _config.Data.SaveScreenshots;
    }

    private void SaveConfigFromUi()
    {
        _config.Llm.Endpoint = TxtEndpoint.Text.Trim();
        _config.Llm.ApiKey = TxtApiKey.Text.Trim();
        _config.Llm.Model = TxtModel.Text.Trim();
        _config.Llm.Temperature = (float)SldTemperature.Value;
        _config.Llm.MaxTokens = int.TryParse(TxtMaxTokens.Text, out var mt) ? mt : 2048;

        _config.Data.SaveScreenshots = ChkSaveScreenshots.IsChecked == true;
        _config.Data.ScreenshotPath = TxtScreenshotPath.Text;
        _config.Data.HistoryRetentionDays = int.TryParse(TxtRetentionDays.Text, out var rd) ? rd : 30;
        _config.Hotkeys.CaptureAndAnalyze = string.IsNullOrWhiteSpace(TxtHotkey.Text)
            ? "Ctrl+Shift+A"
            : TxtHotkey.Text.Trim();

        _config.Save();
        ConfigSaved?.Invoke();
    }

    // ═══════════════════════════════════════════════════════════
    //  Title Bar
    // ═══════════════════════════════════════════════════════════

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe &&
            (fe == BtnMinimize || fe == BtnClose)) return;
        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ═══════════════════════════════════════════════════════════
    //  Toast Notification
    // ═══════════════════════════════════════════════════════════

    private async void ShowToast(string message, int durationMs = 2000)
    {
        ToastText.Text = message;
        ToastBar.Visibility = Visibility.Visible;
        await Task.Delay(durationMs);
        ToastBar.Visibility = Visibility.Collapsed;
    }

    // ═══════════════════════════════════════════════════════════
    //  Tab Actions
    // ═══════════════════════════════════════════════════════════

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();

        var healthy = await _client.IsHealthy();
        if (!healthy)
        {
            MessageBox.Show("Python 服务未响应", "连接测试", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var result = await _client.TestLlmConnectionAsync(_config.Llm);
            if (result == null)
            {
                MessageBox.Show("API 测试无响应", "连接测试", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(result.Message, "连接测试", MessageBoxButton.OK,
                result.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"API 测试失败: {ex.Message}", "连接测试", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SldTemperature_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LblTemperature == null || _config == null) return;
        LblTemperature.Text = e.NewValue.ToString("F1");
    }

    private void SaveConfig(object sender, RoutedEventArgs e) => SaveConfigFromUi();

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        ShowToast("设置已保存");
    }

    private void BrowseScreenshotPath_Click(object sender, RoutedEventArgs e)
    {
        var initial = TxtScreenshotPath.Text;
        if (string.IsNullOrEmpty(initial) || !Directory.Exists(initial))
            initial = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择截图保存目录",
            InitialDirectory = initial,
        };
        if (dialog.ShowDialog() == true)
        {
            TxtScreenshotPath.Text = dialog.FolderName;
            SaveConfigFromUi();
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var btn = (System.Windows.Controls.Button)sender;
        if (btn.Content?.ToString() == "清空历史记录")
        {
            // First click: ask for confirmation
            btn.Content = "确认清空？";
            btn.Style = (Style)FindResource("PrimaryButton");
            // Reset after 3 seconds if not confirmed
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (btn.Content?.ToString() == "确认清空？")
                {
                    btn.Content = "清空历史记录";
                    btn.Style = (Style)FindResource("DangerGhostButton");
                }
            };
            timer.Start();
            return;
        }

        // Confirmed: clear history
        btn.Content = "清空历史记录";
        btn.Style = (Style)FindResource("DangerGhostButton");

        var historyDir = Path.Combine(AppConfig.DataDir, "history");
        if (Directory.Exists(historyDir))
        {
            foreach (var f in Directory.GetFiles(historyDir, "*.json"))
            {
                // 删除关联的截图文件
                try
                {
                    var json = File.ReadAllText(f);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("screenshot_path", out var pathProp)
                        && pathProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var screenshotPath = pathProp.GetString();
                        if (!string.IsNullOrEmpty(screenshotPath) && File.Exists(screenshotPath))
                            File.Delete(screenshotPath);
                    }
                }
                catch { /* 单个记录解析失败不影响继续清理 */ }

                File.Delete(f);
            }
        }

        // 清理默认截图目录中的所有截图文件
        var defaultScreenshotDir = Path.Combine(AppConfig.DataDir, "screenshots");
        if (Directory.Exists(defaultScreenshotDir))
        {
            foreach (var f in Directory.GetFiles(defaultScreenshotDir, "*.png"))
                SafeDelete(f);
        }

        // 清理用户自定义截图目录中的所有截图文件（与默认目录不同时）
        if (_config.Data.SaveScreenshots && !string.IsNullOrEmpty(_config.Data.ScreenshotPath))
        {
            var customDir = _config.Data.ScreenshotPath;
            if (Path.GetFullPath(customDir) != Path.GetFullPath(defaultScreenshotDir)
                && Directory.Exists(customDir))
            {
                foreach (var f in Directory.GetFiles(customDir, "*.png"))
                    SafeDelete(f);
            }
        }

        ShowToast("已清空所有历史记录");
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* 跳过无法删除的文件 */ }
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        // 优先打开用户设置的截图保存目录（开启截图保存且设置了路径时）
        var targetDir = (_config.Data.SaveScreenshots && !string.IsNullOrEmpty(_config.Data.ScreenshotPath))
            ? _config.Data.ScreenshotPath
            : AppConfig.DataDir;
        Directory.CreateDirectory(targetDir);
        Process.Start(new ProcessStartInfo("explorer.exe", targetDir) { UseShellExecute = true });
    }

    private async void RestartService_Click(object sender, RoutedEventArgs e)
    {
        LblServiceStatus.Text = "Python 服务: 重启中...";
        ServiceDot.Fill = (Brush)FindResource("MutedForegroundBrush");
        if (RestartRequested != null)
            await RestartRequested.Invoke();
        await RefreshServiceStatusAsync();
    }

    private async void RefreshService_Click(object sender, RoutedEventArgs e)
    {
        await RefreshServiceStatusAsync();
    }

    private async Task RefreshServiceStatusAsync()
    {
        LblServiceStatus.Text = "Python 服务: 检测中...";
        ServiceDot.Fill = (Brush)FindResource("MutedForegroundBrush");
        var healthy = await _client.IsHealthy();
        if (healthy)
        {
            LblServiceStatus.Text = "Python 服务: 运行中";
            ServiceDot.Fill = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            LblServiceStatus.Text = "Python 服务: 未运行";
            ServiceDot.Fill = (Brush)FindResource("ErrorBrush");
        }
    }
}
