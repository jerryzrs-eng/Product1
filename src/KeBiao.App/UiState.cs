using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeBiao.Core;

namespace KeBiao.App;

/// <summary>界面状态（位置、高度、折叠、锁定、字号、置顶），和课表数据分开保存，不进历史版本。</summary>
public sealed class UiState
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? ExpandedHeight { get; set; }
    public double? CollapsedHeight { get; set; }
    public bool Collapsed { get; set; }
    public bool Locked { get; set; }
    /// <summary>字号档位索引：-3..3，0 = 标准（100%）。每档增减 15%，主界面整体缩放倍率 = 1.0 + FontLevel * 0.15。</summary>
    public int FontLevel { get; set; }
    public bool Topmost { get; set; }
    public bool TrayPromoted { get; set; }
    /// <summary>字号方案版本：0 = 旧的 5 档（0.75/0.875/1.0/1.2/1.45），1 = 新的等差 7 档（0.55..1.45）。用于把老用户保存的字号迁移到新档位。</summary>
    public int FontScheme { get; set; } = 1;

    public UiState() { }

    // 旧文件没有 FontScheme。反序列化时用 0，新建状态仍使用当前方案。
    [JsonConstructor]
    public UiState(int fontScheme = 0) => FontScheme = fontScheme;

    public double Scale => 1.0 + FontLevel * 0.15;

    public static UiState Load(string dir)
    {
        try
        {
            var path = Path.Combine(dir, "ui.json");
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<UiState>(File.ReadAllText(path), JsonOptions.Default) ?? new UiState();
                MigrateFontScheme(s);
                return s;
            }
        }
        catch { }
        return new UiState();
    }

    /// <summary>旧 5 档字号（倍率 0.75/0.875/1.0/1.2/1.45）迁移到新等差 7 档，就近取档，尽量保持视觉字号不变。</summary>
    private static void MigrateFontScheme(UiState s)
    {
        if (s.FontScheme >= 1) return;
        double old = s.FontLevel switch { -2 => 0.75, -1 => 0.875, 1 => 1.2, 2 => 1.45, _ => 1.0 };
        s.FontLevel = (int)Math.Round((old - 1.0) / 0.15);
        s.FontScheme = 1;
    }

    public void Save(string dir)
    {
        try
        {
            var path = Path.Combine(dir, "ui.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, JsonOptions.Default));
            File.Move(path + ".tmp", path, true);
        }
        catch { }
    }
}
