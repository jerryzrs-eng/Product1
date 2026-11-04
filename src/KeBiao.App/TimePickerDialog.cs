using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KeBiao.Core;

namespace KeBiao.App;

/// <summary>选时间：小时、分钟两个滚轮；也可以直接输入数字（1505 → 15:05）。</summary>
public sealed class TimePickerDialog : Window
{
    private readonly WheelPicker _hour = new() { Count = 24 };
    private readonly WheelPicker _minute = new() { Count = 60 };
    private readonly TextBox _input;
    private readonly Button _ok;
    private bool _sync;
    private TimeOnly _value;
    private bool _accepted;

    private TimePickerDialog(string title, TimeOnly initial)
    {
        _value = initial;
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

        _input = new TextBox
        {
            Text = initial.ToString("HH:mm"),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MaxLength = 5,
            Margin = new Thickness(0, 14, 0, 4),
            MinHeight = 56,
        };
        _input.TextChanged += (_, _) => OnTyped();
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && _ok!.IsEnabled) Accept();
            if (e.Key == System.Windows.Input.Key.Escape) Close();
        };

        var hint = new TextBlock
        {
            Text = "可直接输入数字，如 1505",
            Style = (Style)Application.Current.FindResource("Hint"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var wheels = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        wheels.Children.Add(_hour);
        wheels.Children.Add(new TextBlock { Text = ":", FontSize = 30, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 4) });
        wheels.Children.Add(_minute);
        _hour.SetValue(initial.Hour, false);
        _minute.SetValue(initial.Minute, false);
        _hour.ValueChanged += (_, user) => { if (user) FromWheels(); };
        _minute.ValueChanged += (_, user) => { if (user) FromWheels(); };

        _ok = new Button { Content = "确定", Style = (Style)Application.Current.FindResource("PrimaryButton") };
        _ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "取消", Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.FindResource("Ink") });
        stack.Children.Add(_input);
        stack.Children.Add(hint);
        stack.Children.Add(wheels);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Margin = new Thickness(20),
            Padding = new Thickness(28, 24, 28, 22),
            Width = 380,
            CornerRadius = new CornerRadius(18),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.25 },
            Child = stack,
        };
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    public TimeOnly Value => _value;

    private void OnTyped()
    {
        if (_sync) return;
        bool ok = TimeInput.TryParse(_input.Text, out var t);
        _ok.IsEnabled = ok;
        if (ok)
        {
            _input.ClearValue(Control.BorderBrushProperty);
            _value = t;
            _hour.SetValue(t.Hour, true);
            _minute.SetValue(t.Minute, true);
        }
        else
        {
            _input.BorderBrush = (Brush)Application.Current.FindResource("Warn");
        }
    }

    private void FromWheels()
    {
        _value = new TimeOnly(_hour.Value, _minute.Value);
        _sync = true;
        _input.Text = _value.ToString("HH:mm");
        _input.ClearValue(Control.BorderBrushProperty);
        _ok.IsEnabled = true;
        _sync = false;
    }

    private void Accept()
    {
        if (TimeInput.TryParse(_input.Text, out var t)) _value = t;
        _accepted = true;
        Close();
    }

    /// <summary>弹出选择，取消返回 null。</summary>
    public static TimeOnly? Ask(string title, TimeOnly initial)
    {
        var d = new TimePickerDialog(title, initial);
        d.ShowDialog();
        return d._accepted ? d._value : null;
    }

    /// <summary>截图用：创建但不显示。</summary>
    public static Window CreateForPreview(string title, TimeOnly initial) => new TimePickerDialog(title, initial);
}
