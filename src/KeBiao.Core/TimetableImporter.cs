using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace KeBiao.Core;

public sealed record ImportIssue(int Row, int Column, string Message)
{
    /// <summary>Row/Column 从 1 开始，对应原表格的位置；0 表示不针对具体格子。</summary>
    public override string ToString() => Row > 0 ? $"第 {Row} 行：{Message}" : Message;
}

public sealed class ImportResult
{
    public string? ClassName { get; set; }
    public DateOnly? TermStart { get; set; }
    public List<Period> Periods { get; } = new();
    /// <summary>行 = 节次，7 列 = 周一…周日。</summary>
    public List<List<string>> Timetable { get; } = new();
    /// <summary>每一节在原表格中的行号（1 起），用于标出错误位置。</summary>
    public List<int> SourceRows { get; } = new();
    /// <summary>预览时每一行哪些格子有问题：key = (节次下标, 列)，列 0 = 时间，1..7 = 周一…周日。</summary>
    public Dictionary<(int Lesson, int Col), string> CellErrors { get; } = new();
    public List<ImportIssue> Errors { get; } = new();
    public List<ImportIssue> Warnings { get; } = new();

    public bool Ok => Errors.Count == 0 && Periods.Count > 0;
}

public static partial class TimetableImporter
{
    static TimetableImporter() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // ---------- 入口 ----------

    public static ImportResult FromFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".xlsx" or ".xlsm" => FromXlsx(path),
                ".csv" or ".txt" or ".tsv" or ".md" => FromText(ReadTextAuto(path)),
                ".xls" => Fail("暂不支持旧版 .xls 文件，请在 Excel 里“另存为 .xlsx”后再导入。"),
                _ => Fail("只支持 .xlsx、.csv 文件，或直接粘贴表格文字。"),
            };
        }
        catch (IOException)
        {
            return Fail("文件打不开，可能正在被 Excel 占用。请先关闭 Excel 再导入。");
        }
        catch (Exception ex)
        {
            return Fail("文件读取失败：" + ex.Message);
        }
    }

    public static ImportResult FromXlsx(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        ImportResult? first = null;
        foreach (var ws in wb.Worksheets)
        {
            var rows = SheetToRows(ws);
            if (FindHeader(rows) < 0) continue;
            var r = FromRows(rows);
            if (r.Ok) return r;
            first ??= r;
        }
        return first ?? Fail("没有找到表头。表格里需要有一行写着“时间”和“周一”到“周五”（或“星期一”…）。");
    }

    public static ImportResult FromText(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        List<List<string>> rows;
        if (lines.Count(l => l.TrimStart().StartsWith('|')) >= 2)
            rows = ParseMarkdown(lines);
        else if (lines.Any(l => l.Contains('\t')))
            rows = ParseDelimited(text, '\t');
        else
            rows = ParseDelimited(text, text.Count(c => c == '，') > text.Count(c => c == ',') ? '，' : ',');
        return FromRows(rows);
    }

    // ---------- 核心：从二维表格解析 ----------

    private static readonly string[][] DayNames =
    {
        new[] { "周一", "星期一", "礼拜一", "一", "mon", "monday" },
        new[] { "周二", "星期二", "礼拜二", "二", "tue", "tuesday" },
        new[] { "周三", "星期三", "礼拜三", "三", "wed", "wednesday" },
        new[] { "周四", "星期四", "礼拜四", "四", "thu", "thursday" },
        new[] { "周五", "星期五", "礼拜五", "五", "fri", "friday" },
        new[] { "周六", "星期六", "礼拜六", "六", "sat", "saturday" },
        new[] { "周日", "星期日", "礼拜日", "日", "sun", "sunday", "周天", "星期天", "礼拜天", "天" },
    };

    private static int DayOf(string header)
    {
        var h = header.Trim().ToLowerInvariant();
        for (int d = 0; d < 7; d++)
            if (DayNames[d].Contains(h)) return d;
        return -1;
    }

    private static int FindHeader(List<List<string>> rows)
    {
        for (int r = 0; r < rows.Count; r++)
        {
            int days = rows[r].Count(c => DayOf(c) >= 0 && DayOf(c) <= 4);
            if (days >= 3) return r;
        }
        return -1;
    }

    public static ImportResult FromRows(List<List<string>> rows)
    {
        var result = new ImportResult();
        int header = FindHeader(rows);
        if (header < 0)
        {
            result.Errors.Add(new ImportIssue(0, 0, "没有找到表头。表格里需要有一行写着“时间”和“周一”到“周五”（或“星期一”…）。"));
            return result;
        }

        // 表头之前的“班级”“开学日期”
        for (int r = 0; r < header; r++)
        {
            var row = rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                var key = row[c].Trim().TrimEnd(':', '：');
                var value = row.Skip(c + 1).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
                var inline = InlineValue(row[c]);
                if (key.StartsWith("班级"))
                {
                    var v = inline ?? value;
                    if (!string.IsNullOrWhiteSpace(v)) result.ClassName = v;
                }
                else if (key.StartsWith("开学"))
                {
                    var v = inline ?? value;
                    if (v != null)
                    {
                        if (TryParseDate(v, out var d)) result.TermStart = d;
                        else result.Warnings.Add(new ImportIssue(r + 1, c + 1, $"开学日期“{v}”看不懂，已忽略，可在设置里补填"));
                    }
                }
            }
        }

        var head = rows[header];
        int timeCol = -1, startCol = -1, endCol = -1;
        var dayCols = new int[7];
        Array.Fill(dayCols, -1);
        for (int c = 0; c < head.Count; c++)
        {
            var h = head[c].Trim();
            int d = DayOf(h);
            if (d >= 0 && dayCols[d] < 0) { dayCols[d] = c; continue; }
            if (h is "开始" or "开始时间" or "上课" or "上课时间") startCol = c;
            else if (h is "结束" or "结束时间" or "下课" or "下课时间") endCol = c;
            else if (h.Contains("时间") || h is "时段" or "作息") timeCol = c;
        }
        if (timeCol < 0 && (startCol < 0 || endCol < 0))
        {
            result.Errors.Add(new ImportIssue(header + 1, 0, "表头里缺少“时间”列（或“开始”“结束”两列）"));
            return result;
        }

        for (int r = header + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Cell(int c) => c >= 0 && c < row.Count ? row[c] ?? "" : "";
            var subjects = Enumerable.Range(0, 7).Select(d => CellParser.Clean(Cell(dayCols[d]))).ToList();
            string timeText = timeCol >= 0 ? Cell(timeCol) : (Cell(startCol) + "-" + Cell(endCol));
            bool timeEmpty = timeCol >= 0 ? string.IsNullOrWhiteSpace(Cell(timeCol)) : string.IsNullOrWhiteSpace(Cell(startCol)) && string.IsNullOrWhiteSpace(Cell(endCol));
            if (timeEmpty && subjects.All(s => s.Length == 0)) continue; // 空行

            int idx = result.Periods.Count;
            int timeColumnNumber = (timeCol >= 0 ? timeCol : startCol) + 1;
            Period period;
            if (timeEmpty)
            {
                result.Errors.Add(new ImportIssue(r + 1, timeColumnNumber, "缺少上下课时间"));
                result.CellErrors[(idx, 0)] = "缺少时间";
                period = new Period();
            }
            else if (!TryParseRange(timeText, out period))
            {
                result.Errors.Add(new ImportIssue(r + 1, timeColumnNumber, $"时间“{timeText.Trim()}”看不懂，应写成 07:40-08:10"));
                result.CellErrors[(idx, 0)] = "时间格式不对";
                period = new Period();
            }
            else if (period.End <= period.Start)
            {
                result.Errors.Add(new ImportIssue(r + 1, timeColumnNumber, $"下课时间 {period.End:HH\\:mm} 不晚于上课时间 {period.Start:HH\\:mm}"));
                result.CellErrors[(idx, 0)] = "下课早于上课";
            }

            for (int d = 0; d < 7; d++)
            {
                var parsed = CellParser.Parse(subjects[d]);
                if (parsed.Error != null)
                {
                    result.Errors.Add(new ImportIssue(r + 1, dayCols[d] + 1, $"{ScheduleEngine.WeekdayName(d + 1)}“{subjects[d]}”：{parsed.Error}"));
                    result.CellErrors[(idx, d + 1)] = parsed.Error;
                }
            }

            result.Periods.Add(period);
            result.Timetable.Add(subjects);
            result.SourceRows.Add(r + 1);
        }

        if (result.Periods.Count == 0)
            result.Errors.Add(new ImportIssue(0, 0, "表头下面没有读到任何一节课"));

        // 时间顺序和重叠
        for (int i = 1; i < result.Periods.Count; i++)
        {
            var prev = result.Periods[i - 1];
            var cur = result.Periods[i];
            if (prev.End == default && prev.Start == default) continue;
            if (cur.End == default && cur.Start == default) continue;
            if (cur.Start < prev.End)
            {
                result.Errors.Add(new ImportIssue(result.SourceRows[i], 0,
                    $"这一节 {cur.Start:HH\\:mm} 开始，和上一节（{prev.Start:HH\\:mm}-{prev.End:HH\\:mm}）时间重叠或顺序颠倒"));
                result.CellErrors[(i, 0)] = "时间重叠";
            }
        }

        for (int d = 0; d < 5; d++)
            if (dayCols[d] < 0)
                result.Warnings.Add(new ImportIssue(header + 1, 0, $"没有找到“{ScheduleEngine.WeekdayName(d + 1)}”这一列，当作没课"));

        return result;
    }

    // ---------- 工具方法 ----------

    private static string? InlineValue(string cell)
    {
        var m = Regex.Match(cell, @"^\s*(班级|开学日期|开学)\s*[:：]\s*(.+)$");
        return m.Success ? m.Groups[2].Value.Trim() : null;
    }

    [GeneratedRegex(@"^\s*(\d{1,2})\s*[:：.．点]\s*(\d{1,2})\s*$")]
    private static partial Regex TimeRegex();

    public static bool TryParseTime(string text, out TimeOnly time)
    {
        time = default;
        text = text.Trim();
        var m = TimeRegex().Match(text);
        if (m.Success)
        {
            int h = int.Parse(m.Groups[1].Value), mi = int.Parse(m.Groups[2].Value);
            if (h > 23 || mi > 59) return false;
            time = new TimeOnly(h, mi);
            return true;
        }
        // Excel 里的时间可能是 0~1 之间的小数
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var frac) && frac >= 0 && frac < 1)
        {
            int minutes = (int)Math.Round(frac * 24 * 60);
            if (minutes >= 24 * 60) return false;
            time = new TimeOnly(minutes / 60, minutes % 60);
            return true;
        }
        return false;
    }

    public static bool TryParseRange(string text, out Period period)
    {
        period = new Period();
        var parts = Regex.Split(text.Trim(), @"\s*(?:-|–|—|－|~|～|至|到)+\s*").Where(p => p.Length > 0).ToArray();
        if (parts.Length != 2) return false;
        if (!TryParseTime(parts[0], out var s) || !TryParseTime(parts[1], out var e)) return false;
        period = new Period(s, e);
        return true;
    }

    public static bool TryParseDate(string text, out DateOnly date)
    {
        date = default;
        text = text.Trim();
        var m = Regex.Match(text, @"^(\d{4})\s*[-/.年]\s*(\d{1,2})\s*[-/.月]\s*(\d{1,2})\s*日?$");
        if (m.Success)
        {
            try { date = new DateOnly(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)); return true; }
            catch { return false; }
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial > 20000 && serial < 80000)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }
        return DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static List<List<string>> SheetToRows(IXLWorksheet ws)
    {
        var rows = new List<List<string>>();
        var used = ws.RangeUsed();
        if (used == null) return rows;
        int lastRow = used.LastRow().RowNumber(), lastCol = used.LastColumn().ColumnNumber();
        for (int r = 1; r <= lastRow; r++)
        {
            var row = new List<string>();
            for (int c = 1; c <= lastCol; c++)
                row.Add(CellText(ws.Cell(r, c)));
            rows.Add(row);
        }
        // 合并单元格：把左上角的值填到整个区域里（常见于“上午/下午”这种合并格，不影响解析）
        return rows;
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        try
        {
            switch (cell.DataType)
            {
                case XLDataType.DateTime:
                    var dt = cell.GetDateTime();
                    return dt.Date == new DateTime(1899, 12, 30) || dt.Date == new DateTime(1899, 12, 31) || dt.Year < 1901
                        ? dt.ToString("HH:mm") : dt.ToString("yyyy-MM-dd");
                case XLDataType.TimeSpan:
                    var ts = cell.GetTimeSpan();
                    return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
                case XLDataType.Number:
                    var n = cell.GetDouble();
                    if (n >= 0 && n < 1 && cell.Style.NumberFormat.Format.Contains(':'))
                    {
                        var t = TimeSpan.FromMinutes(Math.Round(n * 24 * 60));
                        return $"{t.Hours:00}:{t.Minutes:00}";
                    }
                    return cell.GetFormattedString();
                default:
                    return cell.GetString();
            }
        }
        catch
        {
            return cell.GetFormattedString();
        }
    }

    private static List<List<string>> ParseMarkdown(string[] lines)
    {
        var rows = new List<List<string>>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith('|'))
            {
                // 表格外的“班级：xxx”“开学日期：xxx”
                if (InlineValue(line) != null) rows.Add(new List<string> { line });
                continue;
            }
            line = line.Trim('|');
            var cells = line.Split('|').Select(c => c.Trim()).ToList();
            if (cells.All(c => Regex.IsMatch(c, @"^:?-{2,}:?$"))) continue; // 分隔线
            rows.Add(cells.Select(c => c.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n")).ToList());
        }
        return rows;
    }

    /// <summary>解析 CSV/TSV，支持双引号包起来的多行格子。</summary>
    private static List<List<string>> ParseDelimited(string text, char sep)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(ch);
            }
            else if (ch == '"' && cell.Length == 0) quoted = true;
            else if (ch == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); }
            else cell.Append(ch);
        }
        row.Add(cell.ToString());
        if (row.Any(c => c.Length > 0)) rows.Add(row);
        return rows;
    }

    /// <summary>CSV 可能是 UTF-8，也可能是 Excel 存的 GBK。</summary>
    public static string ReadTextAuto(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }

    private static ImportResult Fail(string message)
    {
        var r = new ImportResult();
        r.Errors.Add(new ImportIssue(0, 0, message));
        return r;
    }

    /// <summary>把导入结果写进课表数据：替换整张课表，作息写入指定的一套（或新建）。</summary>
    public static void Apply(ScheduleData data, ImportResult result, string? targetBellId, string? newBellName, bool applyClassName, bool applyTermStart)
    {
        BellSchedule bell;
        if (targetBellId != null && data.Bells.FirstOrDefault(b => b.Id == targetBellId) is { } existing)
            bell = existing;
        else
        {
            bell = new BellSchedule { Name = string.IsNullOrWhiteSpace(newBellName) ? "导入的作息" : newBellName.Trim() };
            data.Bells.Add(bell);
        }
        bell.Periods = result.Periods.Select(p => p.Clone()).ToList();
        data.ActiveBellId = bell.Id;
        data.Timetable = result.Timetable.Select(r => r.ToList()).ToList();
        data.SubjectOverrides.Clear();
        if (applyClassName && !string.IsNullOrWhiteSpace(result.ClassName)) data.ClassName = result.ClassName!;
        if (applyTermStart && result.TermStart.HasValue) data.TermStart = result.TermStart;
        data.Normalize();
    }
}
