using System.Reflection;
using ClosedXML.Excel;

namespace KeBiao.Core;

public static class TemplateWriter
{
    public const string TemplateFileName = "课表模板.xlsx";
    public const string GuideFileName = "课表转写说明.md";

    public static string GuideMarkdown()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("KeBiao.Core.Guide.md")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>把模板和转写说明写到指定文件夹，返回两个文件的路径。</summary>
    public static (string Template, string Guide) WriteAll(string folder)
    {
        Directory.CreateDirectory(folder);
        var t = Path.Combine(folder, TemplateFileName);
        var g = Path.Combine(folder, GuideFileName);
        WriteTemplate(t);
        File.WriteAllText(g, GuideMarkdown(), new System.Text.UTF8Encoding(true));
        return (t, g);
    }

    public static void WriteTemplate(string path) => WriteTemplate(path, SampleData.Create());

    public static void WriteTemplate(string path, ScheduleData data)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("课表");
        ws.Style.Font.FontName = "Microsoft YaHei";
        ws.Style.Font.FontSize = 11;
        ws.Style.NumberFormat.Format = "@"; // 全部按文字处理，防止 Excel 自动把时间、日期改成别的格式

        ws.Cell(1, 1).Value = "班级";
        ws.Cell(1, 2).Value = data.ClassName;
        ws.Cell(2, 1).Value = "开学日期";
        ws.Cell(2, 2).Value = data.TermStart?.ToString("yyyy-MM-dd") ?? "";
        ws.Range(1, 1, 2, 1).Style.Font.Bold = true;
        ws.Range(1, 1, 2, 1).Style.Font.FontColor = XLColor.FromHtml("#6B7280");

        const int head = 4;
        string[] headers = { "节次", "时间", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(head, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2F7");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var tt = data.Active;
        var bell = tt.ActiveBell;
        int rows = Math.Max(bell.Periods.Count, tt.Grid.Count);
        for (int i = 0; i < rows; i++)
        {
            int r = head + 1 + i;
            ws.Cell(r, 1).Value = i < bell.Periods.Count && bell.Periods[i].Label is { } lb ? lb : (i + 1).ToString();
            ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(r, 1).Style.Font.FontColor = XLColor.FromHtml("#6B7280");
            if (i < bell.Periods.Count)
                ws.Cell(r, 2).Value = $"{bell.Periods[i].Start:HH\\:mm}-{bell.Periods[i].End:HH\\:mm}";
            for (int d = 0; d < 7; d++)
            {
                var v = i < tt.Grid.Count ? tt.Grid[i][d] : "";
                var cell = ws.Cell(r, 3 + d);
                cell.Value = v;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }
        }
        var table = ws.Range(head, 1, head + rows, 9);
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorderColor = XLColor.FromHtml("#D1D5DB");
        table.Style.Border.OutsideBorderColor = XLColor.FromHtml("#D1D5DB");
        ws.Column(1).Width = 9;
        ws.Column(2).Width = 14;
        for (int c = 3; c <= 9; c++) ws.Column(c).Width = 12;
        ws.SheetView.FreezeRows(head);

        var help = wb.Worksheets.Add("填写规则");
        string[] lines =
        {
            "填写规则",
            "",
            "1. 单班填写“课表”页。多班复制课表页，每班一页，用班名命名工作表。单张导入时可用表头上方的班级和开学日期。",
            "2. “时间”写成 07:40-08:10。每一行是一节课，从早到晚排列，时间不能重叠。",
            "   “节次”可以写数字，也可以写名称（如 早读、午托、晚自习一，最多 4 个字）。写数字的会自动编号。",
            "3. 周一到周日每格写科目名。没课就空着。周六、周日没课就整列空着。",
            "4. 走班或选课，原样写，例如：生物/政治。屏幕上会照原样显示。",
            "5. 单双周不同的课，写成：单:音乐 双:美术（可以写在一行，也可以按 Alt+回车 分两行）。",
            "6. 升旗、午托、班会、自习等也可以当作一节课填进去。",
            "7. 填好后保存，在“课表”软件里：设置 → 导入导出 → 从文件导入。",
        };
        for (int i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(1, 1).Style.Font.FontSize = 14;
        help.Column(1).Width = 100;
        help.Style.Font.FontName = "Microsoft YaHei";

        ws.SetTabActive();
        wb.SaveAs(path);
    }
}
