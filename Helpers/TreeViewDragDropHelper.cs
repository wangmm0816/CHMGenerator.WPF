using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CHMGenerator.WPF.Models;

namespace CHMGenerator.WPF.Helpers;

/// <summary>
/// TreeView 拖拽辅助类：
/// 1. 支持内部节点拖拽调整层级
/// 2. 支持从外部拖入文件（HTML/Word）到指定文件夹下
/// 3. 支持点击空白区域取消选中
/// </summary>
public static class TreeViewDragDropHelper
{
    public static readonly DependencyProperty EnableDragDropProperty =
        DependencyProperty.RegisterAttached("EnableDragDrop", typeof(bool), typeof(TreeViewDragDropHelper),
            new PropertyMetadata(false, OnEnableDragDropChanged));

    public static bool GetEnableDragDrop(DependencyObject obj) => (bool)obj.GetValue(EnableDragDropProperty);
    public static void SetEnableDragDrop(DependencyObject obj, bool value) => obj.SetValue(EnableDragDropProperty, value);

    /// <summary>内部节点拖拽完成时触发。参数：(draggedNode, targetNode)</summary>
    public static event Action<DocumentNode, DocumentNode>? NodeDropped;

    /// <summary>外部文件拖入时触发。参数：(filePaths, targetFolderOrNull)</summary>
    public static event Action<string[], DocumentNode?>? ExternalFilesDropped;

    /// <summary>点击空白区域时触发（用于取消选中）</summary>
    public static event Action? BlankAreaClicked;

    // 拖拽数据格式标识
    private const string DragDataFormat = "CHMGenerator.DocumentNode";

    private static Point _startPoint;
    private static DocumentNode? _draggedNode;
    private static bool _isDragging;

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
                treeView.AllowDrop = true;
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

        // 检测是否点击了空白区域
        if (sender is TreeView treeView)
        {
            var pos = e.GetPosition(treeView);
            var hitItem = VisualHitTest<TreeViewItem>(treeView, pos);
            if (hitItem == null)
            {
                BlankAreaClicked?.Invoke();
            }
        }
    }

    private static void TreeView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_isDragging) return;

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
                    _isDragging = true;

                    // 用 DataObject 同时存自定义格式和节点引用，确保 Drop 时能取到
                    var data = new DataObject();
                    data.SetData(DragDataFormat, node);
                    // 同时塞一个非 null 的 string，避免某些情况下 DataObject 判空
                    data.SetData(DataFormats.Text, node.Title);

                    try
                    {
                        DragDrop.DoDragDrop(treeView, data, DragDropEffects.Move);
                    }
                    finally
                    {
                        _draggedNode = null;
                        _isDragging = false;
                    }
                }
            }
        }
    }

    private static void TreeView_DragOver(object sender, DragEventArgs e)
    {
        // 内部节点拖拽
        var draggedNode = e.Data.GetDataPresent(DragDataFormat)
            ? e.Data.GetData(DragDataFormat) as DocumentNode
            : _draggedNode;

        if (draggedNode != null)
        {
            // 判断目标是否合法（不能拖到自己或自己的子节点上）
            var targetNode = FindTargetNode(sender, e);
            if (targetNode == null)
            {
                // 没命中节点，但允许 Drop（移动到根）
                e.Effects = DragDropEffects.Move;
            }
            else if (targetNode == draggedNode || IsAncestorOrSelf(draggedNode, targetNode))
            {
                // 拖到自己或子节点上，禁止
                e.Effects = DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.Move;
            }
            e.Handled = true;
            return;
        }

        // 外部文件拖入
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
    }

    private static void TreeView_Drop(object sender, DragEventArgs e)
    {
        if (sender is TreeView treeView)
        {
            var targetNode = FindTargetNode(sender, e);

            // 1. 内部节点拖拽
            var draggedNode = e.Data.GetDataPresent(DragDataFormat)
                ? e.Data.GetData(DragDataFormat) as DocumentNode
                : _draggedNode;

            if (draggedNode != null)
            {
                if (targetNode == null)
                {
                    // 拖到空白处：移动到根
                    // 用 null 作为 targetNode 通知 ViewModel，由 ViewModel 处理
                    NodeDroppedToRoot?.Invoke(draggedNode);
                    e.Handled = true;
                }
                else if (targetNode != draggedNode && !IsAncestorOrSelf(draggedNode, targetNode))
                {
                    NodeDropped?.Invoke(draggedNode, targetNode);
                    e.Handled = true;
                }
                return;
            }

            // 2. 外部文件拖入
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    DocumentNode? targetFolder = null;
                    if (targetNode != null)
                    {
                        targetFolder = targetNode.IsFolder ? targetNode : targetNode.Parent;
                    }

                    ExternalFilesDropped?.Invoke(files, targetFolder);
                    e.Handled = true;
                }
            }
        }
    }

    /// <summary>内部节点拖拽到空白处时触发（移动到根）</summary>
    public static event Action<DocumentNode>? NodeDroppedToRoot;

    /// <summary>
    /// 从 DragEventArgs 找到当前命中的 TreeViewItem 对应的 DocumentNode
    /// </summary>
    private static DocumentNode? FindTargetNode(object sender, DragEventArgs e)
    {
        if (sender is TreeView treeView)
        {
            var pos = e.GetPosition(treeView);
            var hitItem = VisualHitTest<TreeViewItem>(treeView, pos);
            return hitItem?.DataContext as DocumentNode;
        }
        return null;
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

