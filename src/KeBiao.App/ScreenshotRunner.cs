using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeBiao.Core;

namespace KeBiao.App;

/// <summary>
/// 自动截图：KeBiao.exe --screenshot &lt;文件夹&gt;
/// 用示例课表和固定时间，把主界面各种状态、设置页、对话框渲染成 PNG，供云端检查界面。
/// </summary>
public static class ScreenshotRunner
{
    private static readonly DateOnly Mon = new(2026, 11, 2);
    private static string _dir = "";

    public static void Run(string dir)
    {
        _dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(_dir);
        var log = new System.Text.StringBuilder();

        void Try(string name, Action a)
        {
            try { a(); log.AppendLine("ok   " + name); }
            catch (Exception ex) { log.AppendLine("FAIL " + name + ": " + ex); }
        }

        var sample = SampleData.Create();

        Try("01-展开-课间-10点01", () => View("01-展开-课间-10点01", sample, At(Mon, 10, 1)));
        Try("02-展开-即将开始-10点18", () => View("02-展开-即将开始-10点18", sample, At(Mon, 10, 18)));
        Try("03-展开-上课中-10点40", () => View("03-展开-上课中-10点40", sample, At(Mon, 10, 40)));
        Try("04-折叠-上课中且下节即将开始-17点07", () => View("04-折叠-上课中且下节即将开始-17点07", sample, At(Mon, 17, 7), collapsed: true));
        Try("05-折叠-课间-10点18", () => View("05-折叠-课间-10点18", sample, At(Mon, 10, 18), collapsed: true));
        Try("06-折叠-课间未到10分钟-10点01", () => View("06-折叠-课间未到10分钟-10点01", sample, At(Mon, 10, 1), collapsed: true));
        Try("07-折叠-放学后", () => View("07-折叠-放学后", sample, At(Mon, 19, 31), collapsed: true));
        Try("08-展开-放学后", () => View("08-展开-放学后", sample, At(Mon, 18, 30)));
        Try("09-展开-周六无课", () => View("09-展开-周六无课", sample, At(Mon.AddDays(5), 9, 0)));
        Try("10-展开-周日按周五上课", () =>
        {
            var d = sample.DeepClone();
            ScheduleEngine.SetOverride(d, Mon.AddDays(6), DayOverrideKind.FollowWeekday, 5);
            View("10-展开-周日按周五上课", d, At(Mon.AddDays(6), 9, 20));
        });
        Try("11-展开-拖动调课", () => View("11-展开-拖动调课", sample, At(Mon, 8, 30), edit: true));
        Try("12-展开-还没有课表", () => View("12-展开-还没有课表", ScheduleData.CreateEmpty(), At(Mon, 9, 0)));
        Try("13-折叠-特大字号", () => View("13-折叠-特大字号", sample, At(Mon, 17, 7), collapsed: true, scale: 1.45));
        Try("14-展开-特大字号", () => View("14-展开-特大字号", sample, At(Mon, 10, 40), scale: 1.45));
        Try("15-折叠-放假", () =>
        {
            var d = sample.DeepClone();
            ScheduleEngine.SetOverride(d, Mon, DayOverrideKind.Holiday);
            View("15-折叠-放假", d, At(Mon, 9, 0), collapsed: true);
        });
        Try("16-展开-锁定", () => View("16-展开-锁定", sample, At(Mon, 14, 0), locked: true));
        Try("17-展开-晚自习-节次名", () => View("17-展开-晚自习-节次名", sample, At(Mon, 18, 50)));
        Try("18-折叠-特小字号", () => View("18-折叠-特小字号", sample, At(Mon, 12, 40), collapsed: true, scale: 0.75));
        Try("19-展开-小字号", () => View("19-展开-小字号", sample, At(Mon, 12, 40), scale: 0.875));
        Try("19b-展开-考试课表", () =>
        {
            var d = sample.DeepClone();
            var exam = SampleData.ExamTimetable();
            d.Timetables.Add(exam);
            ScheduleEngine.SetOverride(d, Mon, DayOverrideKind.UseTimetable, timetableId: exam.Id);
            View("19b-展开-考试课表", d, At(Mon, 9, 0));
        });

        CheckLessonFollowing(sample);

        // 设置窗口
        var tmp = Path.Combine(Path.GetTempPath(), "kebiao-shots-" + Guid.NewGuid().ToString("N"));
        var store = new DataStore(tmp);
        var data = SampleData.Create();
        store.Save(data, "导入课表");
        ScheduleEngine.SetOverride(data, Mon.AddDays(6), DayOverrideKind.FollowWeekday, 5);
        store.Save(data, "11月8日按周五上课");
        ScheduleEngine.SetOverride(data, Mon.AddDays(10), DayOverrideKind.Holiday);
        store.Save(data, "11月12日放假");
        ScheduleEngine.SwapForDay(data, Mon, 1, 2);
        store.Save(data, "调课：第1节⇄第2节（仅11月2日）");
        var examTt = SampleData.ExamTimetable();
        data.Timetables.Add(examTt);
        ScheduleEngine.SetOverrideRange(data, Mon.AddDays(16), Mon.AddDays(18), DayOverrideKind.UseTimetable, timetableId: examTt.Id);
        store.Save(data, "11月18日–11月20日使用“期中考试”");
        var app = new AppController(store, () => At(Mon, 10, 18));

        foreach (var (page, name) in new[] { ("basic", "20-设置-基本"), ("table", "21-设置-课表与作息"), ("dates", "22-设置-日期调整"), ("import", "23-设置-导入导出"), ("history", "24-设置-历史版本") })
        {
            Try(name, () =>
            {
                var w = new SettingsWindow(app);
                w.Show();
                w.ShowPage(page);
                Snap(w, name);
                w.Close();
            });
        }

        Try("25-导入预览-正常", () =>
        {
            var r = TimetableImporter.FromText("""
                班级：26级7班
                开学日期：2026-09-01

                | 节次 | 时间 | 周一 | 周二 | 周三 | 周四 | 周五 | 周六 | 周日 |
                |---|---|---|---|---|---|---|---|---|
                | 1 | 07:40-08:10 | 升旗 | 早读 | 早读 | 早读 | 早读 | | |
                | 2 | 08:20-09:00 | 语文 | 数学 | 英语 | 语文 | 物理 | | |
                | 3 | 09:10-09:50 | 历史 | 语文 | 数学 | 英语 | 数学 | | |
                | 4 | 10:25-11:05 | 数学 | 英语 | 物理 | 化学 | 语文 | | |
                | 5 | 13:45-14:25 | 体育 | 化学 | 生物 | 历史 | 单:音乐 双:美术 | | |
                | 6 | 16:30-17:10 | 语文 | 生物/政治 | 英语 | 物理 | | | |
                """);
            var w = new ImportPreviewWindow(app, r);
            w.Show();
            Snap(w, "25-导入预览-正常");
            w.Close();
        });
        Try("26-导入预览-有错误", () =>
        {
            var r = TimetableImporter.FromText("班级,26级7班\n时间,周一,周二,周三,周四,周五\n08:00-08:40,语文,数学,,,\n08:30-09:10,数学,物理 双:化学,,,\n9点,英语,,,,\n");
            var w = new ImportPreviewWindow(app, r);
            w.Show();
            Snap(w, "26-导入预览-有错误");
            w.Close();
        });
        Try("27-对话框-调课范围", () =>
        {
            var d = ChoiceDialog.CreateChoices("第1节 语文 ⇄ 第2节 历史", null, new[] { "仅今天", "以后每个周一都这样" });
            d.Show();
            Snap(d, "27-对话框-调课范围");
            d.Close();
        });
        Try("28-对话框-单双周", () =>
        {
            var d = ChoiceDialog.CreateChoices("把本周改成单周", null, new[] { "只改本周", "改本学期（之后每周都跟着对调）" });
            d.Show();
            Snap(d, "28-对话框-单双周");
            d.Close();
        });
        Try("29-菜单", () => MenuShot(app));
        Try("28b-对话框-退出", () =>
        {
            var d = ChoiceDialog.CreateConfirm("退出课表？", "退出后桌面上不再显示课表。下次开机会自动启动（如果开启了开机启动），也可以从开始菜单打开。", "退出", true);
            d.Show();
            Snap(d, "28b-对话框-退出");
            d.Close();
        });
        Try("30-时间滚轮", () =>
        {
            var d = TimePickerDialog.CreateForPreview("第3节 上课", new TimeOnly(10, 25));
            d.Show();
            Snap(d, "30-时间滚轮");
            d.Close();
        });
        Try("31-格子编辑-普通", () =>
        {
            var d = CellEditorDialog.CreateForPreview("周一 · 第2节", "历史", sample.Active.UsedSubjects());
            d.Show();
            Snap(d, "31-格子编辑-普通");
            d.Close();
        });
        Try("32-格子编辑-单双周", () =>
        {
            var d = CellEditorDialog.CreateForPreview("周五 · 第5节", "单:音乐\n双:美术", sample.Active.UsedSubjects());
            d.Show();
            Snap(d, "32-格子编辑-单双周");
            d.Close();
        });

        Try("40-展开-看下一天", () => View("40-展开-看下一天", sample, At(Mon, 18, 30), preview: Mon.AddDays(1)));
        Try("41-展开-周五看下一天-跳过周末", () => View("41-展开-周五看下一天-跳过周末", sample, At(Mon.AddDays(4), 18, 30),
            preview: ScheduleViewModel.NextSchoolDay(sample, Mon.AddDays(4))));
        Try("42-主窗口-双击底部自动适应", () => FitCheck(tmp + "-fit", sample, log));

        File.WriteAllText(Path.Combine(_dir, "log.txt"), log.ToString());
        try { Directory.Delete(tmp, true); } catch { }
    }

    private static DateTime At(DateOnly d, int h, int m) => d.ToDateTime(new TimeOnly(h, m));

    /// <summary>
    /// 实测主窗口：先设成装不下的固定高度，再执行“双击底部自动适应”，检查是否还有课被藏在下面。
    /// 当天 12 节课（课多的情况），时间在放学后。
    /// </summary>
    private static void FitCheck(string dir, ScheduleData data, System.Text.StringBuilder log)
    {
        var store = new DataStore(dir);
        store.Save(data.DeepClone(), "截图");
        var app = new AppController(store, () => At(Mon, 20, 0));
        app.Ui.Locked = false;
        app.Ui.Collapsed = false;
        app.Ui.ExpandedHeight = 300;
        app.Vm.Locked = false;
        app.Vm.Collapsed = false;
        var w = new MainWindow(app, app.Vm) { Left = 40, Top = 40, MaxHeight = SystemParameters.WorkArea.Height };
        w.Show();
        try
        {
            w.UpdateLayout();
            Pump();
            log.AppendLine($"     固定高度 300：可滚动 {w.View.RowsScroller.ScrollableHeight:0}");
            Snap(w, "42a-主窗口-固定高度太矮");
            w.FitToContent();
            Pump();
            double hidden = w.View.RowsScroller.ScrollableHeight;
            log.AppendLine($"     自动适应后：窗口高 {w.ActualHeight:0}，工作区高 {SystemParameters.WorkArea.Height:0}，还需滚动 {hidden:0}");
            Snap(w, "42b-主窗口-自动适应后");
            if (hidden > 1 && w.ActualHeight < SystemParameters.WorkArea.Height - 1)
                throw new InvalidOperationException($"自动适应后仍有 {hidden:0} 的内容被藏住");
        }
        finally
        {
            app.AllowWindowClose();
            w.Close();
        }
    }

    private static void View(string name, ScheduleData data, DateTime now, bool collapsed = false, bool edit = false, double scale = 1.0, bool locked = false, DateOnly? preview = null)
    {
        var vm = new ScheduleViewModel { Collapsed = collapsed, EditMode = edit, Scale = scale, Locked = locked, PreviewDate = preview, PreviewRemaining = 0.6 };
        vm.Update(data, now);
        if (edit && vm.Rows.Count > 3)
        {
            vm.Rows[1].IsDragging = true;
            vm.Rows[2].IsDropTarget = true;
        }
        var view = new ScheduleView { DataContext = vm, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var host = new Border
        {
            Padding = new Thickness(36),
            Background = new LinearGradientBrush(Color.FromRgb(0x1E, 0x5A, 0xC8), Color.FromRgb(0x0B, 0x2A, 0x6B), 70),
            Child = view,
        };
        RenderElement(host, name);
    }

    private static void CheckLessonFollowing(ScheduleData data)
    {
        var vm = new ScheduleViewModel();
        var view = new ScheduleView { DataContext = vm };
        var window = new Window
        {
            Content = view, Width = 350, Height = 350, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false,
        };
        window.Show();
        try
        {
            void CheckAt(int hour, int minute)
            {
                vm.Update(data, At(Mon, hour, minute));
                window.UpdateLayout();
                Pump();
                foreach (int? period in new[] { vm.VisiblePeriods.Current, vm.VisiblePeriods.Next })
                {
                    int index = vm.Rows.ToList().FindIndex(r => r.PeriodIndex == period);
                    var row = (FrameworkElement)view.RowsList.ItemContainerGenerator.ContainerFromIndex(index);
                    double top = row.TranslatePoint(new Point(), view.RowsList).Y;
                    double offset = view.RowsScroller.VerticalOffset;
                    if (top < offset - 1 || top + row.ActualHeight > offset + view.RowsScroller.ViewportHeight + 1)
                        throw new InvalidOperationException("自动滚动后，当前课程或下一课程不在可见范围内。");
                }
            }

            CheckAt(10, 40);
            double before = view.RowsScroller.VerticalOffset;
            CheckAt(14, 50);
            if (view.RowsScroller.VerticalOffset <= before)
                throw new InvalidOperationException("课程推进后，展开列表没有向下滚动。");
            Snap(window, "19c-展开-自动跟随");

            vm.Collapsed = true;
            Pump();
            if (view.TopResizeGrip.Visibility != Visibility.Visible || view.BottomResizeGrip.Visibility != Visibility.Visible)
                throw new InvalidOperationException("解锁后无法在折叠模式拖动边框。");
            vm.Locked = true;
            Pump();
            if (view.TopResizeGrip.Visibility != Visibility.Collapsed || view.BottomResizeGrip.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("锁定后仍可拖动边框。");
        }
        finally
        {
            window.Close();
        }
    }

    private static void RenderElement(FrameworkElement e, string name)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        e.Arrange(new Rect(e.DesiredSize));
        e.UpdateLayout();
        Pump();
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        e.Arrange(new Rect(e.DesiredSize));
        e.UpdateLayout();
        Save(e, e.ActualWidth, e.ActualHeight, name);
    }

    private static void Snap(Window w, string name)
    {
        w.UpdateLayout();
        Pump();
        var root = (FrameworkElement)w.Content;
        Save(w, Math.Max(w.ActualWidth, root.ActualWidth), Math.Max(w.ActualHeight, root.ActualHeight), name);
    }

    private static void MenuShot(AppController app)
    {
        var menu = app.BuildMenu();
        var host = new Border { Padding = new Thickness(20), Background = Brushes.WhiteSmoke };
        var panel = new StackPanel { Background = Brushes.White, Width = 240 };
        foreach (var item in menu.Items.Cast<object>().ToList())
        {
            menu.Items.Remove(item);
            panel.Children.Add((UIElement)item);
        }
        host.Child = panel;
        RenderElement(host, "29-菜单");
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Save(Visual v, double w, double h, string name)
    {
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * 2), (int)Math.Ceiling(h * 2), 192, 192, PixelFormats.Pbgra32);
        rtb.Render(v);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(_dir, name + ".png"));
        enc.Save(fs);
    }
}
