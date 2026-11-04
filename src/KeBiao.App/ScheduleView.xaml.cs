using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeBiao.App;

public partial class ScheduleView : UserControl
{
    public event EventHandler? LockClicked;
    public event EventHandler? CollapseClicked;
    public event EventHandler? DoneClicked;

    public ScheduleView()
    {
        InitializeComponent();
        LockButton.Click += (_, _) => LockClicked?.Invoke(this, EventArgs.Empty);
        LockButton2.Click += (_, _) => LockClicked?.Invoke(this, EventArgs.Empty);
        CollapseButton.Click += (_, _) => CollapseClicked?.Invoke(this, EventArgs.Empty);
        ExpandButton.Click += (_, _) => CollapseClicked?.Invoke(this, EventArgs.Empty);
        DoneButton.Click += (_, _) => DoneClicked?.Invoke(this, EventArgs.Empty);
        DataContextChanged += OnDataContextChanged;
    }

    public ScheduleViewModel? Vm => DataContext as ScheduleViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ScheduleViewModel old) old.PropertyChanged -= OnVmChanged;
        if (e.NewValue is ScheduleViewModel vm) vm.PropertyChanged += OnVmChanged;
        UpdateLockGlyph();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScheduleViewModel.Locked)) UpdateLockGlyph();
    }

    private void UpdateLockGlyph()
    {
        bool locked = Vm?.Locked == true;
        foreach (var b in new[] { LockButton, LockButton2 })
        {
            b.Content = locked ? "" : "";
            b.ToolTip = locked ? "已锁定，点一下解锁" : "锁定位置";
            b.Foreground = locked ? (Brush)FindResource("AccentInk") : (Brush)FindResource("InkMuted");
        }
    }

    /// <summary>点是否落在某个按钮上（按钮上的按下不触发拖动/长按）。</summary>
    public static bool IsOnButton(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (d is ScheduleView) return false;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    /// <summary>找出某个点（相对本控件）下面的那一节课。</summary>
    public LessonRow? RowAt(Point p)
    {
        LessonRow? found = null;
        VisualTreeHelper.HitTest(this, null, r =>
        {
            DependencyObject? d = r.VisualHit;
            while (d != null && d != this)
            {
                if (d is FrameworkElement fe && fe.DataContext is LessonRow row && row.State != RowState.Message)
                {
                    found = row;
                    return HitTestResultBehavior.Stop;
                }
                d = VisualTreeHelper.GetParent(d);
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(p));
        return found;
    }
}
