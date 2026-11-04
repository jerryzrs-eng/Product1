using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace KeBiao.App;

/// <summary>开机启动、托盘图标常显等和 Windows 打交道的设置。</summary>
internal static class SystemIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "KeBiao";

    public static string ExePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    public static bool AutoStartEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValueName) is string s && s.Length > 0;
            }
            catch { return false; }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) key.SetValue(RunValueName, $"\"{ExePath}\" --autostart");
                else key.DeleteValue(RunValueName, false);
            }
            catch { }
        }
    }

    /// <summary>
    /// Windows 11 默认把新托盘图标收进“^”里。找到本程序对应的托盘记录，设为始终显示。
    /// Windows 10 没有公开的做法，只能由老师手动拖出来（安装说明里有写）。
    /// 返回 true 表示已经找到并设置。
    /// </summary>
    public static bool TryPromoteTrayIcon()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true);
            if (root == null) return false;
            var exe = ExePath;
            foreach (var name in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(name, true);
                if (sub?.GetValue("ExecutablePath") is string path &&
                    string.Equals(path, exe, StringComparison.OrdinalIgnoreCase))
                {
                    sub.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }
}
