namespace KeBiao.Core;

public sealed record Lesson(int Index, string Name, TimeOnly Start, TimeOnly End)
{
    /// <summary>显示用的节次号（从 1 开始）。</summary>
    public int Number => Index + 1;
}

public sealed record DayPlan(
    DateOnly Date,
    bool IsHoliday,
    int EffectiveWeekday,
    int? WeekNumber,
    bool IsOddWeek,
    DayOverride? Override,
    bool HasSubjectOverride,
    IReadOnlyList<Lesson> Lessons);

public enum Phase
{
    /// <summary>今天没有课（放假、周末或课表为空）。</summary>
    NoClassToday,
    /// <summary>今天第一节课还没开始。</summary>
    BeforeClass,
    InClass,
    /// <summary>两节课之间。</summary>
    Break,
    /// <summary>今天的课都结束了。</summary>
    AfterSchool,
}

public sealed record Status(
    DateTime Now,
    DayPlan Today,
    Phase Phase,
    Lesson? Current,
    double CurrentProgress,
    Lesson? Next,
    bool NextIsSoon,
    DateOnly? LaterDate,
    Lesson? LaterFirst);

public static class ScheduleEngine
{
    public static readonly TimeSpan SoonWindow = TimeSpan.FromMinutes(10);

    /// <summary>没有填开学日期时，用一个固定的周一作为单双周的基准。</summary>
    private static readonly DateOnly FallbackAnchor = new(2000, 1, 3);

    public static int ToWeekday(DayOfWeek d) => d == DayOfWeek.Sunday ? 7 : (int)d;

    public static DateOnly MondayOf(DateOnly d) => d.AddDays(-(ToWeekday(d.DayOfWeek) - 1));

    /// <summary>第几周（开学那周为第 1 周）。没填开学日期或还没开学时返回 null。</summary>
    public static int? WeekNumber(ScheduleData data, DateOnly date)
    {
        if (data.TermStart is not { } start) return null;
        int days = MondayOf(date).DayNumber - MondayOf(start).DayNumber;
        if (days < 0) return null;
        return days / 7 + 1;
    }

    /// <summary>未经任何手动对调时，这一周是不是单周。</summary>
    public static bool BaseIsOdd(ScheduleData data, DateOnly date)
    {
        var anchor = MondayOf(data.TermStart ?? FallbackAnchor);
        int weeks = (int)Math.Floor((MondayOf(date).DayNumber - anchor.DayNumber) / 7.0);
        return ((weeks % 2) + 2) % 2 == 0; // 基准周（第 1 周）为单周
    }

    public static bool IsOddWeek(ScheduleData data, DateOnly date)
    {
        bool odd = BaseIsOdd(data, date);
        if (data.ParityInverted) odd = !odd;
        if (data.FlippedWeeks.Contains(MondayOf(date))) odd = !odd;
        return odd;
    }

    /// <summary>只把这一周的单双对调。</summary>
    public static void FlipWeek(ScheduleData data, DateOnly date)
    {
        var monday = MondayOf(date);
        if (!data.FlippedWeeks.Remove(monday)) data.FlippedWeeks.Add(monday);
    }

    /// <summary>把整个学期的单双对调（本周也随之改变）。</summary>
    public static void FlipTerm(ScheduleData data) => data.ParityInverted = !data.ParityInverted;

    public static DayOverride? GetOverride(ScheduleData data, DateOnly date) =>
        data.DayOverrides.LastOrDefault(o => o.Date == date);

    public static void SetOverride(ScheduleData data, DateOnly date, DayOverrideKind kind, int weekday = 1)
    {
        data.DayOverrides.RemoveAll(o => o.Date == date);
        data.DayOverrides.Add(new DayOverride { Date = date, Kind = kind, Weekday = weekday });
        data.DayOverrides.Sort((a, b) => a.Date.CompareTo(b.Date));
    }

    public static void ClearOverride(ScheduleData data, DateOnly date)
    {
        data.DayOverrides.RemoveAll(o => o.Date == date);
        data.SubjectOverrides.RemoveAll(o => o.Date == date);
    }

    /// <summary>这一天按课表本来应该上的科目（按节次，含空节），不含临时换课。</summary>
    public static List<string> BaseSubjects(ScheduleData data, DateOnly date, out int weekday, out bool odd, out bool holiday)
    {
        var ov = GetOverride(data, date);
        holiday = ov?.Kind == DayOverrideKind.Holiday;
        weekday = ov?.Kind == DayOverrideKind.FollowWeekday ? Math.Clamp(ov.Weekday, 1, 7) : ToWeekday(date.DayOfWeek);
        odd = IsOddWeek(data, date);
        var bell = data.ActiveBell;
        var list = new List<string>(bell.Periods.Count);
        for (int i = 0; i < bell.Periods.Count; i++)
        {
            string raw = i < data.Timetable.Count && data.Timetable[i].Count >= weekday ? data.Timetable[i][weekday - 1] : "";
            list.Add(holiday ? "" : CellParser.Resolve(raw, odd));
        }
        return list;
    }

    public static DayPlan GetDayPlan(ScheduleData data, DateOnly date)
    {
        var subjects = BaseSubjects(data, date, out int weekday, out bool odd, out bool holiday);
        var subOv = holiday ? null : data.SubjectOverrides.LastOrDefault(o => o.Date == date);
        if (subOv != null)
        {
            for (int i = 0; i < subjects.Count && i < subOv.Subjects.Count; i++)
                subjects[i] = subOv.Subjects[i] ?? "";
        }

        var bell = data.ActiveBell;
        var lessons = new List<Lesson>();
        for (int i = 0; i < bell.Periods.Count; i++)
        {
            var name = subjects[i].Trim();
            if (name.Length == 0) continue;
            lessons.Add(new Lesson(i, name, bell.Periods[i].Start, bell.Periods[i].End));
        }
        lessons.Sort((a, b) => a.Start.CompareTo(b.Start));

        return new DayPlan(date, holiday, weekday, WeekNumber(data, date), odd,
            GetOverride(data, date), subOv != null, lessons);
    }

    public static Status GetStatus(ScheduleData data, DateTime now)
    {
        var date = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        var plan = GetDayPlan(data, date);

        Lesson? current = plan.Lessons.LastOrDefault(l => l.Start <= time && time < l.End);
        Lesson? next = plan.Lessons.FirstOrDefault(l => l.Start > time);
        bool soon = next != null && (next.Start.ToTimeSpan() - time.ToTimeSpan()) <= SoonWindow;
        double progress = 0;
        if (current != null)
        {
            double total = (current.End.ToTimeSpan() - current.Start.ToTimeSpan()).TotalSeconds;
            progress = total <= 0 ? 0 : Math.Clamp((time.ToTimeSpan() - current.Start.ToTimeSpan()).TotalSeconds / total, 0, 1);
        }

        Phase phase;
        if (plan.Lessons.Count == 0) phase = Phase.NoClassToday;
        else if (current != null) phase = Phase.InClass;
        else if (next == null) phase = Phase.AfterSchool;
        else if (time < plan.Lessons[0].Start) phase = Phase.BeforeClass;
        else phase = Phase.Break;

        DateOnly? laterDate = null;
        Lesson? laterFirst = null;
        if (phase is Phase.NoClassToday or Phase.AfterSchool)
        {
            for (int i = 1; i <= 14; i++)
            {
                var d = date.AddDays(i);
                var p = GetDayPlan(data, d);
                if (p.Lessons.Count > 0)
                {
                    laterDate = d;
                    laterFirst = p.Lessons[0];
                    break;
                }
            }
        }

        return new Status(now, plan, phase, current, progress, next, soon, laterDate, laterFirst);
    }

    /// <summary>仅今天：互换两节课（按节次）。</summary>
    public static void SwapForDay(ScheduleData data, DateOnly date, int a, int b)
    {
        var subjects = BaseSubjects(data, date, out _, out _, out _);
        var existing = data.SubjectOverrides.LastOrDefault(o => o.Date == date);
        if (existing != null)
            for (int i = 0; i < subjects.Count && i < existing.Subjects.Count; i++) subjects[i] = existing.Subjects[i];
        if (a < 0 || b < 0 || a >= subjects.Count || b >= subjects.Count || a == b) return;
        (subjects[a], subjects[b]) = (subjects[b], subjects[a]);
        data.SubjectOverrides.RemoveAll(o => o.Date == date);
        data.SubjectOverrides.Add(new DaySubjectOverride { Date = date, Subjects = subjects });
    }

    /// <summary>以后每周：直接互换周课表里对应星期的两个格子（含单双周写法）。</summary>
    public static void SwapPermanent(ScheduleData data, DateOnly date, int a, int b)
    {
        if (a == b) return;
        data.Normalize();
        var plan = GetDayPlan(data, date);
        int col = plan.EffectiveWeekday - 1;
        if (a >= data.Timetable.Count || b >= data.Timetable.Count) return;
        (data.Timetable[a][col], data.Timetable[b][col]) = (data.Timetable[b][col], data.Timetable[a][col]);
        var existing = data.SubjectOverrides.LastOrDefault(o => o.Date == date);
        if (existing != null && a < existing.Subjects.Count && b < existing.Subjects.Count)
            (existing.Subjects[a], existing.Subjects[b]) = (existing.Subjects[b], existing.Subjects[a]);
    }

    /// <summary>清理已经过去的日期调整，避免数据越积越多。</summary>
    public static void Prune(ScheduleData data, DateOnly today)
    {
        var cutoff = today.AddDays(-7);
        data.DayOverrides.RemoveAll(o => o.Date < cutoff);
        data.SubjectOverrides.RemoveAll(o => o.Date < cutoff);
        data.FlippedWeeks.RemoveAll(w => w < MondayOf(today).AddDays(-28));
    }

    public static string WeekdayName(int weekday) => weekday switch
    {
        1 => "周一", 2 => "周二", 3 => "周三", 4 => "周四", 5 => "周五", 6 => "周六", 7 => "周日", _ => "",
    };
}
