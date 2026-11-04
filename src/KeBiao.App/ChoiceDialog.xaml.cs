using System.Windows;
using System.Windows.Controls;

namespace KeBiao.App;

/// <summary>触屏友好的选择对话框：一列大按钮，返回所选下标，取消返回 -1。</summary>
public partial class ChoiceDialog : Window
{
    public int Result { get; private set; } = -1;

    public ChoiceDialog(string title, string? body, string[] choices, int primary = 0, bool cancellable = true)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body ?? "";
        BodyText.Visibility = string.IsNullOrEmpty(body) ? Visibility.Collapsed : Visibility.Visible;
        for (int i = 0; i < choices.Length; i++)
        {
            int idx = i;
            var b = new Button
            {
                Content = choices[i],
                Margin = new Thickness(0, 0, 0, 10),
                MinHeight = 50,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = (Style)FindResource(i == primary ? "PrimaryButton" : "BaseButton"),
            };
            b.Click += (_, _) => { Result = idx; DialogResult = true; };
            Choices.Children.Add(b);
        }
        CancelButton.Visibility = cancellable ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Click += (_, _) => { Result = -1; DialogResult = false; };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && cancellable) { Result = -1; DialogResult = false; } };
    }

    public static int Ask(string title, string? body, params string[] choices)
    {
        var d = new ChoiceDialog(title, body, choices);
        d.ShowDialog();
        return d.Result;
    }

    public static void Info(string title, string? body)
    {
        var d = new ChoiceDialog(title, body, new[] { "知道了" }, 0, false);
        d.ShowDialog();
    }

    public static bool Confirm(string title, string? body, string yes) => Ask(title, body, yes) == 0;
}
