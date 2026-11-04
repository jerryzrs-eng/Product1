using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using KeBiao.Core;
using Microsoft.Win32;

namespace KeBiao.App;

/// <summary>程序的总控：持有数据、主窗口、托盘图标，处理所有菜单动作。</summary>
public sealed class AppController
{
    private readonly Func<DateTime> _now;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private System.Windows.Forms.NotifyIcon? _tray;
    private SettingsWindow? _settings;
    private DateOnly _lastDate;
    private bool _peeking;

    public DataStore Store { get; }
    public ScheduleData Data { get; private set; }
    public UiState Ui { get; }
    public ScheduleViewModel Vm { get; } = new();
    public MainWindow? Window { get; private set; }
    public bool Exiting { get; private set; }

    public AppController(DataStore store, Func<DateTime>? now = null)
    {
        Store = store;
        _now = now ?? (() => DateTime.Now);
        Data = store.Load();
        Ui = UiState.Load(store.Directory);
        Vm.Locked = Ui.Locked;
        Vm.Collapsed = Ui.Collapsed;
        Vm.Scale = Ui.Scale;
        _lastDate = DateOnly.FromDateTime(_now());
        PruneIfNeeded(force: true);
        Refresh();
    }

    public DateTime Now => _now();

    // ---------- 启动 ----------

    public void Start()
    {
        CreateWindow();
        CreateTray();

        _timer.Tick += (_, _) => { PruneIfNeeded(false); Refresh(); };
        _timer.Start();
        _peekTimer.Tick += (_, _) => EndPeek();

        SystemEvents.TimeChanged += (_, _) => Dispatch(Refresh);
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.Resume) Dispatch(Refresh); };
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatch(() => Window?.EnsureOnScreen());

        if (Data.IsEmpty) OpenSettings("import");
    }

    private static void Dispatch(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);

    private void CreateWindow()
    {
        var w = new MainWindow(this, Vm)
        {
            Topmost = Ui.Topmost,
            MaxHeight = SystemParameters.WorkArea.Height,
        };
        if (Ui.Left is double l && Ui.Top is double t)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = l;
            w.Top = t;
        }
        else
        {
            w.Left = -10000; // 先放在屏幕外，算好尺寸再挪到右上角
            w.Top = -10000;
        }
        w.Closed += (_, _) =>
        {
            if (Exiting) return;
            // 资源管理器重启时，挂在桌面上的窗口会被连带销毁，过一会儿重新建一个
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, _) => { t.Stop(); if (!Exiting) CreateWindow(); };
            t.Start();
        };
        w.Show();
        w.ApplyLayer(Ui.Topmost, false);
        if (Ui.Left == null)
        {
            w.PlaceDefault();
            SavePosition(w.Left, w.Top);
        }
        w.EnsureOnScreen();
        Window = w;
    }

    private void CreateTray()
    {
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/kebiao.ico"));
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(res!.Stream, System.Windows.Forms.SystemInformation.SmallIconSize),
            Text = "课表",
            Visible = true,
        };
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right) ShowMenu();
            else if (e.Button == System.Windows.Forms.MouseButtons.Left) TogglePeek();
        };

        if (!Ui.TrayPromoted)
        {
            int tries = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) =>
            {
                if (SystemIntegration.TryPromoteTrayIcon())
                {
                    Ui.TrayPromoted = true;
                    Ui.Save(Store.Directory);
                    t.Stop();
                }
                else if (++tries >= 10) t.Stop();
            };
            t.Start();
        }
    }

    // ---------- 刷新与保存 ----------

    public void Refresh() => Vm.Update(Data, Now);

    private void PruneIfNeeded(bool force)
    {
        var today = DateOnly.FromDateTime(Now);
        if (!force && today == _lastDate) return;
        _lastDate = today;
        int before = Data.DayOverrides.Count + Data.SubjectOverrides.Count + Data.FlippedWeeks.Count;
        ScheduleEngine.Prune(Data, today);
        if (Data.DayOverrides.Count + Data.SubjectOverrides.Count + Data.FlippedWeeks.Count != before)
        {
            try { Store.SaveQuietly(Data); } catch { }
        }
    }

    /// <summary>保存一次修改（会产生一个历史版本）。</summary>
    public void Commit(string reason)
    {
        try
        {
            Store.Save(Data, reason);
        }
        catch (Exception ex)
        {
            ChoiceDialog.Info("保存失败", "课表没能写入磁盘：" + ex.Message + "\n本次修改在重启后会丢失。");
        }
        Refresh();
        _settings?.OnDataChanged();
    }

    public void ReplaceData(ScheduleData data, string reason)
    {
        Data = data;
        Data.Normalize();
        Commit(reason);
    }

    public bool TrySaveData(ScheduleData data, string reason)
    {
        try
        {
            Store.Save(data, reason);
        }
        catch (Exception ex)
        {
            ChoiceDialog.Info("保存失败", ex.Message);
            return false;
        }
        Data = data;
        Refresh();
        _settings?.OnDataChanged();
        return true;
    }

    public void SaveUi() => Ui.Save(Store.Directory);

    public void SavePosition(double left, double top)
    {
        Ui.Left = left;
        Ui.Top = top;
        SaveUi();
    }

    // ---------- 主界面动作 ----------

    public void ToggleLock()
    {
        Ui.Locked = !Ui.Locked;
        Vm.Locked = Ui.Locked;
        SaveUi();
    }

    public void ToggleCollapse()
    {
        Ui.Collapsed = !Ui.Collapsed;
        Vm.Collapsed = Ui.Collapsed;
        SaveUi();
    }

    public void SetFontLevel(int level)
    {
        Ui.FontLevel = Math.Clamp(level, -2, 2);
        Vm.Scale = Ui.Scale;
        SaveUi();
        Window?.Dispatcher.BeginInvoke(() => Window?.EnsureOnScreen(), DispatcherPriority.Loaded);
    }

    public void SetTopmost(bool on)
    {
        Ui.Topmost = on;
        SaveUi();
        Window?.ApplyLayer(on, _peeking);
    }

    public void SetEditMode(bool on)
    {
        if (on && Data.IsEmpty)
        {
            ChoiceDialog.Info("还没有课表", "先在设置里导入课表，再来调课。");
            return;
        }
        Vm.EditMode = on;
        // 编辑时总是展开整天的课；结束后恢复原来的折叠状态
        Vm.Collapsed = !on && Ui.Collapsed;
        Refresh();
    }

    /// <summary>编辑模式下把一节课拖到另一节上。</summary>
    public void RequestSwap(int a, int b)
    {
        if (a == b) return;
        var today = DateOnly.FromDateTime(Now);
        var plan = ScheduleEngine.GetDayPlan(Data, today);
        var tags = plan.Timetable.ActiveBell.Tags();
        string Name(int i) => plan.Lessons.FirstOrDefault(l => l.Index == i)?.Name ?? "空";
        string P(int i) => i < tags.Count ? ScheduleEngine.PeriodName(tags[i]) : $"第{i + 1}节";
        var weekday = ScheduleEngine.WeekdayName(plan.EffectiveWeekday);
        int choice = ChoiceDialog.Ask(
            $"{P(a)} {Name(a)} ⇄ {P(b)} {Name(b)}",
            null,
            "仅今天",
            $"以后每个{weekday}都这样");
        if (choice < 0) return;
        if (choice == 0)
        {
            ScheduleEngine.SwapForDay(Data, today, a, b);
            Commit($"调课：{P(a)}⇄{P(b)}（仅{today.Month}月{today.Day}日）");
        }
        else
        {
            ScheduleEngine.SwapPermanent(Data, today, a, b);
            Commit($"调课：{weekday}{P(a)}⇄{P(b)}（以后每周）");
        }
    }

    public string DescribeOverride(DayOverrideKind kind, int weekday, string? timetableId) => kind switch
    {
        DayOverrideKind.Holiday => "放假",
        DayOverrideKind.UseTimetable => $"使用“{Data.FindTimetable(timetableId)?.Name ?? "?"}”",
        _ => $"按{ScheduleEngine.WeekdayName(weekday)}上课",
    };

    public void SetDay(DateOnly date, DayOverrideKind kind, int weekday = 1, string? timetableId = null) =>
        SetDays(date, date, kind, weekday, timetableId);

    public void SetDays(DateOnly from, DateOnly to, DayOverrideKind kind, int weekday = 1, string? timetableId = null)
    {
        if (to < from) (from, to) = (to, from);
        ScheduleEngine.SetOverrideRange(Data, from, to, kind, weekday, timetableId);
        var label = DescribeOverride(kind, weekday, timetableId);
        var range = from == to ? $"{from.Month}月{from.Day}日" : $"{from.Month}月{from.Day}日–{to.Month}月{to.Day}日";
        Commit($"{range}{label}");
    }

    public void SwitchTimetable(string id)
    {
        if (Data.ActiveTimetableId == id || Data.FindTimetable(id) is not { } t) return;
        var data = Data.DeepClone();
        data.ActiveTimetableId = id;
        TrySaveData(data, $"切换到课表“{t.Name}”");
    }

    public void ClearDay(DateOnly date)
    {
        ScheduleEngine.ClearOverride(Data, date);
        Commit($"{date.Month}月{date.Day}日恢复正常课表");
    }

    // ---------- 托盘 ----------

    private void TogglePeek()
    {
        if (_peeking) { EndPeek(); return; }
        if (Window == null) return;
        _peeking = true;
        Window.ApplyLayer(Ui.Topmost, true);
        _peekTimer.Start();
    }

    private void EndPeek()
    {
        _peekTimer.Stop();
        if (!_peeking) return;
        _peeking = false;
        Window?.ApplyLayer(Ui.Topmost, false);
    }

    public ContextMenu BuildMenu()
    {
        var today = DateOnly.FromDateTime(Now);
        var ov = ScheduleEngine.GetOverride(Data, today);
        bool hasSwap = Data.SubjectOverrides.Any(o => o.Date == today);
        var menu = new ContextMenu();

        var day = new MenuItem { Header = "今天的课表" };
        for (int wd = 1; wd <= 7; wd++)
        {
            int w = wd;
            var item = new MenuItem
            {
                Header = $"按{ScheduleEngine.WeekdayName(wd)}上课",
                IsCheckable = true,
                IsChecked = ov?.Kind == DayOverrideKind.FollowWeekday && ov.Weekday == wd,
            };
            item.Click += (_, _) => SetDay(today, DayOverrideKind.FollowWeekday, w);
            day.Items.Add(item);
        }
        var holiday = new MenuItem { Header = "今天放假", IsCheckable = true, IsChecked = ov?.Kind == DayOverrideKind.Holiday };
        holiday.Click += (_, _) => SetDay(today, DayOverrideKind.Holiday);
        day.Items.Add(holiday);
        foreach (var t in Data.Timetables.Where(t => t.Id != Data.ActiveTimetableId))
        {
            var id = t.Id;
            var item = new MenuItem
            {
                Header = $"今天用“{t.Name}”",
                IsCheckable = true,
                IsChecked = ov?.Kind == DayOverrideKind.UseTimetable && ov.TimetableId == id,
            };
            item.Click += (_, _) => SetDay(today, DayOverrideKind.UseTimetable, 1, id);
            day.Items.Add(item);
        }
        day.Items.Add(new Separator());
        var reset = new MenuItem { Header = "恢复正常", IsEnabled = ov != null || hasSwap };
        reset.Click += (_, _) => ClearDay(today);
        day.Items.Add(reset);
        var other = new MenuItem { Header = "提前设置其他日期…" };
        other.Click += (_, _) => OpenSettings("dates");
        day.Items.Add(other);
        menu.Items.Add(day);

        if (Data.Timetables.Count > 1)
        {
            var switcher = new MenuItem { Header = "切换课表" };
            foreach (var t in Data.Timetables)
            {
                var id = t.Id;
                var item = new MenuItem { Header = t.Name, IsCheckable = true, IsChecked = t.Id == Data.ActiveTimetableId };
                item.Click += (_, _) => SwitchTimetable(id);
                switcher.Items.Add(item);
            }
            menu.Items.Add(switcher);
        }

        var edit = new MenuItem { Header = Vm.EditMode ? "结束拖动调课" : "拖动调课" };
        edit.Click += (_, _) => SetEditMode(!Vm.EditMode);
        menu.Items.Add(edit);

        var settings = new MenuItem { Header = "设置…" };
        settings.Click += (_, _) => OpenSettings(null);
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Exit(confirm: true);
        menu.Items.Add(exit);
        return menu;
    }

    public void ShowMenu()
    {
        var menu = BuildMenu();
        menu.Placement = PlacementMode.MousePoint;
        menu.Opened += (_, _) =>
        {
            // 从托盘打开时必须让菜单拿到前台，否则点别处菜单不会消失
            if (PresentationSource.FromVisual(menu) is HwndSource src) Native.SetForegroundWindow(src.Handle);
        };
        menu.IsOpen = true;
    }

    // ---------- 设置 / 退出 ----------

    public void OpenSettings(string? page)
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        if (page != null) _settings.ShowPage(page);
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    public void Exit(bool confirm)
    {
        if (confirm && !ChoiceDialog.Confirm("退出课表？", "退出后桌面上不再显示课表。下次开机会自动启动（如果开启了开机启动），也可以从开始菜单打开。", "退出"))
            return;
        Exiting = true;
        _timer.Stop();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _settings?.Close();
        Window?.Close();
        Application.Current.Shutdown();
    }

    /// <summary>系统关机、安装程序要求退出时调用，不弹确认。</summary>
    public void ExitQuietly() => Exit(false);

    public static void LogError(Exception ex)
    {
        try
        {
            var path = Path.Combine(DataStore.DefaultDirectory, "error.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
