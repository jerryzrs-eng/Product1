namespace KeBiao.Core;

/// <summary>示例课表：用于模板和界面截图，不会自动写入老师的数据。</summary>
public static class SampleData
{
    public static readonly (string Time, string[] Days)[] Rows =
    {
        ("07:40-08:10", new[] { "升旗", "早读", "早读", "早读", "早读", "", "" }),
        ("08:20-09:00", new[] { "语文", "数学", "英语", "语文", "物理", "", "" }),
        ("09:10-09:50", new[] { "历史", "语文", "数学", "英语", "数学", "", "" }),
        ("10:25-11:05", new[] { "数学", "英语", "物理", "化学", "语文", "", "" }),
        ("11:20-12:00", new[] { "数学", "物理", "语文", "数学", "英语", "", "" }),
        ("12:30-13:30", new[] { "午托(英)", "午托(数)", "午托(语)", "午托(物)", "午托(化)", "", "" }),
        ("13:45-14:25", new[] { "体育", "化学", "生物", "历史", "单:音乐\n双:美术", "", "" }),
        ("14:40-15:20", new[] { "生物", "地理", "体育", "生物", "化学", "", "" }),
        ("15:35-16:15", new[] { "地理", "历史", "化学", "体育", "班会", "", "" }),
        ("16:30-17:10", new[] { "语文", "生物/政治", "英语", "物理", "", "", "" }),
        ("17:15-17:55", new[] { "生物/政治", "自习", "生物/政治", "自习", "", "", "" }),
    };

    public static ScheduleData Create()
    {
        var data = ScheduleData.CreateEmpty();
        data.ClassName = "26级7班";
        data.TermStart = new DateOnly(2026, 9, 1);
        data.Bells[0].Name = "夏季作息";
        foreach (var (time, days) in Rows)
        {
            TimetableImporter.TryParseRange(time, out var p);
            data.Bells[0].Periods.Add(p);
            data.Timetable.Add(days.ToList());
        }
        data.Normalize();
        return data;
    }
}
