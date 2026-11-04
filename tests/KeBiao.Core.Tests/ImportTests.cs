using System.Text;
using ClosedXML.Excel;
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
        Assert.Equal(12, r.Periods.Count);
        Assert.Equal("早间", r.Periods[0].Label);
        Assert.Null(r.Periods[1].Label);
        Assert.Equal("晚自习一", r.Periods[11].Label);
        Assert.Equal(new TimeOnly(10, 25), r.Periods[3].Start);
        Assert.Equal("单:音乐\n双:美术", r.Timetable[6][4]);
        Assert.Equal("", r.Timetable[0][5]);
    }

    [Fact]
    public void WorkbookReadsAllClassesAndKeepsErrorsWithTheirSheet()
    {
        var path = Path.Combine(_dir, "年级课表.xlsx");
        using (var workbook = new XLWorkbook())
        {
            workbook.Worksheets.Add("填写规则").Cell("A1").Value = "说明";
            for (int i = 1; i <= 6; i++)
            {
                var sheet = workbook.Worksheets.Add($"高二{i}班");
                sheet.Cell("A1").Value = "时间";
                sheet.Cell("B1").Value = "周一";
                sheet.Cell("C1").Value = "周二";
                sheet.Cell("D1").Value = "周三";
                sheet.Cell("A2").Value = i == 3 ? "待定" : "08:00-08:40";
                sheet.Cell("B2").Value = $"课程{i}";
            }
            workbook.SaveAs(path);
        }

        var result = TimetableImporter.FromFile(path, out var sheets);

        Assert.Equal(Enumerable.Range(1, 6).Select(i => $"高二{i}班"), sheets.Select(s => s.Name));
        Assert.Same(sheets[0].Result, result);
        Assert.Equal("课程6", sheets[5].Result.Timetable[0][0]);
        Assert.All(sheets.Where(s => s.Name != "高二3班"), s => Assert.True(s.Result.Ok));
        Assert.Equal(2, Assert.Single(sheets[2].Result.Errors).Row);
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
    public void ApplyIntoExistingTimetable()
    {
        var data = SampleData.Create();
        var winter = data.Active.Bells[0].Clone("冬季作息");
        data.Active.Bells.Add(winter);
        data.Active.ActiveBellId = winter.Id;
        var r = TimetableImporter.FromText("节次,时间,周一,周二,周三,周四,周五\n早读,07:30-08:00,语文,,,,\n第一节,08:00-08:40,数学,,,,\n");
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        TimetableImporter.Apply(data, r, data.Active.Id, null, true, true, true);
        Assert.Single(data.Timetables);
        Assert.Equal(2, winter.Periods.Count);
        Assert.Equal("早读", winter.Periods[0].Label);
        Assert.Null(winter.Periods[1].Label);
        Assert.Equal(12, data.Active.Bells[0].Periods.Count); // 夏季作息不动
        Assert.Equal("数学", ScheduleEngine.GetDayPlan(data, new DateOnly(2026, 11, 2)).Lessons[1].Name);
    }

    [Fact]
    public void ApplyAsNewTimetable()
    {
        var data = SampleData.Create();
        var oldId = data.ActiveTimetableId;
        var r = TimetableImporter.FromText("时间,周一,周二,周三,周四,周五\n08:00-10:00,语文,,,,\n");
        var tt = TimetableImporter.Apply(data, r, null, "期中考试", false, false, false);
        Assert.Equal(2, data.Timetables.Count);
        Assert.Equal("期中考试", tt.Name);
        Assert.Equal(oldId, data.ActiveTimetableId);
    }

    [Fact]
    public void LongLabelIsTrimmedWithWarning()
    {
        var r = TimetableImporter.FromText("节次,时间,周一,周二,周三,周四,周五\n晚自习第一节,18:30-19:10,自习,,,,\n");
        Assert.True(r.Ok);
        Assert.Equal("晚自习第", r.Periods[0].Label);
        Assert.Contains(r.Warnings, w => w.Message.Contains("4 个字"));
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

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"ClassName":"无关配置","FontLevel":2}""")]
    [InlineData("""{"SchemaVersion":"2","Timetables":[{"Grid":[],"Bells":[]}]}""")]
    [InlineData("""{"SchemaVersion":0,"Timetables":[{"Grid":[],"Bells":[]}]}""")]
    [InlineData("""{"SchemaVersion":1,"Timetables":[{"Grid":[],"Bells":[]}]}""")]
    [InlineData("""{"SchemaVersion":2,"Timetable":[],"Bells":[]}""")]
    [InlineData("""{"Timetables":[]}""")]
    [InlineData("""{"Timetables":[{}]}""")]
    [InlineData("""{"Timetables":[{"Grid":[],"Bells":[{}]}]}""")]
    [InlineData("""{"Timetables":[{"Grid":[],"Bells":[{"Periods":[{}]}]}]}""")]
    [InlineData("""{"Timetables":[{"Grid":[],"Bells":[]}],"SubjectOverrides":[{"Subjects":null}]}""")]
    public void RejectsJsonWithoutAValidSchedule(string json)
    {
        var path = new DataStore(_dir).DataPath;
        File.WriteAllText(path, json);

        Assert.Null(DataStore.ReadExport(path, out var error));
        Assert.Contains("缺少有效的课表数据", error);
    }

    [Fact]
    public void RejectsFutureExportVersion()
    {
        var path = new DataStore(_dir).DataPath;
        File.WriteAllText(path, """{"SchemaVersion":999,"Timetables":[{"Grid":[],"Bells":[]}]}""");

        Assert.Null(DataStore.ReadExport(path, out var error));
        Assert.Contains("请更新软件", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"SchemaVersion\":1,")]
    public void ImportsLegacyExport(string versionField)
    {
        var path = new DataStore(_dir).DataPath;
        File.WriteAllText(path, $"""
            {
              {{versionField}}
              "ClassName": "旧班级",
              "Bells": [{"Periods":[{"Start":"08:00:00","End":"08:40:00"}]}],
              "Timetable": [["语文"]]
            }
            """);

        var data = DataStore.ReadExport(path, out var error);
        Assert.Null(error);
        Assert.NotNull(data);
        Assert.Equal(ScheduleData.CurrentSchema, data.SchemaVersion);
        Assert.Equal("旧班级", data.ClassName);
        Assert.Equal("语文", data.Active.Grid[0][0]);
        Assert.Equal(new TimeOnly(8, 0), data.Active.ActiveBell.Periods[0].Start);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptsCurrentExportsIncludingEmptySchedules(bool empty)
    {
        var store = new DataStore(_dir);
        var original = empty ? ScheduleData.CreateEmpty() : SampleData.Create();
        store.Export(original, store.DataPath);

        var data = DataStore.ReadExport(store.DataPath, out var error);
        Assert.Null(error);
        Assert.NotNull(data);
        Assert.Equal(empty, data.IsEmpty);
        Assert.Equal(original.ActiveTimetableId, data.ActiveTimetableId);
        Assert.Equal(original.ClassName, data.ClassName);
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
