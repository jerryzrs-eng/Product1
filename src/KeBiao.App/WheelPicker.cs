using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KeBiao.App;

/// <summary>
/// 类似 iOS 闹钟的循环滚轮：手指上下拖动、甩动后自动停在某一格；点上下的格子直接跳过去；鼠标滚轮一次一格。
/// </summary>
public sealed class WheelPicker : FrameworkElement
{
    private const int Visible = 5;
    private readonly DispatcherTimer _anim = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly List<(DateTime t, double y)> _samples = new();
    private double _offset;           // 以“格”为单位的连续位置
    private double _animFrom, _animTo;
    private DateTime _animStart;
    private double _animMs;
    private bool _pressed, _moved;
    private double _downY, _downOffset;

    public int Count { get; init; } = 60;
    public double ItemHeight { get; init; } = 56;
    public Func<int, string> Format { get; init; } = i => i.ToString("00");

    /// <summary>停稳后的值变化（不论来自手势还是代码）。参数 true 表示由用户操作引起。</summary>
    public event Action<int, bool>? ValueChanged;

    public WheelPicker()
    {
        Width = 120;
        Height = ItemHeight * Visible;
        ClipToBounds = true;
        Focusable = false;
        Cursor = Cursors.SizeNS;
        _anim.Tick += OnAnim;
        Loaded += (_, _) => Height = ItemHeight * Visible;
    }

    public int Value => Mod((int)Math.Round(_offset), Count);

    public void SetValue(int v, bool animate)
    {
        // 走最近的方向（循环）
        int cur = (int)Math.Round(_offset);
        int diff = Mod(v - cur, Count);
        if (diff > Count / 2) diff -= Count;
        if (animate) AnimateTo(cur + diff, 220, false);
        else
        {
            _anim.Stop();
            _offset = cur + diff;
            InvalidateVisual();
        }
    }

    private static int Mod(int a, int n) => ((a % n) + n) % n;

    // ---------- 绘制 ----------

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double cy = h / 2;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // 整块可点
        var band = new Rect(4, cy - ItemHeight / 2, w - 8, ItemHeight);
        dc.DrawRoundedRectangle((Brush)Application.Current.FindResource("AccentFill"), null, band, 12, 12);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var font = new Typeface((FontFamily)Application.Current.FindResource("UiFont"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        int first = (int)Math.Floor(_offset) - Visible / 2 - 1;
        for (int i = first; i <= first + Visible + 2; i++)
        {
            double y = cy + (i - _offset) * ItemHeight;
            double dist = Math.Abs(i - _offset);
            if (dist > Visible / 2.0 + 0.6) continue;
            double size = 30 - Math.Min(dist, 2) * 4;
            byte alpha = (byte)Math.Clamp(255 - dist * 85, 40, 255);
            var brush = new SolidColorBrush(dist < 0.5 ? Color.FromArgb(alpha, 0x17, 0x47, 0xC9) : Color.FromArgb(alpha, 0x1C, 0x1C, 0x1E));
            var ft = new FormattedText(Format(Mod(i, Count)), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, font, size, brush, dpi);
            dc.DrawText(ft, new Point((w - ft.Width) / 2, y - ft.Height / 2));
        }
        // 上下渐隐
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(230, 255, 255, 255), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.3));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.7));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(230, 255, 255, 255), 1));
        dc.DrawRectangle(fade, null, new Rect(0, 0, w, h));
    }

    // ---------- 手势 ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _anim.Stop();
        _pressed = true;
        _moved = false;
        _downY = e.GetPosition(this).Y;
        _downOffset = _offset;
        _samples.Clear();
        _samples.Add((DateTime.Now, _downY));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_pressed) return;
        double y = e.GetPosition(this).Y;
        if (Math.Abs(y - _downY) > 4) _moved = true;
        if (!_moved) return;
        _offset = _downOffset - (y - _downY) / ItemHeight;
        _samples.Add((DateTime.Now, y));
        if (_samples.Count > 6) _samples.RemoveAt(0);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        ReleaseMouseCapture();
        if (!_moved)
        {
            // 点击：跳到被点的那一格
            double y = e.GetPosition(this).Y;
            int steps = (int)Math.Round((y - ActualHeight / 2) / ItemHeight);
            AnimateTo(Math.Round(_offset) + steps, 220, true);
            return;
        }
        // 甩动：按最后一段速度多滚几格
        double velocity = 0; // 格/秒
        if (_samples.Count >= 2)
        {
            var (t0, y0) = _samples[0];
            var (t1, y1) = _samples[^1];
            double dt = (t1 - t0).TotalSeconds;
            if (dt > 0.001 && (DateTime.Now - t1).TotalMilliseconds < 120) velocity = -(y1 - y0) / ItemHeight / dt;
        }
        double target = Math.Round(_offset + velocity * 0.22);
        AnimateTo(target, 180 + Math.Min(Math.Abs(target - _offset) * 40, 500), true);
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double baseTarget = _anim.IsEnabled ? _animTo : Math.Round(_offset);
        AnimateTo(baseTarget + (e.Delta > 0 ? -1 : 1), 140, true);
        e.Handled = true;
    }

    private bool _animByUser;

    private void AnimateTo(double target, double ms, bool byUser)
    {
        _animFrom = _offset;
        _animTo = target;
        _animStart = DateTime.Now;
        _animMs = Math.Max(ms, 1);
        _animByUser = byUser;
        _anim.Start();
    }

    private void OnAnim(object? sender, EventArgs e)
    {
        double p = Math.Min((DateTime.Now - _animStart).TotalMilliseconds / _animMs, 1);
        double ease = 1 - Math.Pow(1 - p, 3);
        _offset = _animFrom + (_animTo - _animFrom) * ease;
        InvalidateVisual();
        if (p >= 1)
        {
            _anim.Stop();
            _offset = _animTo;
            ValueChanged?.Invoke(Value, _animByUser);
        }
    }
}
