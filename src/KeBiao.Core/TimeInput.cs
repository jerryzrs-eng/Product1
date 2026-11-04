using System.Text.RegularExpressions;

namespace KeBiao.Core;

/// <summary>时间输入：支持纯数字（1505 → 15:05，740 → 07:40，8 → 08:00）和 7:40 / 7：40 写法。</summary>
public static class TimeInput
{
    public static bool TryParse(string? text, out TimeOnly time)
    {
        time = default;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return false;
        if (Regex.IsMatch(t, @"^\d{1,4}$"))
        {
            int h, m;
            switch (t.Length)
            {
                case 1:
                case 2: h = int.Parse(t); m = 0; break;
                case 3: h = int.Parse(t[..1]); m = int.Parse(t[1..]); break;
                default: h = int.Parse(t[..2]); m = int.Parse(t[2..]); break;
            }
            if (h > 23 || m > 59) return false;
            time = new TimeOnly(h, m);
            return true;
        }
        return TimetableImporter.TryParseTime(t, out time);
    }
}
