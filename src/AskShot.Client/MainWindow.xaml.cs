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

        ChkSaveScreenshots.IsChecked = _config.Data.SaveScreenshots;
        TxtScreenshotPath.Text = string.IsNullOrEmpty(_config.Data.ScreenshotPath)
            ? Path.Combine(AppConfig.DataDir, "screenshots")
            : _config.Data.ScreenshotPath;
        TxtScreenshotPath.IsEnabled = _config.Data.SaveScreenshots;
        TxtRetentionDays.Text = _config.Data.HistoryRetentionDays.ToString();
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
        LblConnectionStatus.Text = "测试中...";
        StatusDot.Fill = (Brush)FindResource("MutedForegroundBrush");
        SaveConfigFromUi();

        var healthy = await _client.IsHealthy();
        if (!healthy)
        {
            LblConnectionStatus.Text = "Python 服务未响应";
            StatusDot.Fill = (Brush)FindResource("ErrorBrush");
            return;
        }

        try
        {
            var result = await _client.TestLlmConnectionAsync(_config.Llm);
            if (result == null)
            {
                LblConnectionStatus.Text = "API 测试无响应";
                StatusDot.Fill = (Brush)FindResource("ErrorBrush");
                return;
            }

            LblConnectionStatus.Text = result.Message;
            StatusDot.Fill = (Brush)FindResource(result.Ok ? "SuccessBrush" : "ErrorBrush");
        }
        catch (Exception ex)
        {
            LblConnectionStatus.Text = $"API 测试失败: {ex.Message}";
            StatusDot.Fill = (Brush)FindResource("ErrorBrush");
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
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择截图保存目录",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
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
                File.Delete(f);
        }
        ShowToast("已清空所有历史记录");
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        var dataDir = AppConfig.DataDir;
        Directory.CreateDirectory(dataDir);
        Process.Start(new ProcessStartInfo("explorer.exe", dataDir) { UseShellExecute = true });
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
