// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Catpaq.Controls
{
	public partial class BreadcrumbBar : Control
	{
		// Constants

		private const string TemplatePartName_RootBreadcrumbBarItem = "PART_RootBreadcrumbBarItem";
		private const string TemplatePartName_EllipsisBreadcrumbBarItem = "PART_EllipsisBreadcrumbBarItem";
		private const string TemplatePartName_MainItemsRepeater = "PART_MainItemsRepeater";

		// Fields

		private readonly BreadcrumbBarLayout _itemsRepeaterLayout;

		private BreadcrumbBarItem? _rootBreadcrumbBarItem;
		private BreadcrumbBarItem? _ellipsisBreadcrumbBarItem;
		private BreadcrumbBarItem? _lastBreadcrumbBarItem;
		private ItemsRepeater? _itemsRepeater;

		private bool _isEllipsisRendered;

		// Properties

		public int IndexAfterEllipsis
			=> _itemsRepeaterLayout.IndexAfterEllipsis;

		// Events

		public event TypedEventHandler<BreadcrumbBar, BreadcrumbBarItemClickedEventArgs>? ItemClicked;
		public event EventHandler<BreadcrumbBarItemDropDownFlyoutEventArgs>? ItemDropDownFlyoutOpening;
		public event EventHandler<BreadcrumbBarItemDropDownFlyoutEventArgs>? ItemDropDownFlyoutClosed;

		// Constructor

		public BreadcrumbBar()
		{
			DefaultStyleKey = typeof(BreadcrumbBar);

			_itemsRepeaterLayout = new(this);
		}

		// Methods

		protected override void OnApplyTemplate()
		{
			base.OnApplyTemplate();

			_rootBreadcrumbBarItem = GetTemplateChild(TemplatePartName_RootBreadcrumbBarItem) as BreadcrumbBarItem
				?? throw new MissingFieldException($"Could not find {TemplatePartName_RootBreadcrumbBarItem} in the given {nameof(BreadcrumbBar)}'s style.");
			_ellipsisBreadcrumbBarItem = GetTemplateChild(TemplatePartName_EllipsisBreadcrumbBarItem) as BreadcrumbBarItem
				?? throw new MissingFieldException($"Could not find {TemplatePartName_EllipsisBreadcrumbBarItem} in the given {nameof(BreadcrumbBar)}'s style.");
			_itemsRepeater = GetTemplateChild(TemplatePartName_MainItemsRepeater) as ItemsRepeater
				?? throw new MissingFieldException($"Could not find {TemplatePartName_MainItemsRepeater} in the given {nameof(BreadcrumbBar)}'s style.");

			_rootBreadcrumbBarItem.SetOwner(this);
			_ellipsisBreadcrumbBarItem.SetOwner(this);
			_itemsRepeater.Layout = _itemsRepeaterLayout;

			_itemsRepeater.ElementPrepared += ItemsRepeater_ElementPrepared;
			_itemsRepeater.ElementClearing += ItemsRepeater_ElementClearing;
			// ItemsSource 可能晚于模板加载才设置（如代码动态赋值），
			// 不能直接订阅（ItemsSourceView 为 null 会抛空引用）。
			// 订阅时机：模板加载完成时 + ItemsSourceProperty 变化时（见 BreadcrumbBar.Properties.cs）。
			SubscribeCollectionChanged();
			UpdateRootItemVisibility();
		}

		// 供 ItemsSourceProperty 回调调用：ItemsSource 变化后重新尝试订阅集合变化
		internal void TrySubscribeCollectionChanged()
		{
			_collectionSubscribed = false;
			SubscribeCollectionChanged();
		}

		private bool _collectionSubscribed;

		// 在 ItemsSourceView 可用时订阅集合变化（避免 null 订阅）
		private void SubscribeCollectionChanged()
		{
			if (_collectionSubscribed || _itemsRepeater is null)
				return;
			if (_itemsRepeater.ItemsSourceView is { } view)
			{
				view.CollectionChanged += ItemsSourceView_CollectionChanged;
				_collectionSubscribed = true;
			}
		}

		internal protected virtual void RaiseItemClickedEvent(BreadcrumbBarItem item, PointerRoutedEventArgs? pointerRoutedEventArgs = null)
		{
			var index = _itemsRepeater?.GetElementIndex(item) ?? throw new ArgumentNullException($"{_itemsRepeater} is null.");
			var eventArgs = new BreadcrumbBarItemClickedEventArgs(item, index, item == _rootBreadcrumbBarItem, pointerRoutedEventArgs);
			ItemClicked?.Invoke(this, eventArgs);
		}

		internal protected virtual void RaiseItemDropDownFlyoutOpening(BreadcrumbBarItem item, MenuFlyout flyout)
		{
			var index = _itemsRepeater?.GetElementIndex(item) ?? throw new ArgumentNullException($"{_itemsRepeater} is null.");
			ItemDropDownFlyoutOpening?.Invoke(this, new(flyout, item, index, item == _rootBreadcrumbBarItem));
		}

		internal protected virtual void RaiseItemDropDownFlyoutClosed(BreadcrumbBarItem item, MenuFlyout flyout)
		{
			var index = _itemsRepeater?.GetElementIndex(item) ?? throw new ArgumentNullException($"{_itemsRepeater} is null.");
			ItemDropDownFlyoutClosed?.Invoke(this, new(flyout, item, index, item == _rootBreadcrumbBarItem));
		}

		internal protected virtual void OnLayoutUpdated()
		{
			if (_itemsRepeater is null || (_itemsRepeaterLayout.IndexAfterEllipsis > _itemsRepeaterLayout.VisibleItemsCount && _isEllipsisRendered))
				return;

			if (_ellipsisBreadcrumbBarItem is not null && _isEllipsisRendered != _itemsRepeaterLayout.EllipsisIsRendered)
				_ellipsisBreadcrumbBarItem.Visibility = _itemsRepeaterLayout.EllipsisIsRendered ? Visibility.Visible : Visibility.Collapsed;

			_isEllipsisRendered = _itemsRepeaterLayout.EllipsisIsRendered;

			for (int accessibilityIndex = 0, collectionIndex = _itemsRepeaterLayout.IndexAfterEllipsis;
				accessibilityIndex < _itemsRepeaterLayout.VisibleItemsCount;
				accessibilityIndex++, collectionIndex++)
			{
				if (_itemsRepeater.TryGetElement(collectionIndex) is { } element)
				{
					element.SetValue(AutomationProperties.PositionInSetProperty, accessibilityIndex);
					element.SetValue(AutomationProperties.SizeOfSetProperty, _itemsRepeaterLayout.VisibleItemsCount);
				}
			}
		}

		internal bool TryGetElement(int index, out BreadcrumbBarItem? item)
		{
			item = null;

			if (_itemsRepeater is null)
				return false;

			item = _itemsRepeater.TryGetElement(index) as BreadcrumbBarItem;

			return item is not null;
		}

		// Event methods

		private void ItemsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
		{
			if (args.Element is not BreadcrumbBarItem item || _itemsRepeater is null)
				return;

			item.IsLastItem = false;
			item.IsEllipsis = false;

			if (args.Index == _itemsRepeater.ItemsSourceView.Count - 1)
			{
				_lastBreadcrumbBarItem = item;
				_lastBreadcrumbBarItem.IsLastItem = true;
			}

			item.SetOwner(this);
		}

		private void ItemsSourceView_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			if (_lastBreadcrumbBarItem is not null)
				_lastBreadcrumbBarItem.IsLastItem = false;

			if (e.NewItems is not null &&
				e.NewItems.Count > 0 &&
				e.NewItems[e.NewItems.Count - 1] is BreadcrumbBarItem item)
			{
				_lastBreadcrumbBarItem = item;
				item.IsLastItem = true;
			}
		}

		private void ItemsRepeater_ElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
		{
			if (args.Element is BreadcrumbBarItem item)
			{
				item.IsLastItem = false;
				item.IsEllipsis = false;
			}
		}
	}
}
