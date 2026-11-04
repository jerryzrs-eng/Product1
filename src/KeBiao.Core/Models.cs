using System.Text.Json.Serialization;

namespace KeBiao.Core;

/// <summary>一节课的上下课时间。</summary>
public sealed class Period
{
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }

    public Period() { }
    public Period(TimeOnly start, TimeOnly end) { Start = start; End = end; }

    public Period Clone() => new(Start, End);
}

/// <summary>一套作息时间（例如夏季、冬季）。</summary>
public sealed class BellSchedule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "默认作息";
    public List<Period> Periods { get; set; } = new();

    public BellSchedule Clone(string? newName = null) => new()
    {
        Id = newName == null ? Id : Guid.NewGuid().ToString("N"),
        Name = newName ?? Name,
        Periods = Periods.Select(p => p.Clone()).ToList(),
    };
}

public enum DayOverrideKind
{
    /// <summary>这一天按星期 X 的课表上课。</summary>
    FollowWeekday,
    /// <summary>这一天放假。</summary>
    Holiday,
}

/// <summary>某个日期的整体调整：按周 X 上课或放假。</summary>
public sealed class DayOverride
{
    public DateOnly Date { get; set; }
    public DayOverrideKind Kind { get; set; }
    /// <summary>1 = 周一 … 7 = 周日，仅 FollowWeekday 时有效。</summary>
    public int Weekday { get; set; } = 1;
}

/// <summary>某个日期的临时换课结果（按节次存放当天每节课的科目）。</summary>
public sealed class DaySubjectOverride
{
    public DateOnly Date { get; set; }
    public List<string> Subjects { get; set; } = new();
}

/// <summary>需要持久化、参与版本备份的全部课表数据。</summary>
public sealed class ScheduleData
{
    public int SchemaVersion { get; set; } = 1;
    public string ClassName { get; set; } = "";
    public DateOnly? TermStart { get; set; }

    /// <summary>本学期单双周整体对调。</summary>
    public bool ParityInverted { get; set; }

    /// <summary>只对某一周对调单双（存该周周一的日期）。</summary>
    public List<DateOnly> FlippedWeeks { get; set; } = new();

    public string ActiveBellId { get; set; } = "";
    public List<BellSchedule> Bells { get; set; } = new();

    /// <summary>行 = 节次，列 = 周一…周日（固定 7 列），格子里是原始文字。</summary>
    public List<List<string>> Timetable { get; set; } = new();

    public List<DayOverride> DayOverrides { get; set; } = new();
    public List<DaySubjectOverride> SubjectOverrides { get; set; } = new();

    [JsonIgnore]
    public BellSchedule ActiveBell
    {
        get
        {
            var bell = Bells.FirstOrDefault(b => b.Id == ActiveBellId) ?? Bells.FirstOrDefault();
            if (bell == null)
            {
                bell = new BellSchedule();
                Bells.Add(bell);
            }
            ActiveBellId = bell.Id;
            return bell;
        }
    }

    [JsonIgnore]
    public bool IsEmpty => Timetable.All(r => r.All(string.IsNullOrWhiteSpace)) || ActiveBell.Periods.Count == 0;

    public static ScheduleData CreateEmpty()
    {
        var bell = new BellSchedule();
        return new ScheduleData { Bells = { bell }, ActiveBellId = bell.Id };
    }

    /// <summary>保证每行恰好 7 列、行数不少于当前作息的节数。</summary>
    public void Normalize()
    {
        if (Bells.Count == 0) Bells.Add(new BellSchedule());
        _ = ActiveBell;
        foreach (var row in Timetable)
        {
            while (row.Count < 7) row.Add("");
            if (row.Count > 7) row.RemoveRange(7, row.Count - 7);
            for (int i = 0; i < 7; i++) row[i] ??= "";
        }
        int need = Bells.Max(b => b.Periods.Count);
        while (Timetable.Count < need) Timetable.Add(Enumerable.Repeat("", 7).ToList());
    }

    public ScheduleData DeepClone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, JsonOptions.Default);
        return System.Text.Json.JsonSerializer.Deserialize<ScheduleData>(json, JsonOptions.Default)!;
    }
}

public static class JsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };
}
