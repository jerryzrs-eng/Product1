using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using KeBiao.Core;

namespace KeBiao.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum RowState { Past, Current, Soon, Future, Message }

public sealed class LessonRow : Observable
{
    private int _periodIndex = -1;
    private string _number = "";
    private string _name = "";
    private string _timeText = "";
    private RowState _state;
    private double _progress;
    private bool _large;
    private bool _editing;
    private bool _isDragging;
    private bool _isDropTarget;

    public int PeriodIndex { get => _periodIndex; set => Set(ref _periodIndex, value); }
    public string Number { get => _number; set => Set(ref _number, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string TimeText { get => _timeText; set => Set(ref _timeText, value); }
    public RowState State { get => _state; set => Set(ref _state, value); }
    /// <summary>当前这节课已经过去的比例（0~1）。</summary>
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    /// <summary>折叠模式下用大字。</summary>
    public bool Large { get => _large; set => Set(ref _large, value); }
    public bool Editing { get => _editing; set => Set(ref _editing, value); }
    public bool IsDragging { get => _isDragging; set => Set(ref _isDragging, value); }
    public bool IsDropTarget { get => _isDropTarget; set => Set(ref _isDropTarget, value); }

    public void CopyFrom(LessonRow o)
    {
        PeriodIndex = o.PeriodIndex; Number = o.Number; Name = o.Name; TimeText = o.TimeText;
        State = o.State; Progress = o.Progress; Large = o.Large; Editing = o.Editing;
    }
}

public sealed class ScheduleViewModel : Observable
{
    private static readonly CultureInfo Zh = new("zh-CN");

    private string _className = "";
    private string _dateText = "";
    private string? _emptyText;
    private string? _footerText;
    private bool _collapsed;
    private bool _locked;
    private bool _editMode;
    private double _scale = 1.0;

    public string ClassName { get => _className; set => Set(ref _className, value); }
    public string DateText { get => _dateText; set => Set(ref _dateText, value); }
    /// <summary>展开时列表为空要显示的文字（放假、无课、还没导入）。</summary>
    public string? EmptyText { get => _emptyText; set { if (Set(ref _emptyText, value)) Raise(nameof(HasEmptyText)); } }
    public bool HasEmptyText => !string.IsNullOrEmpty(_emptyText);
    /// <summary>展开时列表下方的一行：放学后 / 无课时提示下一次上课。</summary>
    public string? FooterText { get => _footerText; set { if (Set(ref _footerText, value)) Raise(nameof(HasFooterText)); } }
    public bool HasFooterText => !string.IsNullOrEmpty(_footerText);
    public bool Collapsed { get => _collapsed; set { if (Set(ref _collapsed, value)) Raise(nameof(Expanded)); } }
    public bool Expanded => !_collapsed;
    public bool Locked { get => _locked; set => Set(ref _locked, value); }
    public bool EditMode { get => _editMode; set => Set(ref _editMode, value); }
    public double Scale { get => _scale; set => Set(ref _scale, value); }

    public ObservableCollection<LessonRow> Rows { get; } = new();
    public ObservableCollection<LessonRow> CompactRows { get; } = new();

    public void Update(ScheduleData data, DateTime now)
    {
        var s = ScheduleEngine.GetStatus(data, now);
        var plan = s.Today;
        var time = TimeOnly.FromDateTime(now);

        ClassName = string.IsNullOrWhiteSpace(data.ClassName) ? "未设置班级" : data.ClassName;

        var date = now.ToString("M月d日 dddd", Zh);
        var extra = new List<string>();
        if (plan.WeekNumber is { } w) extra.Add($"第{w}周");
        if (UsesParity(data)) extra.Add(plan.IsOddWeek ? "单周" : "双周");
        if (plan.Override?.Kind == DayOverrideKind.FollowWeekday)
            extra.Add($"按{ScheduleEngine.WeekdayName(plan.Override.Weekday)}上课");
        DateText = extra.Count == 0 ? date : date + " · " + string.Join(" ", extra);

        // 展开时的整日列表
        var rows = new List<LessonRow>();
        foreach (var l in plan.Lessons)
        {
            RowState st;
            double progress = 0;
            if (s.Current == l) { st = RowState.Current; progress = s.CurrentProgress; }
            else if (l.End <= time) st = RowState.Past;
            else if (s.Next == l && s.NextIsSoon) st = RowState.Soon;
            else st = RowState.Future;
            rows.Add(MakeRow(l, st, progress, false));
        }
        Sync(Rows, rows);

        string? later = s.LaterFirst != null && s.LaterDate is { } ld
            ? $"{DayLabel(DateOnly.FromDateTime(now), ld)} {s.LaterFirst.Name} {s.LaterFirst.Start:HH\\:mm}"
            : null;

        if (data.IsEmpty)
        {
            EmptyText = "还没有课表\n长按这里，进入设置导入";
            FooterText = null;
        }
        else if (plan.IsHoliday)
        {
            EmptyText = "今日放假";
            FooterText = later;
        }
        else if (plan.Lessons.Count == 0)
        {
            EmptyText = "今日无课";
            FooterText = later;
        }
        else
        {
            EmptyText = null;
            FooterText = s.Phase == Phase.AfterSchool ? "今日课程结束 · " + later : null;
            if (s.Phase == Phase.AfterSchool && later == null) FooterText = "今日课程结束";
        }

        // 折叠时的两行
        var compact = new List<LessonRow>();
        if (data.IsEmpty)
        {
            compact.Add(Message("还没有课表"));
        }
        else
        {
            switch (s.Phase)
            {
                case Phase.InClass:
                    compact.Add(MakeRow(s.Current!, RowState.Current, s.CurrentProgress, true));
                    if (s.Next != null) compact.Add(MakeRow(s.Next, s.NextIsSoon ? RowState.Soon : RowState.Future, 0, true));
                    break;
                case Phase.BeforeClass:
                case Phase.Break:
                    compact.Add(MakeRow(s.Next!, s.NextIsSoon ? RowState.Soon : RowState.Future, 0, true));
                    break;
                case Phase.AfterSchool:
                case Phase.NoClassToday:
                    compact.Add(Message(s.Phase == Phase.AfterSchool ? "今日课程结束" : plan.IsHoliday ? "今日放假" : "今日无课"));
                    if (s.LaterFirst != null && s.LaterDate is { } d)
                    {
                        var r = MakeRow(s.LaterFirst, RowState.Future, 0, true);
                        r.TimeText = $"{DayLabel(DateOnly.FromDateTime(now), d)} {r.TimeText}";
                        compact.Add(r);
                    }
                    break;
            }
        }
        Sync(CompactRows, compact);
    }

    public static bool UsesParity(ScheduleData data) =>
        data.Timetable.Any(r => r.Any(c => CellParser.Parse(c).HasParity));

    private LessonRow MakeRow(Lesson l, RowState st, double progress, bool large) => new()
    {
        PeriodIndex = l.Index,
        Number = l.Number.ToString("00"),
        Name = l.Name,
        TimeText = $"{l.Start:HH\\:mm}–{l.End:HH\\:mm}",
        State = st,
        Progress = progress,
        Large = large,
        Editing = EditMode && !large,
    };

    private static LessonRow Message(string text) => new() { State = RowState.Message, Name = text, Large = true };

    private static string DayLabel(DateOnly today, DateOnly d)
    {
        int diff = d.DayNumber - today.DayNumber;
        if (diff == 1) return "明天";
        if (diff == 2) return "后天";
        var wd = ScheduleEngine.WeekdayName(ScheduleEngine.ToWeekday(d.DayOfWeek));
        return diff <= 6 ? wd : $"{d.Month}月{d.Day}日";
    }

    /// <summary>尽量原地更新，避免每次刷新都重建列表（拖动时会闪）。</summary>
    private static void Sync(ObservableCollection<LessonRow> target, List<LessonRow> source)
    {
        if (target.Count != source.Count || target.Zip(source).Any(p => p.First.PeriodIndex != p.Second.PeriodIndex || (p.First.State == RowState.Message) != (p.Second.State == RowState.Message)))
        {
            target.Clear();
            foreach (var r in source) target.Add(r);
            return;
        }
        for (int i = 0; i < source.Count; i++) target[i].CopyFrom(source[i]);
    }
}
