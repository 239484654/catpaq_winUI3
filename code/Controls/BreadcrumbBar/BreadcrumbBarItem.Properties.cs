// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0
// 手写依赖属性（替代 Files 的 CommunityToolkit GeneratedDependencyProperty）

using Microsoft.UI.Xaml;

namespace Catpaq.Controls
{
    public partial class BreadcrumbBarItem
    {
        public static readonly DependencyProperty IsEllipsisProperty =
            DependencyProperty.Register(nameof(IsEllipsis), typeof(bool), typeof(BreadcrumbBarItem), new PropertyMetadata(false, OnIsEllipsisChanged));
        public bool IsEllipsis
        {
            get => (bool)GetValue(IsEllipsisProperty);
            set => SetValue(IsEllipsisProperty, value);
        }

        public static readonly DependencyProperty IsLastItemProperty =
            DependencyProperty.Register(nameof(IsLastItem), typeof(bool), typeof(BreadcrumbBarItem), new PropertyMetadata(false));
        public bool IsLastItem
        {
            get => (bool)GetValue(IsLastItemProperty);
            set => SetValue(IsLastItemProperty, value);
        }

        public static readonly DependencyProperty IsChevronVisibleProperty =
            DependencyProperty.Register(nameof(IsChevronVisible), typeof(bool), typeof(BreadcrumbBarItem), new PropertyMetadata(true, OnIsChevronVisibleChanged));
        public bool IsChevronVisible
        {
            get => (bool)GetValue(IsChevronVisibleProperty);
            set => SetValue(IsChevronVisibleProperty, value);
        }

        public static readonly DependencyProperty ItemToolTipProperty =
            DependencyProperty.Register(nameof(ItemToolTip), typeof(string), typeof(BreadcrumbBarItem), new PropertyMetadata(null));
        public string? ItemToolTip
        {
            get => (string?)GetValue(ItemToolTipProperty);
            set => SetValue(ItemToolTipProperty, value);
        }

        public static readonly DependencyProperty ChevronToolTipProperty =
            DependencyProperty.Register(nameof(ChevronToolTip), typeof(string), typeof(BreadcrumbBarItem), new PropertyMetadata(null));
        public string? ChevronToolTip
        {
            get => (string?)GetValue(ChevronToolTipProperty);
            set => SetValue(ChevronToolTipProperty, value);
        }

        private static void OnIsEllipsisChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((BreadcrumbBarItem)d).UpdateChevronVisibilityState();

        private static void OnIsChevronVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((BreadcrumbBarItem)d).UpdateChevronVisibilityState();

        private void UpdateChevronVisibilityState()
        {
            var visible = !IsEllipsis && IsChevronVisible;
            VisualStateManager.GoToState(this, visible ? "ChevronVisible" : "ChevronCollapsed", true);
        }
    }
}
