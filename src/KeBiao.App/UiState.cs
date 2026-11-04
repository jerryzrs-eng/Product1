using System;
using System.IO;
using System.Text.Json;
using KeBiao.Core;

namespace KeBiao.App;

/// <summary>界面状态（位置、高度、折叠、锁定、字号、置顶），和课表数据分开保存，不进历史版本。</summary>
public sealed class UiState
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? ExpandedHeight { get; set; }
    public bool Collapsed { get; set; }
    public bool Locked { get; set; }
    /// <summary>-2 = 特小，-1 = 小，0 = 标准，1 = 大，2 = 特大。</summary>
    public int FontLevel { get; set; }
    public bool Topmost { get; set; }
    public bool TrayPromoted { get; set; }

    public double Scale => FontLevel switch { -2 => 0.75, -1 => 0.875, 1 => 1.2, 2 => 1.45, _ => 1.0 };

    public static UiState Load(string dir)
    {
        try
        {
            var path = Path.Combine(dir, "ui.json");
            if (File.Exists(path))
                return JsonSerializer.Deserialize<UiState>(File.ReadAllText(path), JsonOptions.Default) ?? new UiState();
        }
        catch { }
        return new UiState();
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
