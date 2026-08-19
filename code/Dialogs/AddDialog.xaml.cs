// Add 对话框：向导式交互（参考 wx 版 ui/simply_dialog.py / ufrmsimply.pas）。
//
// 交互流程（经用户确认）：
//   默认显示「精简页」：归档名 + 格式/压缩/哈希/线程 + 密码 + 6 个常用开关。
//   点「高级选项...」进入向导：标准(扩展项) → 高级 → 选择 → 其他，
//   每步「下一步」推进，最后一步「确定」执行；向导中可随时「← 返回精简」。
//   密码严格校验：按格式只显示对应密码栏，两次输入必须一致且非空。
//   不记忆设置。
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Catpaq.Core;

namespace Catpaq.Dialogs;

public sealed partial class AddDialog : ContentDialog
{
    private readonly List<string> _files;
    private readonly MainWindow Main = App.MainWindow;
    private bool _running;
    private bool _loaded;
    private bool _wizardMode;   // false=精简模式，true=向导模式
    private int _step;          // 0=标准 1=高级 2=选择 3=其他
    private bool _abortRequested;   // 已请求中止：主按钮 = 继续（重新压缩）
    private bool _completed;        // 操作已正常完成：主按钮 = Close

    private static readonly string[] StepPanels = { "StepAdvanced", "StepSelection", "StepOther" };

    public AddDialog(List<string> files)
    {
        InitializeComponent();
        _files = files;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        EdtFiles.Text = string.Join("\n", _files.Select(Path.GetFileName));
        var archive = Main.ArchivePath;
        if (archive == "" && _files.Count > 0)
        {
            var dir = Path.GetDirectoryName(_files[0]) ?? "";
            archive = Path.Combine(dir, "archive.zpaq");
        }
        EdtArchive.Text = archive;
        EdtExternal.Text = Main.Bridge.ExternalPath;
        ApplyLanguage();
        UpdatePasswordVisibility();
        SetSimpleMode();
    }

    // ------------------------------------------------------------------
    // 模式切换
    // ------------------------------------------------------------------
    // 精简页与向导基础步共用 StandardPanel：
    //   精简模式隐藏扩展行（多分片/分块/块大小/分片/To/Find/Replace）
    //   向导模式展开全部扩展行，构成完整「基础」步
    private void SetSimpleMode()
    {
        _wizardMode = false;
        WizardContent.Visibility = Visibility.Collapsed;
        LblSimpleTitle.Visibility = Visibility.Visible;
        RowMultipart.Visibility = Visibility.Collapsed;
        RowBlocksize.Visibility = Visibility.Collapsed;
        RowTo.Visibility = Visibility.Collapsed;
        RowFind.Visibility = Visibility.Collapsed;
        RowReplace.Visibility = Visibility.Collapsed;
        BtnAdvanced.Visibility = Visibility.Visible;
        BtnBackToSimple.Visibility = Visibility.Collapsed;
        StepIndicator.Visibility = Visibility.Collapsed;
        StandardPanel.Visibility = Visibility.Visible;
        PrimaryButtonText = Main.T("btn.ok", "OK");
        SecondaryButtonText = Main.T("btn.cancel", "Cancel");
        Title = Main.T("add.title", "Add to ZPAQ");
    }

    private void EnterWizard()
    {
        _wizardMode = true;
        _step = 0;
        WizardContent.Visibility = Visibility.Visible;
        LblSimpleTitle.Visibility = Visibility.Collapsed;
        RowMultipart.Visibility = Visibility.Visible;
        RowBlocksize.Visibility = Visibility.Visible;
        RowTo.Visibility = Visibility.Visible;
        RowFind.Visibility = Visibility.Visible;
        RowReplace.Visibility = Visibility.Visible;
        BtnAdvanced.Visibility = Visibility.Collapsed;
        BtnBackToSimple.Visibility = Visibility.Visible;
        StepIndicator.Visibility = Visibility.Visible;
        UpdateStep();
    }

    private void UpdateStep()
    {
        // 基础步（压缩）= StandardPanel（含扩展行），仅在 _step==0 显示；
        // 其余步骤只显示当前步骤面板，翻页后隐藏上一步内容。
        StandardPanel.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var name in StepPanels)
        {
            if (FindName(name) is UIElement panel)
                panel.Visibility = Visibility.Collapsed;
        }
        if (_step > 0 && FindName(StepPanels[_step - 1]) is UIElement current)
            current.Visibility = Visibility.Visible;

        if (!_running && !_abortRequested && !_completed)
        {
            PrimaryButtonText = _step < 3 ? Main.T("add.next", "Next") : Main.T("btn.ok", "OK");
            SecondaryButtonText = Main.T("btn.cancel", "Cancel");
            Title = Main.T("add.title", "Add to ZPAQ") + $" ({_step + 1}/4)";
        }

        // 四个步骤按钮高亮当前步（真彩色），其余灰色
        BtnStep1.Foreground = _step == 0 ? ActiveBrush() : GrayBrush();
        BtnStep2.Foreground = _step == 1 ? ActiveBrush() : GrayBrush();
        BtnStep3.Foreground = _step == 2 ? ActiveBrush() : GrayBrush();
        BtnStep4.Foreground = _step == 3 ? ActiveBrush() : GrayBrush();
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush ActiveBrush()
        => new(Microsoft.UI.Colors.DeepSkyBlue);
    private static Microsoft.UI.Xaml.Media.SolidColorBrush GrayBrush()
        => new(Microsoft.UI.Colors.Gray);

    private void BtnAdvanced_Click(object sender, RoutedEventArgs e) => EnterWizard();
    private void BtnBackToSimple_Click(object sender, RoutedEventArgs e) => SetSimpleMode();

    // 步骤按钮：点哪个跳哪步
    private void BtnStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s && int.TryParse(s, out var step))
        {
            _step = Math.Clamp(step, 0, 3);
            UpdateStep();
        }
    }

    private void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Cancel：运行中 = 中止当前操作并保持打开；空闲/完成后直接关闭
        if (_running)
        {
            args.Cancel = true;
            _abortRequested = true;
            Main.Bridge.KillCommand();
            LblLog.Text = Main.T("add.abort_req", "ABORT requested...");
            SetInputsEnabled(true);
            PrimaryButtonText = Main.T("add.continue", "Continue");
            _running = false;
        }
    }

    // 运行中禁用全部输入控件；空闲（编辑/中止后/完成）恢复。StepLog 保持可用供查看。
    // WinUI 3 中 IsEnabled 只在 Control 上，故遍历面板子树逐个禁用。
    private void SetInputsEnabled(bool enabled)
    {
        SetPanelControlsEnabled(TopPanel, enabled);
        SetPanelControlsEnabled(StandardPanel, enabled);
        foreach (var name in StepPanels)
            if (FindName(name) is UIElement p)
                SetPanelControlsEnabled(p, enabled);
        BtnAdvanced.IsEnabled = enabled;
        BtnBackToSimple.IsEnabled = enabled;
        BtnStep1.IsEnabled = enabled;
        BtnStep2.IsEnabled = enabled;
        BtnStep3.IsEnabled = enabled;
        BtnStep4.IsEnabled = enabled;
    }

    private static void SetPanelControlsEnabled(DependencyObject root, bool enabled)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Control c)
                c.IsEnabled = enabled;
            SetPanelControlsEnabled(child, enabled);
        }
    }

    // ------------------------------------------------------------------
    // 语言
    // ------------------------------------------------------------------
    public void ApplyLanguage()
    {
        Title = Main.T("add.title", "Add to ZPAQ");
        LblFilesHeader.Text = Main.T("add.files_to_add", "Files to add:");
        LblArchive.Text = Main.T("add.archive", "Archive:");
        LblSimpleTitle.Text = Main.T("add.quick", "Quick add");
        LblFmt.Text = Main.T("add.archive_format", "Archive format:");
        LblCompression.Text = Main.T("add.compression", "Compression:");
        LblHash.Text = Main.T("add.hash", "Hash:");
        LblThreads.Text = Main.T("add.threads", "Threads:");
        LblAes.Text = Main.T("add.aes", "AES Password:");
        LblAes2.Text = Main.T("add.confirm", "Confirm:");
        LblFranzen.Text = Main.T("add.franzen", "Franzen Password:");
        LblFranzen2.Text = Main.T("add.confirm", "Confirm:");
        LblSimpleSwitches.Text = Main.T("add.common_switches", "Common switches");
        LblMultipart.Text = Main.T("add.multipart", "Multipart:");
        LblChunked.Text = Main.T("add.chunked", "Chunked:");
        LblBlocksize.Text = Main.T("add.blocksize", "Block size:");
        LblFragment.Text = Main.T("add.fragment", "Fragment:");
        LblTo.Text = Main.T("add.to", "To:");
        LblFind.Text = Main.T("add.find", "Find:");
        LblReplace.Text = Main.T("add.replace", "Replace:");
        LblMinSize.Text = Main.T("add.minsize", "Minsize:");
        LblMaxSize.Text = Main.T("add.maxsize", "Maxsize:");
        LblComment.Text = Main.T("add.comment", "Comment:");
        LblTimeStamp.Text = Main.T("add.timestamp", "Timestamp:");
        LblDateFrom.Text = Main.T("add.datefrom", "Date from:");
        LblDateTo.Text = Main.T("add.dateto", "Date to:");
        LblAdvCheck.Text = Main.T("add.switches", "Switches");
        LblNot.Text = Main.T("add.not", "NOT:");
        LblOnly.Text = Main.T("add.only", "ONLY:");
        LblAlways.Text = Main.T("add.always", "ALWAYS:");
        LblDrive.Text = Main.T("add.drive", "Drive to image:");
        LblExternal.Text = Main.T("add.external_path", "External zpaqfranz:");
        LblTabLog.Text = Main.T("add.log", "Log");
        BtnAdvanced.Content = Main.T("add.advanced_btn", "Advanced options...");
        BtnBackToSimple.Content = Main.T("add.back_simple", "Simple");
        BtnStep1.Content = Main.T("add.step_compress", "Compress");
        BtnStep2.Content = Main.T("add.step_comment", "Comment");
        BtnStep3.Content = Main.T("add.step_match", "Match");
        BtnStep4.Content = Main.T("add.step_deploy", "Deploy");
        PrimaryButtonText = _running ? Main.T("btn.abort", "ABORT")
            : _abortRequested ? Main.T("add.continue", "Continue")
            : _completed ? Main.T("btn.close", "Close")
            : _wizardMode && _step < 3 ? Main.T("add.next", "Next")
            : Main.T("btn.ok", "OK");
        SecondaryButtonText = Main.T("btn.cancel", "Cancel");

        // 开关复选框（精简页 + 向导各页共用/分别），中英标签
        ChkForce.Content = Main.T("add.force", "Force") + " (-force)";
        ChkStdout.Content = Main.T("add.stdout", "Stdout-compatible") + " (-stdout)";
        ChkStore.Content = Main.T("add.store", "Store only") + " (-store)";
        ChkNoChecksum.Content = Main.T("add.nochecksum", "No checksum") + " (-nochecksum)";
        ChkLongPath.Content = Main.T("add.longpath", "Longpath (>255)") + " (-longpath)";
        ChkVss.Content = Main.T("add.use_vss", "Use VSS") + " (-vss)";
        ChkVerbose.Content = Main.T("add.verbose", "Verbose") + " (-verbose)";
        ChkIgnoreSpace.Content = Main.T("add.ignore_space", "Ignore free space") + " (-space)";
        ChkForceOffice.Content = Main.T("add.force_office", "Force old Office") + " (-xls)";
        ChkNoZfs.Content = Main.T("add.no_zfs", "No zfs") + " (-nozfs)";
        ChkForceWindows.Content = Main.T("add.force_windows", "Force Windows") + " (-forcewindows)";
        Chk715.Content = Main.T("add.715", "zpaq 7.15 mode") + " (-715)";
        ChkHome.Content = Main.T("add.home", "1-level home") + " (-home)";
        ChkNoArchiveAttr.Content = Main.T("add.noarchiveattr", "No archive attribute") + " (-noarchiveattr)";
        ChkCrc32.Content = Main.T("add.crc32", "CRC-32 verify") + " (-crc32)";
        ChkRecover.Content = Main.T("add.recover", "Recover") + " (-recover)";
        ChkQuiet.Content = Main.T("add.quiet", "Quiet") + " (-quiet)";
        ChkStats.Content = Main.T("add.stats", "Stats") + " (-stats)";
        ChkNoPrefix.Content = Main.T("add.noprefix", "No prefix") + " (-noprefix)";
        ChkUtf8.Content = Main.T("add.utf8", "UTF-8") + " (-utf8)";
        ChkUtc.Content = Main.T("add.utc", "Force utc time") + " (-utc)";
        ChkTouch.Content = Main.T("add.touch", "Force touch") + " (-touch)";
        ChkKill.Content = Main.T("add.kill", "Kill") + " (-kill)";
        ChkZero.Content = Main.T("add.zero", "Zero") + " (-zero)";
        ChkCollision.Content = Main.T("add.collision", "Collision") + " (-collision)";
        ChkSfx.Content = Main.T("add.sfx", "Create self-extracting archive (SFX)") + " (-sfx)";
        ChkBackup.Content = Main.T("add.backup", "Backup mode (append a new version)") + " (-backup)";

        // 选择页占位符
        EdtNot.PlaceholderText = "*.tmp *.bak";
        EdtOnly.PlaceholderText = "*.jpg *.png";
        EdtAlways.PlaceholderText = "*.log *.txt";
    }

    private void CmbFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        UpdatePasswordVisibility();
    }

    private void UpdatePasswordVisibility()
    {
        var fmt = CmbFormat.SelectedIndex;
        // 0=Normal 1=AES 2=Franzen 3=AES+Franzen
        AesRow.Visibility = fmt is 1 or 3 ? Visibility.Visible : Visibility.Collapsed;
        FranzenRow.Visibility = fmt is 2 or 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BtnBrowseArc_Click(object sender, RoutedEventArgs e)
    {
        // 创建 zpaq 时这里是"选取保存位置"：用保存对话框，而不是打开对话框
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        // 注意：扩展名必须是单段（如 ".zpaq"），多段（如 ".zpaq.franzen"）会抛异常
        picker.FileTypeChoices.Add("ZPAQ archive", new System.Collections.Generic.List<string> { ".zpaq" });
        picker.SuggestedFileName = EdtArchive.Text.Trim() is { Length: > 0 } name
            ? System.IO.Path.GetFileName(name)
            : "archive";
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Main.Hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
            EdtArchive.Text = file.Path;
    }

    private async void BtnBrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Main.Hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            EdtExternal.Text = file.Path;
    }

    // ------------------------------------------------------------------
    // 命令构建（对齐 wx 版 build_command_line）
    // ------------------------------------------------------------------
    private string CmbText(ComboBox cmb)
    {
        var sel = cmb.SelectedItem;
        return sel is ComboBoxItem it && it.Content is string s ? s : "";
    }

    private static string FirstToken(string s)
    {
        var sp = s.IndexOf(' ');
        return sp > 0 ? s[..sp] : s;
    }

    private static string ExtractDigits(string s)
        => new(s.Where(char.IsDigit).ToArray());

    private static string ExtractTimestamp(string s)
    {
        var d = ExtractDigits(s);
        if (d.Length < 8) return "";
        var out_ = $"{d[4..8]}-{d[2..4]}-{d[0..2]}";
        if (d.Length >= 14)
            out_ += $" {d[8..10]}:{d[10..12]}:{d[12..14]}";
        return out_;
    }

    private static string ExtractDateField(string s)
    {
        var d = ExtractDigits(s);
        if (d.Length is not (4 or 6 or 8)) return "";
        if (int.TryParse(d[..4], out var y) && y >= 1900)
            return d;
        return "";
    }

    private string BuildArchiveName()
    {
        var raw = EdtArchive.Text.Trim();
        if (raw == "") raw = "archive";
        var baseName = raw;
        foreach (var suf in new[] { ".zpaq.franzen", ".zpaq" })
        {
            if (baseName.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^suf.Length];
                break;
            }
        }
        foreach (var suf in new[] { "_????????", "_????", "_???", "_??", "_?" })
        {
            if (baseName.EndsWith(suf, StringComparison.Ordinal))
            {
                baseName = baseName[..^suf.Length];
                break;
            }
        }
        var multiSuffix = "";
        var multiText = CmbText(CmbMultipart);
        if (multiText != "" && !multiText.StartsWith("(NO"))
            multiSuffix = FirstToken(multiText);
        var fmt = CmbFormat.SelectedIndex;
        var ext = fmt is 2 or 3 ? ".zpaq.franzen" : ".zpaq";
        return baseName + multiSuffix + ext;
    }

    private string BuildCommandLine()
    {
        var chunkText = CmbText(CmbChunked);
        if (chunkText != "" && !chunkText.StartsWith("NO") && CmbText(CmbMultipart).StartsWith("(NO"))
            CmbMultipart.SelectedIndex = 5;

        var archiveName = BuildArchiveName();
        var cmd = $"a \"{archiveName}\"";
        foreach (var f in _files)
            cmd += $" \"{f}\"";

        var compIdx = CmbCompression.SelectedIndex < 0 ? 1 : CmbCompression.SelectedIndex;
        var blockText = CmbText(CmbBlocksize);
        cmd += blockText != "" && !blockText.StartsWith("DEFAULT")
            ? $" -m{compIdx}{FirstToken(blockText)}"
            : $" -m{compIdx}";

        var fmt = CmbFormat.SelectedIndex;
        if (fmt is 1 or 3 && EdtAes1.Password != "")
            cmd += $" -key \"{EdtAes1.Password}\"";
        if (fmt is 2 or 3 && EdtFranzen1.Password != "")
            cmd += $" -franzen \"{EdtFranzen1.Password}\"";

        var hashText = CmbText(CmbHash);
        if (!hashText.StartsWith("(NONE"))
        {
            var flag = hashText switch
            {
                _ when hashText.StartsWith("XXHASH") => "-xxhash",
                _ when hashText.StartsWith("MD5") => "-md5",
                _ when hashText.StartsWith("SHA1") => "-sha1",
                _ when hashText.StartsWith("SHA256") => "-sha256",
                _ when hashText.StartsWith("SHA3") => "-sha3",
                _ when hashText.StartsWith("XXH3") => "-xxh3",
                _ when hashText.StartsWith("BLAKE3") => "-blake3",
                _ when hashText.StartsWith("WHIRLPOOL") => "-whirlpool",
                _ when hashText.StartsWith("HIGHWAY64") => "-highway64",
                _ when hashText.StartsWith("HIGHWAY128") => "-highway128",
                _ when hashText.StartsWith("HIGHWAY256") => "-highway256",
                _ => "",
            };
            if (flag != "")
                cmd += " " + flag;
        }

        if (chunkText != "" && !chunkText.StartsWith("NO"))
            cmd += " -chunk " + FirstToken(chunkText);

        var tText = CmbText(CmbThreads);
        if (tText != "" && !tText.StartsWith("MAX"))
            cmd += " -t" + tText;

        var fragText = CmbText(CmbFragment);
        if (fragText != "" && !fragText.StartsWith("DEFAULT"))
            cmd += " -fragment " + FirstToken(fragText);

        var ts = ExtractTimestamp(EdtTimeStamp.Text);
        if (ts != "")
            cmd += " -timestamp " + ts;

        var minText = CmbText(CmbMinSize);
        if (minText != "" && !minText.StartsWith("(NO"))
            cmd += " -minsize " + minText;
        var maxText = CmbText(CmbMaxSize);
        if (maxText != "" && !maxText.StartsWith("(NO"))
            cmd += " -maxsize " + maxText;

        var df = ExtractDateField(EdtDateFrom.Text);
        if (df != "") cmd += " -datefrom " + df;
        var dt = ExtractDateField(EdtDateTo.Text);
        if (dt != "") cmd += " -dateto " + dt;

        if (EdtTo.Text.Trim() != "")
            cmd += $" -to \"{EdtTo.Text.Trim()}\"";
        if (EdtFind.Text.Trim() != "")
            cmd += $" -find \"{EdtFind.Text.Trim()}\"";
        if (EdtReplace.Text.Trim() != "")
            cmd += $" -replace \"{EdtReplace.Text.Trim()}\"";
        if (EdtComment.Text.Trim() != "")
            cmd += $" -comment \"{EdtComment.Text.Trim()}\"";

        foreach (var (flag, text) in new[]
                 { ("-not", EdtNot.Text), ("-only", EdtOnly.Text), ("-always", EdtAlways.Text) })
        {
            var t = text.Trim();
            if (t == "" || t.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var tok in t.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                cmd += $" {flag} \"{tok}\"";
        }

        cmd += BuildSwitches();
        cmd += " -catpaqmode";
        return cmd;
    }

    private string BuildSwitches()
    {
        var s = "";
        void Chk(CheckBox c, string flag)
        {
            if (c.IsChecked == true) s += " " + flag;
        }
        Chk(ChkForce, "-force");
        Chk(ChkStdout, "-stdout");
        Chk(ChkStore, "-store");
        Chk(ChkNoChecksum, "-nochecksum");
        Chk(ChkLongPath, "-longpath");
        Chk(ChkVss, "-vss");
        Chk(ChkVerbose, "-verbose");
        Chk(ChkIgnoreSpace, "-space");
        Chk(ChkForceOffice, "-xls");
        Chk(ChkNoZfs, "-nozfs");
        Chk(ChkForceWindows, "-forcewindows");
        Chk(Chk715, "-715");
        Chk(ChkHome, "-home");
        Chk(ChkNoArchiveAttr, "-noarchiveattr");
        Chk(ChkCrc32, "-crc32");
        Chk(ChkRecover, "-recover");
        Chk(ChkQuiet, "-quiet");
        Chk(ChkStats, "-stats");
        Chk(ChkNoPrefix, "-noprefix");
        Chk(ChkUtf8, "-utf8");
        Chk(ChkUtc, "-utc");
        Chk(ChkTouch, "-touch");
        Chk(ChkKill, "-kill");
        Chk(ChkZero, "-zero");
        Chk(ChkCollision, "-collision");
        Chk(ChkSfx, "-sfx");
        Chk(ChkBackup, "-backup");
        return s;
    }

    // ------------------------------------------------------------------
    // 校验 + 执行
    // ------------------------------------------------------------------
    private string? Validate()
    {
        if (EdtArchive.Text.Trim() == "")
            return Main.T("add.enter_archive", "Please enter an archive filename");
        if (_files.Count == 0)
            return Main.T("add.no_files", "No files to add");
        var fmt = CmbFormat.SelectedIndex;
        if (fmt is 1 or 3)
        {
            // 密码可留空（= 不加密），非空时必须两次输入一致
            if (EdtAes1.Password != "" && EdtAes1.Password != EdtAes2.Password)
                return Main.T("add.aes_mismatch", "AES passwords do not match");
        }
        if (fmt is 2 or 3)
        {
            if (EdtFranzen1.Password != "" && EdtFranzen1.Password != EdtFranzen2.Password)
                return Main.T("add.franzen_mismatch", "Franzen passwords do not match");
        }
        return null;
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 1. 运行中：主按钮 = ABORT → 中止请求，按钮变「继续」
        if (_running)
        {
            args.Cancel = true;
            _abortRequested = true;
            Main.Bridge.KillCommand();
            LblLog.Text = Main.T("add.abort_req", "ABORT requested...");
            SetInputsEnabled(true);
            PrimaryButtonText = Main.T("add.continue", "Continue");
            _running = false;
            return;
        }

        // 2. 中止后：主按钮 = 继续 → 重新压缩（同一命令）
        if (_abortRequested)
        {
            args.Cancel = true;
            var err = Validate();
            if (err is not null)
            {
                LblLog.Text = err;
                return;
            }
            _abortRequested = false;
            StartOperation();
            return;
        }

        // 3. 已完成：主按钮 = Close → 不 cancel，让对话框关闭
        if (_completed)
            return;

        // 4. 向导模式：未到最后一步 = 下一步
        if (_wizardMode && _step < 3)
        {
            args.Cancel = true;
            _step++;
            UpdateStep();
            return;
        }

        // 5. 校验并启动
        var err2 = Validate();
        if (err2 is not null)
        {
            args.Cancel = true;
            LblLog.Text = err2;
            return;
        }
        args.Cancel = true; // 保持对话框打开，运行完成后手动关闭
        StartOperation();
    }

    // 启动压缩操作：禁用编辑、订阅事件、运行命令
    private void StartOperation()
    {
        _running = true;
        _completed = false;
        SetInputsEnabled(false);
        ProgressPanel.Visibility = Visibility.Visible;
        PrimaryButtonText = Main.T("btn.abort", "ABORT");
        WizardContent.Visibility = Visibility.Visible;
        foreach (var name in StepPanels)
            if (FindName(name) is UIElement p)
                p.Visibility = Visibility.Collapsed;
        StepLog.Visibility = Visibility.Visible;
        LblSimpleTitle.Visibility = Visibility.Collapsed;

        var cmd = BuildCommandLine();
        MemLog.Text += $"\n=== Command ===\nzpaqfranz {cmd}\n\n";
        Main.AddLog("Command: zpaqfranz " + cmd);
        Main.BridgeOp = BridgeOp.List;
        Main.IsAddRunning = true;
        Main.Bridge.IsDataMode = false;
        Main.Bridge.OnProgress += OnAddProgress;
        Main.Bridge.OnComplete += OnAddComplete;
        Main.Bridge.OnLog += OnAddLog;
        LblLog.Text = Main.T("add.starting", "Starting...");
        if (!Main.Bridge.RunCommandAsync(cmd))
        {
            LblLog.Text = Main.T("add.err_start", "ERROR: could not start add operation.");
            ResetForNextRun();
        }
    }

    private void OnAddLog(string line)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (MemLog.Text.Length > 200000)
                MemLog.Text = MemLog.Text[^100000..];
            MemLog.Text += line + "\n";
            MemLog.SelectionStart = MemLog.Text.Length;
        });
    }

    private void OnAddProgress(int percent, string msg)
    {
        // 命令已结束（_running=false）后迟到的遥测回调直接忽略，
        // 防止它把完成态的 100% 覆盖回中途值（快速压缩时 Exited 可能先于最后一行输出到达）
        if (!_running) return;
        ProgressBar.Value = Math.Clamp(percent, 0, 100);
        LblPct.Text = $"{percent}%";
    }

    private void OnAddComplete(int exitCode)
    {
        Main.Bridge.OnProgress -= OnAddProgress;
        Main.Bridge.OnComplete -= OnAddComplete;
        Main.Bridge.OnLog -= OnAddLog;
        Main.BridgeOp = BridgeOp.List;
        Main.IsAddRunning = false;
        DispatcherQueue.TryEnqueue(() =>
        {
            _running = false;
            // 成功时强制进度条走满 100%，保持显示直到点 Close；
            // 先设 100% 再执行其他 UI 更新，确保不被后续异常/迟到回调干扰
            if (!_abortRequested && exitCode == 0)
            {
                ProgressBar.Value = 100;
                LblPct.Text = "100%";
            }
            SetInputsEnabled(true);
            MemLog.Text += $"\n--- Exit code: {exitCode} ---\n";
            if (_abortRequested)
            {
                // 用户中止：主按钮保持「继续」（重新压缩），可改设置后重试或 Cancel 关闭
                LblLog.Text = Main.T("add.aborted", "Add ABORTED.");
                PrimaryButtonText = Main.T("add.continue", "Continue");
            }
            else
            {
                // 正常完成/失败
                _completed = true;
                LblLog.Text = exitCode == 0
                    ? Main.T("add.complete", "Add complete.")
                    : string.Format(Main.T("add.failed", "Add FAILED (exit code {0})."), exitCode);
                PrimaryButtonText = Main.T("btn.close", "Close");
            }
        });
    }

    private void ResetForNextRun()
    {
        _running = false;
        _abortRequested = false;
        _completed = false;
        Main.IsAddRunning = false;
        SetInputsEnabled(true);
        ProgressPanel.Visibility = Visibility.Collapsed;
        PrimaryButtonText = _wizardMode && _step < 3 ? Main.T("add.next", "Next") : Main.T("btn.ok", "OK");
    }
}
