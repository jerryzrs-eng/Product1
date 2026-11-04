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
    [InlineData(10, 1, Phase.Break, false)]   // 截图时刻：离 10:25 还有 24 分钟，不应提示
    [InlineData(10, 15, Phase.Break, true)]   // 刚好 10 分钟
    [InlineData(10, 14, Phase.Break, false)]
    [InlineData(10, 25, Phase.InClass, false)]
    [InlineData(7, 0, Phase.BeforeClass, false)]
    [InlineData(18, 0, Phase.AfterSchool, false)]
    public void StatusAtTimes(int h, int m, Phase phase, bool soon)
    {
        var s = ScheduleEngine.GetStatus(SampleData.Create(), At(Mon, h, m));
        Assert.Equal(phase, s.Phase);
        Assert.Equal(soon, s.NextIsSoon);
    }

    [Fact]
    public void InClassWithNextSoon()
    {
        var s = ScheduleEngine.GetStatus(SampleData.Create(), At(Mon, 17, 7));
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
        Assert.Equal("语文", data.Timetable[1][0]);
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
        Assert.Equal("语文", data.Timetable[1][1]);
        Assert.Equal("数学", data.Timetable[2][1]);
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

public class CellParserTests
{
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
