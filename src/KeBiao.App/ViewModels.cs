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
    private string _tag = "";
    private string _name = "";
    private string _timeText = "";
    private RowState _state;
    private double _progress;
    private bool _large;
    private bool _editing;
    private bool _isDragging;
    private bool _isDropTarget;

    public int PeriodIndex { get => _periodIndex; set => Set(ref _periodIndex, value); }
    /// <summary>节次：“01”或自定义名称（3~4 个字时分两行显示）。</summary>
    public string Tag { get => _tag; set { if (Set(ref _tag, value)) { Raise(nameof(TagText)); Raise(nameof(TagTwoLines)); } } }
    public string TagText => _tag.Length > 2 ? _tag[..2] + "\n" + _tag[2..] : _tag;
    public bool TagTwoLines => _tag.Length > 2;
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
        PeriodIndex = o.PeriodIndex; Tag = o.Tag; Name = o.Name; TimeText = o.TimeText;
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
    private (int? Current, int? Next) _visiblePeriods;
    private DateOnly? _previewDate;
    private double _previewRemaining;

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

    public (int? Current, int? Next) VisiblePeriods
    {
        get => _visiblePeriods;
        private set => Set(ref _visiblePeriods, value);
    }

    /// <summary>正在临时查看的那一天（下一个有课的日子）；null 表示显示今天。</summary>
    public DateOnly? PreviewDate
    {
        get => _previewDate;
        set { if (Set(ref _previewDate, value)) Raise(nameof(IsPreview)); }
    }
    public bool IsPreview => _previewDate != null;
    /// <summary>距离自动回到今天还剩的比例（1 → 0），用一条渐短的线表示。</summary>
    public double PreviewRemaining { get => _previewRemaining; set => Set(ref _previewRemaining, value); }

    public ObservableCollection<LessonRow> Rows { get; } = new();
    public ObservableCollection<LessonRow> CompactRows { get; } = new();

    private static string DateLine(ScheduleData data, DayPlan plan, string prefix)
    {
        var date = (prefix.Length > 0 ? prefix + " " : "") + plan.Date.ToDateTime(TimeOnly.MinValue).ToString("M月d日 dddd", Zh);
        var extra = new List<string>();
        if (plan.WeekNumber is { } w) extra.Add($"第{w}周");
        if (data.UsesParity) extra.Add(plan.IsOddWeek ? "单周" : "双周");
        if (plan.Override?.Kind == DayOverrideKind.FollowWeekday)
            extra.Add($"按{ScheduleEngine.WeekdayName(plan.Override.Weekday)}上课");
        if (!plan.IsExpired && plan.Override?.Kind == DayOverrideKind.UseTimetable)
            extra.Add($"用“{plan.Timetable.Name}”");
        return extra.Count == 0 ? date : date + " · " + string.Join(" ", extra);
    }

    /// <summary>从明天起找下一个有课的日子（最多往后看 14 天）；都没课就返回明天。</summary>
    public static DateOnly NextSchoolDay(ScheduleData data, DateOnly today)
    {
        for (int i = 1; i <= 14; i++)
        {
            var d = today.AddDays(i);
            if (ScheduleEngine.GetDayPlan(data, d).Lessons.Count > 0) return d;
        }
        return today.AddDays(1);
    }

    /// <summary>临时查看某一天：整天的课都按“未开始”显示。</summary>
    private void UpdatePreview(ScheduleData data, DateTime now, DateOnly day)
    {
        var plan = ScheduleEngine.GetDayPlan(data, day);
        ClassName = string.IsNullOrWhiteSpace(data.ClassName) ? "未设置班级" : data.ClassName;
        int diff = day.DayNumber - DateOnly.FromDateTime(now).DayNumber;
        // “明天”“后天”写在日期前；再往后日期本身已带星期几，不重复
        DateText = DateLine(data, plan, diff is 1 or 2 ? DayLabel(DateOnly.FromDateTime(now), day) : "");
        Sync(Rows, plan.Lessons.Select(l => MakeRow(l, RowState.Future, 0, false)).ToList());
        VisiblePeriods = (null, null);
        EmptyText = plan.IsExpired ? "课表已作废" : plan.Lessons.Count > 0 ? null : plan.IsHoliday ? "放假" : "无课";
        FooterText = null;
    }

    public void Update(ScheduleData data, DateTime now)
    {
        if (PreviewDate is { } preview)
        {
            UpdatePreview(data, now, preview);
            return;
        }
        var s = ScheduleEngine.GetStatus(data, now);
        var plan = s.Today;
        var time = TimeOnly.FromDateTime(now);

        ClassName = string.IsNullOrWhiteSpace(data.ClassName) ? "未设置班级" : data.ClassName;

        DateText = DateLine(data, plan, "");

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
        VisiblePeriods = (s.Current?.Index, s.Next?.Index);

        string? later = s.LaterFirst != null && s.LaterDate is { } ld
            ? $"{DayLabel(DateOnly.FromDateTime(now), ld)} {s.LaterFirst.Name} {s.LaterFirst.Start:HH\\:mm}"
            : null;

        if (plan.IsExpired)
        {
            EmptyText = "课表已作废";
            FooterText = null;
        }
        else if (data.IsEmpty)
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

        // 折叠时先显示当前状态，再显示当天的下一节课。
        var compact = new List<LessonRow>();
        if (plan.IsExpired)
        {
            compact.Add(Message("课表已作废"));
        }
        else if (data.IsEmpty && plan.Lessons.Count == 0)
        {
            compact.Add(Message("还没有课表"));
        }
        else
        {
            compact.Add(s.Phase switch
            {
                Phase.InClass => MakeRow(s.Current!, RowState.Current, s.CurrentProgress, true),
                Phase.BeforeClass => Message("尚未上课"),
                Phase.Break => Message("课间"),
                Phase.AfterSchool => Message("今日课程结束"),
                _ => Message(plan.IsHoliday ? "今日放假" : "今日无课"),
            });
            if (s.Next is { } next)
                compact.Add(MakeRow(next, s.NextIsSoon ? RowState.Soon : RowState.Future, 0, true));
        }
        Sync(CompactRows, compact);
    }

    private LessonRow MakeRow(Lesson l, RowState st, double progress, bool large) => new()
    {
        PeriodIndex = l.Index,
        Tag = l.Tag,
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
