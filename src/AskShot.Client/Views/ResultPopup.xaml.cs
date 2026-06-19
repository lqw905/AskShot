using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace AskShot.Client.Views;

public partial class ResultPopup : Window
{
    private bool _pinned;

    public bool IsPinned => _pinned;

    public string CurrentAnswer { get; private set; } = "";

    public ResultPopup()
    {
        InitializeComponent();
    }

    public void ShowResult(string text, Point cursorPos)
    {
        var cleaned = CleanDisplayText(text);
        CurrentAnswer = cleaned;
        ResultText.Text = cleaned;

        double w = double.IsNaN(Width) ? 430 : Width;
        double h = double.IsNaN(Height) ? 320 : Height;
        double workRight = SystemParameters.WorkArea.Right;
        double workBottom = SystemParameters.WorkArea.Bottom;

        Left = workRight - w - 16;
        Top = workBottom - h - 16;

        Show();
        Activate();
        QuestionBox.Focus();
    }

    public void AppendFollowUp(string text, string question)
    {
        var cleaned = CleanDisplayText(text, question);
        CurrentAnswer = cleaned;
        ResultText.Text += $"\n\n{question}\n{cleaned}";
    }

    // Follow-up question event
    public event EventHandler<string>? FollowUpAsked;

    public void ShowLoadingForQuestion(string question)
    {
        ResultText.Text += "\n\n分析中...";
    }

    private void AskFollowUp_Click(object sender, RoutedEventArgs e)
    {
        var question = QuestionBox.Text.Trim();
        if (!string.IsNullOrEmpty(question))
        {
            FollowUpAsked?.Invoke(this, question);
            QuestionBox.Clear();
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

    private static string CleanDisplayText(string text, string? stripQuestion = null)
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

        // Strip leading repetition of the user's question from the explanation body.
        if (!string.IsNullOrWhiteSpace(stripQuestion))
        {
            var lines = cleaned.Split('\n');
            var q = stripQuestion.Trim();
            for (int i = 0; i < Math.Min(3, lines.Length); i++)
            {
                var line = lines[i].Trim();
                if (line.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith(q, StringComparison.OrdinalIgnoreCase) ||
                    line.Replace(" ", "").Contains(q.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "";
                }
            }
            cleaned = string.Join("\n", lines);
            // Re-collapse multiple blank lines after stripping
            cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");
        }

        return cleaned.Trim();
    }
}
