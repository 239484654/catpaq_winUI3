using System;
using Microsoft.UI.Xaml;

namespace Catpaq;

public partial class App : Application
{
    // 桌面应用没有 Window.Current，页面通过 App.MainWindow 访问主窗口。
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        // 捕获托管侧未处理异常，写入 crash.log 便于诊断
        UnhandledException += (_, e) =>
        {
            Program.WriteCrash("UnhandledException: " + e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Program.WriteCrash("AppDomain.UnhandledException: " + e.ExceptionObject);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
        if (Program.AutoOpenSettings)
        {
            MainWindow.DispatcherQueue.TryEnqueue(() => MainWindow.GoToPage("settings"));
        }
        else if (Program.AutoOpenPage != "")
        {
            MainWindow.DispatcherQueue.TryEnqueue(() => MainWindow.GoToPage(Program.AutoOpenPage));
        }
    }
}
