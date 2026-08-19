// Extract 对话框：对齐 ui/extract_dialog.py（ufrmextract.pas）。
// 支持整档解压（fileNames 为空）或选中文件解压；运行中 ABORT。
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Catpaq.Core;

namespace Catpaq.Dialogs;

public sealed partial class ExtractDialog : ContentDialog
{
    private readonly string _archivePath;
    private readonly List<string> _fileNames;
    private readonly MainWindow Main = App.MainWindow;
    private bool _running;
    private bool _completed;   // 解压已结束（成功或失败）：此时点"关闭"应真正关闭对话框
    private bool _loaded;

    public ExtractDialog(string archivePath, List<string> fileNames)
    {
        InitializeComponent();
        _archivePath = archivePath;
        _fileNames = fileNames;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        EdtArchive.Text = _archivePath;
        EdtDest.Text = Path.Combine(Path.GetDirectoryName(_archivePath) ?? "", "extracted");
        ApplyLanguage();
    }

    public void ApplyLanguage()
    {
        Title = Main.T("ext.title", "Extract");
        LblArchive.Text = Main.T("ext.archive", "Archive:");
        LblDest.Text = Main.T("ext.dest", "Destination folder:");
        if (_fileNames.Count == 0)
            LblFiles.Text = Main.T("ext.all", "Extract everything");
        else if (_fileNames.Count == 1)
            LblFiles.Text = Main.T("ext.extract", "Extract") + ": " + _fileNames[0];
        else
            LblFiles.Text = string.Format(Main.T("ext.items", "Extract {0} items"), _fileNames.Count);
        PrimaryButtonText = _running ? Main.T("btn.abort", "ABORT") : Main.T("btn.ok", "OK");
        SecondaryButtonText = Main.T("btn.cancel", "Cancel");
    }

    private async void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Main.Hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            EdtDest.Text = folder.Path;
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_running)
        {
            // 运行中：点击 = ABORT
            args.Cancel = true;
            Main.Bridge.KillCommand();
            LblLog.Text = Main.T("ext.abort_req", "ABORT requested...");
            _running = false;
            return;
        }
        if (_completed)
        {
            // 解压已结束：点击 = 关闭对话框（不再重复解压）
            args.Cancel = false;
            return;
        }
        var dest = EdtDest.Text.Trim();
        if (dest == "")
        {
            args.Cancel = true;
            return;
        }
        args.Cancel = true; // 保持对话框打开，运行完成后手动关闭

        _completed = false;
        ProgressPanel.Visibility = Visibility.Visible;
        PrimaryButtonText = Main.T("btn.abort", "ABORT");
        _running = true;

        // zpaqfranz（C++ 原版）x 命令语义（实测）：
        //   - pattern 必须匹配归档内完整路径（相对路径/basename 匹配不到 → 0 个文件）
        //   - -to 必须带尾斜杠：无尾斜杠时单匹配文件会被"重命名"成该文件（解压出来是"文件"）
        //   - 多个 pattern 只有 -only 语法可靠（多裸 pattern 提取 0 个文件；每文件单独 -to 只取第一个）
        var cmd = "x \"" + _archivePath + "\"";
        if (_fileNames.Count == 1)
        {
            cmd += " \"" + _fileNames[0] + "\"";
        }
        else if (_fileNames.Count > 1)
        {
            cmd += " -only";
            foreach (var f in _fileNames)
                cmd += " \"" + f + "\"";
        }
        cmd += " -to \"" + dest.TrimEnd('/', '\\') + "/\" -catpaqmode";
        if (Main.PasswordKey != "")
            cmd += " -key \"" + Main.PasswordKey + "\"";
        if (Main.PasswordFranzen != "")
            cmd += " -franzen \"" + Main.PasswordFranzen + "\"";

        Main.AddLog("Command: zpaqfranz " + cmd);
        Main.BridgeOp = BridgeOp.Extract;
        Main.Bridge.IsDataMode = false;
        Main.Bridge.OnProgress += OnExtractProgress;
        Main.Bridge.OnComplete += OnExtractComplete;
        LblLog.Text = Main.T("ext.starting", "Starting...");
        if (!Main.Bridge.RunCommandAsync(cmd))
        {
            LblLog.Text = Main.T("ext.err_start", "ERROR: could not start extraction.");
            ResetForNextRun();
        }
    }

    private void OnExtractProgress(int percent, string msg)
    {
        ProgressBar.Value = Math.Clamp(percent, 0, 100);
        LblPct.Text = $"{percent}%";
    }

    private void OnExtractComplete(int exitCode)
    {
        Main.Bridge.OnProgress -= OnExtractProgress;
        Main.Bridge.OnComplete -= OnExtractComplete;
        Main.BridgeOp = BridgeOp.List;
        DispatcherQueue.TryEnqueue(() =>
        {
            _running = false;
            _completed = true;   // 解压结束（成功/失败都是"已完成"）：主按钮转"关闭"
            ProgressPanel.Visibility = Visibility.Collapsed;
            LblLog.Text = exitCode == 0
                ? Main.T("ext.complete", "Extraction complete.")
                : string.Format(Main.T("ext.failed", "Extraction FAILED (exit code {0})."), exitCode);
            PrimaryButtonText = Main.T("btn.close", "Close");
        });
    }

    private void ResetForNextRun()
    {
        _running = false;
        _completed = false;
        ProgressPanel.Visibility = Visibility.Collapsed;
        PrimaryButtonText = Main.T("btn.ok", "OK");
    }
}
