using KeBiao.Core;

namespace KeBiao.Core.Tests;

public class EngineTests
{
    // 2026-11-02 是周一；开学 2026-09-01（周二）→ 那周为第 1 周
    private static readonly DateOnly Mon = new(2026, 11, 2);
    private static DateTime At(DateOnly d, int h, int m) => d.ToDateTime(new TimeOnly(h, m));

    [Fact]
    public void WeekNumberAndParityFromTermStart()
    {
        var data = SampleData.Create();
        Assert.Equal(10, ScheduleEngine.WeekNumber(data, Mon));
        Assert.False(ScheduleEngine.IsOddWeek(data, Mon));
        Assert.True(ScheduleEngine.IsOddWeek(data, Mon.AddDays(7)));
        Assert.Equal(1, ScheduleEngine.WeekNumber(data, new DateOnly(2026, 8, 31)));
        Assert.Null(ScheduleEngine.WeekNumber(data, new DateOnly(2026, 8, 30)));
    }

    [Fact]
    public void FlipWeekOnlyAffectsThatWeek()
    {
        var data = SampleData.Create();
        ScheduleEngine.FlipWeek(data, Mon.AddDays(3));
        Assert.True(ScheduleEngine.IsOddWeek(data, Mon));            // 第 10 周：双 → 单
        Assert.True(ScheduleEngine.IsOddWeek(data, Mon.AddDays(7)));  // 第 11 周不受影响，仍是单
        Assert.False(ScheduleEngine.IsOddWeek(data, Mon.AddDays(14)));
        ScheduleEngine.FlipWeek(data, Mon);
        Assert.False(ScheduleEngine.IsOddWeek(data, Mon));
    }

    [Fact]
    public void FlipTermAffectsAllWeeks()
    {
        var data = SampleData.Create();
        ScheduleEngine.FlipTerm(data);
        Assert.True(ScheduleEngine.IsOddWeek(data, Mon));
        Assert.False(ScheduleEngine.IsOddWeek(data, Mon.AddDays(7)));
    }

    [Fact]
    public void ParityCellResolves()
    {
        var data = SampleData.Create();
        var fri = Mon.AddDays(4); // 双周
        Assert.Contains(ScheduleEngine.GetDayPlan(data, fri).Lessons, l => l.Name == "美术");
        Assert.Contains(ScheduleEngine.GetDayPlan(data, fri.AddDays(7)).Lessons, l => l.Name == "音乐");
    }

    [Theory]
    [InlineData(10, 1, Phase.Break, true)]      // 默认：下课后下一节就标“即将开始”
    [InlineData(7, 0, Phase.BeforeClass, true)]
    [InlineData(10, 25, Phase.InClass, false)]
    [InlineData(10, 40, Phase.InClass, false)]  // 上课中不标下一节
    [InlineData(20, 0, Phase.AfterSchool, false)]
    public void DefaultSoonAfterPreviousEnds(int h, int m, Phase phase, bool soon)
    {
        var s = ScheduleEngine.GetStatus(SampleData.Create(), At(Mon, h, m));
        Assert.Equal(phase, s.Phase);
        Assert.Equal(soon, s.NextIsSoon);
    }

    [Theory]
    [InlineData(10, 1, Phase.Break, false)]   // 设成 10 分钟：离 10:25 还有 24 分钟，不提示
    [InlineData(10, 15, Phase.Break, true)]   // 刚好 10 分钟
    [InlineData(10, 14, Phase.Break, false)]
    [InlineData(10, 25, Phase.InClass, false)]
    [InlineData(7, 0, Phase.BeforeClass, false)]
    [InlineData(20, 0, Phase.AfterSchool, false)]
    [InlineData(18, 0, Phase.Break, false)]    // 18:40 还有晚自习
    public void StatusAtTimesWith10Minutes(int h, int m, Phase phase, bool soon)
    {
        var data = SampleData.Create();
        data.SoonMinutes = 10;
        var s = ScheduleEngine.GetStatus(data, At(Mon, h, m));
        Assert.Equal(phase, s.Phase);
        Assert.Equal(soon, s.NextIsSoon);
    }

    [Fact]
    public void InClassWithNextSoon()
    {
        var data = SampleData.Create();
        data.SoonMinutes = 10; // 课间只有 5 分钟，上一节还没下课就会提示
        var s = ScheduleEngine.GetStatus(data, At(Mon, 17, 7));
        Assert.Equal("语文", s.Current!.Name);
        Assert.Equal("生物/政治", s.Next!.Name);
        Assert.True(s.NextIsSoon);
        Assert.InRange(s.CurrentProgress, 0.9, 0.95);
    }

    [Fact]
    public void AfterSchoolPointsToNextDay()
    {
        var s = ScheduleEngine.GetStatus(SampleData.Create(), At(Mon.AddDays(4), 18, 0)); // 周五晚
        Assert.Equal(Phase.AfterSchool, s.Phase);
        Assert.Equal(Mon.AddDays(7), s.LaterDate); // 跳过周末
        Assert.Equal("升旗", s.LaterFirst!.Name);
    }

    [Fact]
    public void WeekendHasNoClass()
    {
        var s = ScheduleEngine.GetStatus(SampleData.Create(), At(Mon.AddDays(5), 9, 0));
        Assert.Equal(Phase.NoClassToday, s.Phase);
        Assert.Equal(Mon.AddDays(7), s.LaterDate);
    }

    [Fact]
    public void FollowWeekdayOverride()
    {
        var data = SampleData.Create();
        var sun = Mon.AddDays(6);
        ScheduleEngine.SetOverride(data, sun, DayOverrideKind.FollowWeekday, 5);
        var plan = ScheduleEngine.GetDayPlan(data, sun);
        Assert.Equal(5, plan.EffectiveWeekday);
        Assert.Equal("物理", plan.Lessons[1].Name);
        // 只影响当天
        Assert.Empty(ScheduleEngine.GetDayPlan(data, sun.AddDays(7)).Lessons);
    }

    [Fact]
    public void HolidayOverride()
    {
        var data = SampleData.Create();
        ScheduleEngine.SetOverride(data, Mon, DayOverrideKind.Holiday);
        Assert.Empty(ScheduleEngine.GetDayPlan(data, Mon).Lessons);
        Assert.NotEmpty(ScheduleEngine.GetDayPlan(data, Mon.AddDays(7)).Lessons);
        ScheduleEngine.ClearOverride(data, Mon);
        Assert.NotEmpty(ScheduleEngine.GetDayPlan(data, Mon).Lessons);
    }

    [Fact]
    public void SwapForDayOnlyToday()
    {
        var data = SampleData.Create();
        ScheduleEngine.SwapForDay(data, Mon, 1, 2);
        Assert.Equal("历史", ScheduleEngine.GetDayPlan(data, Mon).Lessons[1].Name);
        Assert.Equal("语文", ScheduleEngine.GetDayPlan(data, Mon.AddDays(7)).Lessons[1].Name);
        Assert.Equal("语文", data.Active.Grid[1][0]);
    }

    [Fact]
    public void SwapPermanentChangesTimetable()
    {
        var data = SampleData.Create();
        ScheduleEngine.SwapPermanent(data, Mon, 1, 2);
        Assert.Equal("历史", ScheduleEngine.GetDayPlan(data, Mon.AddDays(7)).Lessons[1].Name);
    }

    [Fact]
    public void SwapPermanentOnFollowedWeekdayChangesThatColumn()
    {
        var data = SampleData.Create();
        var sun = Mon.AddDays(6);
        ScheduleEngine.SetOverride(data, sun, DayOverrideKind.FollowWeekday, 2);
        ScheduleEngine.SwapPermanent(data, sun, 1, 2);
        Assert.Equal("语文", data.Active.Grid[1][1]);
        Assert.Equal("数学", data.Active.Grid[2][1]);
    }

    [Fact]
    public void SwapEmptyPeriodMovesLesson()
    {
        var data = SampleData.Create();
        var fri = Mon.AddDays(4);
        ScheduleEngine.SwapForDay(data, fri, 8, 9); // 班会 ↔ 空
        var names = ScheduleEngine.GetDayPlan(data, fri).Lessons.Select(l => (l.Index, l.Name)).ToList();
        Assert.Contains((9, "班会"), names);
        Assert.DoesNotContain(names, n => n.Index == 8);
    }

    [Fact]
    public void PruneRemovesOldOverrides()
    {
        var data = SampleData.Create();
        ScheduleEngine.SetOverride(data, Mon.AddDays(-30), DayOverrideKind.Holiday);
        ScheduleEngine.SetOverride(data, Mon.AddDays(3), DayOverrideKind.Holiday);
        ScheduleEngine.Prune(data, Mon);
        Assert.Single(data.DayOverrides);
    }
}

public class TimetableTests
{
    private static readonly DateOnly Mon = new(2026, 11, 2);

    [Fact]
    public void TagsNumberOnlyUnnamedPeriods()
    {
        var tags = SampleData.Create().Active.ActiveBell.Tags();
        Assert.Equal("早间", tags[0]);
        Assert.Equal("01", tags[1]);
        Assert.Equal("04", tags[4]);
        Assert.Equal("午托", tags[5]);
        Assert.Equal("05", tags[6]);
        Assert.Equal("晚自习一", tags[11]);
        var plan = ScheduleEngine.GetDayPlan(SampleData.Create(), Mon);
        Assert.Equal("01", plan.Lessons[1].Tag);
        Assert.Equal("第1节", ScheduleEngine.PeriodName(plan.Lessons[1].Tag));
        Assert.Equal("午托", ScheduleEngine.PeriodName(plan.Lessons[5].Tag));
    }

    [Fact]
    public void LabelsAreLimitedToFourChars()
    {
        Assert.Equal("晚自习一", Period.CleanLabel(" 晚自习一二 "));
        Assert.Null(Period.CleanLabel("  "));
    }

    [Fact]
    public void UseTimetableOverrideForDateRange()
    {
        var data = SampleData.Create();
        var exam = SampleData.ExamTimetable();
        data.Timetables.Add(exam);
        int n = ScheduleEngine.SetOverrideRange(data, Mon.AddDays(2), Mon, DayOverrideKind.UseTimetable, timetableId: exam.Id);
        Assert.Equal(3, n);
        var plan = ScheduleEngine.GetDayPlan(data, Mon.AddDays(1));
        Assert.Same(exam, plan.Timetable);
        Assert.Equal(new[] { "物理", "英语" }, plan.Lessons.Select(l => l.Name));
        Assert.Equal("上午", plan.Lessons[0].Tag);
        Assert.Same(data.Active, ScheduleEngine.GetDayPlan(data, Mon.AddDays(3)).Timetable);
    }

    [Fact]
    public void ManualSwitchChangesActiveTimetable()
    {
        var data = SampleData.Create();
        var exam = SampleData.ExamTimetable();
        data.Timetables.Add(exam);
        data.ActiveTimetableId = exam.Id;
        Assert.Equal("语文", ScheduleEngine.GetDayPlan(data, Mon).Lessons[0].Name);
    }

    [Fact]
    public void DeletingTimetableDropsItsOverrides()
    {
        var data = SampleData.Create();
        var exam = SampleData.ExamTimetable();
        data.Timetables.Add(exam);
        ScheduleEngine.SetOverride(data, Mon, DayOverrideKind.UseTimetable, timetableId: exam.Id);
        data.Timetables.Remove(exam);
        data.Normalize();
        Assert.Empty(data.DayOverrides);
    }

    [Fact]
    public void SwapPermanentEditsTheTimetableUsedThatDay()
    {
        var data = SampleData.Create();
        var exam = SampleData.ExamTimetable();
        data.Timetables.Add(exam);
        ScheduleEngine.SetOverride(data, Mon, DayOverrideKind.UseTimetable, timetableId: exam.Id);
        ScheduleEngine.SwapPermanent(data, Mon, 0, 1);
        Assert.Equal("数学", exam.Grid[0][0]);
        Assert.Equal("升旗", data.Active.Grid[0][0]);
    }

    [Fact]
    public void UsedSubjectsForQuickPick()
    {
        var subjects = SampleData.Create().Active.UsedSubjects();
        Assert.True(subjects.IndexOf("数学") < subjects.IndexOf("音乐")); // 常用的排前面
        Assert.Contains("音乐", subjects);
        Assert.Contains("美术", subjects);
        Assert.Contains("生物/政治", subjects);
        Assert.DoesNotContain(subjects, s => s.Contains(':'));
    }

    [Fact]
    public void MigratesVersion1Data()
    {
        var v1 = """
            {
              "SchemaVersion": 1,
              "ClassName": "26级7班",
              "ActiveBellId": "b1",
              "Bells": [ { "Id": "b1", "Name": "夏季作息", "Periods": [ { "Start": "08:00:00", "End": "08:40:00" } ] } ],
              "Timetable": [ [ "语文", "数学", "", "", "", "", "" ] ],
              "DayOverrides": [ { "Date": "2026-11-08", "Kind": "FollowWeekday", "Weekday": 5 } ]
            }
            """;
        var data = System.Text.Json.JsonSerializer.Deserialize<ScheduleData>(v1, JsonOptions.Default)!;
        data.Normalize();
        Assert.Single(data.Timetables);
        Assert.Equal("夏季作息", data.Active.ActiveBell.Name);
        Assert.Equal("语文", data.Active.Grid[0][0]);
        Assert.Equal(2, data.SchemaVersion);
        Assert.Single(data.DayOverrides);
        var json = System.Text.Json.JsonSerializer.Serialize(data, JsonOptions.Default);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("Bells", out _));      // 旧字段不再写出
        Assert.False(doc.RootElement.TryGetProperty("Timetable", out _));
        Assert.False(doc.RootElement.TryGetProperty("ActiveBellId", out _));
    }
}

public class CellParserTests
{
    [Theory]
    [InlineData("音乐", "美术", "单:音乐\n双:美术")]
    [InlineData("音乐", "", "单:音乐")]
    [InlineData("", "美术", "双:美术")]
    [InlineData("语文", "语文", "语文")]
    [InlineData("", "", "")]
    [InlineData(" 单:物理 ", " ", "单:单物理")] // 手输的“单:”不会被当成单双周写法
    public void Compose(string odd, string even, string expected) =>
        Assert.Equal(expected, CellParser.Compose(odd, even));

    [Theory]
    [InlineData("语文", "语文", "语文")]
    [InlineData("生物/政治", "生物/政治", "生物/政治")]
    [InlineData("单:物理 双:化学", "物理", "化学")]
    [InlineData("单：物理\n双：化学", "物理", "化学")]
    [InlineData("单周:物理<br>双周:化学", "物理", "化学")]
    [InlineData("双:化学", "", "化学")]
    [InlineData("", "", "")]
    public void Resolves(string raw, string odd, string even)
    {
        Assert.Equal(odd, CellParser.Resolve(raw, true));
        Assert.Equal(even, CellParser.Resolve(raw, false));
    }

    [Fact]
    public void ReportsTextBeforeMarker()
    {
        Assert.NotNull(CellParser.Parse("物理 双:化学").Error);
        Assert.NotNull(CellParser.Parse("单:物理 单:化学").Error);
        Assert.Null(CellParser.Parse("单:物理 双:化学").Error);
    }
}

public class TimeInputTests
{
    [Theory]
    [InlineData("1505", 15, 5)]
    [InlineData("740", 7, 40)]
    [InlineData("0740", 7, 40)]
    [InlineData("8", 8, 0)]
    [InlineData("18", 18, 0)]
    [InlineData("7:40", 7, 40)]
    [InlineData("7：40", 7, 40)]
    [InlineData("0000", 0, 0)]
    public void Parses(string text, int h, int m)
    {
        Assert.True(TimeInput.TryParse(text, out var t));
        Assert.Equal(new TimeOnly(h, m), t);
    }

    [Theory]
    [InlineData("2575")]
    [InlineData("760")]
    [InlineData("24")]
    [InlineData("12345")]
    [InlineData("")]
    [InlineData("abc")]
    public void Rejects(string text) => Assert.False(TimeInput.TryParse(text, out _));
}
