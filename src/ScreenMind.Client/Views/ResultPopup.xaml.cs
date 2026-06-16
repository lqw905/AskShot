using System.Windows;
using System.Windows.Input;

namespace ScreenMind.Client.Views;

public partial class ResultPopup : Window
{
    private bool _pinned;

    public ResultPopup()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show analysis result near the mouse cursor.
    /// </summary>
    public void ShowResult(string text, Point cursorPos)
    {
        ResultText.Text = text;

        // Always position in the bottom-right corner of the screen work area.
        // Use WorkArea to avoid overlap with taskbar.
        double workRight = SystemParameters.WorkArea.Right;
        double workBottom = SystemParameters.WorkArea.Bottom;

        // Defer positioning until window is rendered so ActualWidth/Height are known.
        // We'll set position on next render cycle using SizeChanged.
        Loaded += (s, e) =>
        {
            Left = workRight - ActualWidth - 16;
            Top = workBottom - ActualHeight - 16;
        };

        Show();
        Activate();
        QuestionBox.Focus();
    }

    public void AppendText(string text)
    {
        ResultText.Text += "\n\n" + text;
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
            ResultText.Text += $"\n\n🙋 追问: {question}\n分析中...";
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
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
