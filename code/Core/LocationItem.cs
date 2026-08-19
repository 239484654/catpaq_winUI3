// Copyright (c) Files Community. Licensed under the MIT License.
// 移植 Files 4.2.3 的 LocationItem：
// 保留 Path/Text/Icon/Children/IsExpanded/懒加载子文件夹；
// 裁剪 Ioc、IUserSettingsService、MainWindow 依赖，图标改用 SystemIcon 提取。
using Catpaq.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;

namespace Catpaq.Core;

public partial class LocationItem : ExpandableSidebarItemBase, INavigationControlItem
{
	private BitmapImage? icon;
	public BitmapImage? Icon
	{
		get => icon;
		set
		{
			SetProperty(ref icon, value);
			OnPropertyChanged(nameof(IconElement));
		}
	}

	// 分组头等场合用 Fluent 字形图标（如"此电脑" TVMonitor U+E7F4），优先级高于 Icon
	private string? iconGlyph;
	public string? IconGlyph
	{
		get => iconGlyph;
		set
		{
			iconGlyph = value;
			OnPropertyChanged(nameof(IconElement));
		}
	}

	public byte[]? IconData { get; set; }

	private string text = "";
	public string Text
	{
		get => text;
		set
		{
			if (text == value)
				return;
			text = value;
			// Just in case path hasn't been set
			if (ToolTip is "")
				ToolTip = value;
			OnPropertyChanged(nameof(Text));
		}
	}

	private string path = "";
	public string Path
	{
		get => path;
		set
		{
			path = value;
			ToolTip = string.IsNullOrEmpty(Path) ||
				Path.Contains('?', StringComparison.Ordinal) ||
				Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
				Path.EndsWith(".library-ms", StringComparison.OrdinalIgnoreCase) ||
				Path == "Home" ||
				Path == "ReleaseNotes" ||
				Path == "Settings"
				? Text
				: Path;
		}
	}

	public NavigationControlItemType ItemType => NavigationControlItemType.Location;

	public bool IsDefaultLocation { get; set; }

	public object? Children
	{
		get
		{
			if (IsExpandableFolder)
				return ChildItems ??= [];
			return ChildItems;
		}
	}
	public BulkConcurrentObservableCollection<INavigationControlItem>? ChildItems { get; set; }

	protected override string ExpansionPath => path;
	protected override BulkConcurrentObservableCollection<INavigationControlItem> EnsureChildItems() => ChildItems ??= [];

	public IconElement? IconElement
	{
		get
		{
			if (!string.IsNullOrEmpty(IconGlyph))
				return new FontIcon
				{
					Glyph = IconGlyph,
					FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Microsoft.UI.Xaml.Application.Current.Resources["SymbolThemeFontFamily"],
					FontSize = 16,
				};
			if (Icon is null)
				return null;
			return new ImageIconSource { ImageSource = Icon }.CreateIconElement();
		}
	}

	public bool SelectsOnInvoked { get; set; } = true;

	public bool IsHidden { get; set; }

	public bool IsInvalid { get; set; } = false;

	public bool IsPinned { get; set; }

	public SectionType Section { get; set; }

	public ContextMenuOptions MenuOptions { get; set; } = new();

	public bool IsHeader { get; set; }

	private object toolTip = "";
	public virtual object ToolTip
	{
		get => toolTip;
		set => SetProperty(ref toolTip, value);
	}

	public FrameworkElement? ItemDecorator => null;

	public int CompareTo(INavigationControlItem? other)
		=> Text.CompareTo(other?.Text);

	public static T Create<T>() where T : LocationItem, new()
	{
		return new T();
	}

	// 枚举子文件夹（后台线程），图标先用通用文件夹图标占位，然后逐项升级（照抄 Files 两阶段）
	internal static async Task LoadSubfoldersIntoAsync(string enumerationPath, BulkConcurrentObservableCollection<INavigationControlItem> target, Action onLoaded)
	{
		try
		{
			var entries = await Task.Run(() => FolderHelpers.EnumerateSubfolders(enumerationPath, showHidden: false, showProtected: false, showDot: false));
			onLoaded();
			foreach (var entry in entries)
				target.Add(CreateSubfolder(entry));
		}
		catch
		{
			onLoaded();
		}
	}

	internal static LocationItem CreateSubfolder(SubfolderEntry entry)
	{
		return new LocationItem
		{
			Path = entry.Path,
			Text = entry.Name,
			IsExpandableFolder = true,
			HasUnrealizedChildren = entry.HasSubfolders,
			IsHidden = entry.IsHidden,
			MenuOptions = new ContextMenuOptions { IsLocationItem = true, ShowProperties = true, ShowShellItems = true },
		};
	}
}
