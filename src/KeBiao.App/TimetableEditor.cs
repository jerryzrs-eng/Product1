using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeBiao.Core;

namespace KeBiao.App;

/// <summary>“课表与作息”页：表格形式同时编辑上下课时间和每天每节的科目。点“保存”才生效。</summary>
public sealed class TimetableEditor
{
    private sealed class RowBoxes
    {
        public TextBox Start = null!, End = null!;
        public TextBox[] Days = new TextBox[7];
    }

    private readonly AppController _app;
    private readonly Window _owner;
    private readonly ScheduleData _draft;
    private BellSchedule _bell;
    private readonly ComboBox _bells = new() { Width = 220, Margin = new Thickness(0, 0, 12, 0) };
    private readonly Button _useBtn;
    private readonly Grid _grid = new();
    private readonly List<RowBoxes> _rows = new();
    private readonly Brush _errBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
    private bool _loading;

    public bool Dirty { get; private set; }
    public UIElement Root { get; }

    public TimetableEditor(AppController app, Window owner)
    {
        _app = app;
        _owner = owner;
        _draft = app.Data.DeepClone();
        _bell = _draft.ActiveBell;

        var root = new DockPanel();
        var title = new TextBlock { Text = "课表与作息", Style = (Style)Application.Current.FindResource("SectionTitle") };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        // 作息选择
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        bar.Children.Add(new TextBlock { Text = "作息", Style = (Style)Application.Current.FindResource("FieldLabel"), Margin = new Thickness(0, 0, 12, 0) });
        bar.Children.Add(_bells);
        _useBtn = MakeButton("设为使用中", UseSelected);
        bar.Children.Add(_useBtn);
        bar.Children.Add(MakeButton("复制为新作息", CopyBell));
        bar.Children.Add(MakeButton("重命名", RenameBell));
        bar.Children.Add(MakeButton("删除", DeleteBell, "DangerButton"));
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        // 底部按钮
        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        var addRow = MakeButton("添加一节", AddRow);
        var delRow = MakeButton("删除最后一节", RemoveRow);
        bottom.Children.Add(addRow);
        bottom.Children.Add(delRow);
        var save = MakeButton("保存", () => TrySave(), "PrimaryButton");
        save.Margin = new Thickness(0);
        var discard = MakeButton("放弃修改", Discard);
        DockPanel.SetDock(save, Dock.Right);
        DockPanel.SetDock(discard, Dock.Right);
        bottom.Children.Add(save);
        bottom.Children.Add(discard);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var hint = new TextBlock
        {
            Text = "没课的格子留空。走班写成“生物/政治”。单双周不同写成“单:音乐 双:美术”。",
            Style = (Style)Application.Current.FindResource("Hint"),
            Margin = new Thickness(0, 10, 0, 0),
        };
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(hint);

        var scroll = new ScrollViewer
        {
            Content = _grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.Both,
        };
        root.Children.Add(scroll);
        Root = root;

        _bells.SelectionChanged += (_, _) =>
        {
            if (_loading || _bells.SelectedIndex < 0) return;
            var target = _draft.Bells[_bells.SelectedIndex];
            if (target == _bell) return;
            if (!CollectTimes(out var err))
            {
                ChoiceDialog.Info("先改正时间", err);
                _loading = true;
                _bells.SelectedIndex = _draft.Bells.IndexOf(_bell);
                _loading = false;
                return;
            }
            CollectCells();
            _bell = target;
            BuildGrid();
            RefreshBellList();
        };

        RefreshBellList();
        BuildGrid();
    }

    private static Button MakeButton(string text, Action onClick, string style = "BaseButton")
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource(style), Margin = new Thickness(0, 0, 10, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void MarkDirty() { if (!_loading) Dirty = true; }

    private void RefreshBellList()
    {
        _loading = true;
        _bells.Items.Clear();
        foreach (var b in _draft.Bells)
            _bells.Items.Add(b.Id == _draft.ActiveBellId ? $"{b.Name}（使用中）" : b.Name);
        _bells.SelectedIndex = _draft.Bells.IndexOf(_bell);
        _useBtn.IsEnabled = _bell.Id != _draft.ActiveBellId;
        _loading = false;
    }

    // ---------- 表格 ----------

    private void BuildGrid()
    {
        _loading = true;
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        _rows.Clear();

        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        for (int d = 0; d < 7; d++) _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(d < 5 ? 100 : 86) });

        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] heads = { "节次", "上课", "下课", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        for (int c = 0; c < heads.Length; c++)
        {
            var t = new TextBlock
            {
                Text = heads[c],
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("InkSoft"),
                Margin = new Thickness(4, 0, 4, 8),
                HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            };
            Grid.SetColumn(t, c);
            _grid.Children.Add(t);
        }

        _draft.Normalize();
        for (int i = 0; i < _bell.Periods.Count; i++) AddGridRow(i);
        _loading = false;
    }

    private void AddGridRow(int i)
    {
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        int r = i + 1;
        var num = new TextBlock
        {
            Text = (i + 1).ToString(),
            FontSize = 15,
            Foreground = (Brush)Application.Current.FindResource("InkMuted"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        Grid.SetRow(num, r);
        _grid.Children.Add(num);

        var rb = new RowBoxes
        {
            Start = Cell(_bell.Periods[i].Start.ToString("HH:mm"), r, 1, false),
            End = Cell(_bell.Periods[i].End.ToString("HH:mm"), r, 2, false),
        };
        for (int d = 0; d < 7; d++)
            rb.Days[d] = Cell(_draft.Timetable[i][d], r, 3 + d, true);
        _rows.Add(rb);
    }

    private TextBox Cell(string text, int row, int col, bool multi)
    {
        var tb = new TextBox
        {
            Text = text,
            Margin = new Thickness(3),
            FontSize = 15,
            AcceptsReturn = multi,
            TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = 42,
        };
        tb.TextChanged += (_, _) => { MarkDirty(); tb.ClearValue(Control.BorderBrushProperty); tb.ClearValue(FrameworkElement.ToolTipProperty); };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        _grid.Children.Add(tb);
        return tb;
    }

    private void Flag(TextBox tb, string message)
    {
        tb.BorderBrush = _errBrush;
        tb.BorderThickness = new Thickness(2);
        tb.ToolTip = message;
    }

    // ---------- 读取 / 校验 ----------

    private bool CollectTimes(out string error)
    {
        var errors = new List<string>();
        var periods = new List<Period>();
        for (int i = 0; i < _rows.Count; i++)
        {
            var rb = _rows[i];
            bool ok1 = TimetableImporter.TryParseTime(rb.Start.Text, out var s);
            bool ok2 = TimetableImporter.TryParseTime(rb.End.Text, out var e);
            if (!ok1) { Flag(rb.Start, "写成 07:40 这样"); errors.Add($"第{i + 1}节上课时间“{rb.Start.Text}”看不懂"); }
            if (!ok2) { Flag(rb.End, "写成 08:20 这样"); errors.Add($"第{i + 1}节下课时间“{rb.End.Text}”看不懂"); }
            if (ok1 && ok2 && e <= s) { Flag(rb.End, "下课要晚于上课"); errors.Add($"第{i + 1}节下课时间不晚于上课时间"); }
            if (ok1 && ok2 && periods.Count == i && i > 0 && s < periods[i - 1].End)
            {
                Flag(rb.Start, "和上一节重叠");
                errors.Add($"第{i + 1}节和第{i}节时间重叠");
            }
            periods.Add(new Period(s, e));
        }
        error = string.Join("\n", errors.Take(8)) + (errors.Count > 8 ? $"\n……共 {errors.Count} 处" : "");
        if (errors.Count > 0) return false;
        _bell.Periods = periods;
        return true;
    }

    private void CollectCells()
    {
        _draft.Normalize();
        for (int i = 0; i < _rows.Count; i++)
            for (int d = 0; d < 7; d++)
                _draft.Timetable[i][d] = CellParser.Clean(_rows[i].Days[d].Text);
    }

    private List<string> ValidateCells()
    {
        var errors = new List<string>();
        for (int i = 0; i < _rows.Count; i++)
            for (int d = 0; d < 7; d++)
            {
                var p = CellParser.Parse(_rows[i].Days[d].Text);
                if (p.Error != null)
                {
                    Flag(_rows[i].Days[d], p.Error);
                    errors.Add($"第{i + 1}节{ScheduleEngine.WeekdayName(d + 1)}：{p.Error}");
                }
            }
        return errors;
    }

    public bool TrySave()
    {
        bool timesOk = CollectTimes(out var timeErr);
        var cellErrs = ValidateCells();
        if (!timesOk || cellErrs.Count > 0)
        {
            var all = string.Join("\n", new[] { timeErr }.Where(s => s.Length > 0).Concat(cellErrs.Take(8)));
            ChoiceDialog.Info("有几处需要改正（已标红）", all);
            return false;
        }
        CollectCells();

        // 去掉所有作息都用不到的多余行
        int need = _draft.Bells.Max(b => b.Periods.Count);
        if (_draft.Timetable.Count > need) _draft.Timetable.RemoveRange(need, _draft.Timetable.Count - need);

        var data = _app.Data;
        data.Bells = _draft.Bells;
        data.ActiveBellId = _draft.ActiveBellId;
        data.Timetable = _draft.Timetable;
        Dirty = false;
        _app.Commit("编辑课表与作息");
        return true;
    }

    private void Discard()
    {
        if (Dirty && !ChoiceDialog.Confirm("放弃这次的修改？", null, "放弃")) return;
        Dirty = false;
        if (_owner is SettingsWindow sw) sw.ShowPage("table");
    }

    // ---------- 行 ----------

    private void AddRow()
    {
        if (!CollectTimes(out var err)) { ChoiceDialog.Info("先改正时间", err); return; }
        CollectCells();
        var last = _bell.Periods.LastOrDefault();
        var start = last == null ? new TimeOnly(8, 0) : last.End.AddMinutes(10);
        var end = start.AddMinutes(40);
        if (end < start) end = new TimeOnly(23, 59);
        _bell.Periods.Add(new Period(start, end));
        _draft.Normalize();
        _loading = true;
        AddGridRow(_bell.Periods.Count - 1);
        _loading = false;
        Dirty = true;
    }

    private void RemoveRow()
    {
        if (_bell.Periods.Count == 0) return;
        int i = _bell.Periods.Count - 1;
        CollectCells();
        bool hasContent = _draft.Timetable.Count > i && _draft.Timetable[i].Any(c => c.Length > 0);
        if (hasContent && !ChoiceDialog.Confirm($"删除第{i + 1}节？", "这一节已经填了课，会一起删掉。", "删除")) return;
        CollectTimes(out _);
        _bell.Periods.RemoveAt(Math.Min(i, _bell.Periods.Count - 1));
        if (_draft.Bells.All(b => b.Periods.Count <= i) && _draft.Timetable.Count > i) _draft.Timetable.RemoveAt(i);
        BuildGrid();
        Dirty = true;
    }

    // ---------- 多套作息 ----------

    private void UseSelected()
    {
        _draft.ActiveBellId = _bell.Id;
        Dirty = true;
        RefreshBellList();
    }

    private void CopyBell()
    {
        if (!CollectTimes(out var err)) { ChoiceDialog.Info("先改正时间", err); return; }
        CollectCells();
        var name = TextInputDialog.Ask("新作息的名称", _draft.Bells.Count == 1 ? "冬季作息" : $"{_bell.Name} 副本");
        if (string.IsNullOrWhiteSpace(name)) return;
        var copy = _bell.Clone(name.Trim());
        _draft.Bells.Add(copy);
        _bell = copy;
        Dirty = true;
        RefreshBellList();
        BuildGrid();
    }

    private void RenameBell()
    {
        var name = TextInputDialog.Ask("作息名称", _bell.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        _bell.Name = name.Trim();
        Dirty = true;
        RefreshBellList();
    }

    private void DeleteBell()
    {
        if (_draft.Bells.Count <= 1) { ChoiceDialog.Info("至少要保留一套作息", null); return; }
        if (!ChoiceDialog.Confirm($"删除“{_bell.Name}”？", null, "删除")) return;
        CollectCells();
        _draft.Bells.Remove(_bell);
        if (_draft.ActiveBellId == _bell.Id) _draft.ActiveBellId = _draft.Bells[0].Id;
        _bell = _draft.ActiveBell;
        Dirty = true;
        RefreshBellList();
        BuildGrid();
    }
}

/// <summary>输入一行文字的小对话框。</summary>
public sealed class TextInputDialog : Window
{
    private readonly TextBox _box;
    private bool _ok;

    private TextInputDialog(string title, string value)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");

        _box = new TextBox { Text = value, Margin = new Thickness(0, 14, 0, 18), FontSize = 16 };
        var ok = new Button { Content = "确定", Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var cancel = new Button { Content = "取消", Margin = new Thickness(0, 0, 10, 0) };
        ok.Click += (_, _) => { _ok = true; Close(); };
        cancel.Click += (_, _) => Close();
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { _ok = true; Close(); }
            if (e.Key == System.Windows.Input.Key.Escape) Close();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold });
        stack.Children.Add(_box);
        stack.Children.Add(buttons);
        Content = new Border
        {
            Margin = new Thickness(20),
            Padding = new Thickness(28, 24, 28, 22),
            Width = 420,
            CornerRadius = new CornerRadius(18),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.25 },
            Child = stack,
        };
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(string title, string value)
    {
        var d = new TextInputDialog(title, value);
        d.ShowDialog();
        return d._ok ? d._box.Text : null;
    }
}
