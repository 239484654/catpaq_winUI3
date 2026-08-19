// 主窗口：NavigationView + 五页签（Browse/Archive/Log/Settings/Resources），
// 与 wx 版 Notebook 页序对齐。持有共享状态（bridge、归档路径、密码、语言）。
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Catpaq.Core;
using Catpaq.Pages;
using Microsoft.Win32;

namespace Catpaq;

// 桥接操作状态（对齐 wx 版 BridgeOp 枚举）
public enum BridgeOp
{
    List,
    Hash,
    Test,
    Extract,
}

public sealed partial class MainWindow : Window
{
    public ZpaqBridge Bridge { get; } = new();
    public ArchiveData ArchiveData { get; set; } = new();
    public string ArchivePath { get; set; } = "";
    public ArchiveType ArchiveType { get; set; } = ArchiveType.Unknown;
    public string PasswordKey { get; set; } = "";
    public string PasswordFranzen { get; set; } = "";
    public bool LoadingArchive { get; set; }
    public bool KeepTempFiles { get; set; }
    public string TempListFile { get; set; } = "";
    public string ArchiveBrowsePath { get; set; } = "";
    public bool LoadFromBrowseTab { get; set; }
    public string LangName { get; set; } = "en-US";

    public BridgeOp BridgeOp { get; set; } = BridgeOp.List;
    public int HashFileCount { get; set; }
    public bool IsAddRunning { get; set; }   // Add 对话框正在运行：完成时跳过归档列表流程

    public BrowsePage BrowsePage { get; set; } = null!;
    public ArchivePage ArchivePage { get; set; } = null!;
    public LogPage LogPage { get; set; } = null!;
    public SettingsPage SettingsPage { get; set; } = null!;
    public ResourcesPage ResourcesPage { get; set; } = null!;
    public AboutPage AboutPage { get; set; } = null!;

    // 桌面应用文件选择器需要窗口句柄
    public IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>按当前语言取文案。</summary>
    public string T(string key, string en) => Core.I18n.T(key, en, LangName);

    /// <summary>语言切换后刷新导航项与所有已加载页面的文案。</summary>
    public void ApplyLanguage()
    {
        NavBrowse.Content = T("nav.browse", "Browse");
        NavArchive.Content = T("nav.archive", "Archive");
        NavLog.Content = T("nav.log", "Log");
        NavSettings.Content = T("nav.settings", "Settings");
        NavResources.Content = T("nav.resources", "Resources");
        NavAbout.Content = T("nav.about", "About");
        LblLoadingTitle.Text = T("log.opening", "Opening archive...");
        BtnCancelLoading.Content = T("btn.cancel", "Cancel");
        BrowsePage?.ApplyLanguage();
        ArchivePage?.ApplyLanguage();
        LogPage?.ApplyLanguage();
        SettingsPage?.ApplyLanguage();
        ResourcesPage?.ApplyLanguage();
        AboutPage?.ApplyLanguage();
    }

    public MainWindow()
    {
        InitializeComponent();
        Title = "Catpaq V1.0.0-winui";
        ApplySystemTheme();
        DetectDefaultLanguage();
        Bridge.UiDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Bridge.OnComplete += OnBridgeComplete;
        Bridge.OnProgress += OnBridgeProgress;
        Bridge.OnLog += OnBridgeLog;
        Bridge.LoadExternal();
        AddLog("--- System Startup Check ---");
        AddLog($"EXE present: {(Bridge.ExternalPath != "" ? "YES" : "NO")}  [{Bridge.ExternalPath}]");
    }

    // 标题栏并入客户区：Mica 材质覆盖整个标题栏并跟随系统主题（深/浅）。
    private void ApplySystemTheme()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch
        {
            // 低版本系统不支持时回退：恢复系统标题栏
            try { ExtendsContentIntoTitleBar = false; } catch { }
        }
    }

    // 按系统首选语言匹配 I18n.Languages 注册表；无匹配回退 en-US
    private void DetectDefaultLanguage()
    {
        try
        {
            var langs = Windows.System.UserProfile.GlobalizationPreferences.Languages;
            foreach (var l in langs)
            {
                foreach (var (code, _) in I18n.Languages)
                {
                    if (string.Equals(l, code, StringComparison.OrdinalIgnoreCase)
                        || l.StartsWith(code.Split('-')[0] + "-", StringComparison.OrdinalIgnoreCase))
                    {
                        LangName = code;
                        return;
                    }
                }
            }
            LangName = "en-US";
        }
        catch
        {
            LangName = "en-US";
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavBrowse;
        ApplyLanguage();
        ShowEulaIfNeeded();
    }

    // ------------------------------------------------------------------
    // EULA：首次启动弹许可协议（MIT 原文），同意后记录，不同意则退出。
    // ------------------------------------------------------------------
    private void ShowEulaIfNeeded()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Catpaq");
            if (key?.GetValue("EulaAccepted") as string == "1")
                return;
        }
        catch { }
        _ = ShowEulaDialogAsync();
    }

    private async Task ShowEulaDialogAsync()
    {
        try
        {
            var sp = new StackPanel { Spacing = 10 };
            sp.Children.Add(new TextBlock
            {
                Text = T("eula.intro",
                    "This software is licensed under the MIT License. Please read the terms below and choose whether you agree:"),
                TextWrapping = TextWrapping.Wrap,
            });
            sp.Children.Add(new ScrollViewer
            {
                MaxHeight = 300,
                Content = new TextBlock
                {
                    Text = I18n.MitLicense,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
            });
            var dlg = new ContentDialog
            {
                Title = T("eula.title", "License Agreement"),
                Content = sp,
                PrimaryButtonText = T("eula.accept", "Agree"),
                SecondaryButtonText = T("eula.decline", "Decline"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = ContentFrame.XamlRoot,
            };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Catpaq");
                key?.SetValue("EulaAccepted", "1");
            }
            else
            {
                Application.Current.Exit();
            }
        }
        catch
        {
            // 无法正常展示条款时也不放行，直接退出，避免绕过协议
            Application.Current.Exit();
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || ContentFrame is null)
            return;
        var tag = item.Tag as string ?? "";
        switch (tag)
        {
            case "browse": ContentFrame.Navigate(typeof(BrowsePage)); break;
            case "archive": ContentFrame.Navigate(typeof(ArchivePage)); break;
            case "log": ContentFrame.Navigate(typeof(LogPage)); break;
            case "settings": ContentFrame.Navigate(typeof(SettingsPage)); break;
            case "resources": ContentFrame.Navigate(typeof(ResourcesPage)); break;
            case "about": ContentFrame.Navigate(typeof(AboutPage)); break;
        }
    }

    public void GoToPage(string tag)
    {
        foreach (var obj in NavView.MenuItems)
        {
            if (obj is NavigationViewItem it && (it.Tag as string) == tag)
            {
                NavView.SelectedItem = it;
                return;
            }
        }
    }

    // ------------------------------------------------------------------
    // 日志（对齐 main_frame.add_log / add_archive_log）
    // LogPage 未创建时先入缓冲，创建后一次性补显（防止日志丢失）
    // ------------------------------------------------------------------
    private readonly List<string> _logBuffer = new();
    private readonly List<string> _archiveLogBuffer = new();

    public void AddLog(string msg)
    {
        var t = DateTime.Now.ToString("HH:mm:ss");
        var line = $"{t} {msg}";
        DispatcherQueue.TryEnqueue(() =>
        {
            _logBuffer.Add(line);
            if (_logBuffer.Count > 1000) _logBuffer.RemoveAt(0);
            if (LogPage is not null)
                LogPage.AddSystemLog(line);
        });
    }

    public void AddArchiveLog(string msg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _archiveLogBuffer.Add(msg);
            if (_archiveLogBuffer.Count > 1000) _archiveLogBuffer.RemoveAt(0);
            if (LogPage is not null)
                LogPage.AddArchiveLog(msg);
        });
    }

    // 批量追加归档日志（大归档 list 输出可能数万行，逐条追加会卡死 UI）
    private void AddArchiveLogBatch(IList<string> lines)
    {
        if (lines.Count == 0) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            _archiveLogBuffer.AddRange(lines);
            if (_archiveLogBuffer.Count > 1000)
                _archiveLogBuffer.RemoveRange(0, _archiveLogBuffer.Count - 1000);
            LogPage?.AddArchiveLogBatch(lines);
        });
    }

    // 日志页每次创建（导航进入）后调用：以完整历史重建日志视图，保证日志一直可查看
    public void ReloadLogs()
    {
        if (LogPage is null) return;
        LogPage.ClearSystemLog();
        LogPage.ClearArchiveLog();
        LogPage.AddSystemLogBatch(_logBuffer);
        LogPage.AddArchiveLogBatch(_archiveLogBuffer);
    }

    // 清空系统日志（历史 + 页面显示），供测试/哈希等操作开始时清屏
    public void ClearSystemLog()
    {
        _logBuffer.Clear();
        LogPage?.ClearSystemLog();
    }

    // ------------------------------------------------------------------
    // Bridge 回调（对齐 main_frame.on_bridge_*）
    // ------------------------------------------------------------------
    private void OnBridgeLog(string line)
    {
        AddArchiveLog(line);
    }

    private long _lastProgressTick;

    private void OnBridgeProgress(int percent, string msg)
    {
        // 进度遥测可能非常频繁，节流到每 100ms 一次，避免淹没 UI 消息队列
        var now = Environment.TickCount64;
        if (now - _lastProgressTick < 100)
            return;
        _lastProgressTick = now;
        if (LogPage is not null)
        {
            LogPage.UpdateProgress(percent);
            if (BridgeOp == BridgeOp.Hash)
                LogPage.SetStatus($"{T("log.hashing", "Hashing:")} {percent}%");
            else if (BridgeOp == BridgeOp.Test)
                LogPage.SetStatus($"{T("log.testing", "Testing:")} {percent}%");
            else if (BridgeOp == BridgeOp.Extract)
                LogPage.SetStatus($"{T("log.extracting", "Extracting:")} {percent}%");
            else if (percent >= 0)
                LogPage.SetStatus($"{T("log.progress", "Progress:")} {percent}%");
        }
        if (BrowsePage is not null && LoadFromBrowseTab)
            BrowsePage.UpdateLoadingProgress(percent);
    }

    private void OnBridgeComplete(int exitCode)
    {
        AddArchiveLogBatch(Bridge.FlushLogBuffer());
        // Add 对话框操作完成：由 AddDialog.OnAddComplete 负责收尾，
        // 这里直接返回，避免误走归档列表（OnListComplete）流程。
        if (IsAddRunning)
        {
            BridgeOp = BridgeOp.List;
            return;
        }
        var op = BridgeOp;
        if (op == BridgeOp.List)
        {
            BridgeOp = BridgeOp.List;
            OnListComplete(exitCode == 0, exitCode);
            return;
        }
        if (op == BridgeOp.Hash)
        {
            AddLog(string.Format(T("log.op_complete", "Operation complete (exit code {0})"), exitCode));
            if (LogPage is not null)
            {
                LogPage.UpdateProgress(0);
                LogPage.SetStatus("");
            }
        }
        else if (op == BridgeOp.Test)
        {
            AddLog(string.Format(T("log.op_complete", "Operation complete (exit code {0})"), exitCode));
            if (LogPage is not null)
            {
                LogPage.UpdateProgress(0);
                LogPage.SetStatus("");
                LogPage.FinalizeTestLine(exitCode == 0
                    ? T("log.test_ok", "--- Test OK ---")
                    : string.Format(T("log.test_failed", "--- Test FAILED (exit code: {0}) ---"), exitCode));
            }
        }
        else if (op == BridgeOp.Extract)
        {
            AddLog(string.Format(T("log.op_complete", "Operation complete (exit code {0})"), exitCode));
            if (LogPage is not null)
            {
                LogPage.UpdateProgress(0);
                LogPage.SetStatus("");
            }
        }
        BridgeOp = BridgeOp.List;
    }

    // ------------------------------------------------------------------
    // 归档加载流程（对齐 main_frame.run_pakka_list / on_file_list_complete）
    // ------------------------------------------------------------------
    // 打开归档加载覆盖层：显示不确定进度滑块（进度未知），完成后隐藏
    private bool _loadingCancelled;
    private PakkaListStreamParser? _streamParser; // 流式解析器（zpaqfranz 边输出边解析）
    private long _listStartTick;                  // list 起始时间（阶段耗时日志）

    public void ShowLoadingOverlay()
    {
        _loadingCancelled = false;
        LblLoadingTitle.Text = T("log.opening", "Opening archive...");
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    public void HideLoadingOverlay()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    // 取消打开归档：杀掉 zpaqfranz 进程并结束加载流程
    private void BtnCancelLoading_Click(object sender, RoutedEventArgs e)
    {
        _loadingCancelled = true;
        Bridge.KillCommand();
        ArchivePage?.CancelPopulate();
        FinishLoading(clear: true);
    }

    public void LoadArchive(string fileName)
    {
        if (LoadingArchive)
            return;
        if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
        {
            AddLog("Archive not found: " + fileName);
            return;
        }
        ArchivePath = fileName;
        ArchiveType = ArchiveHelpers.DetectArchiveType(fileName);
        if (ArchiveType == ArchiveType.Unknown)
        {
            AddLog("Unknown or invalid file type: " + fileName);
            return;
        }
        AddArchiveLog("Opening: " + fileName);
        AddArchiveLog("Type: " + ArchiveHelpers.ArchiveTypeToStr(ArchiveType));

        LoadingArchive = true;
        ArchiveData = new ArchiveData();
        BridgeOp = BridgeOp.List;
        ShowLoadingOverlay();
        _listStartTick = Environment.TickCount64;

        var cmd = BuildCommandString();
        AddArchiveLog("Running pakka list (streaming)...");
        AddArchiveLog("Command: " + cmd);

        // 流式解析：zpaqfranz 的 stdout 数据行边输出边解析构建 ArchiveData
        _streamParser = new PakkaListStreamParser();
        Bridge.OnDataLine += Bridge_OnDataLine;

        Bridge.IsDataMode = true;
        if (LogPage is not null)
            LogPage.ShowAbortToolbar(true);
        var ok = Bridge.RunCommandAsync(cmd);
        if (!ok)
        {
            Bridge.OnDataLine -= Bridge_OnDataLine;
            _streamParser = null;
            AddArchiveLog("ERROR: could not start list operation.");
            FinishLoading(clear: true);
        }
    }

    // 后台线程回调：把 zpaqfranz 输出的数据行喂给流式解析器
    private void Bridge_OnDataLine(string line) => _streamParser?.FeedLine(line);

    public void OnListComplete(bool success, int exitCode)
    {
        Bridge.OnDataLine -= Bridge_OnDataLine;
        AddArchiveLogBatch(Bridge.FlushDataBuffer());

        var listSeconds = (Environment.TickCount64 - _listStartTick) / 1000.0;
        AddArchiveLog($"List took {listSeconds:0.0}s (exit code {exitCode})");

        if (_loadingCancelled)
        {
            _streamParser = null;
            FinishLoading(clear: true);
            return;
        }
        if (!success || exitCode != 0)
        {
            _streamParser = null;
            AddArchiveLog("ERROR: listing archive failed.");
            FinishLoading(clear: true);
            return;
        }

        var data = _streamParser?.Result ?? new ArchiveData();
        _streamParser = null;
        ArchiveData = data;

        if (data.Files.Count == 0)
        {
            AddArchiveLog("ERROR: no files found in archive.");
            FinishLoading(clear: true);
            return;
        }

        AddArchiveLog($"Found {data.GlobalVersions.Count} versions, {data.Files.Count} files");
        ArchivePage?.Populate(data);
        AddArchiveLog($"{data.Files.Count} files loaded");
        FinishLoading(clear: false);

        if (BrowsePage is not null && ArchiveBrowsePath != "" && ArchiveData.Files.Count > 0)
            BrowsePage.ShowArchiveBrowse(ArchiveBrowsePath, ArchiveData);
        else
        {
            ArchiveBrowsePath = "";
            GoToPage("archive");
        }
    }

    public void FinishLoading(bool clear)
    {
        LoadingArchive = false;
        LoadFromBrowseTab = false;
        HideLoadingOverlay();
        ArchivePage?.FinishLoading(clear);
        if (LogPage is not null)
        {
            LogPage.UpdateProgress(0);
            LogPage.ShowAbortToolbar(false);
        }
    }

    public string BuildCommandString()
    {
        var cmd = "pakka \"" + ArchivePath + "\" -catpaqmode";
        if (ArchiveBrowsePath == "")
            cmd += " -all";
        if (PasswordKey != "")
            cmd += " -key \"" + PasswordKey + "\"";
        if (PasswordFranzen != "")
            cmd += " -franzen \"" + PasswordFranzen + "\"";
        return cmd;
    }

    // ------------------------------------------------------------------
    // 测试 / 哈希（对齐 main_frame.run_test_archive / run_hash）
    // ------------------------------------------------------------------
    public void RunTestArchive(string archivePath, bool allVersions = false)
    {
        if (string.IsNullOrEmpty(archivePath))
            return;
        GoToPage("log");
        AddLog("Testing archive: " + archivePath);
        var cmd = "t \"" + archivePath + "\" -catpaqmode";
        if (allVersions)
            cmd += " -all";
        if (PasswordKey != "")
            cmd += " -key \"" + PasswordKey + "\"";
        if (PasswordFranzen != "")
            cmd += " -franzen \"" + PasswordFranzen + "\"";
        AddLog("Command: zpaqfranz " + cmd);
        ClearSystemLog();
        BridgeOp = BridgeOp.Test;
        Bridge.IsDataMode = false;
        if (LogPage is not null)
            LogPage.ShowAbortToolbar(true);
        if (!Bridge.RunCommandAsync(cmd))
        {
            AddLog("ERROR: could not start test operation.");
            BridgeOp = BridgeOp.List;
            if (LogPage is not null)
            {
                LogPage.UpdateProgress(0);
                LogPage.ShowAbortToolbar(false);
            }
        }
    }

    public void RunHash(string filesStr, string algo, bool ssd, int fileCount)
    {
        var cmd = $"hash {filesStr} -catpaqmode -terse {algo}";
        if (ssd)
            cmd += " -ssd";
        GoToPage("log");
        ClearSystemLog();
        AddLog("Starting hash computation...");
        AddLog("Algorithm: " + algo);
        AddLog("Command: zpaqfranz " + cmd);
        BridgeOp = BridgeOp.Hash;
        HashFileCount = fileCount;
        Bridge.IsDataMode = false;
        if (LogPage is not null)
            LogPage.ShowAbortToolbar(true);
        if (!Bridge.RunCommandAsync(cmd))
        {
            AddLog("ERROR: could not start hash computation.");
            BridgeOp = BridgeOp.List;
            if (LogPage is not null)
            {
                LogPage.UpdateProgress(0);
                LogPage.ShowAbortToolbar(false);
            }
        }
    }

    public void AbortOperation()
    {
        if (Bridge.Busy)
        {
            Bridge.KillCommand();
            AddLog(T("log.killed", "zpaqfranz killed by user"));
        }
        else
            AddLog(T("log.no_proc", "No running zpaqfranz process to abort."));
        if (LogPage is not null)
            LogPage.ShowAbortToolbar(false);
    }

    // ------------------------------------------------------------------
    // Extract / Add 对话框
    // ------------------------------------------------------------------
    public void ShowExtractDialogForPaths(string archivePath, List<string> fileNames)
    {
        var dlg = new Dialogs.ExtractDialog(archivePath, fileNames);
        dlg.XamlRoot = ContentFrame.XamlRoot;
        _ = dlg.ShowAsync();
    }

    public void ShowExtractAllDialog(string archivePath)
    {
        var dlg = new Dialogs.ExtractDialog(archivePath, new List<string>());
        dlg.XamlRoot = ContentFrame.XamlRoot;
        _ = dlg.ShowAsync();
    }

    public void ShowAddDialog(List<string> files)
    {
        var dlg = new Dialogs.AddDialog(files);
        dlg.XamlRoot = ContentFrame.XamlRoot;
        _ = dlg.ShowAsync();
    }
}
