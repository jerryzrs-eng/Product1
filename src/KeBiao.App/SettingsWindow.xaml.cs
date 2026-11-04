using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KeBiao.Core;
using Microsoft.Win32;

namespace KeBiao.App;

public partial class SettingsWindow : Window
{
    private readonly AppController _app;
    private string _page = "basic";
    private TimetableEditor? _editor;
    private bool _suppress;

    public SettingsWindow(AppController app)
    {
        _app = app;
        InitializeComponent();
        NavBasic.Checked += (_, _) => Navigate("basic");
        NavTable.Checked += (_, _) => Navigate("table");
        NavDates.Checked += (_, _) => Navigate("dates");
        NavImport.Checked += (_, _) => Navigate("import");
        NavHistory.Checked += (_, _) => Navigate("history");
        NavBasic.IsChecked = true;
        Closing += (_, e) => { if (!_app.Exiting && !ConfirmLeaveEditor()) e.Cancel = true; };
    }

    private ScheduleData Data => _app.Data;
    private DateOnly Today => DateOnly.FromDateTime(_app.Now);

    public void ShowPage(string page)
    {
        var nav = page switch { "table" => NavTable, "dates" => NavDates, "import" => NavImport, "history" => NavHistory, _ => NavBasic };
        if (nav.IsChecked == true) Navigate(page, force: true);
        else nav.IsChecked = true;
    }

    /// <summary>数据在别处被改了（比如主界面调课），刷新当前页。</summary>
    public void OnDataChanged()
    {
        if (_page == "table" && _editor?.Dirty == true) return; // 正在编辑的课表不打断
        Navigate(_page, force: true);
    }

    private void Navigate(string page, bool force = false)
    {
        if (!force && page == _page && PageHost.Content != null) return;
        if (_page == "table" && page != "table" && !ConfirmLeaveEditor())
        {
            _suppress = true;
            NavTable.IsChecked = true;
            _suppress = false;
            return;
        }
        if (_suppress) return;
        _page = page;
        _editor = null;
        PageHost.Content = page switch
        {
            "table" => BuildTablePage(),
            "dates" => BuildDatesPage(),
            "import" => BuildImportPage(),
            "history" => BuildHistoryPage(),
            _ => BuildBasicPage(),
        };
    }

    private bool ConfirmLeaveEditor()
    {
        if (_editor?.Dirty != true) return true;
        int c = ChoiceDialog.Ask("课表还没保存", null, "保存", "不保存");
        if (c < 0) return false;
        if (c == 0) return _editor.TrySave();
        _editor = null;
        return true;
    }

    // ---------- 小工具 ----------

    private TextBlock PageTitle(string text) => new() { Text = text, Style = (Style)FindResource("SectionTitle") };
    private TextBlock Label(string text) => new() { Text = text, Style = (Style)FindResource("FieldLabel") };
    private TextBlock Hint(string text) => new() { Text = text, Style = (Style)FindResource("Hint") };

    private static Button Btn(string text, Action onClick, string? style = null, FrameworkElement? host = null)
    {
        var b = new Button { Content = text };
        if (style != null) b.Style = (Style)Application.Current.FindResource(style);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>左边一列标签、右边一列内容的表单。</summary>
    private static Grid Form(params (string label, UIElement content)[] rows)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < rows.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock
            {
                Text = rows[i].label,
                Style = (Style)Application.Current.FindResource("FieldLabel"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 0, 0),
            };
            Grid.SetRow(l, i);
            g.Children.Add(l);
            var c = rows[i].content;
            if (c is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, 18);
            Grid.SetRow(c, i);
            Grid.SetColumn(c, 1);
            g.Children.Add(c);
        }
        return g;
    }

    private static StackPanel Row(params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items)
        {
            if (i is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 0, 12, 0);
            p.Children.Add(i);
        }
        return p;
    }

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        PanningMode = PanningMode.VerticalOnly,
    };

    // ---------- 基本 ----------

    private UIElement BuildBasicPage()
    {
        var root = new StackPanel();
        root.Children.Add(PageTitle("基本"));

        var name = new TextBox { Text = Data.ClassName, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        void SaveName()
        {
            var v = name.Text.Trim();
            if (v == Data.ClassName) return;
            Data.ClassName = v;
            _app.Commit("修改班级名称");
        }
        name.LostFocus += (_, _) => SaveName();
        name.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SaveName(); };

        var start = new DatePicker
        {
            SelectedDate = Data.TermStart?.ToDateTime(TimeOnly.MinValue),
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 14, 0),
        };
        var weekText = Label("");
        void UpdateWeek()
        {
            var w = ScheduleEngine.WeekNumber(Data, Today);
            weekText.Text = Data.TermStart == null ? "" : w is int n ? $"本周是第 {n} 周" : "还没开学";
        }
        UpdateWeek();
        start.SelectedDateChanged += (_, _) =>
        {
            DateOnly? d = start.SelectedDate is DateTime dt ? DateOnly.FromDateTime(dt) : null;
            if (d == Data.TermStart) return;
            Data.TermStart = d;
            _app.Commit("修改开学日期");
            UpdateWeek();
        };

        bool showParity = Data.UsesParity;
        var parityText = Label("");
        void UpdateParity() => parityText.Text = ScheduleEngine.IsOddWeek(Data, Today) ? "本周：单周" : "本周：双周";
        UpdateParity();
        var flip = Btn("切换单双周", () =>
        {
            bool odd = ScheduleEngine.IsOddWeek(Data, Today);
            var to = odd ? "双周" : "单周";
            int c = ChoiceDialog.Ask($"把本周改成{to}", null, "只改本周", "改本学期（之后每周都跟着对调）");
            if (c < 0) return;
            if (c == 0) { ScheduleEngine.FlipWeek(Data, Today); _app.Commit($"本周改为{to}"); }
            else { ScheduleEngine.FlipTerm(Data); _app.Commit("本学期单双周对调"); }
            UpdateParity();
        });
        flip.Margin = new Thickness(16, 0, 0, 0);

        var font = new Border
        {
            Background = (Brush)FindResource("SurfaceAlt"),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(2),
        };
        var fontRow = new StackPanel { Orientation = Orientation.Horizontal };
        string[] fontNames = { "特小", "小", "标准", "大", "特大" };
        for (int i = 0; i < fontNames.Length; i++)
        {
            int lv = i - 2;
            var rb = new RadioButton { Content = fontNames[i], GroupName = "font", Style = (Style)FindResource("Segment"), IsChecked = _app.Ui.FontLevel == lv };
            rb.Checked += (_, _) => _app.SetFontLevel(lv);
            fontRow.Children.Add(rb);
        }
        font.Child = fontRow;

        var soon = new Border
        {
            Background = (Brush)FindResource("SurfaceAlt"),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(2),
        };
        var soonRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var m in ScheduleEngine.SoonOptions)
        {
            int minutes = m;
            var rb = new RadioButton
            {
                Content = m == 0 ? "上一节下课后" : $"提前 {m} 分钟",
                GroupName = "soon",
                Style = (Style)FindResource("Segment"),
                IsChecked = Data.SoonMinutes == m,
            };
            rb.Checked += (_, _) =>
            {
                if (Data.SoonMinutes == minutes) return;
                Data.SoonMinutes = minutes;
                _app.Commit(minutes == 0 ? "即将开始：上一节下课后提示" : $"即将开始：提前 {minutes} 分钟提示");
            };
            soonRow.Children.Add(rb);
        }
        soon.Child = soonRow;

        var top = new CheckBox { Content = "始终显示在最上层", IsChecked = _app.Ui.Topmost };
        top.Checked += (_, _) => _app.SetTopmost(true);
        top.Unchecked += (_, _) => _app.SetTopmost(false);
        var topPanel = new StackPanel();
        topPanel.Children.Add(top);
        topPanel.Children.Add(Hint("关闭时课表贴在桌面上，不挡课件；单击托盘图标可临时浮到最上面看一眼。"));

        var auto = new CheckBox { Content = "开机自动启动", IsChecked = SystemIntegration.AutoStartEnabled };
        auto.Checked += (_, _) => SystemIntegration.AutoStartEnabled = true;
        auto.Unchecked += (_, _) => SystemIntegration.AutoStartEnabled = false;

        var rows = new List<(string, UIElement)>
        {
            ("班级", name),
            ("开学日期", Row(start, weekText)),
        };
        // 课表里没有任何单双周的课时，不显示单双周设置
        if (showParity) rows.Add(("单双周", Row(parityText, flip)));
        rows.Add(("即将开始", soon));
        rows.Add(("字号", font));
        rows.Add(("显示", topPanel));
        rows.Add(("启动", auto));
        root.Children.Add(Form(rows.ToArray()));
        return Scroll(root);
    }

    // ---------- 课表与作息 ----------

    private UIElement BuildTablePage()
    {
        _editor = new TimetableEditor(_app, this);
        return _editor.Root;
    }

    // ---------- 日期调整 ----------

    private UIElement BuildDatesPage()
    {
        var root = new StackPanel();
        root.Children.Add(PageTitle("日期调整"));

        var from = new DatePicker { SelectedDate = _app.Now.Date, Width = 200 };
        var to = new DatePicker { SelectedDate = _app.Now.Date, Width = 200 };
        from.SelectedDateChanged += (_, _) =>
        {
            if (from.SelectedDate is DateTime f && (to.SelectedDate is not DateTime t || t < f)) to.SelectedDate = f;
        };
        var kind = new ComboBox { Width = 220 };
        var options = new List<(DayOverrideKind kind, int weekday, string? id)>();
        for (int wd = 1; wd <= 7; wd++) { kind.Items.Add($"按{ScheduleEngine.WeekdayName(wd)}上课"); options.Add((DayOverrideKind.FollowWeekday, wd, null)); }
        kind.Items.Add("放假"); options.Add((DayOverrideKind.Holiday, 1, null));
        foreach (var t in Data.Timetables.Where(t => t.Id != Data.ActiveTimetableId))
        {
            kind.Items.Add($"使用“{t.Name}”");
            options.Add((DayOverrideKind.UseTimetable, 1, t.Id));
        }
        kind.SelectedIndex = 0;
        var add = Btn("添加", () =>
        {
            if (from.SelectedDate is not DateTime f || kind.SelectedIndex < 0) return;
            var t = to.SelectedDate is DateTime tt ? tt : f;
            var (k, wd, id) = options[kind.SelectedIndex];
            int days = Math.Abs((t - f).Days) + 1;
            if (days > 60) { ChoiceDialog.Info("日期范围太长", "一次最多设置 60 天。长期改用另一张课表，请在“课表与作息”里把它设为当前课表。"); return; }
            _app.SetDays(DateOnly.FromDateTime(f), DateOnly.FromDateTime(t), k, wd, id);
        }, "PrimaryButton");
        var dash = Label("至");
        dash.Margin = new Thickness(0, 0, 12, 0);
        root.Children.Add(Row(from, dash, to, kind, add));
        root.Children.Add(new FrameworkElement { Height = 24 });

        // 把连续几天相同的调整合并成一行
        var list = Data.DayOverrides.Where(o => o.Date >= Today).OrderBy(o => o.Date).ToList();
        var groups = new List<List<DayOverride>>();
        foreach (var o in list)
        {
            var last = groups.LastOrDefault();
            var prev = last?.Last();
            if (prev != null && prev.Date.AddDays(1) == o.Date && prev.Kind == o.Kind && prev.Weekday == o.Weekday && prev.TimetableId == o.TimetableId)
                last!.Add(o);
            else groups.Add(new List<DayOverride> { o });
        }
        var swapToday = Data.SubjectOverrides.Any(o => o.Date == Today);
        if (groups.Count == 0 && !swapToday)
            root.Children.Add(Hint("调休补课、放假、考试周在上面添加，只在那几天生效，过后自动失效。"));
        foreach (var g in groups)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string D(DateOnly d) => $"{d.Month}月{d.Day}日 {ScheduleEngine.WeekdayName(ScheduleEngine.ToWeekday(d.DayOfWeek))}";
            var first = g[0];
            var text = g.Count == 1 ? D(first.Date) : $"{D(first.Date)} – {D(g[^1].Date)}";
            if (first.Date == Today) text += g.Count == 1 ? "（今天）" : "（今天起）";
            var dl = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var kl = new TextBlock { Text = _app.DescribeOverride(first.Kind, first.Weekday, first.TimetableId), FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("InkSoft") };
            var dates = g.Select(o => o.Date).ToList();
            var del = Btn("删除", () =>
            {
                foreach (var d in dates) ScheduleEngine.ClearOverride(Data, d);
                _app.Commit(dates.Count == 1 ? $"{dates[0].Month}月{dates[0].Day}日恢复正常课表" : $"{dates[0].Month}月{dates[0].Day}日起 {dates.Count} 天恢复正常课表");
            }, "DangerButton");
            Grid.SetColumn(kl, 1);
            Grid.SetColumn(del, 2);
            row.Children.Add(dl); row.Children.Add(kl); row.Children.Add(del);
            root.Children.Add(row);
        }
        if (swapToday)
        {
            root.Children.Add(new FrameworkElement { Height = 12 });
            root.Children.Add(Row(Label("今天有临时调课"), Btn("恢复今天原来的课表", () =>
            {
                Data.SubjectOverrides.RemoveAll(o => o.Date == Today);
                _app.Commit("撤销今天的临时调课");
            })));
        }
        return Scroll(root);
    }

    // ---------- 导入导出 ----------

    private UIElement BuildImportPage()
    {
        var root = new StackPanel();
        root.Children.Add(PageTitle("导入导出"));

        root.Children.Add(Row(
            Btn("从文件导入（Excel / CSV）", ImportFromFile, "PrimaryButton"),
            Btn("获取模板和转写说明", ExportTemplate)));
        root.Children.Add(new FrameworkElement { Height = 8 });
        root.Children.Add(Hint("原课表格式不一样时：把原课表、模板和转写说明一起发给 AI 助手，让它按模板转写，再导入它给你的文件或表格。"));

        root.Children.Add(new FrameworkElement { Height = 28 });
        root.Children.Add(Label("粘贴导入"));
        var paste = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 180,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 10),
        };
        root.Children.Add(paste);
        root.Children.Add(Row(Btn("预览并导入", () =>
        {
            if (string.IsNullOrWhiteSpace(paste.Text)) return;
            if (ShowImport(TimetableImporter.FromText(paste.Text))) paste.Clear();
        }, "PrimaryButton"), Hint("支持 AI 输出的表格、从 Excel 复制的单元格、CSV 文字。")));

        root.Children.Add(new FrameworkElement { Height = 36 });
        root.Children.Add(Label("全部设置"));
        root.Children.Add(new FrameworkElement { Height = 8 });
        root.Children.Add(Row(Btn("导出到文件", ExportAll), Btn("从文件恢复", ImportAll)));
        root.Children.Add(new FrameworkElement { Height = 8 });
        root.Children.Add(Hint("导出的文件包含班级、开学日期、课表、作息和日期调整，可以拷到另一块白板上恢复。"));
        return Scroll(root);
    }

    private void ImportFromFile()
    {
        var dlg = new OpenFileDialog { Filter = "课表文件|*.xlsx;*.csv;*.txt;*.md|所有文件|*.*", Title = "选择课表文件" };
        if (dlg.ShowDialog(this) != true) return;
        ShowImport(TimetableImporter.FromFile(dlg.FileName));
    }

    private bool ShowImport(ImportResult result)
    {
        var w = new ImportPreviewWindow(_app, result) { Owner = this };
        return w.ShowDialog() == true;
    }

    private void ExportTemplate()
    {
        var dlg = new OpenFolderDialog { Title = "选择保存模板的文件夹" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var (t, _) = TemplateWriter.WriteAll(dlg.FolderName);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{t}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ChoiceDialog.Info("保存失败", ex.Message);
        }
    }

    private void ExportAll()
    {
        var name = string.IsNullOrWhiteSpace(Data.ClassName) ? "课表设置" : $"课表设置-{Data.ClassName}";
        var dlg = new SaveFileDialog { Filter = "课表设置|*.json", FileName = name + ".json" };
        if (dlg.ShowDialog(this) != true) return;
        try { _app.Store.Export(Data, dlg.FileName); }
        catch (Exception ex) { ChoiceDialog.Info("导出失败", ex.Message); }
    }

    private void ImportAll()
    {
        var dlg = new OpenFileDialog { Filter = "课表设置|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        var d = DataStore.ReadExport(dlg.FileName);
        if (d == null) { ChoiceDialog.Info("文件不对", "这不是课表导出的设置文件，或者文件已损坏。"); return; }
        if (!ChoiceDialog.Confirm($"用“{(string.IsNullOrWhiteSpace(d.ClassName) ? "未命名" : d.ClassName)}”的设置替换当前全部设置？",
                "当前设置会存进历史版本，可以随时恢复。", "替换")) return;
        _app.ReplaceData(d, "从文件恢复全部设置");
    }

    // ---------- 历史版本 ----------

    private UIElement BuildHistoryPage()
    {
        var root = new StackPanel();
        root.Children.Add(PageTitle("历史版本"));
        var list = _app.Store.ListBackups();
        if (list.Count == 0)
        {
            root.Children.Add(Hint("还没有历史版本。每次修改课表前会自动保存一份，最多保留 20 份。"));
            return root;
        }
        foreach (var b in list)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new TextBlock { Text = b.SavedAt.ToString("M月d日 HH:mm"), FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var r = new TextBlock { Text = $"“{b.Reason}”之前", FontSize = 15, Foreground = (Brush)FindResource("InkSoft"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var info = b;
            var restore = Btn("恢复", () =>
            {
                if (!ChoiceDialog.Confirm($"恢复到 {info.SavedAt:M月d日 HH:mm} 的版本？", "当前版本也会先存一份，恢复错了还能再恢复回来。", "恢复", danger: false)) return;
                var d = _app.Store.ReadBackup(info);
                if (d == null) { ChoiceDialog.Info("恢复失败", "这个版本的文件已损坏。"); return; }
                _app.ReplaceData(d, $"恢复到 {info.SavedAt:M月d日 HH:mm} 的版本");
            });
            Grid.SetColumn(r, 1);
            Grid.SetColumn(restore, 2);
            g.Children.Add(t); g.Children.Add(r); g.Children.Add(restore);
            root.Children.Add(g);
        }
        return Scroll(root);
    }
}
