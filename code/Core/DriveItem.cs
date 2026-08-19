// 移植 Files 4.2.3 的 DriveItem：
// 保留 Path/Text/Icon/Children/懒加载子文件夹；裁剪 ByteSize、StorageFolder、云端/可弹出服务依赖。
using Catpaq.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;

namespace Catpaq.Core;

public sealed partial class DriveItem : ExpandableSidebarItemBase, INavigationControlItem
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

	public byte[]? IconData { get; set; }

	private string path = "";
	public string Path
	{
		get => path;
		set => path = value;
	}

	public string DeviceID { get; set; } = "";

	public NavigationControlItemType ItemType => NavigationControlItemType.Drive;

	public bool IsRemovable { get; set; }

	public bool IsNetwork { get; set; }

	public bool IsPinned { get; set; }

	public string TypeText { get; set; } = "";

	private string text = "";
	public string Text
	{
		get => text;
		set => SetProperty(ref text, value);
	}

	public SectionType Section { get; set; }

	public ContextMenuOptions MenuOptions { get; set; } = new();

	public object? Children => IsExpandableFolder ? (childItems ??= []) : null;
	private BulkConcurrentObservableCollection<INavigationControlItem>? childItems;

	protected override string ExpansionPath => path;
	protected override BulkConcurrentObservableCollection<INavigationControlItem> EnsureChildItems() => childItems ??= [];

	private object toolTip = "";
	public object ToolTip
	{
		get => toolTip;
		set => SetProperty(ref toolTip, value);
	}

	public IconElement? IconElement
	{
		get
		{
			if (Icon is null)
				return null;
			return new ImageIconSource { ImageSource = Icon }.CreateIconElement();
		}
	}

	public FrameworkElement? ItemDecorator => null;

	public string Id => Path;

	public string Name => Text;

	public int CompareTo(INavigationControlItem? other)
	{
		var result = Type.CompareTo((other as DriveItem)?.Type ?? System.IO.DriveType.Unknown);
		return result == 0 ? Text.CompareTo(other?.Text) : result;
	}

	public System.IO.DriveType Type { get; set; } = System.IO.DriveType.Unknown;

	// 根据驱动器信息创建（在 UI 线程调用）
	public static DriveItem CreateFromDriveInfo(DriveInfo drive)
	{
		var volumeLabel = drive.IsReady ? drive.VolumeLabel : "";
		// 卷标为空时显示"本地磁盘 (C:)"（Files 中文版同款，走 I18n 翻译）
		var localDisk = App.MainWindow?.T("thispc.local_disk", "Local Disk") ?? "本地磁盘";
		var displayName = string.IsNullOrEmpty(volumeLabel)
			? $"{localDisk} ({drive.Name.TrimEnd('\\')})"
			: volumeLabel;
		var item = new DriveItem
		{
			Path = drive.Name,
			DeviceID = drive.Name,
			Text = displayName,
			Type = drive.DriveType,
			ToolTip = drive.Name,
			MenuOptions = new ContextMenuOptions
			{
				IsLocationItem = true,
				ShowEjectDevice = drive.DriveType == System.IO.DriveType.Removable || drive.DriveType == System.IO.DriveType.CDRom,
				ShowShellItems = true,
				ShowProperties = true,
			},
		};
		return item;
	}
}
