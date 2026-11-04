using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeBiao.Core;

namespace KeBiao.App;

/// <summary>导入前的预览：把解析结果按表格摆出来，有问题的格子标红，确认后才写入。</summary>
public sealed class ImportPreviewWindow : Window
{
    private readonly AppController _app;
    private readonly ImportResult _r;
    private readonly ComboBox _target = new() { Width = 260 };
    private readonly CheckBox _makeActive = new() { Content = "设为当前课表", IsChecked = true, Margin = new Thickness(20, 0, 0, 0) };
    private readonly CheckBox _useClass = new();
    private readonly CheckBox _useTerm = new();

    public ImportPreviewWindow(AppController app, ImportResult result)
    {
        _app = app;
        _r = result;
        Title = "导入预览";
        Width = 1080;
        Height = 760;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Background = (Brush)Application.Current.FindResource("Surface");
        UseLayoutRounding = true;
        Content = Build();
    }

    private static Brush R(string key) => (Brush)Application.Current.FindResource(key);

    private UIElement Build()
    {
        var root = new DockPanel { Margin = new Thickness(32, 26, 32, 22) };

        var title = new TextBlock
        {
            Text = _r.Ok ? $"读到 {_r.Periods.Count} 节课" : "有问题需要先改正",
            Style = (Style)Application.Current.FindResource("SectionTitle"),
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        // 底部：选项与按钮
        var bottom = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var ok = new Button { Content = "导入", Style = (Style)Application.Current.FindResource("PrimaryButton"), IsEnabled = _r.Ok };
        var cancel = new Button { Content = "取消", Margin = new Thickness(0, 0, 10, 0) };
        ok.Click += (_, _) => Apply();
        cancel.Click += (_, _) => { DialogResult = false; };
        DockPanel.SetDock(ok, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);

        if (_r.Ok)
        {
            var opts = new StackPanel { Orientation = Orientation.Horizontal };
            opts.Children.Add(new TextBlock { Text = "导入到", Style = (Style)Application.Current.FindResource("FieldLabel"), Margin = new Thickness(0, 0, 10, 0) });
            var data = _app.Data;
            foreach (var t in data.Timetables)
                _target.Items.Add(t.Id == data.ActiveTimetableId ? $"{t.Name}（使用中）" : t.Name);
            _target.Items.Add("新建课表");
            _target.SelectedIndex = Math.Max(0, data.Timetables.FindIndex(t => t.Id == data.ActiveTimetableId));
            opts.Children.Add(_target);
            _target.SelectionChanged += (_, _) => UpdateMakeActive();
            opts.Children.Add(_makeActive);
            UpdateMakeActive();
            if (!string.IsNullOrWhiteSpace(_r.ClassName) && _r.ClassName != _app.Data.ClassName)
            {
                _useClass.Content = $"班级改为“{_r.ClassName}”";
                _useClass.IsChecked = true;
                _useClass.Margin = new Thickness(20, 0, 0, 0);
                opts.Children.Add(_useClass);
            }
            if (_r.TermStart is { } ts && ts != _app.Data.TermStart)
            {
                _useTerm.Content = $"开学日期改为 {ts:yyyy-MM-dd}";
                _useTerm.IsChecked = true;
                _useTerm.Margin = new Thickness(20, 0, 0, 0);
                opts.Children.Add(_useTerm);
            }
            bottom.Children.Add(opts);
        }
        root.Children.Add(bottom);

        // 问题列表
        if (_r.Errors.Count > 0 || _r.Warnings.Count > 0)
        {
            var issues = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var e in _r.Errors.Take(12))
                issues.Children.Add(new TextBlock { Text = e.ToString(), Foreground = R("WarnInk"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            if (_r.Errors.Count > 12)
                issues.Children.Add(new TextBlock { Text = $"……还有 {_r.Errors.Count - 12} 处", Foreground = R("WarnInk"), FontSize = 14 });
            foreach (var w in _r.Warnings.Take(6))
                issues.Children.Add(new TextBlock { Text = w.ToString(), Foreground = R("InkMuted"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            var scroll = new ScrollViewer { Content = issues, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            DockPanel.SetDock(scroll, Dock.Bottom);
            root.Children.Add(scroll);
        }

        root.Children.Add(new ScrollViewer
        {
            Content = BuildTable(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.Both,
        });
        return root;
    }

    private UIElement BuildTable()
    {
        var g = new Grid();
        var tags = new BellSchedule { Periods = _r.Periods }.Tags();
        int[] widths = { 72, 130, 104, 104, 104, 104, 104, 92, 92 };
        foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] heads = { "节次", "时间", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        for (int c = 0; c < heads.Length; c++)
            Put(g, 0, c, new TextBlock { Text = heads[c], FontWeight = FontWeights.SemiBold, Foreground = R("InkSoft"), FontSize = 14, Margin = new Thickness(6, 0, 6, 8) });

        for (int i = 0; i < _r.Periods.Count; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int row = i + 1;
            Put(g, row, 0, new TextBlock { Text = tags[i], Foreground = R("InkMuted"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            var p = _r.Periods[i];
            string time = _r.CellErrors.ContainsKey((i, 0)) && p.Start == default && p.End == default ? "？" : $"{p.Start:HH\\:mm}–{p.End:HH\\:mm}";
            Put(g, row, 1, Box(time, _r.CellErrors.TryGetValue((i, 0), out var te) ? te : null));
            for (int d = 0; d < 7; d++)
            {
                var text = _r.Timetable[i][d];
                Put(g, row, 2 + d, Box(text, _r.CellErrors.TryGetValue((i, d + 1), out var ce) ? ce : null));
            }
        }
        return g;
    }

    private static Border Box(string text, string? error) => new()
    {
        Margin = new Thickness(2),
        Padding = new Thickness(6, 8, 6, 8),
        CornerRadius = new CornerRadius(8),
        MinHeight = 40,
        Background = error != null ? R("WarnFill") : R("SurfaceAlt"),
        BorderBrush = error != null ? R("Warn") : Brushes.Transparent,
        BorderThickness = new Thickness(error != null ? 1.5 : 0),
        ToolTip = error,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = error != null ? R("WarnInk") : R("Ink"),
        },
    };

    private static void Put(Grid g, int row, int col, UIElement e)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    private string? TargetId =>
        _target.SelectedIndex >= 0 && _target.SelectedIndex < _app.Data.Timetables.Count ? _app.Data.Timetables[_target.SelectedIndex].Id : null;

    /// <summary>导入到正在使用的课表时，它本来就是当前课表，不需要这个选项。</summary>
    private void UpdateMakeActive()
    {
        bool isActive = TargetId == _app.Data.ActiveTimetableId;
        _makeActive.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;
        if (TargetId == null) _makeActive.IsChecked = _app.Data.IsEmpty;
    }

    private void Apply()
    {
        var data = _app.Data;
        var targetId = TargetId;
        string? newName = null;
        if (targetId == null)
        {
            newName = TextInputDialog.Ask("新课表的名称", data.IsEmpty ? "常规课表" : "新课表");
            if (newName == null) return;
        }
        bool makeActive = targetId == data.ActiveTimetableId || _makeActive.IsChecked == true;
        var tt = TimetableImporter.Apply(data, _r, targetId, newName, makeActive, _useClass.IsChecked == true, _useTerm.IsChecked == true);
        _app.Commit($"导入课表“{tt.Name}”");
        DialogResult = true;
    }
}
