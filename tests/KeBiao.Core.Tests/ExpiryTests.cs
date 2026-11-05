using System.Text.Json;
using System.Text.Json.Nodes;
using KeBiao.Core;

namespace KeBiao.Core.Tests;

public sealed class ExpiryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kebiao-expiry-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Monday = new(2026, 11, 2);

    [Fact]
    public void DefaultExpiresAtBeginningOfWeek24()
    {
        var data = SampleData.Create();
        var expires = new DateOnly(2027, 2, 8);
        Assert.Equal(23, data.Active.ValidWeeks);
        Assert.Equal(expires, ScheduleEngine.ExpiryDate(data, data.Active));
        Assert.False(ScheduleEngine.IsExpired(data, data.Active, expires.AddDays(-1)));
        Assert.True(ScheduleEngine.IsExpired(data, data.Active, expires));
        Assert.NotEmpty(ScheduleEngine.GetDayPlan(data, expires.AddDays(-7)).Lessons);
        Assert.Empty(ScheduleEngine.GetDayPlan(data, expires).Lessons);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(23)]
    [InlineData(520)]
    public void ValidWeeksUseOpeningWeekAsWeekOne(int weeks)
    {
        var data = SampleData.Create();
        data.TermStart = Monday.AddDays(2);
        data.Active.ValidWeeks = weeks;
        Assert.Equal(Monday.AddDays(weeks * 7), ScheduleEngine.ExpiryDate(data, data.Active));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(521)]
    [InlineData(int.MaxValue)]
    public void NormalizeReplacesInvalidWeeksWithDefault(int weeks)
    {
        var table = new Timetable { ValidWeeks = weeks };
        table.Normalize();
        Assert.Equal(23, table.ValidWeeks);
        Assert.NotNull(table.CreatedOn);
    }

    [Fact]
    public void WithoutTermStartUsesPersistedActivationDayWithoutRounding()
    {
        var data = SampleData.Create();
        data.TermStart = null;
        data.Active.CreatedOn = Monday.AddDays(2);
        data.Active.ValidWeeks = 2;
        data.Normalize();
        Assert.Equal(Monday.AddDays(16), ScheduleEngine.ExpiryDate(data, data.Active));
        Assert.False(ScheduleEngine.IsExpired(data, data.Active, Monday.AddDays(15)));
        Assert.True(ScheduleEngine.IsExpired(data, data.Active, Monday.AddDays(16)));
        data.TermStart = Monday.AddDays(3);
        Assert.Equal(Monday.AddDays(14), ScheduleEngine.ExpiryDate(data, data.Active));
    }

    [Fact]
    public void ExplicitDateOverridesWeeksAndTakesEffectAtMidnight()
    {
        var data = SampleData.Create();
        data.Active.ValidWeeks = 1;
        data.Active.ExpiresOn = Monday.AddDays(1);
        Assert.Equal(Monday.AddDays(1), ScheduleEngine.ExpiryDate(data, data.Active));
        var before = ScheduleEngine.GetStatus(data, Monday.ToDateTime(new TimeOnly(23, 59)));
        var after = ScheduleEngine.GetStatus(data, Monday.AddDays(1).ToDateTime(TimeOnly.MinValue));
        Assert.False(before.Today.IsExpired);
        Assert.True(after.Today.IsExpired);
        Assert.Equal(Phase.NoClassToday, after.Phase);
        Assert.Null(after.Current);
        Assert.Null(after.Next);
        Assert.Null(after.LaterFirst);
    }

    [Fact]
    public void ExpiredTableCannotBeRevivedByWeekdayOrSubjectOverride()
    {
        var data = SampleData.Create();
        data.Active.ExpiresOn = Monday;
        var originalGrid = data.Active.Grid.Select(r => r.ToArray()).ToArray();
        ScheduleEngine.SetOverride(data, Monday, DayOverrideKind.FollowWeekday, 2);
        data.SubjectOverrides.Add(new DaySubjectOverride { Date = Monday, Subjects = new() { "临时语文", "临时数学" } });

        var subjects = ScheduleEngine.BaseSubjects(data, Monday, out var weekday, out _, out var holiday);
        var plan = ScheduleEngine.GetDayPlan(data, Monday);
        Assert.Equal(2, weekday);
        Assert.False(holiday);
        Assert.All(subjects, s => Assert.Empty(s));
        Assert.True(plan.IsExpired);
        Assert.False(plan.HasSubjectOverride);
        Assert.Empty(plan.Lessons);
        Assert.Single(data.SubjectOverrides);
        for (int i = 0; i < originalGrid.Length; i++) Assert.Equal(originalGrid[i], data.Active.Grid[i]);
    }

    [Fact]
    public void DateOverrideHonorsChosenTablesExpiryWithoutSwitchingAutomatically()
    {
        var data = SampleData.Create();
        var originalId = data.ActiveTimetableId;
        data.Active.ExpiresOn = Monday;
        var exam = SampleData.ExamTimetable();
        exam.ExpiresOn = Monday.AddDays(1);
        data.Timetables.Add(exam);
        Assert.True(ScheduleEngine.GetDayPlan(data, Monday).IsExpired);
        Assert.Empty(ScheduleEngine.GetDayPlan(data, Monday).Lessons);

        ScheduleEngine.SetOverride(data, Monday, DayOverrideKind.UseTimetable, timetableId: exam.Id);
        Assert.NotEmpty(ScheduleEngine.GetDayPlan(data, Monday).Lessons);
        ScheduleEngine.SetOverride(data, Monday.AddDays(1), DayOverrideKind.UseTimetable, timetableId: exam.Id);
        var expiredExam = ScheduleEngine.GetDayPlan(data, Monday.AddDays(1));
        Assert.True(expiredExam.IsExpired);
        Assert.Same(exam, expiredExam.Timetable);
        Assert.Empty(expiredExam.Lessons);
        Assert.Equal(originalId, data.ActiveTimetableId);
        Assert.Equal(2, data.Timetables.Count);
    }

    [Fact]
    public void CloneAndExportImportKeepAllExpirySettings()
    {
        var data = SampleData.Create();
        SetExpiry(data.Active);
        AssertSettings(data.Active.Clone("复制课表"));
        AssertSettings(data.DeepClone().Active);
        var store = new DataStore(_directory);
        var exportPath = Path.Combine(_directory, "export.json");
        store.Export(data, exportPath);
        var imported = DataStore.ReadExport(exportPath);
        Assert.NotNull(imported);
        AssertSettings(imported.Active);
    }

    [Fact]
    public void SaveLoadAndHistoryRestoreKeepAllExpirySettings()
    {
        var store = new DataStore(_directory);
        var data = SampleData.Create();
        SetExpiry(data.Active);
        store.Save(data, "保存有效期");
        AssertSettings(store.Load().Active);

        data.Active.ValidWeeks = 40;
        data.Active.ExpiresOn = null;
        data.Active.CreatedOn = Monday;
        store.Save(data, "更改有效期");
        var backup = Assert.Single(store.ListBackups());
        var restored = store.Restore(backup);
        Assert.NotNull(restored);
        AssertSettings(restored.Active);
        AssertSettings(store.Load().Active);
    }

    [Fact]
    public void LoadingHistoricalJsonDefaultsTo23WeeksAndPersistsActivationDate()
    {
        var store = new DataStore(_directory);
        WriteHistoricalData(store);
        var loaded = store.Load();
        Assert.Equal(23, loaded.Active.ValidWeeks);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), loaded.Active.CreatedOn);
        using var saved = JsonDocument.Parse(File.ReadAllText(store.DataPath));
        var storedDate = saved.RootElement.GetProperty("Timetables")[0].GetProperty("CreatedOn").GetString();
        Assert.Equal(loaded.Active.CreatedOn!.Value.ToString("yyyy-MM-dd"), storedDate);
        Assert.Equal(loaded.Active.CreatedOn, new DataStore(_directory).Load().Active.CreatedOn);
        Assert.Empty(store.ListBackups());
    }

    [Fact]
    public void MigrationWriteFailureDoesNotTreatHealthyDataAsDamaged()
    {
        var store = new DataStore(_directory);
        WriteHistoricalData(store);
        var original = File.ReadAllText(store.DataPath);
        Directory.CreateDirectory(store.DataPath + ".tmp");
        var error = Record.Exception(() => store.Load());
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.Equal(original, File.ReadAllText(store.DataPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.damaged-*"));
    }

    [Fact]
    public void VersionOneMigrationAlsoPersistsActivationAndDefaultWeeks()
    {
        var store = new DataStore(_directory);
        File.WriteAllText(store.DataPath, """
            {
              "SchemaVersion": 1,
              "Bells": [{ "Id": "b", "Periods": [{ "Start": "08:00:00", "End": "08:40:00" }] }],
              "ActiveBellId": "b",
              "Timetable": [["语文", "", "", "", "", "", ""]]
            }
            """);
        var loaded = store.Load();
        Assert.Equal(23, loaded.Active.ValidWeeks);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), loaded.Active.CreatedOn);
        Assert.Equal("语文", loaded.Active.Grid[0][0]);
        var persisted = JsonSerializer.Deserialize<ScheduleData>(File.ReadAllText(store.DataPath), JsonOptions.Default)!;
        Assert.Null(persisted.LegacyGrid);
        Assert.Equal(loaded.Active.CreatedOn, persisted.Active.CreatedOn);
        Assert.Equal(23, persisted.Active.ValidWeeks);
    }

    [Fact]
    public void ExtremeStartDateCannotOverflowExpiryCalculation()
    {
        var data = SampleData.Create();
        data.TermStart = DateOnly.MaxValue;
        data.Active.ValidWeeks = 520;
        Assert.Equal(DateOnly.MaxValue, ScheduleEngine.ExpiryDate(data, data.Active));
    }

    private static void SetExpiry(Timetable table)
    {
        table.ValidWeeks = 12;
        table.ExpiresOn = new DateOnly(2027, 1, 1);
        table.CreatedOn = Monday.AddDays(-10);
    }

    private static void AssertSettings(Timetable table)
    {
        Assert.Equal(12, table.ValidWeeks);
        Assert.Equal(new DateOnly(2027, 1, 1), table.ExpiresOn);
        Assert.Equal(Monday.AddDays(-10), table.CreatedOn);
    }

    private static void WriteHistoricalData(DataStore store)
    {
        var data = SampleData.Create();
        data.TermStart = null;
        var root = JsonNode.Parse(JsonSerializer.Serialize(data, JsonOptions.Default))!;
        foreach (var table in root["Timetables"]!.AsArray())
        {
            table!.AsObject().Remove("ValidWeeks");
            table.AsObject().Remove("ExpiresOn");
            table.AsObject().Remove("CreatedOn");
        }
        File.WriteAllText(store.DataPath, root.ToJsonString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
