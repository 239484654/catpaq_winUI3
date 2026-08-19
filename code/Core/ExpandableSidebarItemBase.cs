// 移植 Files 4.2.3 的 ExpandableSidebarItemBase：
// 保留懒加载子文件夹（展开时枚举一次），裁剪 FileSystemWatcher 实时监听 / App.Logger / MainWindow 依赖。
using Catpaq.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Catpaq.Core;

public abstract partial class ExpandableSidebarItemBase : ObservableObject
{
	protected abstract string ExpansionPath { get; }
	protected abstract BulkConcurrentObservableCollection<INavigationControlItem> EnsureChildItems();

	private bool isExpandableFolder;
	public bool IsExpandableFolder
	{
		get => isExpandableFolder;
		set
		{
			if (SetProperty(ref isExpandableFolder, value))
			{
				OnPropertyChanged(nameof(ISidebarItemModel.Children));
				OnPropertyChanged(nameof(IsLeafWithChildren));
				if (value && isExpanded && !childrenLoaded && !childrenLoading)
					_ = LoadSubfoldersAsync();
			}
		}
	}

	private bool hasUnrealizedChildren;
	public bool HasUnrealizedChildren
	{
		get => hasUnrealizedChildren;
		set => SetProperty(ref hasUnrealizedChildren, value);
	}

	public bool IsLeafWithChildren => IsExpandableFolder;

	private bool childrenLoaded;
	private bool childrenLoading;

	private bool isExpanded;
	public bool IsExpanded
	{
		get => isExpanded;
		set
		{
			if (!SetProperty(ref isExpanded, value))
				return;

			if (value)
			{
				if (IsExpandableFolder && !childrenLoaded && !childrenLoading)
					_ = LoadSubfoldersAsync();
			}
		}
	}

	public async Task LoadSubfoldersAsync()
	{
		if (childrenLoaded || childrenLoading)
			return;
		childrenLoading = true;
		try
		{
			await LocationItem.LoadSubfoldersIntoAsync(ExpansionPath, EnsureChildItems(), () =>
			{
				HasUnrealizedChildren = false;
				childrenLoaded = true;
			});
		}
		finally
		{
			childrenLoading = false;
		}
	}

	public bool IsLoaded => childrenLoaded;

	public async Task ReloadSubfoldersAsync()
	{
		if (!childrenLoaded)
			return;
		EnsureChildItems().Clear();
		childrenLoaded = false;
		if (IsExpanded && IsExpandableFolder)
			await LoadSubfoldersAsync();
	}
}
