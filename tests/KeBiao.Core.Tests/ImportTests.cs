using System.Text;
using KeBiao.Core;

namespace KeBiao.Core.Tests;

public class ImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kebiao-tests-" + Guid.NewGuid().ToString("N"));
    public ImportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void TemplateRoundTrip()
    {
        var (template, guide) = TemplateWriter.WriteAll(_dir);
        Assert.Contains("课表转写说明", File.ReadAllText(guide));
        var r = TimetableImporter.FromFile(template);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal("26级7班", r.ClassName);
        Assert.Equal(new DateOnly(2026, 9, 1), r.TermStart);
        Assert.Equal(11, r.Periods.Count);
        Assert.Equal(new TimeOnly(10, 25), r.Periods[3].Start);
        Assert.Equal("单:音乐\n双:美术", r.Timetable[6][4]);
        Assert.Equal("", r.Timetable[0][5]);
    }

    [Fact]
    public void MarkdownFromAgent()
    {
        var md = """
            班级：26级7班
            开学日期：2026年9月1日

            ```
            | 节次 | 时间 | 周一 | 周二 | 周三 | 周四 | 周五 | 周六 | 周日 |
            |---|---|---|---|---|---|---|---|---|
            | 1 | 07:40-08:10 | 升旗 | 早读 | 早读 | 早读 | 早读 | | |
            | 2 | 8:20～9:00 | 语文 | 数学 | 英语 | 语文 | 单:音乐 双:美术 | | |
            ```
            """;
        var r = TimetableImporter.FromText(md);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal("26级7班", r.ClassName);
        Assert.Equal(new DateOnly(2026, 9, 1), r.TermStart);
        Assert.Equal(2, r.Periods.Count);
        Assert.Equal(new TimeOnly(8, 20), r.Periods[1].Start);
        Assert.Equal("单:音乐 双:美术", r.Timetable[1][4]);
    }

    [Fact]
    public void TabSeparatedFromExcelCopy()
    {
        var text = "节次\t开始\t结束\t星期一\t星期二\t星期三\t星期四\t星期五\n1\t7:40\t8:10\t语文\t数学\t英语\t物理\t化学\n2\t8:20\t9:00\t\"单:物理\n双:化学\"\t数学\t英语\t物理\t化学\n";
        var r = TimetableImporter.FromText(text);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal("单:物理\n双:化学", r.Timetable[1][0]);
        Assert.Equal("", r.Timetable[0][6]);
    }

    [Fact]
    public void GbkCsv()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(_dir, "a.csv");
        File.WriteAllText(path, "节次,时间,周一,周二,周三,周四,周五\r\n1,07:40-08:10,语文,数学,英语,物理,化学\r\n", Encoding.GetEncoding("GB18030"));
        var r = TimetableImporter.FromFile(path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal("语文", r.Timetable[0][0]);
    }

    [Fact]
    public void ReportsErrorsWithRowNumbers()
    {
        var text = "时间,周一,周二,周三,周四,周五\n08:00-08:40,语文,,,,\n08:30-09:10,数学,,,,\n9点,英语,,,,\n10:00-09:00,物理 双:化学,,,,\n";
        var r = TimetableImporter.FromText(text);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Row == 3 && e.Message.Contains("重叠"));
        Assert.Contains(r.Errors, e => e.Row == 4 && e.Message.Contains("看不懂"));
        Assert.Contains(r.Errors, e => e.Row == 5 && e.Message.Contains("不晚于"));
        Assert.Contains(r.Errors, e => e.Row == 5 && e.Message.Contains("缺少"));
    }

    [Fact]
    public void MissingHeader()
    {
        var r = TimetableImporter.FromText("语文 数学 英语");
        Assert.False(r.Ok);
        Assert.Contains("表头", r.Errors[0].Message);
    }

    [Fact]
    public void ApplyReplacesTimetableAndKeepsOtherBells()
    {
        var data = SampleData.Create();
        var winter = data.Bells[0].Clone("冬季作息");
        data.Bells.Add(winter);
        var r = TimetableImporter.FromText("时间,周一,周二,周三,周四,周五\n08:00-08:40,语文,,,,\n");
        TimetableImporter.Apply(data, r, winter.Id, null, true, true);
        Assert.Equal(winter.Id, data.ActiveBellId);
        Assert.Single(winter.Periods);
        Assert.Equal(11, data.Bells[0].Periods.Count);
        Assert.Equal("语文", ScheduleEngine.GetDayPlan(data, new DateOnly(2026, 11, 2)).Lessons.Single().Name);
    }
}

public class DataStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kebiao-store-" + Guid.NewGuid().ToString("N"));
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void SaveLoadAndBackups()
    {
        var store = new DataStore(_dir);
        Assert.True(store.Load().IsEmpty);
        var data = SampleData.Create();
        store.Save(data, "导入课表");
        Assert.Empty(store.ListBackups()); // 第一次保存之前没有旧版本
        data.ClassName = "改过的名字";
        store.Save(data, "修改班级");
        var backups = store.ListBackups();
        Assert.Single(backups);
        Assert.Equal("修改班级", backups[0].Reason);
        Assert.Equal("26级7班", store.ReadBackup(backups[0])!.ClassName);
        Assert.Equal("改过的名字", store.Load().ClassName);

        var restored = store.Restore(backups[0])!;
        Assert.Equal("26级7班", restored.ClassName);
        Assert.Equal("26级7班", store.Load().ClassName);
    }

    [Fact]
    public void KeepsAtMost20Backups()
    {
        var store = new DataStore(_dir);
        var data = SampleData.Create();
        for (int i = 0; i < 30; i++) { data.ClassName = "班" + i; store.Save(data, "第" + i); }
        Assert.Equal(DataStore.MaxBackups, store.ListBackups().Count);
        Assert.Equal("第29", store.ListBackups()[0].Reason);
    }

    [Fact]
    public void RecoversFromDamagedFile()
    {
        var store = new DataStore(_dir);
        var data = SampleData.Create();
        store.Save(data, "a");
        store.Save(data, "b");
        File.WriteAllText(store.DataPath, "{ broken");
        Assert.Equal("26级7班", store.Load().ClassName);
    }

    [Fact]
    public void RoundTripsAllFields()
    {
        var store = new DataStore(_dir);
        var data = SampleData.Create();
        ScheduleEngine.SetOverride(data, new DateOnly(2026, 11, 8), DayOverrideKind.FollowWeekday, 5);
        ScheduleEngine.FlipWeek(data, new DateOnly(2026, 11, 2));
        ScheduleEngine.SwapForDay(data, new DateOnly(2026, 11, 2), 1, 2);
        store.Save(data, "x");
        var back = store.Load();
        Assert.Equal(5, back.DayOverrides.Single().Weekday);
        Assert.Single(back.FlippedWeeks);
        Assert.Single(back.SubjectOverrides);
        Assert.Equal(new DateOnly(2026, 9, 1), back.TermStart);
    }
}
