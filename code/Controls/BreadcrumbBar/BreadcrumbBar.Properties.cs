// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0
// 手写依赖属性（替代 Files 的 CommunityToolkit GeneratedDependencyProperty）

using Microsoft.UI.Xaml;

namespace Catpaq.Controls
{
    public partial class BreadcrumbBar
    {
        public static readonly DependencyProperty RootItemProperty =
            DependencyProperty.Register(nameof(RootItem), typeof(FrameworkElement), typeof(BreadcrumbBar), new PropertyMetadata(null, OnRootItemPropertyChanged));
        public FrameworkElement? RootItem
        {
            get => (FrameworkElement?)GetValue(RootItemProperty);
            set => SetValue(RootItemProperty, value);
        }

        // 根项内容为空时整项隐藏（Catpaq：地址栏左侧已有独立"此电脑"按钮，不再需要根项半圆）
        private static void OnRootItemPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BreadcrumbBar bar)
                bar.UpdateRootItemVisibility();
        }

        internal void UpdateRootItemVisibility()
        {
            if (_rootBreadcrumbBarItem is not null)
                _rootBreadcrumbBarItem.Visibility = RootItem is null ? Visibility.Collapsed : Visibility.Visible;
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(object), typeof(BreadcrumbBar), new PropertyMetadata(null, OnItemsSourcePropertyChanged));
        public object? ItemsSource
        {
            get => GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        // ItemsSource 变化后重新尝试订阅集合变化（模板可能晚于 ItemsSource 加载）
        private static void OnItemsSourcePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((BreadcrumbBar)d).TrySubscribeCollectionChanged();
        }

        public static readonly DependencyProperty ItemTemplateProperty =
            DependencyProperty.Register(nameof(ItemTemplate), typeof(object), typeof(BreadcrumbBar), new PropertyMetadata(null));
        public object? ItemTemplate
        {
            get => GetValue(ItemTemplateProperty);
            set => SetValue(ItemTemplateProperty, value);
        }

        public static readonly DependencyProperty EllipsisButtonToolTipProperty =
            DependencyProperty.Register(nameof(EllipsisButtonToolTip), typeof(string), typeof(BreadcrumbBar), new PropertyMetadata(null));
        public string? EllipsisButtonToolTip
        {
            get => (string?)GetValue(EllipsisButtonToolTipProperty);
            set => SetValue(EllipsisButtonToolTipProperty, value);
        }

        public static readonly DependencyProperty RootItemToolTipProperty =
            DependencyProperty.Register(nameof(RootItemToolTip), typeof(string), typeof(BreadcrumbBar), new PropertyMetadata(null));
        public string? RootItemToolTip
        {
            get => (string?)GetValue(RootItemToolTipProperty);
            set => SetValue(RootItemToolTipProperty, value);
        }

        public static readonly DependencyProperty RootItemChevronToolTipProperty =
            DependencyProperty.Register(nameof(RootItemChevronToolTip), typeof(string), typeof(BreadcrumbBar), new PropertyMetadata(null));
        public string? RootItemChevronToolTip
        {
            get => (string?)GetValue(RootItemChevronToolTipProperty);
            set => SetValue(RootItemChevronToolTipProperty, value);
        }
    }
}
