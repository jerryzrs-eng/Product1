using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KeBiao.Core;

namespace KeBiao.App;

/// <summary>
/// 编辑一个格子：点常用科目快速填入，或手动输入；打开“单双周不同”后分别填单周、双周。
/// </summary>
public sealed class CellEditorDialog : Window
{
    private readonly TextBox _same = Box();
    private readonly TextBox _odd = Box();
    private readonly TextBox _even = Box();
    private readonly CheckBox _parity = new() { Content = "单双周不同", Margin = new Thickness(0, 14, 0, 6) };
    private readonly StackPanel _samePanel = new();
    private readonly Grid _parityPanel = new();
    private TextBox _target;
    private string? _result;

    private static TextBox Box() => new() { FontSize = 18, MaxLength = 16, MinHeight = 48 };

    private CellEditorDialog(string title, string raw, IReadOnlyList<string> subjects)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        UseLayoutRounding = true;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        System.Windows.Input.Stylus.SetIsPressAndHoldEnabled(this, false);

        var p = CellParser.Parse(raw);
        if (p.HasParity)
        {
            _parity.IsChecked = true;
            _odd.Text = p.Odd ?? "";
            _even.Text = p.Even ?? "";
            _same.Text = p.Odd ?? "";
        }
        else
        {
            _same.Text = p.Odd ?? "";
            _odd.Text = _same.Text;
        }
        _target = p.HasParity ? _odd : _same;
        foreach (var b in new[] { _same, _odd, _even })
        {
            var box = b;
            box.GotKeyboardFocus += (_, _) => { _target = box; HighlightTarget(); };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) Accept();
                if (e.Key == System.Windows.Input.Key.Escape) Close();
            };
        }

        // 常用科目
        var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 0), MaxWidth = 520 };
        foreach (var s in subjects.Take(30))
        {
            var chip = new Button
            {
                Content = s,
                MinWidth = 64,
                MinHeight = 44,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 8),
                FontSize = 16,
            };
            chip.Click += (_, _) =>
            {
                _target.Text = s;
                // 单双周模式下填完单周自动跳到双周
                if (_parity.IsChecked == true && _target == _odd) { _target = _even; HighlightTarget(); }
            };
            chips.Children.Add(chip);
        }

        _samePanel.Children.Add(_same);

        _parityPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        _parityPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _parityPanel.RowDefinitions.Add(new RowDefinition());
        _parityPanel.RowDefinitions.Add(new RowDefinition());
        AddRow(_parityPanel, 0, "单周", _odd);
        AddRow(_parityPanel, 1, "双周", _even);

        _parity.Checked += (_, _) =>
        {
            if (_odd.Text.Length == 0 && _even.Text.Length == 0) _odd.Text = _same.Text;
            UpdateMode();
            _target = _even;
            _even.Focus();
        };
        _parity.Unchecked += (_, _) =>
        {
            if (_same.Text.Length == 0) _same.Text = _odd.Text.Length > 0 ? _odd.Text : _even.Text;
            UpdateMode();
            _target = _same;
            _same.Focus();
        };
        UpdateMode();

        var clear = new Button { Content = "清空", Style = (Style)Application.Current.FindResource("DangerButton") };
        clear.Click += (_, _) => { _result = ""; Close(); };
        var ok = new Button { Content = "确定", Style = (Style)Application.Current.FindResource("PrimaryButton") };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "取消", Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => Close();
        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        buttons.Children.Add(clear);
        DockPanel.SetDock(ok, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.FindResource("Ink") });
        if (chips.Children.Count > 0) stack.Children.Add(chips);
        stack.Children.Add(_parity);
        stack.Children.Add(_samePanel);
        stack.Children.Add(_parityPanel);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Margin = new Thickness(20),
            Padding = new Thickness(28, 24, 28, 22),
            Width = 580,
            CornerRadius = new CornerRadius(18),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.25 },
            Child = stack,
        };
        Loaded += (_, _) => { _target.Focus(); _target.SelectAll(); };
    }

    private static void AddRow(Grid g, int row, string label, TextBox box)
    {
        var l = new TextBlock { Text = label, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("InkSoft") };
        box.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(l, row);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        g.Children.Add(l);
        g.Children.Add(box);
    }

    private void UpdateMode()
    {
        bool parity = _parity.IsChecked == true;
        _samePanel.Visibility = parity ? Visibility.Collapsed : Visibility.Visible;
        _parityPanel.Visibility = parity ? Visibility.Visible : Visibility.Collapsed;
        HighlightTarget();
    }

    private void HighlightTarget()
    {
        foreach (var b in new[] { _same, _odd, _even })
        {
            bool on = b == _target;
            b.BorderBrush = on ? (Brush)Application.Current.FindResource("Accent") : (Brush)Application.Current.FindResource("FieldEdge");
            b.BorderThickness = new Thickness(on ? 2 : 1);
        }
    }

    private void Accept()
    {
        _result = _parity.IsChecked == true
            ? CellParser.Compose(_odd.Text, _even.Text)
            : CellParser.Sanitize(_same.Text);
        Close();
    }

    /// <summary>返回新的格子文字；取消返回 null。</summary>
    public static string? Ask(string title, string raw, IReadOnlyList<string> subjects)
    {
        var d = new CellEditorDialog(title, raw, subjects);
        d.ShowDialog();
        return d._result;
    }

    public static Window CreateForPreview(string title, string raw, IReadOnlyList<string> subjects) => new CellEditorDialog(title, raw, subjects);
}
