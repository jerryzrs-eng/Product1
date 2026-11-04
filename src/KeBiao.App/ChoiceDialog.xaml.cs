using System.Windows;
using System.Windows.Controls;

namespace KeBiao.App;

/// <summary>
/// 触屏友好的对话框，三种排法：
/// 确认（取消 + 一个动作并排，危险动作标红）；多选（选项竖排，取消在最下面同样宽）；提示（只有“知道了”）。
/// </summary>
public partial class ChoiceDialog : Window
{
    public int Result { get; private set; } = -1;

    private ChoiceDialog(string title, string? body)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body ?? "";
        BodyText.Visibility = string.IsNullOrEmpty(body) ? Visibility.Collapsed : Visibility.Visible;
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Finish(-1); };
    }

    private Button Make(string text, string style, int result)
    {
        var b = new Button { Content = text, MinHeight = 48, Style = (Style)FindResource(style) };
        b.Click += (_, _) => Finish(result);
        return b;
    }

    private void Finish(int result)
    {
        Result = result;
        DialogResult = result >= 0;
    }

    /// <summary>多个选项（第一个为推荐），返回所选下标，取消返回 -1。</summary>
    public static ChoiceDialog CreateChoices(string title, string? body, string[] choices)
    {
        var d = new ChoiceDialog(title, body);
        for (int i = 0; i < choices.Length; i++)
        {
            var b = d.Make(choices[i], i == 0 ? "PrimaryButton" : "BaseButton", i);
            b.Margin = new Thickness(0, 0, 0, 10);
            d.Buttons.Children.Add(b);
        }
        var cancel = d.Make("取消", "QuietButton", -1);
        d.Buttons.Children.Add(cancel);
        return d;
    }

    /// <summary>确认：取消和动作按钮并排，各占一半。</summary>
    public static ChoiceDialog CreateConfirm(string title, string? body, string yes, bool danger)
    {
        var d = new ChoiceDialog(title, body);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = d.Make("取消", "BaseButton", -1);
        var ok = d.Make(yes, danger ? "DangerFillButton" : "PrimaryButton", 0);
        Grid.SetColumn(ok, 2);
        row.Children.Add(cancel);
        row.Children.Add(ok);
        d.Buttons.Children.Add(row);
        return d;
    }

    public static int Ask(string title, string? body, params string[] choices)
    {
        var d = CreateChoices(title, body, choices);
        d.ShowDialog();
        return d.Result;
    }

    public static void Info(string title, string? body)
    {
        var d = new ChoiceDialog(title, body);
        d.Buttons.Children.Add(d.Make("知道了", "PrimaryButton", 0));
        d.ShowDialog();
    }

    /// <summary>danger = true 时动作按钮标红（退出、删除、放弃、替换）。</summary>
    public static bool Confirm(string title, string? body, string yes, bool danger = true)
    {
        var d = CreateConfirm(title, body, yes, danger);
        d.ShowDialog();
        return d.Result == 0;
    }
}
