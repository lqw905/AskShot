using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace AskShot.Client.Views;

public partial class ResultPopup : Window
{
    private bool _pinned;
    private bool _positioned;

    public bool IsPinned => _pinned;

    public ResultPopup()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show analysis result near the mouse cursor.
    /// </summary>
    public void ShowResult(string text, Point cursorPos)
    {
        ResultText.Text = CleanDisplayText(text);

        // Always position in the bottom-right corner of the screen work area.
        // Use WorkArea to avoid overlap with taskbar.
        double workRight = SystemParameters.WorkArea.Right;
        double workBottom = SystemParameters.WorkArea.Bottom;

        // Defer positioning until window is rendered so ActualWidth/Height are known.
        // We'll set position on next render cycle using SizeChanged.
        if (!_positioned)
        {
            Left = workRight - ActualWidth - 16;
            Top = workBottom - ActualHeight - 16;
            _positioned = true;
        }

        Show();
        Activate();
        QuestionBox.Focus();
    }

    public void AppendText(string text)
    {
        ResultText.Text += "\n\n" + CleanDisplayText(text);
    }

    // Follow-up question event
    public event EventHandler<string>? FollowUpAsked;

    private void AskFollowUp_Click(object sender, RoutedEventArgs e)
    {
        var question = QuestionBox.Text.Trim();
        if (!string.IsNullOrEmpty(question))
        {
            FollowUpAsked?.Invoke(this, question);
            QuestionBox.Clear();
            ResultText.Text += $"\n\n追问: {question}\n分析中...";
        }
    }

    private void QuestionBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AskFollowUp_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_pinned) Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        Topmost = _pinned;
        PinButton.Content = _pinned ? "已固定" : "固定";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ResultText.Text);
    }

    private static string CleanDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var cleaned = text.Replace("\r\n", "\n").Replace("\r", "\n");
        cleaned = Regex.Replace(cleaned, @"```[a-zA-Z0-9_-]*\n?", "");
        cleaned = cleaned.Replace("```", "");
        cleaned = Regex.Replace(cleaned, @"(?m)^\s{0,3}#{1,6}\s*", "");
        cleaned = Regex.Replace(cleaned, @"\*\*([^*\n]+)\*\*", "$1");
        cleaned = Regex.Replace(cleaned, @"__([^_\n]+)__", "$1");
        cleaned = Regex.Replace(cleaned, @"(?<!\*)\*([^*\n]+)\*(?!\*)", "$1");
        cleaned = Regex.Replace(cleaned, @"(?m)^\s*[*+-]\s+", "• ");
        cleaned = Regex.Replace(cleaned, @"(?m)^\s*[-*_]{3,}\s*$", "");
        cleaned = Regex.Replace(cleaned, @"[ \t]+\n", "\n");
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");
        return cleaned.Trim();
    }
}
