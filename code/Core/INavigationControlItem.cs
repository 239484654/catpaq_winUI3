// Copyright (c) Files Community. Licensed under the MIT License.
// 照抄 Files 4.2.3 (src\Files.App\Data\Contracts\INavigationControlItem.cs)
// 裁剪：LinuxDistro / FileTag 类型、CloudDrives / Network / WSL / FileTag / Home 分组（Catpaq 只用到 Pinned / Drives）。
using Catpaq.Controls;

namespace Catpaq.Core;

public interface INavigationControlItem : IComparable<INavigationControlItem>, INotifyPropertyChanged, ISidebarItemModel
{
	public string Text { get; }

	public string Path { get; }

	public SectionType Section { get; }

	public NavigationControlItemType ItemType { get; }

	public ContextMenuOptions MenuOptions { get; }
}

public enum NavigationControlItemType
{
	Drive,
	Location
}

public enum SectionType
{
	Pinned,
	Drives
}

public sealed class ContextMenuOptions
{
	public bool IsLocationItem { get; set; }

	public bool ShowUnpinItem { get; set; }

	public bool ShowProperties { get; set; }

	public bool ShowEjectDevice { get; set; }

	public bool ShowShellItems { get; set; }
}
