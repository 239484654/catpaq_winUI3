// catpaq WinUI 3 版入口。unpackaged 应用的标准 Main() 引导。
using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Catpaq;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // 启动参数：--settings 自动打开设置页；--page=<tag> 自动打开任意页；
        // 其余以 .zpaq 结尾的参数视为要打开的归档文件（双击 .zpaq 由系统传入完整路径）
        foreach (var a in args)
        {
            if (string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase))
                AutoOpenSettings = true;
            else if (a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))
                AutoOpenPage = a["--page=".Length..];
            else if (a.EndsWith(".zpaq", StringComparison.OrdinalIgnoreCase) && File.Exists(a))
                StartupArchive = a;
        }
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
        catch (Exception ex)
        {
            WriteCrash(ex.ToString());
            throw;
        }
    }

    public static bool AutoOpenSettings { get; private set; }
    public static string AutoOpenPage { get; private set; } = "";
    public static string StartupArchive { get; set; } = "";

    public static void WriteCrash(string text)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {text}\n");
        }
        catch { }
    }
}
