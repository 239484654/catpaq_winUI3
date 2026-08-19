// Log 页：系统日志 + 归档日志 + 进度 + ABORT 栏（对齐 ui/log_tab.py）。
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Catpaq.Pages;

public sealed partial class LogPage : Page
{
    private MainWindow Main => App.MainWindow;

    public LogPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Main.LogPage = this;
            ApplyLanguage();
            Main.ReloadLogs(); // 以完整历史重建日志视图（切回日志页后日志依然可查看）
        };
    }

    private const int MaxBatchLines = 1000;   // 单批最多追加的行数（防大文本阻塞 UI）
    private const int MaxTextChars = 2_000_000; // TextBox 文本总量上限（裁剪掉早期部分）

    public void AddSystemLog(string msg)
    {
        SystemLog.Text += msg + "\n";
        TrimIfHuge(SystemLog);
        SystemLog.SelectionStart = SystemLog.Text.Length;
    }

    // 批量追加（大归档 list 输出可能数万行，逐条 Text += 会卡死 UI）
    public void AddSystemLogBatch(IList<string> lines)
    {
        if (lines.Count == 0) return;
        var capped = CapLines(lines);
        SystemLog.Text += string.Join("\n", capped) + "\n";
        TrimIfHuge(SystemLog);
        SystemLog.SelectionStart = SystemLog.Text.Length;
    }

    public void AddSystemLogRaw(string line)
    {
        var trim = line.Trim();
        if (trim.Length >= 8 && trim.StartsWith("Scan "))
        {
            AppendOrReplaceScanLine(line);
            return;
        }
        SystemLog.Text += line + "\n";
        SystemLog.SelectionStart = SystemLog.Text.Length;
    }

    private int _scanLineIndex = -1;

    private void AppendOrReplaceScanLine(string line)
    {
        var lines = SystemLog.Text.Split('\n').ToList();
        if (_scanLineIndex >= 0 && _scanLineIndex < lines.Count)
        {
            lines[_scanLineIndex] = line;
            SystemLog.Text = string.Join('\n', lines);
        }
        else
        {
            SystemLog.Text += line + "\n";
            _scanLineIndex = SystemLog.Text.Split('\n').Length - 2;
        }
        SystemLog.SelectionStart = SystemLog.Text.Length;
    }

    private int _testLineIndex = -1;

    public void AppendOrReplaceTestLine(string status)
    {
        var lines = SystemLog.Text.Split('\n').ToList();
        if (_testLineIndex >= 0 && _testLineIndex < lines.Count)
        {
            lines[_testLineIndex] = status;
            SystemLog.Text = string.Join('\n', lines);
        }
        else
        {
            SystemLog.Text += status + "\n";
            _testLineIndex = SystemLog.Text.Split('\n').Length - 2;
        }
        SystemLog.SelectionStart = SystemLog.Text.Length;
    }

    public void FinalizeTestLine(string verdict)
    {
        var lines = SystemLog.Text.Split('\n').ToList();
        if (_testLineIndex >= 0 && _testLineIndex < lines.Count)
        {
            lines[_testLineIndex] = verdict;
            SystemLog.Text = string.Join('\n', lines);
            _testLineIndex = -1;
        }
        else
        {
            SystemLog.Text += verdict + "\n";
        }
        SystemLog.SelectionStart = SystemLog.Text.Length;
    }

    public void AddArchiveLog(string msg)
    {
        ArchiveLog.Text += msg + "\n";
        TrimIfHuge(ArchiveLog);
        ArchiveLog.SelectionStart = ArchiveLog.Text.Length;
    }

    // 批量追加（大归档 list 输出可能数万行，逐条 Text += 会卡死 UI）
    public void AddArchiveLogBatch(IList<string> lines)
    {
        if (lines.Count == 0) return;
        var capped = CapLines(lines);
        ArchiveLog.Text += string.Join("\n", capped) + "\n";
        TrimIfHuge(ArchiveLog);
        ArchiveLog.SelectionStart = ArchiveLog.Text.Length;
    }

    // 只保留每批的尾部最多 MaxBatchLines 行
    private static List<string> CapLines(IList<string> lines)
    {
        if (lines.Count <= MaxBatchLines)
            return lines.ToList();
        return lines.Skip(lines.Count - MaxBatchLines).ToList();
    }

    // 文本总量超上限时裁剪掉早期部分，防止 TextBox 文本无限增长拖慢 UI
    private static void TrimIfHuge(TextBox tb)
    {
        if (tb.Text.Length > MaxTextChars)
            tb.Text = tb.Text[^MaxTextChars..];
    }

    public void ClearSystemLog() => SystemLog.Text = "";
    public void ClearArchiveLog() => ArchiveLog.Text = "";

    public void UpdateProgress(int percent)
    {
        Gauge.Value = Math.Clamp(percent, 0, 100);
    }

    public void SetStatus(string msg)
    {
        LblStatus.Text = msg;
    }

    public void ShowAbortToolbar(bool show)
    {
        AbortBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ApplyLanguage()
    {
        BtnAbort.Content = Main.T("btn.abort", "ABORT");
        LblStatus.Text = Main.T("log.loading", "Loading archive...");
        LblSystemLog.Text = Main.T("log.system", "System log");
        LblArchiveLog.Text = Main.T("log.archive", "Archive log");
    }

    private void BtnAbort_Click(object sender, RoutedEventArgs e)
    {
        Main.AbortOperation();
    }
}
