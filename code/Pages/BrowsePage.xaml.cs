// Browse 页（Files 风格文件资源管理器）。
// 移植自 ui/explorer_panel.py + ui/browse_tab.py 的核心导航/浏览逻辑。
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Catpaq.Core;

namespace Catpaq.Pages;

public sealed partial class BrowsePage : Page
{
    private MainWindow Main => App.MainWindow;

    public sealed class ItemVm : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public string SizeText { get; set; } = "";
        public string ModifiedText { get; set; } = "";
        public string TypeText { get; set; } = "";
        public bool ArchiveMode { get; set; }
        // 此电脑页分组头（"文件夹" / "设备和驱动器"）：整行渲染为分组标题
        public bool IsGroupHeader { get; set; }
        // 行图标（此电脑页异步加载真实系统图标）
        private Microsoft.UI.Xaml.Media.Imaging.BitmapImage? _iconSource;
        public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? IconSource
        {
            get => _iconSource;
            set => SetProperty(ref _iconSource, value);
        }
    }

    private readonly List<ItemVm> _items = new();
    private string _currentPath = "";
    // 导航位置：IsArchive=true 时 Path 为归档文件路径、Sub 为归档内子路径
    private sealed record NavLoc(bool IsArchive, string Path, string Sub = "");
    private readonly List<NavLoc> _history = new();
    private readonly List<NavLoc> _forward = new();
    // 已解析的归档数据缓存（后退/前进跨 zpaq 内外时避免重新解析）
    private readonly Dictionary<string, ArchiveData> _archiveCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingArchiveSub;   // 异步加载归档后要恢复的子路径
    private bool _restoreArchiveMode;     // 正处于历史恢复进入归档的过程中
    // 复选框选中集合（点复选框才选中，用于添加/解压等多选操作）
    private readonly HashSet<string> _checkedPaths = new(StringComparer.OrdinalIgnoreCase);
    public bool ArchiveBrowseMode { get; private set; }
    public string ArchiveBrowsePath { get; private set; } = "";
    private ArchiveData _archiveData = new();   // 当前归档数据（用于目录层级浏览）
    private string _archiveRoot = "";           // 归档内所有文件的公共目录（虚拟根）
    private string _archiveSub = "";            // 归档内当前子路径（相对 _archiveRoot，""=根）

    public BrowsePage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Main.BrowsePage = this;
            ApplyLanguage();
            UpdateSidebarSelection();
            if (_currentPath == "")
                NavigateToPath(DefaultStartPath());
            TryOpenStartupArchive();
        };
    }

    // 命令行启动（双击 .zpaq 由系统传入路径）：启动后停在浏览页并进入归档浏览模式
    private void TryOpenStartupArchive()
    {
        var path = Program.StartupArchive;
        if (path == "")
            return;
        Program.StartupArchive = "";
        Main.ArchiveBrowsePath = path;
        Main.LoadFromBrowseTab = true;
        Main.LoadArchive(path);
    }

    private static string DefaultStartPath()
    {
        return OperatingSystem.IsWindows() ? "C:\\" : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    // "此电脑"视图标记：_currentPath 等于该值时文件列表显示全部驱动器
    private const string ThisPcMarker = "ThisPC";

    private void BtnThisPc_Click(object sender, RoutedEventArgs e)
    {
        ShowThisPc();
    }

    // 进入"此电脑"视图（显示所有驱动器，Files 同款）
    public void ShowThisPc()
    {
        if (ArchiveBrowseMode)
            ExitArchiveBrowseMode();
        if (_currentPath == ThisPcMarker)
        {
            RefreshThisPcList();
            return;
        }
        var old = CurrentLoc;
        if (old.IsArchive
            || !string.Equals(old.Path, ThisPcMarker, StringComparison.Ordinal))
        {
            _history.Add(old);
            _forward.Clear();
        }
        _currentPath = ThisPcMarker;
        UpdateNavButtons();
        UpdateSidebarSelection();
        RefreshThisPcList();
    }

    // 此电脑页固定文件夹（照抄 Files/Windows 此电脑：桌面、文档、下载、音乐、图片、视频，存在才显示）
    // 显示名走 I18n（Key, 英文默认, 路径）
    private static readonly (string Key, string En, Func<string> Path)[] ThisPcFolders =
    {
        ("thispc.desktop", "Desktop", () => Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
        ("thispc.documents", "Documents", () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        ("thispc.downloads", "Downloads", () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
        ("thispc.music", "Music", () => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
        ("thispc.pictures", "Pictures", () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
        ("thispc.videos", "Videos", () => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
    };

    // 此电脑专属页（照抄 Files）："文件夹"分组 + "设备和驱动器"分组，行带真实系统图标
    private async void RefreshThisPcList()
    {
        var seq = ++_refreshSeq;
        _items.Clear();

        // 分组 1：固定文件夹
        _items.Add(new ItemVm { Name = Main.T("thispc.folder_group", "Folders"), IsGroupHeader = true });
        foreach (var f in ThisPcFolders)
        {
            string p;
            try { p = f.Path(); } catch { continue; }
            if (string.IsNullOrEmpty(p) || !Directory.Exists(p))
                continue;
            _items.Add(new ItemVm
            {
                Name = Main.T(f.Key, f.En),
                FullPath = p,
                IsDirectory = true,
                TypeText = Main.T("col.folder", "Folder"),
            });
        }

        // 分组 2：设备和驱动器
        _items.Add(new ItemVm { Name = Main.T("thispc.devices_group", "Devices and drives"), IsGroupHeader = true });
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady)
                continue;
            string label;
            long total = -1;
            long free = -1;
            string typeText = Main.T("thispc.local_disk", "Local Disk");
            try
            {
                label = string.IsNullOrEmpty(d.VolumeLabel) ? d.Name.TrimEnd('\\') : d.VolumeLabel;
                total = d.TotalSize;
                free = d.TotalFreeSpace;
                if (d.DriveType == DriveType.Removable) typeText = Main.T("thispc.removable", "Removable Disk");
                else if (d.DriveType == DriveType.Network) typeText = Main.T("thispc.network", "Network Drive");
                else if (d.DriveType == DriveType.CDRom) typeText = Main.T("thispc.cdrom", "CD Drive");
            }
            catch
            {
                label = d.Name.TrimEnd('\\');
            }
            _items.Add(new ItemVm
            {
                Name = label + "  (" + d.Name.TrimEnd('\\') + ")\\",
                FullPath = d.Name,
                IsDirectory = true,
                Size = total,
                Modified = DateTime.MinValue,
                SizeText = total > 0 ? ArchiveHelpers.FormatFileSize(total) : "",
                ModifiedText = free > 0 ? Main.T("thispc.free", "Free") + " " + ArchiveHelpers.FormatFileSize(free) : "",
                TypeText = typeText,
            });
        }

        FileList.ItemsSource = _items.ToArray();
        BuildCrumbsForThisPc();
        UpdateStatus();

        // 行图标异步加载（真实系统图标，加载完成后自动刷新该行）
        foreach (var vm in _items)
        {
            if (vm.IsGroupHeader)
                continue;
            _ = LoadThisPcItemIconAsync(vm, seq);
        }
    }

    private async Task LoadThisPcItemIconAsync(ItemVm vm, int seq)
    {
        var icon = await SystemIcon.GetIconAsync(vm.FullPath, vm.IsDirectory);
        if (seq == _refreshSeq && icon is not null)
            vm.IconSource = icon;
    }

    private void BuildCrumbsForThisPc()
    {
        _currentParts = [new PathItem { Label = Main.T("expl.this_pc", "This PC"), Path = ThisPcMarker }];
        OmnibarCrumbs.ItemsSource = _currentParts;
        OmnibarCrumbs.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------
    // 侧边栏（照抄 Files：SidebarView 控件 + SidebarViewModel 扁平树）
    // ------------------------------------------------------------------
    public SidebarViewModel SidebarVm { get; } = new();

    private void Sidebar_ItemInvoked(object sender, Catpaq.Controls.ItemInvokedEventArgs e)
    {
        if (e is not { }) return;
        // SidebarView 已经把分组头过滤掉，这里直接取当前选中的项
        if (SidebarVm.SelectedItem is INavigationControlItem item &&
            item is not LocationItem { IsHeader: true })
        {
            if (Directory.Exists(item.Path))
                NavigateToPath(item.Path);
        }
    }

    private void UpdateSidebarSelection()
    {
        SidebarVm.UpdateSelectionForPath(_currentPath == ThisPcMarker ? "" : (ArchiveBrowseMode ? "" : _currentPath));
    }

    // ------------------------------------------------------------------
    // 导航
    // ------------------------------------------------------------------
    // 当前所处位置（文件系统目录 或 归档位置）
    private NavLoc CurrentLoc => ArchiveBrowseMode
        ? new NavLoc(true, ArchiveBrowsePath, _archiveSub)
        : new NavLoc(false, _currentPath, "");

    // 刷新导航按钮可用状态：后退/前进看历史栈；上级看是否还能再上级（跨 zpaq 内外均可）
    private void UpdateNavButtons()
    {
        BtnBack.IsEnabled = _history.Count > 0;
        BtnForward.IsEnabled = _forward.Count > 0;
        BtnUp.IsEnabled = CanGoUp();
    }

    private bool CanGoUp()
    {
        // 归档模式：总能上级（归档内逐级向上，归档根时退出回文件系统）
        if (ArchiveBrowseMode)
            return true;
        if (_currentPath == ThisPcMarker)
            return false;
        try
        {
            return _currentPath != "" && Directory.GetParent(_currentPath) is not null;
        }
        catch
        {
            return false;
        }
    }

    public void NavigateToPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;
        if (ArchiveBrowseMode)
            ExitArchiveBrowseMode();
        var old = CurrentLoc;
        var norm = path.TrimEnd('\\', '/');
        if (old.IsArchive
            || string.Compare(old.Path.TrimEnd('\\', '/'), norm, StringComparison.OrdinalIgnoreCase) != 0)
        {
            _history.Add(old);
            _forward.Clear();
        }
        _currentPath = norm + Path.DirectorySeparatorChar;
        UpdateNavButtons();
        UpdateSidebarSelection();
        RefreshList();
    }

    // 归档内导航到指定子路径（记录历史，便于后退/前进跨 zpaq 内外）
    private void NavigateArchiveSub(string sub)
    {
        sub = sub.Trim('/');
        if (_archiveSub == sub)
            return;
        _history.Add(CurrentLoc);
        _forward.Clear();
        _archiveSub = sub;
        UpdateNavButtons();
        RefreshArchiveList();
    }

    // 应用一个历史位置（恢复文件系统目录或重新进入归档）
    private void GoToLoc(NavLoc loc)
    {
        if (loc.IsArchive)
        {
            if (_archiveCache.TryGetValue(loc.Path, out var data))
            {
                EnterArchiveCore(loc.Path, data, loc.Sub);
                return;
            }
            // 缓存未命中（极罕见）：异步重新加载归档，完成后恢复到目标子路径
            _restoreArchiveMode = true;
            _pendingArchiveSub = loc.Sub;
            Main.ArchiveBrowsePath = loc.Path;
            Main.LoadFromBrowseTab = true;
            Main.LoadArchive(loc.Path);
            return;
        }
        // "此电脑"不是真实目录路径，NavigateToPath 的 Directory.Exists 检查会挡掉，需单独恢复
        if (loc.Path == ThisPcMarker)
        {
            if (ArchiveBrowseMode)
                ExitArchiveBrowseMode();
            _currentPath = ThisPcMarker;
            UpdateNavButtons();
            UpdateSidebarSelection();
            RefreshThisPcList();
            return;
        }
        NavigateToPath(loc.Path);
    }

    public void GoBack()
    {
        if (_history.Count == 0)
            return;
        _forward.Add(CurrentLoc);
        var loc = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        GoToLoc(loc);
    }

    public void GoForward()
    {
        if (_forward.Count == 0)
            return;
        _history.Add(CurrentLoc);
        var loc = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        GoToLoc(loc);
    }

    public void GoUp()
    {
        if (ArchiveBrowseMode)
        {
            if (_archiveSub != "")
            {
                // 归档内：逐级向上
                var idx = _archiveSub.LastIndexOf('/');
                NavigateArchiveSub(idx < 0 ? "" : _archiveSub[..idx]);
            }
            else
            {
                // 归档根：退出回文件系统（进入归档前的目录）
                if (_currentPath != "" && Directory.Exists(_currentPath))
                    NavigateToPath(_currentPath);
                else
                    ExitArchiveBrowseMode();
            }
            return;
        }
        var parent = Directory.GetParent(_currentPath);
        if (parent is not null)
            NavigateToPath(parent.FullName);
    }

    private int _refreshSeq;

    // 异步刷新：后台线程枚举 + 读取元数据，避免大目录/网络目录阻塞 UI
    public async void RefreshList()
    {
        if (ArchiveBrowseMode)
            return;
        var path = _currentPath;
        if (path == ThisPcMarker)
        {
            RefreshThisPcList();
            return;
        }
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;
        var seq = ++_refreshSeq;
        BuildCrumbs(path);

        var (items, denied, ioError) = await Task.Run(() => EnumerateItems(path));
        if (seq != _refreshSeq)
            return; // 已被更新的导航取代，丢弃过期结果
        if (denied)
        {
            FileList.ItemsSource = Array.Empty<ItemVm>();
            LblStatus.Text = Main.T("expl.access_denied", "Access denied") + "  ·  " + path;
            return;
        }
        if (ioError)
        {
            FileList.ItemsSource = Array.Empty<ItemVm>();
            LblStatus.Text = Main.T("expl.read_error", "Cannot read folder") + "  ·  " + path;
            return;
        }

        _items.Clear();
        _items.AddRange(items);
        FileList.ItemsSource = _items.ToArray();
        UpdateStatus();
    }

    // 后台枚举目录（不访问 UI），返回项列表与错误标志
    private (List<ItemVm> Items, bool Denied, bool IoError) EnumerateItems(string path)
    {
        var list = new List<ItemVm>();
        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(path);
        }
        catch (UnauthorizedAccessException)
        {
            return (list, true, false);
        }
        catch (Exception)
        {
            return (list, false, true);
        }

        foreach (var full in entries)
        {
            var name = Path.GetFileName(full);
            bool isDir = Directory.Exists(full);
            long size = -1;
            DateTime mod = DateTime.MinValue;
            try
            {
                if (isDir)
                    mod = Directory.GetLastWriteTime(full);
                else
                {
                    var fi = new FileInfo(full);
                    size = fi.Length;
                    mod = fi.LastWriteTime;
                }
            }
            catch { }
            list.Add(new ItemVm
            {
                Name = isDir ? name + "\\" : name,
                FullPath = full,
                IsDirectory = isDir,
                Size = size,
                Modified = mod,
                SizeText = isDir ? "" : ArchiveHelpers.FormatFileSize(size),
                ModifiedText = mod == DateTime.MinValue ? "" : mod.ToString("yyyy-MM-dd HH:mm"),
                TypeText = isDir ? Main.T("col.folder", "Folder") : Path.GetExtension(name).TrimStart('.').ToUpperInvariant(),
            });
        }

        // 目录在前
        list.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.CompareOrdinal(a.Name, b.Name);
        });
        return (list, false, false);
    }

    // 面包屑数据项（系统 BreadcrumbBar 用）
    public sealed class PathItem
    {
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public override string ToString() => Label;
    }

    private List<PathItem>? _currentParts;   // 文件系统面包屑层级

    // 地址栏面包屑（Files Omnibar 风格）：用系统 BreadcrumbBar 渲染 Windows 11 风格分层，
    // 自动折叠为「…」；点击当前级进入编辑模式。
    private void BuildCrumbs(string path)
    {
        _currentParts = SplitPath(path)
            .Select(p => new PathItem { Label = p.Label, Path = p.Path })
            .ToList();
        OmnibarCrumbs.ItemsSource = _currentParts;
        OmnibarCrumbs.Visibility = Visibility.Visible;
    }

    // 文件系统面包屑点击：普通级导航；当前级（最后一级）进入编辑模式
    private void OmnibarCrumbs_ItemClicked(object sender, Catpaq.Controls.BreadcrumbBarItemClickedEventArgs args)
    {
        if (ArchiveBrowseMode)
        {
            HandleArchiveCrumb(args.Index);
            return;
        }
        if (_currentParts is null || args.Index < 0 || args.Index >= _currentParts.Count)
            return;
        // 点击当前级（最后一项）：无操作，不进入编辑、不导航（当前就是这里）
        if (args.Index == _currentParts.Count - 1)
            return;
        NavigateToPath(_currentParts[args.Index].Path);
    }

    // 把路径拆成各层级（含根盘符），返回 (显示名, 完整路径)
    private static List<(string Label, string Path)> SplitPath(string path)
    {
        var list = new List<(string, string)>();
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return list;
            var rootLabel = root.TrimEnd('\\', '/');
            if (rootLabel.Length == 2 && rootLabel[1] == ':') rootLabel += "\\";
            list.Add((rootLabel, root));
            var rest = path[root.Length..].Trim('\\', '/');
            if (rest == "") return list;
            var current = root.TrimEnd('\\', '/');
            foreach (var seg in rest.Split('\\', '/'))
            {
                if (seg == "") continue;
                current = current + Path.DirectorySeparatorChar + seg;
                list.Add((seg, current));
            }
        }
        catch { }
        return list;
    }

    private void UpdateStatus()
    {
        int total = 0, dirs = 0, files = 0;
        foreach (var i in _items)
        {
            if (i.Name == "..") continue;
            total++;
            if (i.IsDirectory) dirs++; else files++;
        }
        // 归档浏览模式下显示归档位置，而不是文件系统当前路径
        var loc = ArchiveBrowseMode
            ? ArchiveBrowsePath + (_archiveSub == "" ? "" : "/" + _archiveSub.Trim('/'))
            : _currentPath;
        LblStatus.Text = $"{total} {Main.T("expl.items", "items")}  ·  "
            + $"{dirs} {Main.T("expl.folders", "folders")}  ·  "
            + $"{files} {Main.T("expl.files", "files")}  ·  {loc}";
    }

    // ------------------------------------------------------------------
    // 归档浏览模式（双击 .zpaq 后同一界面显示归档内容）
    // ------------------------------------------------------------------
    // 进入归档浏览（双击 .zpaq 等正常入口）：把进入前的文件系统位置记入历史
    public void ShowArchiveBrowse(string archivePath, ArchiveData data)
    {
        if (_restoreArchiveMode)
        {
            // 历史恢复进入归档：异步加载完成后走到这里，恢复目标子路径且不再压历史
            _restoreArchiveMode = false;
            EnterArchiveCore(archivePath, data, _pendingArchiveSub ?? "");
            _pendingArchiveSub = null;
            return;
        }
        _history.Add(CurrentLoc);
        _forward.Clear();
        EnterArchiveCore(archivePath, data, "");
    }

    // 设置归档浏览状态并刷新列表（不涉及历史栈）
    private void EnterArchiveCore(string archivePath, ArchiveData data, string sub)
    {
        ArchiveBrowseMode = true;
        ArchiveBrowsePath = archivePath;
        _archiveData = data;
        _archiveCache[archivePath] = data;
        _layerCache.Clear();
        _archiveSub = sub;
        _archiveRoot = ComputeArchiveRoot();
        ArchiveBar.Visibility = Visibility.Visible;
        UpdateNavButtons();
        RefreshArchiveList();
    }

    // 归档内所有文件目录的公共前缀，作为虚拟根（归档常把所有文件存在同一目录下）
    private string ComputeArchiveRoot()
    {
        var dirs = new List<string>();
        foreach (var fe in _archiveData.Files)
        {
            if (fe.Versions.Count == 0) continue;
            if (fe.Versions[^1].IsDeleted) continue;
            var p = fe.FileName.Replace('\\', '/').TrimEnd('/');
            var i = p.LastIndexOf('/');
            dirs.Add(i > 0 ? p[..i] : "");
        }
        if (dirs.Count == 0) return "";
        var segs = dirs[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        var common = new List<string>();
        foreach (var seg in segs)
        {
            var c = (common.Count > 0 ? string.Join("/", common) + "/" : "") + seg;
            if (dirs.Any(d => !string.Equals(d, c, StringComparison.OrdinalIgnoreCase)
                              && !d.StartsWith(c + "/", StringComparison.OrdinalIgnoreCase)))
                break;
            common.Add(seg);
        }
        return string.Join("/", common);
    }

    private int _archiveRefreshSeq;

    // 归档层列表缓存：目录名 + 当前层文件引用（不复制对象），避免每次导航全量遍历
    private readonly Dictionary<string, (List<(string Name, string Path)> Dirs, List<ArchiveFileEntry> Files)> _layerCache =
        new(StringComparer.OrdinalIgnoreCase);

    // 按当前子路径显示归档内的直接子项（目录 + 文件），构成层级结构。
    // 构建放后台线程 + 按层缓存，避免大归档在 UI 线程反复全量遍历。
    private async void RefreshArchiveList()
    {
        var seq = ++_archiveRefreshSeq;
        BuildArchiveCrumbs();
        var sub = _archiveSub == "" ? "" : _archiveSub + "/";
        var prefix = _archiveRoot == "" ? sub : _archiveRoot + "/" + sub;

        var layer = await Task.Run(() => BuildArchiveItems(prefix, sub));
        if (seq != _archiveRefreshSeq)
            return; // 已被更新的刷新取代

        _items.Clear();
        foreach (var (name, path) in layer.Dirs)
            _items.Add(new ItemVm
            {
                Name = name + "/",
                FullPath = path,
                IsDirectory = true,
                TypeText = Main.T("col.folder", "Folder"),
                ArchiveMode = true,
            });
        foreach (var fe in layer.Files)
        {
            if (fe.Versions.Count == 0) continue;
            var fv = fe.Versions[^1];
            if (fv.IsDeleted) continue;
            var disp = Path.GetFileName(fe.FileName.Replace('\\', '/'));
            if (string.IsNullOrEmpty(disp)) continue;
            DateTime mod = DateTime.MinValue;
            if (DateTime.TryParse(fv.DateStr, out var parsed)) mod = parsed;
            _items.Add(new ItemVm
            {
                Name = disp,
                FullPath = fe.FileName.Replace('\\', '/'),
                IsDirectory = false,
                Size = fv.Size,
                Modified = mod,
                SizeText = ArchiveHelpers.FormatFileSize(fv.Size),
                ModifiedText = mod == DateTime.MinValue ? "" : mod.ToString("yyyy-MM-dd HH:mm"),
                TypeText = Path.GetExtension(disp).TrimStart('.').ToUpperInvariant(),
                ArchiveMode = true,
            });
        }
        FileList.ItemsSource = _items.ToArray();
        UpdateStatus();
    }

    // 后台构建归档当前层的目录/文件项（不访问 UI；命中缓存直接返回，构建后缓存）
    private (List<(string Name, string Path)> Dirs, List<ArchiveFileEntry> Files) BuildArchiveItems(string prefix, string sub)
    {
        if (_layerCache.TryGetValue(sub, out var cached))
            return cached;

        var dirDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ArchiveFileEntry>();

        foreach (var fe in _archiveData.Files)
        {
            if (fe.Versions.Count == 0) continue;
            if (fe.Versions[^1].IsDeleted) continue;
            var rel = fe.FileName.Replace('\\', '/');
            var isDirEntry = rel.EndsWith("/");
            var p = rel.TrimEnd('/');
            if (p == "") continue;
            if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = p[prefix.Length..];
            if (rest == "") continue;   // 根目录自身
            var slash = rest.IndexOf('/');
            if (slash >= 0)
            {
                // 属于更深层：第一段是子目录
                var dirName = rest[..slash];
                if (!dirDict.ContainsKey(dirName))
                    dirDict[dirName] = (sub + dirName).Trim('/');
            }
            else if (isDirEntry)
            {
                // 显式目录条目
                if (!dirDict.ContainsKey(rest))
                    dirDict[rest] = (sub + rest).Trim('/');
            }
            else
            {
                // 当前层的直接文件：缓存引用
                files.Add(fe);
            }
        }

        var dirs = dirDict
            .Select(kv => (Name: kv.Key, Path: kv.Value))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sortedFiles = files.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();

        var result = (dirs, sortedFiles);
        _layerCache[sub] = result;
        if (_layerCache.Count > 64)
            _layerCache.Clear(); // 限容：超大归档缓存过多层时整体清空
        return result;
    }

    private List<PathItem>? _archiveCrumbs;   // 归档面包屑层级

    // 归档浏览的面包屑：[ARCHIVE] › archive.zpaq › 子路径…
    private void BuildArchiveCrumbs()
    {
        _archiveCrumbs = new List<PathItem>
        {
            new() { Label = "[ARCHIVE]", Path = "" },
            new() { Label = Path.GetFileName(ArchiveBrowsePath), Path = "" },
        };
        if (_archiveSub != "")
        {
            foreach (var seg in _archiveSub.Split('/', StringSplitOptions.RemoveEmptyEntries))
                _archiveCrumbs.Add(new PathItem { Label = seg, Path = "" });
        }
        OmnibarCrumbs.ItemsSource = _archiveCrumbs;
        OmnibarCrumbs.Visibility = Visibility.Visible;
    }

    // 归档面包屑点击：0=[ARCHIVE]退出；1=归档根；中间=跳转；最后一级=进入编辑模式
    private void HandleArchiveCrumb(int index)
    {
        if (_archiveCrumbs is null || index < 0 || index >= _archiveCrumbs.Count)
            return;
        if (index == 0)
        {
            GoUp(); // 退出归档浏览回文件系统
            return;
        }
        if (index == 1)
        {
            NavigateArchiveSub(""); // 归档根
            return;
        }
        // 点击当前级（最后一项）：无操作，不进入编辑、不导航
        if (index == _archiveCrumbs.Count - 1)
            return;
        var target = string.Join("/", _archiveCrumbs.Skip(2).Take(index - 1).Select(x => x.Label));
        NavigateArchiveSub(target);
    }

    // chevron 下拉：填充该层级的子目录（Files 同款行为）
    private async void OmnibarCrumbs_DropDownFlyoutOpening(object? sender, Catpaq.Controls.BreadcrumbBarItemDropDownFlyoutEventArgs e)
    {
        var flyout = e.Flyout;
        flyout.Items.Clear();
        if (e.Index < 0)
            return;
        if (ArchiveBrowseMode)
        {
            var prefix = GetArchiveFolderPrefix(e.Index);
            var basePath = _archiveRoot == "" ? prefix : (prefix == "" ? _archiveRoot : _archiveRoot + "/" + prefix);
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fe in _archiveData.Files)
            {
                if (fe.Versions.Count == 0) continue;
                if (fe.Versions[^1].IsDeleted) continue;
                var rel = fe.FileName.Replace('\\', '/').TrimEnd('/');
                if (rel == "") continue;
                if (!rel.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(rel, basePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                var rest = rel[basePath.Length..].TrimStart('/');
                var slash = rest.IndexOf('/');
                if (slash < 0) continue; // 文件
                dirs.Add(rest[..slash]);
            }
            foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var targetSub = prefix == "" ? d : prefix + "/" + d;
                var mi = new MenuFlyoutItem { Text = d };
                mi.Click += (_, _) => NavigateArchiveSub(targetSub);
                flyout.Items.Add(mi);
            }
            return;
        }
        // 文件系统：异步枚举该层级的子目录
        if (_currentParts is null || e.Index >= _currentParts.Count)
            return;
        var folder = _currentParts[e.Index].Path;
        if (folder == "" || !Directory.Exists(folder))
            return;
        var names = await Task.Run(() =>
        {
            try
            {
                return Directory.GetDirectories(folder)
                    .Select(Path.GetFileName)
                    .Where(n => n != null)
                    .Take(200)
                    .ToList();
            }
            catch { return new List<string?>(); }
        });
        foreach (var name in names)
        {
            var mi = new MenuFlyoutItem { Text = name };
            mi.Click += (_, _) => NavigateToPath(Path.Combine(folder, name ?? ""));
            flyout.Items.Add(mi);
        }
    }

    private string GetArchiveFolderPrefix(int index)
    {
        if (index <= 0 || _archiveCrumbs is null)
            return "";
        return string.Join("/", _archiveCrumbs.Skip(2).Take(index - 1).Select(x => x.Label));
    }

    // ------------------------------------------------------------------
    // 地址栏编辑模式（Files Omnibar：点击当前级/空白处 → 输入框，失焦恢复面包屑）
    // ------------------------------------------------------------------
    private Brush? _omnibarBgBrush;      // 浏览模式的地址栏背景（编辑时置空避免双框，退出后恢复）
    private Brush? _omnibarBgBorderBrush; // 浏览模式的地址栏边框

    private void EnterEditMode()
    {
        _omnibarBgBrush ??= OmnibarBg.Background;
        _omnibarBgBorderBrush ??= OmnibarBg.BorderBrush;
        // 编辑模式下外层框整体透明（背景+边框），由 TextBox 单独呈现一个框，
        // 避免 TextBox 焦点背景与外层框叠加成"两框"。
        OmnibarBg.Background = null;
        OmnibarBg.BorderBrush = null;
        EdtPathBox.Text = ArchiveBrowseMode
            ? "[ARCHIVE]" + (_archiveSub == "" ? "" : " " + _archiveSub)
            : _currentPath == ThisPcMarker
                ? Main.T("expl.this_pc", "This PC")
                : _currentPath.TrimEnd('\\', '/');
        EdtPathBox.Visibility = Visibility.Visible;
        OmnibarCrumbs.Visibility = Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(() =>
        {
            EdtPathBox.Focus(FocusState.Pointer);
            EdtPathBox.Select(EdtPathBox.Text.Length, 0);
        });
    }

    private void ExitEditMode()
    {
        EdtPathBox.Visibility = Visibility.Collapsed;
        OmnibarCrumbs.Visibility = Visibility.Visible;
        OmnibarBg.Background = _omnibarBgBrush;
        OmnibarBg.BorderBrush = _omnibarBgBorderBrush;
    }

    // 点击地址栏中「没有按钮的空白区域」进入编辑模式（Files 行为）。
    // 面包屑项/chevron/根项都是按钮，各自处理点击（导航/下拉/编辑），这里只响应空白区域。
    private void Omnibar_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (EdtPathBox.Visibility == Visibility.Visible)
            return;
        if (IsWithinButton(e.OriginalSource))
            return;
        EnterEditMode();
    }

    // 沿视觉树向上查找：点击源是否落在某个按钮内部
    private static bool IsWithinButton(object? source)
    {
        if (source is not DependencyObject d)
            return false;
        while (d != null)
        {
            if (d is ButtonBase)
                return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void EdtPathBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            NavigateFromEdit(EdtPathBox.Text);
            ExitEditMode();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            ExitEditMode();
        }
    }

    private void EdtPathBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (EdtPathBox.Visibility != Visibility.Visible)
            return;
        NavigateFromEdit(EdtPathBox.Text);
        ExitEditMode();
    }

    // 解析编辑框内容：支持文件系统路径与 [ARCHIVE] 归档内路径
    private void NavigateFromEdit(string text)
    {
        text = text.Trim();
        if (text == "")
            return;
        if (text.StartsWith("[ARCHIVE]", StringComparison.OrdinalIgnoreCase))
        {
            var sub = text["[ARCHIVE]".Length..].Trim().TrimStart('/', '\\').Replace('\\', '/');
            if (ArchiveBrowseMode)
                NavigateArchiveSub(sub);
            return;
        }
        // "此电脑"：进入此电脑视图
        if (string.Equals(text, Main.T("expl.this_pc", "This PC"), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "ThisPC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "My Computer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "Computer", StringComparison.OrdinalIgnoreCase))
        {
            ShowThisPc();
            return;
        }
        if (Directory.Exists(text))
        {
            if (ArchiveBrowseMode)
                ExitArchiveBrowseMode();
            NavigateToPath(text);
        }
    }

    public void ExitArchiveBrowseMode()
    {
        ArchiveBrowseMode = false;
        ArchiveBrowsePath = "";
        _archiveData = new();
        _archiveRoot = "";
        _archiveSub = "";
        _layerCache.Clear();
        ArchiveBar.Visibility = Visibility.Collapsed;
        Main.ArchiveBrowsePath = "";
        UpdateNavButtons();
        // 不在此刷新列表：调用方（NavigateToPath 等）会在导航后统一刷新
    }

    // ------------------------------------------------------------------
    // 工具栏
    // ------------------------------------------------------------------
    private void BtnBack_Click(object sender, RoutedEventArgs e) => GoBack();
    private void BtnForward_Click(object sender, RoutedEventArgs e) => GoForward();
    private void BtnUp_Click(object sender, RoutedEventArgs e) => GoUp();
    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (ArchiveBrowseMode) RefreshArchiveList(); else RefreshList();
    }
    private void BtnBackFs_Click(object sender, RoutedEventArgs e)
    {
        // 退出归档回文件系统（记录历史，后退可回到归档内）
        if (_currentPath != "" && Directory.Exists(_currentPath))
            NavigateToPath(_currentPath);
        else
            ExitArchiveBrowseMode();
    }
    private void BtnExtract_Click(object sender, RoutedEventArgs e) => OnExtract();
    private void BtnTest_Click(object sender, RoutedEventArgs e) => Main.RunTestArchive(ArchiveBrowsePath);

    private void EdtFilter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
            ApplyFilter(EdtFilter.Text);
    }

    private void ApplyFilter(string text)
    {
        text = text.Trim();
        if (text == "")
        {
            FileList.ItemsSource = _items.ToArray();
            return;
        }
        var needle = text.ToLowerInvariant();
        FileList.ItemsSource = _items.Where(i => i.Name.ToLowerInvariant().Contains(needle)).ToArray();
    }

    // ------------------------------------------------------------------
    // 打开/激活
    // ------------------------------------------------------------------
    private void FileList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ItemVm item)
            OpenItem(item);
    }

    // 复选框勾选/取消：维护选中集合（勾选不触发打开）
    private void ItemCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is ItemVm vm)
        {
            if (cb.IsChecked == true) _checkedPaths.Add(vm.FullPath);
            else _checkedPaths.Remove(vm.FullPath);
        }
    }

    private void FileList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.DataContext is ItemVm item)
            OpenItem(item);
    }

    private DateTime _lastOpen;
    private string _lastOpenPath = "";

    private void OpenItem(ItemVm item)
    {
        // 此电脑页分组头不可点击进入
        if (item.IsGroupHeader)
            return;
        // 单击即进入；防抖避免双击触发两次
        if (item.FullPath == _lastOpenPath && (DateTime.Now - _lastOpen).TotalMilliseconds < 400)
            return;
        _lastOpenPath = item.FullPath;
        _lastOpen = DateTime.Now;

        if (ArchiveBrowseMode)
        {
            // 归档内：目录 → 进入下一层；文件 → 不打开（返回上级用面包屑）
            if (item.IsDirectory)
                NavigateArchiveSub(item.FullPath);
            return;
        }
        if (item.IsDirectory)
        {
            NavigateToPath(item.FullPath);
            return;
        }
        if (item.FullPath.ToLowerInvariant().Contains(".zpaq"))
        {
            Main.ArchiveBrowsePath = item.FullPath;
            Main.LoadFromBrowseTab = true;
            Main.LoadArchive(item.FullPath);
            return;
        }
        OpenWithShell(item.FullPath);
    }

    private void OnExtract()
    {
        var sel = SelectedPaths();
        if (sel.Count == 0)
            return;
        Main.ShowExtractDialogForPaths(ArchiveBrowsePath, sel);
    }

    private List<string> SelectedPaths()
    {
        // 只取复选框勾选的项（排除空路径与 ".."）
        // 归档模式：文件夹的 FullPath 是相对路径（如 "folder"），而 zpaqfranz 的 x 命令
        // pattern 必须匹配归档内完整路径（C++ 原版 jollymatch 全路径匹配），
        // 这里统一补上 _archiveRoot 前缀；文件本来就是完整路径，保持原样。
        var prefix = ArchiveBrowseMode && _archiveRoot != ""
            ? _archiveRoot.TrimEnd('/') + "/"
            : "";
        return _checkedPaths
            .Where(p => p != "" && Path.GetFileName(p) != "..")
            .Select(p => prefix != "" && !p.StartsWith(_archiveRoot, StringComparison.OrdinalIgnoreCase)
                ? prefix + p.TrimStart('/')
                : p)
            .ToList();
    }

    private static void OpenWithShell(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    // ------------------------------------------------------------------
    // 键盘/右键
    // ------------------------------------------------------------------
    private void FileList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            if (FileList.SelectedItem is ItemVm item)
                OpenItem(item);
        }
        else if (e.Key == VirtualKey.Back)
        {
            GoUp();
        }
        else if (e.Key == VirtualKey.F5)
        {
            RefreshList();
        }
    }

    private void FileList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.DataContext is ItemVm item)
        {
            FileList.SelectedItem = item;
            var flyout = new MenuFlyout();
            var mOpen = new MenuFlyoutItem { Text = Main.T("mnu.open", "Open") };
            mOpen.Click += (_, _) => OpenItem(item);
            flyout.Items.Add(mOpen);
            var mAdd = new MenuFlyoutItem { Text = Main.T("mnu.add_zpaq", "Add to ZPAQ...") };
            mAdd.Click += (_, _) => OnAdd();
            flyout.Items.Add(mAdd);
            var mRefresh = new MenuFlyoutItem { Text = Main.T("mnu.refresh", "Refresh") };
            mRefresh.Click += (_, _) => RefreshList();
            flyout.Items.Add(mRefresh);
            flyout.ShowAt(FileList, e.GetPosition(FileList));
        }
    }

    private void OnAdd()
    {
        var sel = SelectedPaths();
        if (sel.Count == 0)
            return;
        Main.ShowAddDialog(sel);
    }

    // ------------------------------------------------------------------
    // 加载覆盖层（宿主调用）
    // ------------------------------------------------------------------
    public void UpdateLoadingProgress(int percent)
    {
        // WinUI 版在 Log 页展示加载进度，此处保留接口占位。
    }

    // ------------------------------------------------------------------
    // 语言切换（宿主 ApplyLanguage 调用）
    // ------------------------------------------------------------------
    public void ApplyLanguage()
    {
        ToolTipService.SetToolTip(BtnBack, Main.T("btn.back", "Back"));
        ToolTipService.SetToolTip(BtnForward, Main.T("btn.forward", "Forward"));
        ToolTipService.SetToolTip(BtnUp, Main.T("btn.up", "Up"));
        ToolTipService.SetToolTip(BtnRefresh, Main.T("btn.refresh", "Refresh"));
        EdtFilter.PlaceholderText = Main.T("expl.filter_hint", "Filter");
        BtnBackFs.Content = Main.T("expl.back_fs", "← Back to filesystem");
        BtnExtract.Content = Main.T("btn.extract", "Extract");
        BtnTest.Content = Main.T("btn.test", "Test");
        LblColName.Text = Main.T("col.name", "Name");
        LblColSize.Text = Main.T("col.size", "Size");
        LblColModified.Text = Main.T("col.modified", "Modified");
        LblColType.Text = Main.T("col.type", "Type");
        SidebarVm.RefreshSectionTitles();
        RefreshList();
    }
}

// 此电脑页模板选择：分组头 → 分组标题行；普通项 → 图标+复选框+信息行
public sealed class ThisPcItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GroupHeaderTemplate { get; set; }
    public DataTemplate? ItemRowTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        if (item is BrowsePage.ItemVm { IsGroupHeader: true } && GroupHeaderTemplate is not null)
            return GroupHeaderTemplate;
        return ItemRowTemplate!;
    }

    protected override DataTemplate SelectTemplateCore(object item, Microsoft.UI.Xaml.DependencyObject container)
        => SelectTemplateCore(item);
}
