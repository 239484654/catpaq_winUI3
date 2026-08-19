// ZpaqBridge: 异步封装 zpaqfranz 可执行文件。
// 移植自 core/zpaq_bridge.py（对齐 uzpaqbridge.pas）。
//
// 后台子进程运行 zpaqfranz 命令，读取 stdout+stderr，
// 解析 @SPK@ 遥测标记与 "Scan"/"W" 进度行，并通过事件通知 UI 线程。
using System.Diagnostics;
using System.Text;
using Microsoft.UI.Dispatching;

namespace Catpaq.Core;

public sealed class ZpaqBridge
{
    public event Action<int>? OnComplete;          // exit code
    public event Action<string>? OnLog;            // 日志行
    public event Action<int, string>? OnProgress;  // percent, msg
    public event Action<string>? OnDataLine;       // data mode 的数据行（后台线程回调，用于流式解析）

    private Process? _proc;
    private bool _busy;
    private bool _isDataMode;
    private string _exePath = "";
    private readonly object _bufferLock = new();
    private readonly List<string> _logBuffer = new();
    private readonly List<string> _dataBuffer = new();

    // Telemetry
    private int _progFilePerc, _progGlobalPerc, _progEta, _progDecPerc;
    private long _progLavorati, _progTotali;
    private string _listPhase = "";

    public string ExternalPath => _exePath;
    public bool Busy => _busy;
    public bool IsDataMode { get => _isDataMode; set => _isDataMode = value; }
    public int? Pid => _proc is { HasExited: false } ? _proc.Id : null;
    public int ProgFilePerc => _progFilePerc;
    public int ProgGlobalPerc => _progGlobalPerc;
    public long ProgLavorati => _progLavorati;
    public long ProgTotali => _progTotali;
    public int ProgEta => _progEta;
    public int ProgDecPerc => _progDecPerc;
    public string ListPhase => _listPhase;

    // 用于从后台线程调度回 UI 线程
    public DispatcherQueue? UiDispatcher { get; set; }

    /// <summary>定位 zpaqfranz 可执行文件（优先程序目录）。</summary>
    public bool LoadExternal(string path = "")
    {
        if (path != "")
            _exePath = path;
        if (_exePath == "")
        {
            var exeName = OperatingSystem.IsWindows() ? "zpaqfranz.exe" : "zpaqfranz";
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, exeName),
                Path.Combine(Directory.GetCurrentDirectory(), exeName),
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    _exePath = c;
                    break;
                }
            }
        }
        return File.Exists(_exePath);
    }

    /// <summary>解析命令行（尊重双引号，对齐 Pascal SplitCmdToParams）。</summary>
    public static List<string> SplitCmdToParams(string cmd)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        foreach (char ch in cmd)
        {
            if (ch == '"')
                inQuote = !inQuote;
            else if (ch == ' ' && !inQuote)
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
                sb.Append(ch);
        }
        if (sb.Length > 0)
            tokens.Add(sb.ToString());
        return tokens;
    }

    /// <summary>异步启动命令。返回是否成功启动。</summary>
    public bool RunCommandAsync(string cmd)
    {
        if (_busy)
            return false;
        if (!LoadExternal())
            return false;

        CleanupPrevious();

        _progFilePerc = 0;
        _progGlobalPerc = 0;
        _progLavorati = 0;
        _progTotali = 0;
        _progEta = 0;
        _listPhase = "";
        _progDecPerc = 0;

        var args = SplitCmdToParams(cmd);
        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        _busy = true;
        try
        {
            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += OnOutputData;
            _proc.ErrorDataReceived += OnOutputData;
            _proc.Exited += OnExited;
            if (!_proc.Start())
            {
                _proc = null;
                _busy = false;
                return false;
            }
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
            return true;
        }
        catch (Exception)
        {
            _proc = null;
            _busy = false;
            return false;
        }
    }

    private void OnOutputData(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null)
            return;
        ProcessLogLine(e.Data);
    }

    private void OnExited(object? sender, EventArgs e)
    {
        int exitCode = -1;
        try { exitCode = _proc?.ExitCode ?? -1; } catch { /* ExitCode may throw if never started */ }
        try
        {
            // 确保 stdout/stderr 异步读取全部完成并派发完毕，流式解析的数据才完整
            _proc?.WaitForExit();
        }
        catch { }
        _busy = false;
        Dispatch(() => OnComplete?.Invoke(exitCode));
    }

    /// <summary>软中止：TerminateProcess。</summary>
    public void AbortCommand()
    {
        try { _proc?.Kill(); }
        catch { /* already dead */ }
    }

    /// <summary>硬杀整个进程树。返回是否杀掉了进程。</summary>
    public bool KillCommand()
    {
        if (_proc is not { HasExited: false })
            return false;
        try
        {
            _proc.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void CleanupPrevious()
    {
        if (_proc is { HasExited: false })
        {
            try { _proc.Kill(); } catch { }
        }
        _proc = null;
    }

    private void Dispatch(Action a)
    {
        if (UiDispatcher != null)
            UiDispatcher.TryEnqueue(() => { try { a(); } catch { } });
        else
            a();
    }

    /// <summary>逐行处理日志/遥测（对齐 Python process_log_line）。</summary>
    public void ProcessLogLine(string s)
    {
        // @SPK@DEC@ : 解压/扫描阶段
        if (s.StartsWith("@SPK@DEC@"))
        {
            var parts = s.Split('@');
            if (parts.Length >= 7)
            {
                _listPhase = "DEC";
                _progDecPerc = ToInt(parts[3]);
                _progLavorati = ToInt64(parts[4]);
                _progTotali = ToInt64(parts[5]);
                _progEta = ToInt(parts[6]);
                _progGlobalPerc = _progDecPerc;
                Dispatch(() => OnProgress?.Invoke(_progGlobalPerc, ""));
            }
            return;
        }
        // @SPK@PRG@ : 进度阶段
        if (s.StartsWith("@SPK@PRG@"))
        {
            var parts = s.Split('@');
            if (parts.Length >= 7)
            {
                _listPhase = "PRG";
                _progFilePerc = ToInt(parts[3]);
                _progLavorati = ToInt64(parts[4]);
                _progTotali = ToInt64(parts[5]);
                _progEta = ToInt(parts[6]);
                _progGlobalPerc = _progTotali > 0
                    ? 100 - (int)(_progLavorati * 100.0 / _progTotali)
                    : 100;
                Dispatch(() => OnProgress?.Invoke(_progGlobalPerc, ""));
            }
            return;
        }
        // @SPK@EXT@ : 解压/测试阶段
        if (s.StartsWith("@SPK@EXT@"))
        {
            var parts = s.Split('@');
            if (parts.Length >= 7)
            {
                _listPhase = "EXT";
                _progGlobalPerc = ToInt(parts[3]);
                _progLavorati = ToInt64(parts[4]);
                _progTotali = ToInt64(parts[5]);
                _progEta = ToInt(parts[6]);
                _progFilePerc = parts.Length >= 8 ? ToInt(parts[7]) : 0;
                Dispatch(() => OnProgress?.Invoke(_progGlobalPerc, ""));
            }
            return;
        }
        // 过滤未知遥测标记（'@' 开头且含第二个 '@' 的行）
        if (s.Length > 1 && s[0] == '@' && s.IndexOf('@', 1) > 1)
            return;
        // "Scan NNN% ..." 列进度行
        if (s.Length >= 8 && s.StartsWith("Scan "))
        {
            _progGlobalPerc = ToInt(s.Substring(5, 3).Trim(), _progGlobalPerc);
            _listPhase = "SCAN";
            Dispatch(() => OnProgress?.Invoke(_progGlobalPerc, ""));
            // list（data mode）时 Scan 行可能每文件一条，数量巨大；只更新进度，
            // 不再进日志/触发 OnLog，避免淹没 UI 消息队列。
            if (!_isDataMode)
            {
                lock (_bufferLock)
                {
                    _logBuffer.Add(s);
                    Dispatch(() => OnLog?.Invoke(s));
                }
            }
            return;
        }
        // 普通行
        lock (_bufferLock)
        {
            if (_isDataMode)
            {
                _dataBuffer.Add(s);
                // 后台线程直接回调，供流式解析（避免全量 temp 文件 + 全量解析）
                try { OnDataLine?.Invoke(s); } catch { }
            }
            else
            {
                _logBuffer.Add(s);
                Dispatch(() => OnLog?.Invoke(s));
            }
        }
    }

    public List<string> FlushLogBuffer()
    {
        lock (_bufferLock)
        {
            var out_ = new List<string>(_logBuffer);
            _logBuffer.Clear();
            return out_;
        }
    }

    public List<string> FlushDataBuffer()
    {
        lock (_bufferLock)
        {
            var out_ = new List<string>(_dataBuffer);
            _dataBuffer.Clear();
            return out_;
        }
    }

    /// <summary>生成临时列表文件路径（对齐 Python get_temp_listing_path）。</summary>
    public static string GetTempListingPath()
    {
        var tempDir = Path.GetTempPath();
        var now = DateTime.Now;
        return Path.Combine(tempDir,
            $"catpaq_listing_{now:yyyyMMdd_HHmmss}_{now.Millisecond:000}.tmp");
    }

    // ---- 解析辅助 ----
    private static int ToInt(string s, int def = 0)
    {
        s = s.Trim();
        return int.TryParse(s, out var v) ? v : def;
    }

    private static long ToInt64(string s, long def = 0)
    {
        s = s.Trim();
        return long.TryParse(s, out var v) ? v : def;
    }
}
