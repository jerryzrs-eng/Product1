using System.Text.RegularExpressions;

namespace KeBiao.Core;

/// <summary>
/// 解析课表格子里的文字。
/// 普通写法：「语文」「生物/政治」（走班，原样显示）。
/// 单双周写法：「单:物理 双:化学」，可写在同一行或分两行，冒号中英文均可，「单周:」「双周:」也可以。
/// </summary>
public static partial class CellParser
{
    [GeneratedRegex(@"(单|双)周?\s*[:：]")]
    private static partial Regex MarkerRegex();

    public sealed record Parsed(string? Odd, string? Even, bool HasParity, string? Error);

    public static Parsed Parse(string? raw)
    {
        var text = Clean(raw);
        if (text.Length == 0) return new Parsed("", "", false, null);

        var matches = MarkerRegex().Matches(text);
        if (matches.Count == 0)
        {
            var plain = string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return new Parsed(plain, plain, false, null);
        }

        string? error = null;
        var prefix = text[..matches[0].Index].Trim();
        if (prefix.Length > 0)
            error = $"“{prefix}”前面缺少“单:”或“双:”";

        string? odd = null, even = null;
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            int start = m.Index + m.Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var value = string.Join(" ", text[start..end].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();
            if (m.Groups[1].Value == "单")
            {
                if (odd != null) error ??= "“单:”写了两次";
                odd = value;
            }
            else
            {
                if (even != null) error ??= "“双:”写了两次";
                even = value;
            }
        }
        return new Parsed(odd ?? "", even ?? "", true, error);
    }

    public static string Resolve(string? raw, bool isOddWeek)
    {
        var p = Parse(raw);
        return (isOddWeek ? p.Odd : p.Even) ?? "";
    }

    public static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var s = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        return s.Trim();
    }
}
