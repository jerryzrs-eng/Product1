using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeBiao.Core;

namespace KeBiao.App;

internal sealed class BatchImportWindow : Window
{
    public BatchImportWindow(AppController app, IReadOnlyList<WorksheetImport> worksheets)
    {
        Title = "批量导入预览";
        Width = 600;
        Height = 500;
        MinWidth = 420;
        MinHeight = 320;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = (FontFamily)FindResource("UiFont");
        Background = (Brush)FindResource("Surface");

        var root = new DockPanel { Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        var import = new Button { Content = $"导入 {worksheets.Count} 张课表", Style = (Style)FindResource("PrimaryButton") };
        buttons.Children.Add(cancel);
        buttons.Children.Add(import);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var names = new HashSet<string>(app.Data.Timetables.Select(t => t.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        var list = new StackPanel();
        var hasErrors = false;
        foreach (var sheet in worksheets)
        {
            var duplicate = !names.Add(sheet.Name.Trim());
            hasErrors |= duplicate || !sheet.Result.Ok;
            list.Children.Add(new TextBlock
            {
                Text = sheet.Name,
                Style = (Style)FindResource("SectionTitle"),
                Margin = new Thickness(0, 8, 0, 4),
            });
            if (duplicate) AddDetail("课表名重复，请在 Excel 中改名后重新导入。", true);
            foreach (var issue in sheet.Result.Errors) AddDetail(issue.ToString(), true);
            foreach (var issue in sheet.Result.Warnings) AddDetail(issue.ToString(), false);
            if (sheet.Result.Ok) AddDetail($"读到 {sheet.Result.Periods.Count} 节课", false);
        }

        import.IsEnabled = !hasErrors;
        import.Click += (_, _) =>
        {
            var draft = app.Data.DeepClone();
            foreach (var sheet in worksheets)
                TimetableImporter.Apply(draft, sheet.Result, null, sheet.Name, false, false, false);
            if (app.TrySaveImportedData(draft, $"批量导入 {worksheets.Count} 张课表"))
                DialogResult = true;
        };

        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        void AddDetail(string message, bool error)
        {
            list.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15,
                Foreground = (Brush)FindResource(error ? "WarnInk" : "InkSoft"),
            });
        }
    }
}
