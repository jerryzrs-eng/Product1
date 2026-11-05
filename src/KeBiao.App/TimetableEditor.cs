using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeBiao.Core;

namespace KeBiao.App;

/// <summary>
/// “课表与作息”页：管理多张课表，每张课表自带若干套作息。
/// 切换当前课表立即生效，课表与作息的编辑通过“保存”提交。
/// </summary>
public sealed class TimetableEditor
{
    private readonly AppController _app;
    private readonly Window _owner;
    private readonly ScheduleData _draft;
    private Timetable _tt;
    private BellSchedule _bell;

    private readonly ComboBox _tables = new() { Name = "TimetableChoice" };
    private readonly ComboBox _bells = new();
    private readonly Button _useTable;
    private readonly Button _useBell;
    private readonly Button _save;
    private readonly ComboBox _expiryMode = new() { Name = "ExpiryMode", Width = 120 };
    private readonly TextBox _expiryWeeks = new() { Name = "ExpiryWeeks", Width = 72, MaxLength = 3 };
    private readonly DatePicker _expiryDate = new() { Name = "ExpiryDate", Width = 180 };
    private readonly TextBlock _expirySummary = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _grid = new();
    private readonly List<Button> _timeButtons = new();
    private bool _loading;
    private bool _validityValid = true;

    private bool _dirty;

    public bool Dirty
    {
        get => _dirty;
        private set
        {
            _dirty = value;
            _save.IsEnabled = value;
        }
    }
    public UIElement Root { get; }

    private static Brush R(string key) => (Brush)Application.Current.FindResource(key);

    public TimetableEditor(AppController app, Window owner)
    {
        _app = app;
        _owner = owner;
        _draft = app.Data.DeepClone();
        _tt = _draft.Active;
        _bell = _tt.ActiveBell;

        var root = new DockPanel();
        var title = new TextBlock { Text = "课表与作息", Style = (Style)Application.Current.FindResource("SectionTitle") };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        // 两行工具条按同样的列对齐，按钮一样大
        _useTable = ToolButton("设为当前", UseTable);
        var tableBar = Bar("课表", _tables, _useTable,
            ToolButton("新建", NewTable), ToolButton("复制", CopyTable), ToolButton("重命名", RenameTable), ToolButton("删除", DeleteTable, "DangerButton"));
        DockPanel.SetDock(tableBar, Dock.Top);
        root.Children.Add(tableBar);

        var validity = BuildValidityBar();
        DockPanel.SetDock(validity, Dock.Top);
        root.Children.Add(validity);

        _useBell = ToolButton("设为当前", UseBell);
        var bellBar = Bar("作息", _bells, _useBell,
            ToolButton("新建", NewBell), ToolButton("复制", CopyBell), ToolButton("重命名", RenameBell), ToolButton("删除", DeleteBell, "DangerButton"));
        bellBar.Margin = new Thickness(0, 0, 0, 18);
        DockPanel.SetDock(bellBar, Dock.Top);
        root.Children.Add(bellBar);

        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        bottom.Children.Add(BottomButton("添加一节", AddRow));
        bottom.Children.Add(BottomButton("删除最后一节", RemoveRow));
        _save = BottomButton("保存", () => TrySave(), "PrimaryButton");
        _save.Name = "SaveTimetable";
        _save.Margin = new Thickness(0);
        _save.IsEnabled = false;
        var discard = BottomButton("放弃修改", Discard);
        DockPanel.SetDock(_save, Dock.Right);
        DockPanel.SetDock(discard, Dock.Right);
        bottom.Children.Add(_save);
        bottom.Children.Add(discard);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        root.Children.Add(new ScrollViewer
        {
            Content = _grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.Both,
        });
        Root = root;

        _tables.SelectionChanged += (_, _) =>
        {
            if (_loading || _tables.SelectedIndex < 0) return;
            if (!_validityValid)
            {
                _loading = true;
                _tables.SelectedIndex = _draft.Timetables.IndexOf(_tt);
                _loading = false;
                return;
            }
            _tt = _draft.Timetables[_tables.SelectedIndex];
            _bell = _tt.ActiveBell;
            RefreshBars();
            BuildGrid();
        };
        _bells.SelectionChanged += (_, _) =>
        {
            if (_loading || _bells.SelectedIndex < 0) return;
            if (!_validityValid)
            {
                _loading = true;
                _bells.SelectedIndex = _tt.Bells.IndexOf(_bell);
                _loading = false;
                return;
            }
            _bell = _tt.Bells[_bells.SelectedIndex];
            RefreshBars();
            BuildGrid();
        };

        RefreshBars();
        BuildGrid();
    }

    private const double ToolWidth = 96, ControlHeight = 44;

    private static Grid Bar(string label, ComboBox combo, params Button?[] buttons)
    {
        var bar = new Grid { Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252) });
        var l = new TextBlock { Text = label, Style = (Style)Application.Current.FindResource("FieldLabel") };
        bar.Children.Add(l);
        combo.Height = ControlHeight;
        combo.Width = 240;
        combo.Margin = new Thickness(0);
        combo.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(combo, 1);
        bar.Children.Add(combo);
        for (int i = 0; i < buttons.Length; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ToolWidth + 10) });
            if (buttons[i] is not { } b) continue; // 空位：让两行按钮上下对齐
            Grid.SetColumn(b, 2 + i);
            bar.Children.Add(b);
        }
        return bar;
    }

    private static Button ToolButton(string text, Action onClick, string style = "BaseButton")
    {
        var b = MakeButton(text, onClick, style);
        b.Width = ToolWidth;
        b.MinWidth = ToolWidth;
        b.Height = ControlHeight;
        b.Padding = new Thickness(0);
        return b;
    }

    private static Button BottomButton(string text, Action onClick, string style = "BaseButton")
    {
        var b = MakeButton(text, onClick, style);
        b.Width = 132;
        b.Height = ControlHeight;
        b.Padding = new Thickness(0);
        return b;
    }

    private static Button MakeButton(string text, Action onClick, string style = "BaseButton")
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource(style), Margin = new Thickness(0, 0, 10, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    internal void RefreshBars()
    {
        _loading = true;
        _tables.Items.Clear();
        foreach (var t in _draft.Timetables)
            _tables.Items.Add(t.Name + (ScheduleEngine.IsExpired(_app.Data, t, DateOnly.FromDateTime(_app.Now))
                ? "（已作废）" : t.Id == _app.Data.ActiveTimetableId ? "（当前）" : ""));
        _tables.SelectedIndex = _draft.Timetables.IndexOf(_tt);
        UpdateUseTable();
        if (_validityValid)
        {
            _expiryMode.SelectedIndex = _tt.ExpiresOn.HasValue ? 1 : 0;
            _expiryWeeks.Text = _tt.ValidWeeks.ToString();
            _expiryDate.SelectedDate = (_tt.ExpiresOn ?? ScheduleEngine.ExpiryDate(_app.Data, _tt)).ToDateTime(TimeOnly.MinValue);
        }
        UpdateValiditySummary(_validityValid);

        _bells.Items.Clear();
        foreach (var b in _tt.Bells)
            _bells.Items.Add(b.Id == _tt.ActiveBellId ? $"{b.Name}（使用中）" : b.Name);
        _bells.SelectedIndex = _tt.Bells.IndexOf(_bell);
        _useBell.IsEnabled = _bell.Id != _tt.ActiveBellId;
        _loading = false;
    }

    private UIElement BuildValidityBar()
    {
        _expiryMode.Items.Add("按周数");
        _expiryMode.Items.Add("按日期");
        System.Windows.Automation.AutomationProperties.SetName(_expiryMode, "作废方式");
        System.Windows.Automation.AutomationProperties.SetName(_expiryWeeks, "有效周数");
        System.Windows.Automation.AutomationProperties.SetName(_expiryDate, "作废日期");
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(new TextBlock { Text = "有效期", Width = 48, VerticalAlignment = VerticalAlignment.Center, Foreground = R("InkSoft") });
        foreach (var control in new FrameworkElement[] { _expiryMode, _expiryWeeks, _expiryDate, _expirySummary })
        {
            control.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(control);
        }
        _expiryMode.SelectionChanged += (_, _) =>
        {
            if (!_loading && _expiryMode.SelectedIndex == 1 && !_tt.ExpiresOn.HasValue)
            {
                _loading = true;
                _expiryDate.SelectedDate = ScheduleEngine.ExpiryDate(_app.Data, _tt).ToDateTime(TimeOnly.MinValue);
                _loading = false;
            }
            EditValidity();
        };
        _expiryWeeks.TextChanged += (_, _) => EditValidity();
        _expiryDate.SelectedDateChanged += (_, _) => EditValidity();
        return row;
    }

    private bool ReadValidity()
    {
        if (_expiryMode.SelectedIndex == 0)
        {
            if (!int.TryParse(_expiryWeeks.Text, out int weeks) || weeks is < 1 or > Timetable.MaxValidWeeks) return false;
            _tt.ValidWeeks = weeks;
            _tt.ExpiresOn = null;
        }
        else
        {
            if (_expiryDate.SelectedDate is not DateTime date) return false;
            _tt.ExpiresOn = DateOnly.FromDateTime(date);
        }
        return true;
    }

    private void EditValidity()
    {
        if (_loading) return;
        Dirty = true;
        _validityValid = ReadValidity();
        UpdateValiditySummary(_validityValid);
        UpdateUseTable();
    }

    private void UpdateUseTable()
    {
        var today = DateOnly.FromDateTime(_app.Now);
        _useTable.IsEnabled = _validityValid && _tt.Id != _app.Data.ActiveTimetableId &&
            _app.Data.FindTimetable(_tt.Id) is { } saved && !ScheduleEngine.IsExpired(_app.Data, saved, today) &&
            !ScheduleEngine.IsExpired(_app.Data, _tt, today);
    }

    private void UpdateValiditySummary(bool valid = true)
    {
        bool weeks = _expiryMode.SelectedIndex == 0;
        _expiryWeeks.Visibility = weeks ? Visibility.Visible : Visibility.Collapsed;
        _expiryDate.Visibility = weeks ? Visibility.Collapsed : Visibility.Visible;
        var date = ScheduleEngine.ExpiryDate(_app.Data, _tt);
        bool expired = ScheduleEngine.IsExpired(_app.Data, _tt, DateOnly.FromDateTime(_app.Now));
        _expirySummary.Foreground = R(valid ? "InkSoft" : "WarnInk");
        string anchor = _app.Data.TermStart.HasValue ? "从开学周起" : "从首次启用日起";
        _expirySummary.Text = !valid ? (weeks ? "请输入 1–520 周" : "请选择作废日期")
            : $"{(weeks ? anchor + " · " : "")}{date:yyyy年M月d日}起作废{(expired ? " · 仅在设置中保留" : "")}";
    }

    // ---------- 表格 ----------

    private void BuildGrid()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        _timeButtons.Clear();
        _tt.Normalize();

        int[] widths = { 90, 84, 84, 100, 100, 100, 100, 100, 86, 86 };
        foreach (var w in widths) _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] heads = { "节次", "上课", "下课", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        for (int c = 0; c < heads.Length; c++)
            Put(0, c, new TextBlock
            {
                Text = heads[c],
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = R("InkSoft"),
                Margin = new Thickness(4, 0, 4, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
            });

        var tags = _bell.Tags();
        for (int i = 0; i < _bell.Periods.Count; i++)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int r = i + 1;
            Put(r, 0, LabelBox(i, tags[i]));
            Put(r, 1, TimeButton(i, true));
            Put(r, 2, TimeButton(i, false));
            for (int d = 0; d < 7; d++) Put(r, 3 + d, CellButton(i, d));
        }
    }

    private void Put(int row, int col, UIElement e)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        _grid.Children.Add(e);
    }

    /// <summary>节次名输入框；空着时淡淡显示自动编号。</summary>
    private UIElement LabelBox(int i, string autoTag)
    {
        var period = _bell.Periods[i];
        var placeholder = new TextBlock
        {
            Text = string.IsNullOrEmpty(period.Label) ? autoTag : "",
            FontSize = 15,
            Foreground = R("InkMuted"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var box = new TextBox
        {
            Text = period.Label ?? "",
            MaxLength = Period.MaxLabelLength,
            FontSize = 15,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent,
            MinHeight = 48,
            ToolTip = "节次名，最多 4 个字；留空自动编号",
        };
        box.TextChanged += (_, _) =>
        {
            period.Label = Period.CleanLabel(box.Text);
            Dirty = true;
            RefreshPlaceholders();
        };
        box.LostFocus += (_, _) => RefreshPlaceholders();
        var g = new Grid { Margin = new Thickness(3) };
        g.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8) });
        g.Children.Add(placeholder);
        g.Children.Add(box);
        g.Tag = placeholder;
        return g;
    }

    /// <summary>改了某一节的名称后，后面自动编号要跟着变。</summary>
    private void RefreshPlaceholders()
    {
        var tags = _bell.Tags();
        foreach (var child in _grid.Children.OfType<Grid>())
        {
            int row = Grid.GetRow(child) - 1;
            if (Grid.GetColumn(child) != 0 || row < 0 || row >= tags.Count || child.Tag is not TextBlock ph) continue;
            ph.Text = string.IsNullOrEmpty(_bell.Periods[row].Label) ? tags[row] : "";
        }
    }

    private Button TimeButton(int i, bool start)
    {
        var p = _bell.Periods[i];
        var b = new Button
        {
            Content = (start ? p.Start : p.End).ToString("HH:mm"),
            Margin = new Thickness(3),
            MinHeight = 48,
            MinWidth = 0,
            Padding = new Thickness(0),
            FontSize = 16,
            Background = Brushes.White,
        };
        b.Click += (_, _) =>
        {
            var tag = ScheduleEngine.PeriodName(_bell.Tags()[i]);
            var initial = start ? p.Start : p.End;
            if (!start && p.End <= p.Start) initial = p.Start.Add(TypicalLength(i));
            var v = TimePickerDialog.Ask($"{tag} {(start ? "上课" : "下课")}", initial);
            if (v == null) return;
            if (start)
            {
                // 改上课时间时，下课时间跟着平移，保持课长不变
                var len = p.End - p.Start;
                p.Start = v.Value;
                if (len > TimeSpan.Zero) p.End = p.Start.Add(len);
            }
            else p.End = v.Value;
            Dirty = true;
            BuildGrid();
            ValidateTimes(_bell, mark: true);
        };
        b.Tag = (i, start);
        _timeButtons.Add(b);
        return b;
    }

    private TimeSpan TypicalLength(int i)
    {
        for (int k = i - 1; k >= 0; k--)
        {
            var len = _bell.Periods[k].End - _bell.Periods[k].Start;
            if (len > TimeSpan.Zero) return len;
        }
        return TimeSpan.FromMinutes(40);
    }

    private Button CellButton(int i, int d)
    {
        var raw = _tt.Grid[i][d];
        var p = CellParser.Parse(raw);
        object content;
        if (p.HasParity)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(ParityLine("单", p.Odd));
            sp.Children.Add(ParityLine("双", p.Even));
            content = sp;
        }
        else content = new TextBlock { Text = p.Odd, FontSize = 15, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };

        var b = new Button
        {
            Content = content,
            Margin = new Thickness(3),
            MinHeight = 48,
            MinWidth = 0,
            Padding = new Thickness(4, 4, 4, 4),
            Background = raw.Length == 0 ? R("SurfaceAlt") : Brushes.White,
            BorderBrush = raw.Length == 0 ? Brushes.Transparent : R("FieldEdge"),
        };
        b.Click += (_, _) =>
        {
            var tag = ScheduleEngine.PeriodName(_bell.Tags()[i]);
            var v = CellEditorDialog.Ask($"{ScheduleEngine.WeekdayName(d + 1)} · {tag}", _tt.Grid[i][d], _tt.UsedSubjects());
            if (v == null || v == _tt.Grid[i][d]) return;
            _tt.Grid[i][d] = v;
            Dirty = true;
            BuildGrid();
        };
        return b;
    }

    private static UIElement ParityLine(string tag, string? subject)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new Border
        {
            Background = R(tag == "单" ? "AccentFill" : "WarnFill"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 0, 4, 1),
            Margin = new Thickness(0, 1, 5, 1),
            Child = new TextBlock { Text = tag, FontSize = 11, Foreground = R(tag == "单" ? "AccentInk" : "WarnInk") },
        });
        sp.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(subject) ? "—" : subject,
            FontSize = 13,
            Foreground = string.IsNullOrEmpty(subject) ? R("InkMuted") : R("Ink"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return sp;
    }

    // ---------- 校验与保存 ----------

    /// <summary>检查一套作息的时间：下课晚于上课、前后不重叠。返回问题描述。</summary>
    private List<string> ValidateTimes(BellSchedule bell, bool mark)
    {
        var errors = new List<string>();
        var bad = new HashSet<(int, bool)>();
        var tags = bell.Tags();
        for (int i = 0; i < bell.Periods.Count; i++)
        {
            var p = bell.Periods[i];
            var name = ScheduleEngine.PeriodName(tags[i]);
            if (p.End <= p.Start) { errors.Add($"{name}下课时间不晚于上课时间"); bad.Add((i, false)); }
            if (i > 0 && p.Start < bell.Periods[i - 1].End)
            {
                errors.Add($"{name}和上一节时间重叠");
                bad.Add((i, true));
            }
        }
        if (mark && bell == _bell)
        {
            foreach (var b in _timeButtons)
            {
                var key = ((int, bool))b.Tag;
                if (bad.Contains(key))
                {
                    b.BorderBrush = R("Warn");
                    b.BorderThickness = new Thickness(2);
                    b.Foreground = R("WarnInk");
                }
            }
        }
        return errors;
    }

    public bool TrySave()
    {
        if (!Dirty) return true;
        if (!ReadValidity())
        {
            ChoiceDialog.Info("有效期未填写完整", _expiryMode.SelectedIndex == 0 ? "请输入 1–520 之间的周数。" : "请选择作废日期。");
            return false;
        }

        var all = new List<string>();
        foreach (var t in _draft.Timetables)
            foreach (var b in t.Bells)
                foreach (var e in ValidateTimes(b, mark: true))
                    all.Add(_draft.Timetables.Count > 1 || t.Bells.Count > 1 ? $"“{t.Name}”{b.Name}：{e}" : e);
        if (all.Count > 0)
        {
            ChoiceDialog.Info("时间有冲突（已标红）", string.Join("\n", all.Take(8)) + (all.Count > 8 ? $"\n……共 {all.Count} 处" : ""));
            return false;
        }

        foreach (var t in _draft.Timetables)
        {
            int need = t.Bells.Max(b => b.Periods.Count);
            if (t.Grid.Count > need) t.Grid.RemoveRange(need, t.Grid.Count - need);
        }

        var data = _app.Data.DeepClone();
        data.Timetables = _draft.DeepClone().Timetables;
        if (!_app.TrySaveData(data, "编辑课表、作息与有效期")) return false;
        Dirty = false;
        RefreshBars();
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
        var last = _bell.Periods.LastOrDefault();
        TimeOnly start, end;
        if (last == null)
        {
            start = new TimeOnly(8, 0);
            end = start.AddMinutes(40);
        }
        else
        {
            var gap = _bell.Periods.Count >= 2 ? _bell.Periods[^1].Start - _bell.Periods[^2].End : TimeSpan.FromMinutes(10);
            if (gap <= TimeSpan.Zero || gap > TimeSpan.FromMinutes(30)) gap = TimeSpan.FromMinutes(10);
            start = last.End.Add(gap);
            end = start.Add(TypicalLength(_bell.Periods.Count));
            if (end < start) end = new TimeOnly(23, 59);
        }
        _bell.Periods.Add(new Period(start, end));
        _tt.Normalize();
        Dirty = true;
        BuildGrid();
    }

    private void RemoveRow()
    {
        if (_bell.Periods.Count == 0) return;
        int i = _bell.Periods.Count - 1;
        bool hasContent = _tt.Grid.Count > i && _tt.Grid[i].Any(c => c.Length > 0);
        var name = ScheduleEngine.PeriodName(_bell.Tags()[i]);
        if (hasContent && !ChoiceDialog.Confirm($"删除{name}？", "这一节已经填了课，会一起删掉。", "删除")) return;
        _bell.Periods.RemoveAt(i);
        if (_tt.Bells.All(b => b.Periods.Count <= i) && _tt.Grid.Count > i) _tt.Grid.RemoveAt(i);
        Dirty = true;
        BuildGrid();
    }

    // ---------- 多张课表 ----------

    private void UseTable()
    {
        if (_app.Data.FindTimetable(_tt.Id) == null)
        {
            ChoiceDialog.Info("请先保存新课表", "保存后即可设为当前课表。");
            return;
        }
        if (ScheduleEngine.IsExpired(_app.Data, _app.Data.FindTimetable(_tt.Id)!, DateOnly.FromDateTime(_app.Now)))
        {
            ChoiceDialog.Info("课表已作废", "调整有效期并保存后，才可设为当前课表。");
            return;
        }
        _app.SwitchTimetable(_tt.Id);
    }

    private void NewTable()
    {
        if (!_validityValid) return;
        var name = TextInputDialog.Ask("新课表的名称", "期中考试");
        if (string.IsNullOrWhiteSpace(name)) return;
        var t = new Timetable { Name = name.Trim() };
        var bell = _bell.Clone(_bell.Name); // 沿用当前作息时间
        bell.Id = Guid.NewGuid().ToString("N");
        t.Bells.Add(bell);
        t.ActiveBellId = bell.Id;
        t.Normalize();
        _draft.Timetables.Add(t);
        _tt = t;
        _bell = bell;
        Dirty = true;
        RefreshBars();
        BuildGrid();
    }

    private void CopyTable()
    {
        if (!_validityValid) return;
        var name = TextInputDialog.Ask("复制出的课表名称", $"{_tt.Name} 副本");
        if (string.IsNullOrWhiteSpace(name)) return;
        var t = _tt.Clone(name.Trim());
        _draft.Timetables.Add(t);
        _tt = t;
        _bell = t.ActiveBell;
        Dirty = true;
        RefreshBars();
        BuildGrid();
    }

    private void RenameTable()
    {
        var name = TextInputDialog.Ask("课表名称", _tt.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        _tt.Name = name.Trim();
        Dirty = true;
        RefreshBars();
    }

    private void DeleteTable()
    {
        if (!_validityValid) return;
        if (_draft.Timetables.Count <= 1) { ChoiceDialog.Info("至少要保留一张课表", null); return; }
        int days = _draft.DayOverrides.Count(o => o.Kind == DayOverrideKind.UseTimetable && o.TimetableId == _tt.Id);
        var body = days > 0 ? $"有 {days} 天的日期调整用到这张课表，会一起删除。" : null;
        if (!ChoiceDialog.Confirm($"删除课表“{_tt.Name}”？", body, "删除")) return;
        _draft.Timetables.Remove(_tt);
        _tt = _draft.FindTimetable(_app.Data.ActiveTimetableId) ?? _draft.Timetables[0];
        _bell = _tt.ActiveBell;
        Dirty = true;
        RefreshBars();
        BuildGrid();
    }

    // ---------- 多套作息 ----------

    private void UseBell()
    {
        _tt.ActiveBellId = _bell.Id;
        Dirty = true;
        RefreshBars();
    }

    /// <summary>
    /// 新建一套空白作息：节数和节次名沿用当前作息，保证和这张课表的科目一行对一行；
    /// 时间从 08:00 起按 40 分钟一节、课间 10 分钟排好，再逐个改成实际时间。
    /// </summary>
    private void NewBell()
    {
        var name = TextInputDialog.Ask("新作息的名称", "新作息");
        if (string.IsNullOrWhiteSpace(name)) return;
        var bell = new BellSchedule { Name = name.Trim() };
        int count = Math.Max(_bell.Periods.Count, 1);
        var start = new TimeOnly(8, 0);
        for (int i = 0; i < count; i++)
        {
            var end = start.AddMinutes(40);
            if (end < start) end = new TimeOnly(23, 59); // 跨过午夜就停在 23:59
            bell.Periods.Add(new Period(start, end, i < _bell.Periods.Count ? _bell.Periods[i].Label : null));
            var next = end.AddMinutes(10);
            start = next < end ? new TimeOnly(23, 59) : next;
        }
        _tt.Bells.Add(bell);
        _bell = bell;
        Dirty = true;
        RefreshBars();
        BuildGrid();
    }

    private void CopyBell()
    {
        var name = TextInputDialog.Ask("新作息的名称", _tt.Bells.Count == 1 ? "冬季作息" : $"{_bell.Name} 副本");
        if (string.IsNullOrWhiteSpace(name)) return;
        var copy = _bell.Clone(name.Trim());
        _tt.Bells.Add(copy);
        _bell = copy;
        Dirty = true;
        RefreshBars();
        BuildGrid();
    }

    private void RenameBell()
    {
        var name = TextInputDialog.Ask("作息名称", _bell.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        _bell.Name = name.Trim();
        Dirty = true;
        RefreshBars();
    }

    private void DeleteBell()
    {
        if (_tt.Bells.Count <= 1) { ChoiceDialog.Info("每张课表至少要保留一套作息", null); return; }
        if (!ChoiceDialog.Confirm($"删除“{_bell.Name}”？", null, "删除")) return;
        _tt.Bells.Remove(_bell);
        if (_tt.ActiveBellId == _bell.Id) _tt.ActiveBellId = _tt.Bells[0].Id;
        _bell = _tt.ActiveBell;
        Dirty = true;
        RefreshBars();
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

        _box = new TextBox { Text = value, Margin = new Thickness(0, 14, 0, 18), FontSize = 16, MaxLength = 20 };
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
