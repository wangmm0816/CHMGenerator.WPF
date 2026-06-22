using System.Windows;
using System.Windows.Controls;
using CHMGenerator.WPF.Models;

namespace CHMGenerator.WPF.Helpers;

/// <summary>
/// 让 TreeView 支持 SelectedItem 双向绑定的附加属性
/// （WPF 原生 TreeView.SelectedItem 是只读的，不能直接 Binding）
/// </summary>
public static class TreeViewSelectedItemBinder
{
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.RegisterAttached(
            "SelectedItem",
            typeof(object),
            typeof(TreeViewSelectedItemBinder),
            new PropertyMetadata(null, OnSelectedItemChanged));

    public static object GetSelectedItem(DependencyObject obj) => obj.GetValue(SelectedItemProperty);
    public static void SetSelectedItem(DependencyObject obj, object value) => obj.SetValue(SelectedItemProperty, value);

    private static readonly DependencyProperty SelectedItemWatcherProperty =
        DependencyProperty.RegisterAttached(
            "SelectedItemWatcher",
            typeof(SelectedItemWatcher),
            typeof(TreeViewSelectedItemBinder),
            new PropertyMetadata(null));

    /// <summary>
    /// 当外部设置 SelectedItem 时，把对应 TreeViewItem 的 IsSelected 设为 true
    /// </summary>
    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView treeView) return;

        // 注册 TreeView 的 SelectedChanged 事件（只注册一次）
        var watcher = (SelectedItemWatcher?)treeView.GetValue(SelectedItemWatcherProperty);
        if (watcher == null)
        {
            watcher = new SelectedItemWatcher(treeView);
            treeView.SetValue(SelectedItemWatcherProperty, watcher);
        }

        // 程序代码改了 SelectedItem 时，要同步到 TreeView
        if (e.NewValue is DocumentNode newNode && !ReferenceEquals(e.OldValue, newNode))
        {
            newNode.IsSelected = true;
        }
    }

    /// <summary>
    /// 内部监听器：监听 TreeView.SelectedItem 变化，写回 SelectedItem 附加属性
    /// </summary>
    private class SelectedItemWatcher
    {
        private readonly TreeView _treeView;

        public SelectedItemWatcher(TreeView treeView)
        {
            _treeView = treeView;
            _treeView.SelectedItemChanged += OnSelectedItemChanged;
        }

        private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // 把 TreeView 实际选中的节点回写到附加属性
            // 用 SetValue 而不是 SetSelectedItem，避免再次触发 OnSelectedItemChanged
            _treeView.SetValue(SelectedItemProperty, e.NewValue);
        }
    }
}
