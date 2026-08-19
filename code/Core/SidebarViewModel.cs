// Copyright (c) Files Community. Licensed under the MIT License.
// 移植 Files 4.2.3 的 SidebarViewModel + SidebarViewModel.FlatTree：
// 保留 Pinned（快速访问钉住项）+ Drives（此电脑驱动器）两个分组 + FlatSidebarItems 扁平树；
// 裁剪 Home/库/网络/云盘/WSL/标签/回收站、右键菜单、拖拽排序、多标签展开记忆（TabExpansion）、
// Ioc/App 单例依赖（图标改 SystemIcon，导航改事件回调）。
using Catpaq.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Specialized;
using System.IO;

namespace Catpaq.Core;

public sealed partial class SidebarViewModel : ObservableObject, IDisposable
{
	public event EventHandler<INavigationControlItem>? ItemNavigationRequested;

	// 根级分组（Pinned / Drives 分组头）
	private readonly BulkConcurrentObservableCollection<INavigationControlItem> sidebarItems = [];
	public BulkConcurrentObservableCollection<INavigationControlItem> SidebarItems => sidebarItems;

	private readonly DispatcherQueue? dispatcherQueue = App.MainWindow?.DispatcherQueue;

	// 当前浏览路径（用于侧边栏选中高亮）
	private string _currentPath = "";
	public string CurrentPath
	{
		get => _currentPath;
		set
		{
			if (SetProperty(ref _currentPath, value))
				RefreshSelectionForCurrentPath();
		}
	}

	public SidebarViewModel()
	{
		_ = InitializeAsync();
	}

	// 异步填充分组数据（照抄 Files：Pinned = 系统快速访问钉住项；Drives = 系统驱动器）
	private async Task InitializeAsync()
	{
		var pinnedHeader = new LocationItem
		{
			Text = App.MainWindow?.T("sidebar.quick_access", "Quick access") ?? "快速访问",
			Path = "",
			IsHeader = true,
			Section = SectionType.Pinned,
			MenuOptions = new ContextMenuOptions { IsLocationItem = true },
		};
		pinnedHeader.IsExpandableFolder = true;
		pinnedHeader.HasUnrealizedChildren = false;
		pinnedHeader.IsExpanded = true;
		sidebarItems.Add(pinnedHeader);

		var drivesHeader = new LocationItem
		{
			Text = App.MainWindow?.T("sidebar.this_pc", "This PC") ?? "此电脑",
			Path = "",
			IsHeader = true,
			Section = SectionType.Drives,
			IconGlyph = "\uE7F4", // TVMonitor（Segoe Fluent Icons）—— Windows 11"此电脑"标准图标
			MenuOptions = new ContextMenuOptions { IsLocationItem = true },
		};
		drivesHeader.IsExpandableFolder = true;
		drivesHeader.HasUnrealizedChildren = false;
		drivesHeader.IsExpanded = true;
		sidebarItems.Add(drivesHeader);

		// 快速访问分组头图标：照抄 Files 的 Assets/FluentIcons/SidebarSections PNG；
		// 此电脑分组头直接用 Fluent 字形（IconGlyph），与地址栏一致
		LoadHeaderIcon(pinnedHeader, "Pinned");

		await LoadPinnedFoldersAsync(pinnedHeader);
		LoadDrives(drivesHeader);
	}

	private static void LoadHeaderIcon(LocationItem header, string assetName)
	{
		try
		{
			header.Icon = new BitmapImage(new Uri($"ms-appx:///Assets/FluentIcons/SidebarSections/{assetName}.png"));
		}
		catch
		{
		}
	}

	private async Task LoadPinnedFoldersAsync(LocationItem header)
	{
		try
		{
			foreach (var pf in await QuickAccessService.GetPinnedFoldersAsync())
			{
				var item = new LocationItem
				{
					Path = pf.Path,
					Text = pf.Name,
					Section = SectionType.Pinned,
					IsPinned = true,
					MenuOptions = new ContextMenuOptions { IsLocationItem = true, ShowUnpinItem = true, ShowShellItems = true },
				};
				item.Icon = await SystemIcon.GetIconAsync(pf.Path, isFolder: true);
				header.ChildItems ??= [];
				header.ChildItems.Add(item);
			}
		}
		catch
		{
		}
	}

	private void LoadDrives(LocationItem header)
	{
		foreach (var d in DriveInfo.GetDrives())
		{
			if (!d.IsReady)
				continue;
			var item = DriveItem.CreateFromDriveInfo(d);
			item.Section = SectionType.Drives;
			_ = LoadDriveIconAsync(item);
			header.ChildItems ??= [];
			header.ChildItems.Add(item);
		}
	}

	private async Task LoadDriveIconAsync(DriveItem item)
	{
		item.Icon = await SystemIcon.GetIconAsync(item.Path, isFolder: false);
	}

	// 分组头不导航；叶子项导航到其路径
	private void HandleItemInvoked(INavigationControlItem item)
	{
		if (item is LocationItem { IsHeader: true })
			return;
		ItemNavigationRequested?.Invoke(this, item);
	}

	public void NotifyItemInvoked(INavigationControlItem item) => HandleItemInvoked(item);

	// 供宿主导航后调用，高亮侧边栏中匹配当前路径的项
	public void UpdateSelectionForPath(string path)
	{
		CurrentPath = path;
	}

	// 语言切换时刷新分组头文案（Text setter 触发 PropertyChanged → 侧栏自动更新）
	public void RefreshSectionTitles()
	{
		foreach (var item in sidebarItems)
		{
			if (item is not LocationItem { IsHeader: true } header)
				continue;
			header.Text = header.Section == SectionType.Pinned
				? App.MainWindow?.T("sidebar.quick_access", "Quick access") ?? "快速访问"
				: App.MainWindow?.T("sidebar.this_pc", "This PC") ?? "此电脑";
		}
	}

	#region FlatTree（照抄 Files 的 SidebarViewModel.FlatTree.cs）

	private BulkConcurrentObservableCollection<FlatSidebarItem>? _flatSidebarItems;
	public BulkConcurrentObservableCollection<FlatSidebarItem> FlatSidebarItems
	{
		get
		{
			if (_flatSidebarItems is null)
				InitializeFlatTree();
			return _flatSidebarItems!;
		}
	}

	private SidebarDisplayMode _actualDisplayMode;
	public SidebarDisplayMode ActualDisplayMode
	{
		get => _actualDisplayMode;
		set => SetProperty(ref _actualDisplayMode, value);
	}

	private bool IsCompactDisplayMode => ActualDisplayMode == SidebarDisplayMode.Compact;

	private readonly Dictionary<ISidebarItemModel, FlatSidebarItem> _flatLookup = [];
	private readonly Dictionary<INotifyCollectionChanged, ISidebarItemModel> _flatChildCollectionParents = [];
	private readonly Dictionary<ISidebarItemModel, INotifyCollectionChanged> _flatChildCollectionByItem = [];

	private void InitializeFlatTree()
	{
		_flatSidebarItems = [];
		PropertyChanged += FlatTree_VMPropertyChanged;
		RebuildFlatTree();
		sidebarItems.CollectionChanged += FlatTree_SidebarItemsChanged;
	}

	private void CollectVisibleSubtree(ISidebarItemModel item, int depth, List<FlatSidebarItem> sink)
	{
		if (depth > 0 && IsCompactDisplayMode)
			return;
		sink.Add(new FlatSidebarItem(item, depth));
		if (!item.IsExpanded)
			return;
		foreach (var child in EnumerateChildren(item))
			CollectVisibleSubtree(child, depth + 1, sink);
	}

	private static IEnumerable<ISidebarItemModel> EnumerateChildren(ISidebarItemModel item)
		=> item.Children as IEnumerable<ISidebarItemModel> ?? Array.Empty<ISidebarItemModel>();

	private void RegisterNodes(IEnumerable<FlatSidebarItem> nodes)
	{
		foreach (var node in nodes)
		{
			_flatLookup[node.Item] = node;
			SubscribeFlatItem(node.Item);
		}
	}

	private void RebuildFlatTree()
	{
		if (_flatSidebarItems is null)
			return;
		_flatSidebarItems.BeginBulkOperation();
		try
		{
			foreach (var node in _flatSidebarItems)
				UnsubscribeFlatItem(node.Item);
			_flatSidebarItems.Clear();
			_flatLookup.Clear();
			_flatChildCollectionByItem.Clear();
			var batch = new List<FlatSidebarItem>();
			foreach (var section in sidebarItems)
				CollectVisibleSubtree(section, 0, batch);
			RegisterNodes(batch);
			_flatSidebarItems.AddRange(batch);
		}
		finally
		{
			_flatSidebarItems.EndBulkOperation();
		}
		UpdateSectionPredecessorFlags();
		RefreshSelectionForCurrentPath();
	}

	private void FlatTree_VMPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ActualDisplayMode))
			RebuildFlatTree();
	}

	private void FlatTree_SidebarItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (dispatcherQueue is null)
			return;
		_ = dispatcherQueue.TryEnqueue(async () => await HandleSidebarItemsChangedAsync(e));
	}

	private async Task HandleSidebarItemsChangedAsync(NotifyCollectionChangedEventArgs e)
	{
		switch (e.Action)
		{
			case NotifyCollectionChangedAction.Add:
				int insertIndex = e.NewStartingIndex >= 0 ? FlatIndexOfSection(e.NewStartingIndex) : FlatSidebarItems.Count;
				await BuildAndInsertChildrenAsync(insertIndex, CastModels(e.NewItems!), 0);
				break;
			case NotifyCollectionChangedAction.Remove:
				RemoveSubtrees(e.OldItems!);
				break;
			default:
				RebuildFlatTree();
				return;
		}
		UpdateSectionPredecessorFlags();
		RefreshSelectionForCurrentPath();
	}

	private int FlatIndexOfSection(int sectionIndex)
	{
		int seen = 0;
		for (int i = 0; i < FlatSidebarItems.Count; i++)
		{
			if (FlatSidebarItems[i].Depth != 0)
				continue;
			if (seen == sectionIndex)
				return i;
			seen++;
		}
		return FlatSidebarItems.Count;
	}

	private int FindSubtreeEnd(int start, int parentDepth)
	{
		int end = start + 1;
		while (end < FlatSidebarItems.Count && FlatSidebarItems[end].Depth > parentDepth)
			end++;
		return end;
	}

	private void RemoveSubtreeRange(int start, int end)
	{
		int count = end - start;
		if (count <= 0)
			return;
		for (int i = start; i < end; i++)
		{
			var removed = FlatSidebarItems[i];
			UnsubscribeFlatItem(removed.Item);
			_flatLookup.Remove(removed.Item);
		}
		FlatSidebarItems.RemoveRange(start, count);
	}

	private bool TryGetFlatPosition(ISidebarItemModel item, out int start, out FlatSidebarItem node)
	{
		if (_flatLookup.TryGetValue(item, out node!))
		{
			start = FlatSidebarItems.IndexOf(node);
			if (start >= 0)
				return true;
		}
		start = -1;
		node = null!;
		return false;
	}

	private void RemoveSubtrees(System.Collections.IList items)
	{
		foreach (var raw in items)
		{
			if (raw is ISidebarItemModel item && TryGetFlatPosition(item, out var start, out var node))
				RemoveSubtreeRange(start, FindSubtreeEnd(start, node.Depth));
		}
	}

	private static IEnumerable<ISidebarItemModel> CastModels(System.Collections.IList items)
	{
		foreach (var raw in items)
			if (raw is ISidebarItemModel item)
				yield return item;
	}

	private async Task BuildAndInsertChildrenAsync(int insertAt, IEnumerable<ISidebarItemModel> children, int childDepth)
	{
		List<FlatSidebarItem>? batch = null;
		foreach (var child in children)
		{
			if (_flatLookup.ContainsKey(child))
				continue;
			batch ??= [];
			CollectVisibleSubtree(child, childDepth, batch);
		}
		if (batch is not null)
			await InsertChunkedAsync(insertAt, batch);
	}

	private void SubscribeFlatItem(ISidebarItemModel item)
	{
		item.PropertyChanged += FlatTree_ItemPropertyChanged;
		if (item.Children is INotifyCollectionChanged notify && !_flatChildCollectionParents.ContainsKey(notify))
		{
			notify.CollectionChanged += FlatTree_ChildCollectionChanged;
			_flatChildCollectionParents[notify] = item;
			_flatChildCollectionByItem[item] = notify;
		}
	}

	private void UnsubscribeFlatItem(ISidebarItemModel item)
	{
		item.PropertyChanged -= FlatTree_ItemPropertyChanged;
		if (item.Children is INotifyCollectionChanged notify && _flatChildCollectionParents.Remove(notify))
		{
			_flatChildCollectionByItem.Remove(item);
			notify.CollectionChanged -= FlatTree_ChildCollectionChanged;
		}
	}

	private void ResubscribeChildren(ISidebarItemModel item)
	{
		if (_flatChildCollectionByItem.Remove(item, out var oldCollection))
		{
			_flatChildCollectionParents.Remove(oldCollection);
			oldCollection.CollectionChanged -= FlatTree_ChildCollectionChanged;
		}
		if (item.Children is INotifyCollectionChanged newCollection && !_flatChildCollectionParents.ContainsKey(newCollection))
		{
			newCollection.CollectionChanged += FlatTree_ChildCollectionChanged;
			_flatChildCollectionParents[newCollection] = item;
			_flatChildCollectionByItem[item] = newCollection;
		}
	}

	private void FlatTree_ItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not ISidebarItemModel item)
			return;

		if (e.PropertyName == nameof(ISidebarItemModel.Children) && _flatLookup.ContainsKey(item))
			ResubscribeChildren(item);

		if (e.PropertyName == nameof(ISidebarItemModel.IsExpanded) && dispatcherQueue is not null)
			_ = dispatcherQueue.TryEnqueue(async () => await HandleItemExpansionChangedAsync(item));
	}

	private async Task HandleItemExpansionChangedAsync(ISidebarItemModel item)
	{
		if (!TryGetFlatPosition(item, out var start, out var node))
			return;
		if (item.IsExpanded)
			await BuildAndInsertChildrenAsync(start + 1, EnumerateChildren(item), node.Depth + 1);
		else
			RemoveSubtreeRange(start + 1, FindSubtreeEnd(start, node.Depth));
		if (node.Depth == 0)
			UpdateSectionPredecessorFlags();
		RefreshSelectionForCurrentPath();
	}

	private void FlatTree_ChildCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (sender is not INotifyCollectionChanged notify)
			return;
		if (!_flatChildCollectionParents.TryGetValue(notify, out var parent))
			return;
		if (dispatcherQueue is null)
			return;
		_ = dispatcherQueue.TryEnqueue(async () => await HandleChildCollectionChangedAsync(parent, e));
	}

	private const int FlatTreeInsertChunkSize = 100;

	private async Task HandleChildCollectionChangedAsync(ISidebarItemModel parent, NotifyCollectionChangedEventArgs e)
	{
		if (!parent.IsExpanded || !TryGetFlatPosition(parent, out var parentStart, out var parentNode))
			return;
		int childDepth = parentNode.Depth + 1;

		switch (e.Action)
		{
			case NotifyCollectionChangedAction.Add:
				await BuildAndInsertChildrenAsync(
					FindChildInsertionIndex(parentStart, childDepth, e.NewStartingIndex),
					CastModels(e.NewItems!),
					childDepth);
				break;
			case NotifyCollectionChangedAction.Remove:
				RemoveSubtrees(e.OldItems!);
				break;
			default:
				RemoveSubtreeRange(parentStart + 1, FindSubtreeEnd(parentStart, parentNode.Depth));
				await BuildAndInsertChildrenAsync(parentStart + 1, EnumerateChildren(parent), childDepth);
				break;
		}
		UpdateSectionPredecessorFlags();
		RefreshSelectionForCurrentPath();
	}

	private async Task InsertChunkedAsync(int insertAt, List<FlatSidebarItem> batch)
	{
		if (batch.Count == 0)
			return;
		if (batch.Count <= FlatTreeInsertChunkSize)
		{
			RegisterNodes(batch);
			FlatSidebarItems.InsertRange(insertAt, batch);
			return;
		}
		var currentInsertAt = insertAt;
		for (int i = 0; i < batch.Count; i += FlatTreeInsertChunkSize)
		{
			var chunkEnd = Math.Min(i + FlatTreeInsertChunkSize, batch.Count);
			var chunk = batch.GetRange(i, chunkEnd - i);
			RegisterNodes(chunk);
			FlatSidebarItems.InsertRange(currentInsertAt, chunk);
			if (chunkEnd < batch.Count)
			{
				await Task.Delay(1);
				currentInsertAt = FlatSidebarItems.IndexOf(batch[chunkEnd - 1]) + 1;
				if (currentInsertAt <= 0)
					return;
			}
		}
	}

	private void RefreshSelectionForCurrentPath()
	{
		if (!string.IsNullOrEmpty(CurrentPath))
			UpdateSidebarSelectedItemFromArgs(CurrentPath);
	}

	// 选中与当前路径匹配的侧边栏项（只匹配叶子项，忽略分组头）
	private void UpdateSidebarSelectedItemFromArgs(string path)
	{
		var norm = path.TrimEnd('\\', '/');
		foreach (var flat in FlatSidebarItems)
		{
			var item = flat.Item;
			if (item is LocationItem { IsHeader: true })
				continue;
			var itemPath = item.Path?.TrimEnd('\\', '/');
			if (!string.IsNullOrEmpty(itemPath) &&
				string.Equals(itemPath, norm, StringComparison.OrdinalIgnoreCase))
			{
				SelectedItem = item;
				return;
			}
		}
	}

	private ISidebarItemModel? _selectedItem;
	public ISidebarItemModel? SelectedItem
	{
		get => _selectedItem;
		set => SetProperty(ref _selectedItem, value);
	}

	private void UpdateSectionPredecessorFlags()
	{
		if (_flatSidebarItems is null)
			return;
		bool prevWasExpanded = false;
		foreach (var node in _flatSidebarItems)
		{
			if (node.Depth != 0)
				continue;
			node.HasExpandedPredecessor = !IsCompactDisplayMode && prevWasExpanded;
			prevWasExpanded = node.Item.Children is null ? true : node.Item.IsExpanded;
		}
	}

	private int FindChildInsertionIndex(int parentStart, int childDepth, int sourceIndex)
	{
		int parentDepth = childDepth - 1;
		int countSeen = 0;
		int i = parentStart + 1;
		while (i < FlatSidebarItems.Count && FlatSidebarItems[i].Depth > parentDepth)
		{
			if (FlatSidebarItems[i].Depth == childDepth)
			{
				if (sourceIndex >= 0 && countSeen == sourceIndex)
					return i;
				countSeen++;
			}
			i++;
		}
		return i;
	}

	#endregion

	public void Dispose()
	{
		if (_flatSidebarItems is not null)
		{
			PropertyChanged -= FlatTree_VMPropertyChanged;
			sidebarItems.CollectionChanged -= FlatTree_SidebarItemsChanged;
			foreach (var node in _flatSidebarItems)
				UnsubscribeFlatItem(node.Item);
		}
	}
}
