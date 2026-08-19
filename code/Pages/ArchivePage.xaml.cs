// Archive 页：归档文件树（含版本历史）+ 过滤 + 时间机滑块。
// 移植自 ui/archive_tab.py（对齐 ufrmmain.pas TabArchive）。
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Catpaq.Core;

namespace Catpaq.Pages;

public sealed partial class ArchivePage : Page
{
    private MainWindow Main => App.MainWindow;

    public sealed class VersionVm
    {
        public int Version { get; set; }
        public string DateStr { get; set; } = "";
        public long Size { get; set; }
        public bool IsDeleted { get; set; }
        public string Display => IsDeleted
            ? $"v{Version}  DELETED"
            : $"v{Version}  {DateStr}  {ArchiveHelpers.FormatFileSize(Size)}";
    }

    public sealed class FileVm
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public List<VersionVm> Versions { get; } = new();
    }

    private ArchiveData _data = new();
    // 默认只显示每个文件的最新版本（第一层文件完整），避免大归档版本节点爆炸；
    // 需要时通过按钮切换显示全部版本
    private bool _showAllVersions = false;
    private int _populateSeq;
    private long _populateStartTick;

    public ArchivePage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Main.ArchivePage = this;
            ApplyLanguage();
            if (_data.Files.Count > 0)
                Populate(_data);
        };
    }

    public async void Populate(ArchiveData data)
    {
        var seq = ++_populateSeq;
        _populateStartTick = Environment.TickCount64;
        _data = data;
        FileTree.RootNodes.Clear();

        var root = new TreeViewNode { Content = Main.ArchivePath != "" ? Path.GetFileName(Main.ArchivePath) : "Archive" };
        FileTree.RootNodes.Add(root);

        // 分批填充：大归档可能有数万/数十万节点，一次性添加会长时间阻塞 UI
        const int batchSize = 400;
        var buffer = new List<TreeViewNode>(batchSize);
        foreach (var fe in data.Files)
        {
            if (seq != _populateSeq)
                return; // 已被更新的加载取代
            var node = new TreeViewNode
            {
                Content = new FileVm
                {
                    Name = fe.FileName,
                    FullPath = fe.FileName,
                    IsDirectory = fe.FileName.EndsWith("/") || fe.FileName.EndsWith("\\"),
                },
            };
            var vm = (FileVm)node.Content;
            foreach (var fv in fe.Versions)
                vm.Versions.Add(new VersionVm
                {
                    Version = fv.Version,
                    DateStr = fv.DateStr,
                    Size = fv.Size,
                    IsDeleted = fv.IsDeleted,
                });
            if (_showAllVersions)
            {
                foreach (var v in vm.Versions)
                    node.Children.Add(new TreeViewNode { Content = v });
            }
            else
            {
                if (vm.Versions.Count > 0)
                    node.Children.Add(new TreeViewNode { Content = vm.Versions[^1] });
            }
            buffer.Add(node);
            if (buffer.Count >= batchSize)
            {
                foreach (var n in buffer)
                    root.Children.Add(n);
                buffer.Clear();
                await Task.Yield(); // 让出 UI 队列，保持界面响应
            }
        }
        if (buffer.Count > 0)
        {
            foreach (var n in buffer)
                root.Children.Add(n);
        }
        root.IsExpanded = true;
        FileTree.Expand(root);
        LblFileCount.Text = $"{data.Files.Count} {Main.T("arc.files_count", "files")}";
        LblArchiveInfo.Text = Main.ArchivePath != ""
            ? $"{Path.GetFileName(Main.ArchivePath)}  ({ArchiveHelpers.ArchiveTypeToStr(Main.ArchiveType)})"
            : Main.T("arc.no_archive", "No archive loaded");
        LblLoadInfo.Text = $"{data.Files.Count} {Main.T("arc.files_count", "files")} loaded";
        var secs = (Environment.TickCount64 - _populateStartTick) / 1000.0;
        Main.AddArchiveLog($"Archive tree populated in {secs:0.0}s ({data.Files.Count} files)");
    }

    // 取消进行中的分批填充（用户点取消后停止继续添加节点）
    public void CancelPopulate()
    {
        _populateSeq++;
        FileTree.RootNodes.Clear();
    }

    public void FinishLoading(bool clear)
    {
        LoadGauge.Value = 0;
        if (clear)
        {
            FileTree.RootNodes.Clear();
            LblArchiveInfo.Text = Main.T("arc.no_archive", "No archive loaded");
            LblFileCount.Text = $"0 {Main.T("arc.files_count", "files")}";
        }
    }

    public void ApplyLanguage()
    {
        LblArchiveInfo.Text = Main.ArchivePath != ""
            ? $"{Path.GetFileName(Main.ArchivePath)}  ({ArchiveHelpers.ArchiveTypeToStr(Main.ArchiveType)})"
            : Main.T("arc.no_archive", "No archive loaded");
        BtnOpen.Content = Main.T("btn.open", "Select ZPAQ...");
        BtnTimeMachine.Content = _showAllVersions
            ? Main.T("arc.show_latest", "Show latest only")
            : Main.T("arc.show_all", "Show ALL versions");
        LblFilter.Text = Main.T("arc.filter", "Filter:");
        if (_data.Files.Count > 0)
            LblFileCount.Text = $"{_data.Files.Count} {Main.T("arc.files_count", "files")}";
    }

    private async void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".zpaq");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Main.Hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            Main.LoadArchive(file.Path);
    }

    private void BtnTimeMachine_Click(object sender, RoutedEventArgs e)
    {
        _showAllVersions = !_showAllVersions;
        BtnTimeMachine.Content = _showAllVersions
            ? Main.T("arc.show_latest", "Show latest only")
            : Main.T("arc.show_all", "Show ALL versions");
        if (_data.Files.Count > 0)
            Populate(_data);
    }

    private void EdtFilter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            var needle = EdtFilter.Text.Trim().ToLowerInvariant();
            if (needle == "")
            {
                Populate(_data);
                return;
            }
            FileTree.RootNodes.Clear();
            var root = new TreeViewNode { Content = "Filtered" };
            foreach (var fe in _data.Files)
            {
                if (fe.FileName.ToLowerInvariant().Contains(needle))
                    root.Children.Add(new TreeViewNode { Content = fe.FileName });
            }
            FileTree.RootNodes.Add(root);
            root.IsExpanded = true;
        }
    }

    private void TimeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // 时间机：简化实现——滑块禁用时忽略。
    }

    private void FileTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node && node.Content is FileVm vm)
        {
            if (vm.IsDirectory)
            {
                node.IsExpanded = !node.IsExpanded;
            }
        }
    }

    private void FileTree_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.DataContext is FileVm vm)
        {
            var flyout = new MenuFlyout();
            var mExtract = new MenuFlyoutItem { Text = Main.T("arc.extract_folder", "Extract folder to...") };
            mExtract.Click += (_, _) => Main.ShowExtractDialogForPaths(Main.ArchivePath,
                new List<string> { vm.FullPath });
            flyout.Items.Add(mExtract);
            var mAll = new MenuFlyoutItem { Text = Main.T("arc.extract_all", "Extract everything to...") };
            mAll.Click += (_, _) => Main.ShowExtractAllDialog(Main.ArchivePath);
            flyout.Items.Add(mAll);
            flyout.ShowAt(FileTree, e.GetPosition(FileTree));
        }
    }
}
