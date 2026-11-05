using System.Text.Json;

namespace KeBiao.Core;

public sealed record BackupInfo(string Path, DateTime SavedAt, string Reason);

/// <summary>
/// 课表数据的保存、读取和历史版本。
/// 每次保存前，把旧版本连同“这次要做什么修改”一起存进 backups 目录，最多保留 20 份。
/// </summary>
public sealed class DataStore
{
    public const int MaxBackups = 20;

    private sealed class BackupFile
    {
        public DateTime SavedAt { get; set; }
        public string Reason { get; set; } = "";
        public ScheduleData Data { get; set; } = new();
    }

    public string Directory { get; }
    public string DataPath => Path.Combine(Directory, "data.json");
    public string BackupDir => Path.Combine(Directory, "backups");

    public DataStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(Directory);
        System.IO.Directory.CreateDirectory(BackupDir);
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeBiao");

    public ScheduleData Load()
    {
        if (File.Exists(DataPath))
        {
            ScheduleData? loaded = null;
            bool needsExpiryMigration = false;
            try
            {
                var data = JsonSerializer.Deserialize<ScheduleData>(File.ReadAllText(DataPath), JsonOptions.Default);
                if (data != null)
                {
                    needsExpiryMigration = data.LegacyGrid != null || data.LegacyBells != null
                        || data.Timetables.Count == 0 || data.Timetables.Any(t => t.CreatedOn == null);
                    data.Normalize();
                    loaded = data;
                }
            }
            catch (Exception)
            {
                // 文件损坏：保留现场，改用最近的备份
                try { File.Copy(DataPath, DataPath + $".damaged-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
            }
            if (loaded != null)
            {
                // 固定旧课表的首次启用日期；写盘失败须直接报告，不能回退到旧备份。
                if (needsExpiryMigration) WriteAtomic(loaded);
                return loaded;
            }
            foreach (var b in ListBackups())
            {
                var d = ReadBackup(b);
                if (d != null) { d.Normalize(); WriteAtomic(d); return d; }
            }
        }
        return ScheduleData.CreateEmpty();
    }

    /// <summary>保存新数据。reason 描述这次修改，例如“导入课表”。</summary>
    public void Save(ScheduleData data, string reason)
    {
        data.Normalize();
        if (File.Exists(DataPath))
        {
            try
            {
                var old = JsonSerializer.Deserialize<ScheduleData>(File.ReadAllText(DataPath), JsonOptions.Default);
                if (old != null)
                {
                    var backup = new BackupFile { SavedAt = DateTime.Now, Reason = reason, Data = old };
                    var name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
                    File.WriteAllText(Path.Combine(BackupDir, name), JsonSerializer.Serialize(backup, JsonOptions.Default));
                }
            }
            catch (Exception) { /* 旧文件读不出来就不备份它 */ }
        }
        WriteAtomic(data);
        TrimBackups();
    }

    /// <summary>不产生历史版本的保存（例如每天自动清理过期调整）。</summary>
    public void SaveQuietly(ScheduleData data)
    {
        data.Normalize();
        WriteAtomic(data);
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        var list = new List<BackupInfo>();
        foreach (var file in System.IO.Directory.GetFiles(BackupDir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var savedAt = doc.RootElement.GetProperty("SavedAt").GetDateTime();
                var reason = doc.RootElement.GetProperty("Reason").GetString() ?? "";
                list.Add(new BackupInfo(file, savedAt, reason));
            }
            catch { }
        }
        return list.OrderByDescending(b => b.SavedAt).ThenByDescending(b => b.Path, StringComparer.Ordinal).ToList();
    }

    public ScheduleData? ReadBackup(BackupInfo info)
    {
        try
        {
            var b = JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(info.Path), JsonOptions.Default);
            return b?.Data;
        }
        catch { return null; }
    }

    public ScheduleData? Restore(BackupInfo info)
    {
        var data = ReadBackup(info);
        if (data == null) return null;
        Save(data, $"恢复到 {info.SavedAt:M月d日 HH:mm} 的版本");
        return data;
    }

    public void Export(ScheduleData data, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions.Default));

    public static ScheduleData? ReadExport(string path) => ReadExport(path, out _);

    public static ScheduleData? ReadExport(string path, out string? error)
    {
        error = "文件缺少有效的课表数据，或文件已损坏。";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            bool hasTimetables = root.TryGetProperty(nameof(ScheduleData.Timetables), out _);
            bool hasLegacyFields = root.TryGetProperty("Timetable", out _) || root.TryGetProperty("Bells", out _);
            int schema = hasTimetables ? ScheduleData.CurrentSchema : 1;
            if (root.TryGetProperty(nameof(ScheduleData.SchemaVersion), out var version))
            {
                if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out schema))
                    return null;
                if (schema > ScheduleData.CurrentSchema)
                {
                    error = $"此文件使用较新的课表数据格式（版本 {schema}），请更新软件后再导入。";
                    return null;
                }
            }

            // 先确认文件中确实有课表结构，再调用 Normalize 补齐或迁移旧数据。
            bool valid = schema switch
            {
                1 => !hasTimetables && HasTimetable(root, "Timetable"),
                2 => !hasLegacyFields
                    && TryGetArray(root, nameof(ScheduleData.Timetables), out var tables)
                    && tables.GetArrayLength() > 0
                    && tables.EnumerateArray().All(table => HasTimetable(table, nameof(Timetable.Grid))),
                _ => false,
            };
            if (!valid) return null;

            var data = root.Deserialize<ScheduleData>(JsonOptions.Default);
            if (data == null || data.FlippedWeeks == null || data.DayOverrides == null
                || data.SubjectOverrides == null || data.SubjectOverrides.Any(o => o?.Subjects == null)
                || data.Timetables.Any(t => t.Name == null))
                return null;

            data.Normalize();
            error = null;
            return data;
        }
        catch { return null; }
    }

    private static bool HasTimetable(JsonElement table, string gridProperty) =>
        TryGetArray(table, gridProperty, out _)
        && TryGetArray(table, nameof(Timetable.Bells), out var bells)
        && bells.EnumerateArray().All(bell =>
            TryGetArray(bell, nameof(BellSchedule.Periods), out var periods)
            && periods.EnumerateArray().All(period =>
                period.ValueKind == JsonValueKind.Object
                && period.TryGetProperty(nameof(Period.Start), out _)
                && period.TryGetProperty(nameof(Period.End), out _)));

    private static bool TryGetArray(JsonElement parent, string name, out JsonElement array)
    {
        array = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out array)
            && array.ValueKind == JsonValueKind.Array;
    }

    private void WriteAtomic(ScheduleData data)
    {
        var tmp = DataPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOptions.Default));
        File.Move(tmp, DataPath, true);
    }

    private void TrimBackups()
    {
        foreach (var b in ListBackups().Skip(MaxBackups))
        {
            try { File.Delete(b.Path); } catch { }
        }
    }
}
