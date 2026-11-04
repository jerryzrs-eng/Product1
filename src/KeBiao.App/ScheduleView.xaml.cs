using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace KeBiao.App;

public partial class ScheduleView : UserControl
{
    public event EventHandler? LockClicked;
    public event EventHandler? CollapseClicked;
    public event EventHandler? DoneClicked;
    public event EventHandler? DayClicked;
    internal event EventHandler? LayoutHeightChanged;
    private bool _layoutUpdatePending;

    public ScheduleView()
    {
        InitializeComponent();
        LockButton.Click += (_, _) => LockClicked?.Invoke(this, EventArgs.Empty);
        LockButton2.Click += (_, _) => LockClicked?.Invoke(this, EventArgs.Empty);
        CollapseButton.Click += (_, _) => CollapseClicked?.Invoke(this, EventArgs.Empty);
        ExpandButton.Click += (_, _) => CollapseClicked?.Invoke(this, EventArgs.Empty);
        DoneButton.Click += (_, _) => DoneClicked?.Invoke(this, EventArgs.Empty);
        DayButton.Click += (_, _) => DayClicked?.Invoke(this, EventArgs.Empty);
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => QueueLayoutUpdate();
        CompactRowsList.SizeChanged += (_, _) => QueueLayoutUpdate();
        RowsScroller.ScrollChanged += (_, e) =>
        {
            if (e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0) QueueLayoutUpdate();
        };
    }

    public ScheduleViewModel? Vm => DataContext as ScheduleViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ScheduleViewModel old) old.PropertyChanged -= OnVmChanged;
        if (e.NewValue is ScheduleViewModel vm) vm.PropertyChanged += OnVmChanged;
        UpdateLockGlyph();
        QueueLayoutUpdate();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScheduleViewModel.Locked)) UpdateLockGlyph();
        if (e.PropertyName is nameof(ScheduleViewModel.VisiblePeriods) or nameof(ScheduleViewModel.Expanded)
            or nameof(ScheduleViewModel.IsPreview)
            or nameof(ScheduleViewModel.EditMode) or nameof(ScheduleViewModel.Scale)
            or nameof(ScheduleViewModel.EmptyText) or nameof(ScheduleViewModel.FooterText))
            QueueLayoutUpdate();
    }

    private void QueueLayoutUpdate()
    {
        if (_layoutUpdatePending || !IsLoaded) return;
        _layoutUpdatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _layoutUpdatePending = false;
            LayoutHeightChanged?.Invoke(this, EventArgs.Empty);
            if (Vm is not { Expanded: true, EditMode: false } vm || RowsScroller.ScrollableHeight <= 0) return;
            if (vm.IsPreview) { RowsScroller.ScrollToTop(); return; }

            var period = vm.VisiblePeriods.Current ?? vm.VisiblePeriods.Next;
            if (period == null) return;
            for (int i = 0; i < vm.Rows.Count; i++)
            {
                if (vm.Rows[i].PeriodIndex != period) continue;
                if (RowsList.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement row)
                    RowsScroller.ScrollToVerticalOffset(row.TranslatePoint(new Point(), RowsList).Y);
                break;
            }
        }));
    }

    internal double MinimumContentHeight()
    {
        // 以实测行高预留相邻两节课的空间，同时计入标题、提示和字号缩放。
        bool compact = Vm?.Collapsed == true;
        var list = compact ? CompactRowsList : RowsList;
        double available = compact ? CompactPanel.ActualHeight : RowsScroller.ActualHeight;
        double required = compact
            ? LockButton2.DesiredSize.Height + ExpandButton.DesiredSize.Height
            : EmptyMessage.DesiredSize.Height;
        double previous = 0;
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row) break;
            required = Math.Max(required, previous + row.ActualHeight);
            previous = row.ActualHeight;
        }
        return Math.Max(0, ActualHeight + (required - available) * (Vm?.Scale ?? 1));
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

    /// <summary>按钮、滚动条和高度手柄处理自己的拖动。</summary>
    public static bool IsOnInteractiveControl(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is ButtonBase or Thumb or ScrollBar) return true;
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
