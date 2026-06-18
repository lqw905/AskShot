using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AskShot.Client.Services;

namespace AskShot.Client.Views;

public partial class HistoryWindow : Window
{
    private readonly InferenceClient _client;
    private List<HistoryEntry> _records = [];

    public HistoryWindow(InferenceClient client)
    {
        InitializeComponent();
        _client = client;
        Loaded += async (_, _) => await LoadRecentAsync();
    }

    private async Task LoadRecentAsync()
    {
        try
        {
            StatusText.Text = "加载中...";
            var records = await _client.GetRecentAsync(limit: 50, hours: 24 * 365 * 10);
            SetRecords(records ?? []);
            StatusText.Text = $"共 {_records.Count} 条记录";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加载失败: {ex.Message}";
        }
    }

    private async Task SearchAsync()
    {
        try
        {
            var query = SearchBox.Text.Trim();
            StatusText.Text = "搜索中...";
            var records = string.IsNullOrEmpty(query)
                ? await _client.GetRecentAsync(limit: 50, hours: 24 * 365 * 10)
                : await _client.SearchHistoryAsync(query, limit: 50);
            SetRecords(records ?? []);
            StatusText.Text = $"共 {_records.Count} 条记录";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"搜索失败: {ex.Message}";
        }
    }

    private void SetRecords(List<HistoryEntry> records)
    {
        _records = records;
        HistoryList.Items.Clear();
        foreach (var record in _records)
        {
            var firstLine = (record.Analysis ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "(空记录)";
            if (firstLine.Length > 80)
                firstLine = firstLine[..80] + "...";

            HistoryList.Items.Add(new ListBoxItem
            {
                Content = $"{(record.IsFavorite ? "★" : " ")} {record.Id}  {firstLine}",
                Tag = record,
            });
        }

        if (HistoryList.Items.Count > 0)
            HistoryList.SelectedIndex = 0;
        else
            DetailsBox.Text = "";
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not ListBoxItem { Tag: HistoryEntry record })
            return;

        DetailsBox.Text = JsonSerializer.Serialize(record, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void Recent_Click(object sender, RoutedEventArgs e) => await LoadRecentAsync();

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not ListBoxItem { Tag: HistoryEntry record })
            return;

        try
        {
            record.IsFavorite = await _client.ToggleFavoriteAsync(record.Id);
            var index = HistoryList.SelectedIndex;
            SetRecords(_records);
            HistoryList.SelectedIndex = index;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"收藏失败: {ex.Message}";
        }
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync();
    }
}
