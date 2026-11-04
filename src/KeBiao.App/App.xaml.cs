using System;
using System.Linq;
using System.Threading;
using System.Windows;
using KeBiao.Core;

namespace KeBiao.App;

public partial class App : Application
{
    private const string MutexName = @"Local\KeBiao.SingleInstance";
    private const string ShowEventName = @"Local\KeBiao.Show";
    private const string ExitEventName = @"Local\KeBiao.Exit";

    private Mutex? _mutex;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var zh = new System.Globalization.CultureInfo("zh-CN");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = zh;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = zh;
        Thread.CurrentThread.CurrentCulture = zh;
        Thread.CurrentThread.CurrentUICulture = zh;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("zh-CN")));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.DatePicker), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => CenterDatePickerText((System.Windows.Controls.DatePicker)sender)));
        var args = e.Args;

        int shot = Array.IndexOf(args, "--screenshot");
        if (shot >= 0)
        {
            try
            {
                ScreenshotRunner.Run(shot + 1 < args.Length ? args[shot + 1] : "screenshots");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                System.IO.File.WriteAllText("screenshot-error.txt", ex.ToString());
                Shutdown(1);
                return;
            }
            Shutdown(0);
            return;
        }

        bool wantExit = args.Contains("--exit");
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // 已经在运行：通知它显示设置，或者退出（安装/卸载时使用）
            using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, wantExit ? ExitEventName : ShowEventName);
            ev.Set();
            if (wantExit)
            {
                try { _mutex.WaitOne(TimeSpan.FromSeconds(10)); } catch (AbandonedMutexException) { }
            }
            Shutdown();
            return;
        }
        if (wantExit) { Shutdown(); return; }

        DispatcherUnhandledException += (_, ex) =>
        {
            AppController.LogError(ex.Exception);
            ex.Handled = true; // 小工具常驻桌面，出错时记日志，不直接崩溃
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { if (ex.ExceptionObject is Exception x) AppController.LogError(x); };
        SessionEnding += (_, _) => _controller?.ExitQuietly();

        _controller = new AppController(new DataStore(DataStore.DefaultDirectory));
        _controller.Start();

        Listen(ShowEventName, () => _controller.OpenSettings(null));
        Listen(ExitEventName, () => _controller.ExitQuietly());
    }

    /// <summary>系统自带的日期框把文字贴在顶部，这里把里面的输入区改成垂直居中。</summary>
    private static void CenterDatePickerText(System.Windows.Controls.DatePicker dp)
    {
        dp.ApplyTemplate();
        if (dp.Template?.FindName("PART_TextBox", dp) is not System.Windows.Controls.Primitives.DatePickerTextBox tb) return;
        tb.VerticalContentAlignment = VerticalAlignment.Center;
        tb.VerticalAlignment = VerticalAlignment.Stretch;
        tb.ApplyTemplate();
        foreach (var part in new[] { "PART_ContentHost", "PART_Watermark" })
            if (tb.Template?.FindName(part, tb) is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
    }

    private void Listen(string name, Action action)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        ThreadPool.RegisterWaitForSingleObject(ev, (_, _) => Dispatcher.BeginInvoke(action), null, Timeout.Infinite, false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
