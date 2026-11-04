using KeBiao.Core;

// 用法：KeBiao.TemplateGen <输出文件夹>
var folder = args.Length > 0 ? args[0] : "templates";
var (t, g) = TemplateWriter.WriteAll(folder);
Console.WriteLine(t);
Console.WriteLine(g);
