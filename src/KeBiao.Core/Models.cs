using System.Text.Json.Serialization;

namespace KeBiao.Core;

/// <summary>一节课的上下课时间和（可选的）节次名称。</summary>
public sealed class Period
{
    public const int MaxLabelLength = 4;

    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }

    /// <summary>自定义节次名，如“早读”“午托”“晚自习一”，最多 4 个字；空表示按顺序编号。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Label { get; set; }

    public Period() { }
    public Period(TimeOnly start, TimeOnly end, string? label = null) { Start = start; End = end; Label = CleanLabel(label); }

    public Period Clone() => new(Start, End, Label);

    public static string? CleanLabel(string? label)
    {
        var s = (label ?? "").Trim();
        if (s.Length == 0) return null;
        return s.Length > MaxLabelLength ? s[..MaxLabelLength] : s;
    }
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

    /// <summary>每一节在界面上显示的名称：有自定义名用自定义名，否则在没起名的节次里按顺序编号 01、02…</summary>
    public List<string> Tags()
    {
        var tags = new List<string>(Periods.Count);
        int n = 0;
        foreach (var p in Periods)
            tags.Add(string.IsNullOrWhiteSpace(p.Label) ? (++n).ToString("00") : p.Label!);
        return tags;
    }
}

/// <summary>一张课表：科目表 + 它自己的若干套作息。</summary>
public sealed class Timetable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "课表";
    public string ActiveBellId { get; set; } = "";
    public List<BellSchedule> Bells { get; set; } = new();

    /// <summary>行 = 节次，列 = 周一…周日（固定 7 列），格子里是原始文字（单双周写成“单:x 双:y”）。</summary>
    public List<List<string>> Grid { get; set; } = new();

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
    public bool IsEmpty => Grid.All(r => r.All(string.IsNullOrWhiteSpace)) || ActiveBell.Periods.Count == 0;

    public void Normalize()
    {
        if (Bells.Count == 0) Bells.Add(new BellSchedule());
        _ = ActiveBell;
        foreach (var b in Bells)
            foreach (var p in b.Periods) p.Label = Period.CleanLabel(p.Label);
        foreach (var row in Grid)
        {
            while (row.Count < 7) row.Add("");
            if (row.Count > 7) row.RemoveRange(7, row.Count - 7);
            for (int i = 0; i < 7; i++) row[i] ??= "";
        }
        int need = Bells.Max(b => b.Periods.Count);
        while (Grid.Count < need) Grid.Add(Enumerable.Repeat("", 7).ToList());
    }

    public Timetable Clone(string newName)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, JsonOptions.Default);
        var copy = System.Text.Json.JsonSerializer.Deserialize<Timetable>(json, JsonOptions.Default)!;
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = newName;
        return copy;
    }

    /// <summary>这张课表里用过的科目，按出现次数排序（编辑格子时做快捷按钮）。</summary>
    public List<string> UsedSubjects()
    {
        var counts = new Dictionary<string, int>();
        foreach (var row in Grid)
            foreach (var cell in row)
            {
                var p = CellParser.Parse(cell);
                foreach (var s in p.HasParity ? new[] { p.Odd, p.Even } : new[] { p.Odd })
                    if (!string.IsNullOrWhiteSpace(s)) counts[s!] = counts.GetValueOrDefault(s!) + 1;
            }
        return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).ToList();
    }
}

public enum DayOverrideKind
{
    /// <summary>这一天按星期 X 的课表上课。</summary>
    FollowWeekday,
    /// <summary>这一天放假。</summary>
    Holiday,
    /// <summary>这一天使用另一张课表（考试周等）。</summary>
    UseTimetable,
}

/// <summary>某个日期的整体调整。</summary>
public sealed class DayOverride
{
    public DateOnly Date { get; set; }
    public DayOverrideKind Kind { get; set; }
    /// <summary>1 = 周一 … 7 = 周日，仅 FollowWeekday 时有效。</summary>
    public int Weekday { get; set; } = 1;
    /// <summary>仅 UseTimetable 时有效。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TimetableId { get; set; }
}

/// <summary>某个日期的临时换课结果（按节次存放当天每节课的科目）。</summary>
public sealed class DaySubjectOverride
{
    public DateOnly Date { get; set; }
    public List<string> Subjects { get; set; } = new();
}

/// <summary>需要持久化、参与版本备份的全部数据。</summary>
public sealed class ScheduleData
{
    public const int CurrentSchema = 2;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public string ClassName { get; set; } = "";
    public DateOnly? TermStart { get; set; }

    /// <summary>本学期单双周整体对调。</summary>
    public bool ParityInverted { get; set; }

    /// <summary>只对某一周对调单双（存该周周一的日期）。</summary>
    public List<DateOnly> FlippedWeeks { get; set; } = new();

    /// <summary>
    /// 下一节什么时候标“即将开始”：0 = 上一节下课后（课间、早上第一节前）就标；N = 开课前 N 分钟内才标。
    /// </summary>
    public int SoonMinutes { get; set; }

    public string ActiveTimetableId { get; set; } = "";
    public List<Timetable> Timetables { get; set; } = new();

    public List<DayOverride> DayOverrides { get; set; } = new();
    public List<DaySubjectOverride> SubjectOverrides { get; set; } = new();

    // ---- 第 1 版的数据（只有一张课表），读取时自动迁移 ----
    [JsonPropertyName("Bells"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BellSchedule>? LegacyBells { get; set; }
    [JsonPropertyName("ActiveBellId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyActiveBellId { get; set; }
    [JsonPropertyName("Timetable"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<List<string>>? LegacyGrid { get; set; }

    /// <summary>当前使用的课表（手动选择的那张）。</summary>
    [JsonIgnore]
    public Timetable Active
    {
        get
        {
            var t = Timetables.FirstOrDefault(x => x.Id == ActiveTimetableId) ?? Timetables.FirstOrDefault();
            if (t == null)
            {
                t = new Timetable { Name = "默认课表" };
                Timetables.Add(t);
            }
            ActiveTimetableId = t.Id;
            return t;
        }
    }

    public Timetable? FindTimetable(string? id) => id == null ? null : Timetables.FirstOrDefault(t => t.Id == id);

    [JsonIgnore]
    public bool IsEmpty => Active.IsEmpty;

    public static ScheduleData CreateEmpty()
    {
        var d = new ScheduleData();
        d.Normalize();
        return d;
    }

    public void Normalize()
    {
        if (LegacyGrid != null || LegacyBells != null)
        {
            var t = new Timetable
            {
                Name = "默认课表",
                Bells = LegacyBells ?? new List<BellSchedule>(),
                ActiveBellId = LegacyActiveBellId ?? "",
                Grid = LegacyGrid ?? new List<List<string>>(),
            };
            Timetables.Insert(0, t);
            ActiveTimetableId = t.Id;
            LegacyBells = null;
            LegacyActiveBellId = null;
            LegacyGrid = null;
        }
        SchemaVersion = CurrentSchema;
        _ = Active;
        foreach (var t in Timetables) t.Normalize();
        DayOverrides.RemoveAll(o => o.Kind == DayOverrideKind.UseTimetable && FindTimetable(o.TimetableId) == null);
    }

    /// <summary>课表里有没有任何一格设置了单双周。</summary>
    [JsonIgnore]
    public bool UsesParity => Timetables.Any(t => t.Grid.Any(r => r.Any(c => CellParser.Parse(c).HasParity)));

    public ScheduleData DeepClone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, JsonOptions.Default);
        var d = System.Text.Json.JsonSerializer.Deserialize<ScheduleData>(json, JsonOptions.Default)!;
        d.Normalize();
        return d;
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
