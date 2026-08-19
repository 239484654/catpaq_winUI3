// Resources 页：任务管理器式系统 CPU / 内存实时监控 + zpaqfranz 进程状态 + Kill。
// 采样：GetSystemTimes 差值法算 CPU 使用率；GlobalMemoryStatusEx 取物理内存。
// 后台线程（Threading.Timer）持续采样并存入静态历史缓冲——切走页面数据不丢失，
// 切回时从缓冲恢复完整曲线。网格参考线；虚线=系统总量，实线=zpaqfranz 占用。
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Catpaq.Core;

namespace Catpaq.Pages;

public sealed partial class ResourcesPage : Page
{
    private MainWindow Main => App.MainWindow;
    private DispatcherTimer? _uiTimer;

    // ---- P/Invoke：系统 CPU / 内存 ----
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ---- 后台采样：静态，跨页面实例共享，切屏不丢数据 ----
    private const int HistoryMax = 120;              // 保留 120 个采样点（2 分钟）
    private static readonly object s_lock = new();
    private static readonly List<(double Sys, double Zpaq)> s_cpuHist = new();
    private static readonly List<(double Sys, double Zpaq)> s_memHist = new();
    private static System.Threading.Timer? s_sampleTimer;
    private static long s_lastIdle, s_lastKernel, s_lastUser, s_lastZpaqCpuTicks;
    private static long s_lastUsed, s_lastTotal;

    public ResourcesPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Main.ResourcesPage = this;
            ApplyLanguage();
            EnsureSamplerStarted();
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += UiTick;
            _uiTimer.Start();
            UiTick(this, null!);
        };
        Unloaded += (_, _) => _uiTimer?.Stop();
    }

    // 后台采样器：整个进程生命周期运行，页面不可见时也持续记录
    private static void EnsureSamplerStarted()
    {
        if (s_sampleTimer is not null)
            return;
        s_sampleTimer = new System.Threading.Timer(_ => SampleTick(), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private static void SampleTick()
    {
        var (sysCpu, zpaqCpu) = SampleCpu();
        var (sysMem, zpaqMem, used, total) = SampleMemory();
        lock (s_lock)
        {
            s_cpuHist.Add((sysCpu, zpaqCpu));
            if (s_cpuHist.Count > HistoryMax) s_cpuHist.RemoveAt(0);
            s_memHist.Add((sysMem, zpaqMem));
            if (s_memHist.Count > HistoryMax) s_memHist.RemoveAt(0);
        }
        s_lastUsed = used;
        s_lastTotal = total;
    }

    // 两次 GetSystemTimes 采样求系统 CPU 使用率 + zpaqfranz CPU 占用
    private static (double Sys, double Zpaq) SampleCpu()
    {
        try
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                return (0, 0);
            long i = ((long)idle.dwHighDateTime << 32) | idle.dwLowDateTime;
            long k = ((long)kernel.dwHighDateTime << 32) | kernel.dwLowDateTime;
            long u = ((long)user.dwHighDateTime << 32) | user.dwLowDateTime;
            long di = i - s_lastIdle, dk = k - s_lastKernel, du = u - s_lastUser;
            s_lastIdle = i; s_lastKernel = k; s_lastUser = u;
            long total = dk + du;
            if (total <= 0) return (0, 0);
            double sys = Math.Clamp(100.0 * (total - di) / total, 0, 100);

            double zpaq = 0;
            var pid = App.MainWindow.Bridge.Pid;
            if (pid is int p)
            {
                try
                {
                    using var proc = Process.GetProcessById(p);
                    long ticks = proc.TotalProcessorTime.Ticks;
                    if (s_lastZpaqCpuTicks > 0)
                        zpaq = Math.Clamp((ticks - s_lastZpaqCpuTicks) * 100.0 / total, 0, 100);
                    s_lastZpaqCpuTicks = ticks;
                }
                catch { s_lastZpaqCpuTicks = 0; }
            }
            else
            {
                s_lastZpaqCpuTicks = 0;
            }
            return (sys, zpaq);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static (double Sys, double Zpaq, long Used, long Total) SampleMemory()
    {
        try
        {
            var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref st))
                return (0, 0, 0, 0);
            double zpaq = 0;
            var pid = App.MainWindow.Bridge.Pid;
            if (pid is int p)
            {
                try
                {
                    using var proc = Process.GetProcessById(p);
                    if (st.ullTotalPhys > 0)
                        zpaq = Math.Clamp(proc.WorkingSet64 * 100.0 / st.ullTotalPhys, 0, 100);
                }
                catch { }
            }
            return (st.dwMemoryLoad, zpaq, (long)(st.ullTotalPhys - st.ullAvailPhys), (long)st.ullTotalPhys);
        }
        catch
        {
            return (0, 0, 0, 0);
        }
    }

    // ---- UI 刷新（1 秒一次）----
    private void UiTick(object? sender, object e)
    {
        lock (s_lock)
        {
            if (s_cpuHist.Count > 0)
            {
                var last = s_cpuHist[^1];
                LblCpuPct.Text = $"{last.Sys:0}%";
                LblCpuDetail.Text = string.Format(Main.T("res.cpu_detail",
                    "Processors: {0}   zpaqfranz CPU: {1:F1}%"),
                    Environment.ProcessorCount, last.Zpaq);
            }
            if (s_memHist.Count > 0)
            {
                var last = s_memHist[^1];
                LblMemPct.Text = $"{last.Sys:0}%";
                LblMemDetail.Text = string.Format(Main.T("res.mem_detail",
                    "In use {0} / {1}   zpaqfranz: {2:F1}%"),
                    ArchiveHelpers.FormatFileSize(s_lastUsed),
                    ArchiveHelpers.FormatFileSize(s_lastTotal), last.Zpaq);
            }
            DrawPlot(CpuGrid, CpuTotalFill, CpuTotalLine, CpuZpaqFill, CpuZpaqLine,
                s_cpuHist, CpuPlot.ActualWidth, CpuPlot.ActualHeight);
            DrawPlot(MemGrid, MemTotalFill, MemTotalLine, MemZpaqFill, MemZpaqLine,
                s_memHist, MemPlot.ActualWidth, MemPlot.ActualHeight);
        }

        // zpaqfranz 进程状态（UI 线程访问）
        var proc = GetZpaqProc();
        if (proc is null)
        {
            LblProcess.Text = Main.T("res.not_running", "zpaqfranz: not running");
            LblNote.Text = "";
            return;
        }
        LblProcess.Text = $"{Main.T("res.running", "zpaqfranz: running")} (PID {proc.Id})";
        try
        {
            LblNote.Text = string.Format(Main.T("res.cpu_time", "zpaqfranz CPU time: {0:F1}s   Working set: {1}"),
                proc.TotalProcessorTime.TotalSeconds, ArchiveHelpers.FormatFileSize(proc.WorkingSet64));
        }
        catch
        {
            LblNote.Text = "";
        }
    }

    // 画密集网格（每 10% 一条横线）+ 带色虚线（总量）+ 实线（zpaq 占用）+ 线下渐变涂色
    // 固定时间窗：曲线从右端向左增长，点数不足时不拉伸起始位置
    private static void DrawPlot(Canvas grid, Polygon totalFill, Polyline totalLine,
        Polygon zpaqFill, Polyline zpaqLine, List<(double Sys, double Zpaq)> hist,
        double width, double height)
    {
        if (width <= 0) width = 320;
        if (height <= 0) height = 80;

        // 网格：0~100% 每 10% 一条横线（密集追踪线）
        var gridBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 128, 128, 128));
        try
        {
            if (Application.Current.Resources.TryGetValue("DividerStrokeColorDefaultBrush", out var b) && b is Brush br)
                gridBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70,
                    ((SolidColorBrush)br).Color.R, ((SolidColorBrush)br).Color.G, ((SolidColorBrush)br).Color.B));
        }
        catch { }
        grid.Children.Clear();
        for (int p = 0; p <= 10; p++)
        {
            double y = height * (100 - p * 10) / 100.0;
            grid.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = width, Y2 = y,
                Stroke = gridBrush, StrokeThickness = 1
            });
        }

        // 固定时间窗：x 步长按满窗计算，数据从右端进入，不随点数拉伸
        double step = width / (HistoryMax - 1);
        int n = hist.Count;
        double leftX = width - (n - 1) * step;

        totalLine.Points = BuildLine(hist, width, height, step, h => h.Sys);
        totalFill.Points = BuildFill(totalLine.Points, width, height, leftX);
        totalFill.Fill = MakeFillBrush(LineColor(totalLine), 60);

        zpaqLine.Points = BuildLine(hist, width, height, step, h => h.Zpaq);
        zpaqFill.Points = BuildFill(zpaqLine.Points, width, height, leftX);
        zpaqFill.Fill = MakeFillBrush(LineColor(zpaqLine), 100);
    }

    private static PointCollection BuildLine(List<(double Sys, double Zpaq)> hist,
        double width, double height, double step, Func<(double Sys, double Zpaq), double> valueOf)
    {
        var pts = new PointCollection();
        int n = hist.Count;
        for (int i = 0; i < n; i++)
        {
            double x = width - (n - 1 - i) * step;
            double y = height - Math.Clamp(valueOf(hist[i]), 0, 100) / 100.0 * height;
            pts.Add(new Point(x, y));
        }
        return pts;
    }

    // 线上点 + 右下角 + 左下角 构成闭合填充多边形
    private static PointCollection BuildFill(PointCollection linePts, double width, double height, double leftX)
    {
        var pts = new PointCollection();
        foreach (var p in linePts)
            pts.Add(p);
        pts.Add(new Point(width, height));
        pts.Add(new Point(leftX, height));
        return pts;
    }

    // 曲线下方自上而下的渐变涂色
    private static Brush MakeFillBrush(Color baseColor, byte alphaTop)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop
        { Color = Windows.UI.Color.FromArgb(alphaTop, baseColor.R, baseColor.G, baseColor.B), Offset = 0 });
        g.GradientStops.Add(new GradientStop
        { Color = Windows.UI.Color.FromArgb(0, baseColor.R, baseColor.G, baseColor.B), Offset = 1 });
        return g;
    }

    private static Color LineColor(Polyline line)
    {
        return line.Stroke is SolidColorBrush sb
            ? sb.Color
            : Windows.UI.Color.FromArgb(255, 0, 120, 212);
    }

    public void ApplyLanguage()
    {
        LblProcess.Text = Main.T("res.not_running", "zpaqfranz: not running");
        BtnKill.Content = Main.T("res.kill", "Kill zpaqfranz");
        LblCpuTitle.Text = Main.T("res.cpu", "CPU");
        LblMemTitle.Text = Main.T("res.memory", "Memory");
    }

    private Process? GetZpaqProc()
    {
        try
        {
            var pid = Main.Bridge.Pid;
            return pid is null ? null : Process.GetProcessById(pid.Value);
        }
        catch
        {
            return null;
        }
    }

    private void BtnKill_Click(object sender, RoutedEventArgs e)
    {
        Main.AbortOperation();
    }
}
