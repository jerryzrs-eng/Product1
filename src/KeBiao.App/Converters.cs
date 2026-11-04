using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KeBiao.App;

/// <summary>把 0~1 的进度转成 Grid 的星号宽度；参数 "rest" 表示剩余部分。</summary>
public sealed class ProgressToStarConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double p = value is double d ? Math.Clamp(d, 0, 1) : 0;
        if (parameter as string == "rest") p = 1 - p;
        return new GridLength(Math.Max(p, 0.0001), GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
