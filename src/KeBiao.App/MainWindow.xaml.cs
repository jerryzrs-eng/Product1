using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace KeBiao.App;

/// <summary>
/// 桌面上常驻的课表窗口：不进任务栏、不抢焦点、没有关闭按钮。
/// 按住拖动可移动（锁定后不能移动）；长按或右键弹出菜单；编辑模式下拖动课程互换。
/// </summary>
public partial class MainWindow : Window
{
    private const double DragThreshold = 8;
    private readonly AppController _app;
    private readonly DispatcherTimer _longPress = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private IntPtr _hwnd;
    private bool _desktopLayer = true;

    private bool _pressed, _dragging;
    private Point _downScreen;
    private double _downLeft, _downTop;
    private LessonRow? _dragRow, _dropRow;

    public MainWindow(AppController app, ScheduleViewModel vm)
    {
        _app = app;
        InitializeComponent();
        DataContext = vm;
        View.DataContext = vm;
        View.LockClicked += (_, _) => _app.ToggleLock();
        View.CollapseClicked += (_, _) => _app.ToggleCollapse();
        View.DoneClicked += (_, _) => _app.SetEditMode(false);

        _longPress.Tick += (_, _) => OnLongPress();
        PreviewMouseLeftButtonDown += OnDown;
        PreviewMouseMove += OnMove;
        PreviewMouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) => { if (_pressed) Reset(); };
        MouseRightButtonUp += (_, e) => { e.Handled = true; _app.ShowMenu(); };
        Closing += (_, e) => { if (!_app.Exiting) e.Cancel = true; };
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => EnsureOnScreen();
    }

    public bool IsDragging => _dragging;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
        ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE; // 不出现在 Alt+Tab，点它不抢走 PPT 的焦点
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        ApplyLayer(Topmost, false);
    }

    /// <summary>
    /// topmost = 始终置顶；否则贴在桌面层（被其他窗口盖住，按“显示桌面”时仍可见）。
    /// peek = 托盘单击时临时浮到最上面。
    /// </summary>
    public void ApplyLayer(bool topmost, bool peek)
    {
        _desktopLayer = !topmost && !peek;
        Topmost = topmost || peek;
        if (_hwnd == IntPtr.Zero) return;
        // 归属到桌面窗口，按 Win+D 显示桌面时不会被一起藏起来
        Native.SetWindowLongPtr(_hwnd, Native.GWLP_HWNDPARENT, _desktopLayer ? Native.GetShellWindow() : IntPtr.Zero);
        if (_desktopLayer)
            Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_WINDOWPOSCHANGING && _desktopLayer)
        {
            var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
            if ((pos.flags & Native.SWP_NOZORDER) == 0)
            {
                pos.hwndInsertAfter = Native.HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        else if (msg == Native.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    // ---------- 按下 / 拖动 / 长按 ----------

    private Point ScreenDip(MouseEventArgs e)
    {
        var p = PointToScreen(e.GetPosition(this));
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(p) : p;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (ScheduleView.IsOnButton(e.OriginalSource as DependencyObject)) return;
        _pressed = true;
        _dragging = false;
        _downScreen = ScreenDip(e);
        _downLeft = Left;
        _downTop = Top;
        _dragRow = View.Vm?.EditMode == true ? View.RowAt(e.GetPosition(View)) : null;
        CaptureMouse();
        if (View.Vm?.EditMode != true) _longPress.Start();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        var now = ScreenDip(e);
        var d = now - _downScreen;
        if (!_dragging)
        {
            if (Math.Abs(d.X) < DragThreshold && Math.Abs(d.Y) < DragThreshold) return;
            _longPress.Stop();
            _dragging = true;
            if (_dragRow != null) _dragRow.IsDragging = true;
        }

        if (View.Vm?.EditMode == true)
        {
            if (_dragRow == null) return;
            var target = View.RowAt(e.GetPosition(View));
            if (target == _dragRow) target = null;
            if (target != _dropRow)
            {
                if (_dropRow != null) _dropRow.IsDropTarget = false;
                _dropRow = target;
                if (_dropRow != null) _dropRow.IsDropTarget = true;
            }
        }
        else if (View.Vm?.Locked != true)
        {
            Left = _downLeft + d.X;
            Top = _downTop + d.Y;
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        bool moved = _dragging && View.Vm?.EditMode != true && View.Vm?.Locked != true;
        var src = _dragRow;
        var dst = _dropRow;
        Reset();
        if (moved)
        {
            EnsureOnScreen();
            _app.SavePosition(Left, Top);
        }
        if (src != null && dst != null) _app.RequestSwap(src.PeriodIndex, dst.PeriodIndex);
        e.Handled = true;
    }

    private void OnLongPress()
    {
        Reset();
        _app.ShowMenu();
    }

    private void Reset()
    {
        _longPress.Stop();
        _pressed = false;
        _dragging = false;
        if (_dragRow != null) _dragRow.IsDragging = false;
        if (_dropRow != null) _dropRow.IsDropTarget = false;
        _dragRow = _dropRow = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    /// <summary>窗口至少要有一部分留在屏幕内（换分辨率、拔掉副屏之后）。</summary>
    public void EnsureOnScreen()
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        double w = ActualWidth > 0 ? ActualWidth : 340, h = ActualHeight > 0 ? ActualHeight : 200;
        const double keep = 120;
        double left = Math.Clamp(Left, vl - w + keep, vl + vw - keep);
        double top = Math.Clamp(Top, vt, vt + vh - Math.Min(h, keep));
        if (Math.Abs(left - Left) > 0.5) Left = left;
        if (Math.Abs(top - Top) > 0.5) Top = top;
    }

    public void PlaceDefault()
    {
        var wa = SystemParameters.WorkArea;
        UpdateLayout();
        double w = ActualWidth > 0 ? ActualWidth : 350;
        Left = wa.Right - w - 16;
        Top = wa.Top + 16;
    }
}
