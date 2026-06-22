using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CHMGenerator.WPF.Models;

namespace CHMGenerator.WPF.Helpers;

/// <summary>
/// TreeView 拖拽辅助类：触发拖拽事件，由 ViewModel 处理实际移动
/// </summary>
public static class TreeViewDragDropHelper
{
    public static readonly DependencyProperty EnableDragDropProperty =
        DependencyProperty.RegisterAttached("EnableDragDrop", typeof(bool), typeof(TreeViewDragDropHelper),
            new PropertyMetadata(false, OnEnableDragDropChanged));

    public static bool GetEnableDragDrop(DependencyObject obj) => (bool)obj.GetValue(EnableDragDropProperty);
    public static void SetEnableDragDrop(DependencyObject obj, bool value) => obj.SetValue(EnableDragDropProperty, value);

    /// <summary>
    /// 当用户拖拽完成后触发。参数：(draggedNode, targetNode)
    /// </summary>
    public static event Action<DocumentNode, DocumentNode>? NodeDropped;

    private static Point _startPoint;
    private static DocumentNode? _draggedNode;

    private static void OnEnableDragDropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeView treeView)
        {
            if ((bool)e.NewValue)
            {
                treeView.PreviewMouseLeftButtonDown += TreeView_PreviewMouseLeftButtonDown;
                treeView.PreviewMouseMove += TreeView_PreviewMouseMove;
                treeView.DragOver += TreeView_DragOver;
                treeView.Drop += TreeView_Drop;
            }
            else
            {
                treeView.PreviewMouseLeftButtonDown -= TreeView_PreviewMouseLeftButtonDown;
                treeView.PreviewMouseMove -= TreeView_PreviewMouseMove;
                treeView.DragOver -= TreeView_DragOver;
                treeView.Drop -= TreeView_Drop;
            }
        }
    }

    private static void TreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(null);
    }

    private static void TreeView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        var diff = _startPoint - pos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is TreeView treeView)
            {
                var hitItem = VisualHitTest<TreeViewItem>(treeView, _startPoint);
                if (hitItem?.DataContext is DocumentNode node)
                {
                    _draggedNode = node;
                    try
                    {
                        DragDrop.DoDragDrop(treeView, node, DragDropEffects.Move);
                    }
                    finally
                    {
                        _draggedNode = null;
                    }
                }
            }
        }
    }

    private static void TreeView_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedNode != null)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private static void TreeView_Drop(object sender, DragEventArgs e)
    {
        if (_draggedNode == null) return;

        if (sender is TreeView treeView)
        {
            var pos = e.GetPosition(treeView);
            var hitItem = VisualHitTest<TreeViewItem>(treeView, pos);
            var targetNode = hitItem?.DataContext as DocumentNode;

            if (targetNode != null && targetNode != _draggedNode && !IsAncestorOrSelf(_draggedNode, targetNode))
            {
                NodeDropped?.Invoke(_draggedNode, targetNode);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// 判断 maybeAncestor 是否是 node 的祖先或自身
    /// </summary>
    private static bool IsAncestorOrSelf(DocumentNode maybeAncestor, DocumentNode node)
    {
        var p = node;
        while (p != null)
        {
            if (p == maybeAncestor) return true;
            p = p.Parent;
        }
        return false;
    }

    /// <summary>
    /// 在 visual tree 中查找指定类型的祖先（包含命中测试）
    /// </summary>
    private static T? VisualHitTest<T>(DependencyObject reference, Point point) where T : DependencyObject
    {
        // VisualTreeHelper.HitTest 只接受 Visual，先检查类型
        if (reference is not Visual visual) return null;

        var hitResult = VisualTreeHelper.HitTest(visual, point);
        var current = hitResult?.VisualHit;

        while (current != null)
        {
            if (current is T t) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
