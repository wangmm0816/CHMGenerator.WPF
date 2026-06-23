using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CHMGenerator.WPF.Models;

namespace CHMGenerator.WPF.Helpers;

/// <summary>
/// 拖拽时相对于目标节点的位置。
/// </summary>
public enum DropPosition
{
    /// <summary>插到目标节点前面（同级）</summary>
    Before,
    /// <summary>插到目标节点后面（同级）</summary>
    After,
    /// <summary>作为目标节点的子节点（仅文件夹有效）</summary>
    Inside
}

/// <summary>
/// TreeView 拖拽辅助类（v4）：
/// 关键修复点：
/// 1. 修复坐标系 bug：_startPoint 改为相对 treeView 的本地坐标（v3 用屏幕坐标导致命中错误节点）
/// 2. 加 Shift/Ctrl 修饰键：Shift=强制同级，Ctrl=强制作为子节点
/// 3. 加 DropPositionChanged 事件，让 MainWindow 可以在状态栏显示当前落点提示
/// 4. Adorner 加文字标签，明确显示"插入到 X 前/后"或"作为 X 的子节点"
/// </summary>
public static class TreeViewDragDropHelper
{
    public static readonly DependencyProperty EnableDragDropProperty =
        DependencyProperty.RegisterAttached("EnableDragDrop", typeof(bool), typeof(TreeViewDragDropHelper),
            new PropertyMetadata(false, OnEnableDragDropChanged));

    public static bool GetEnableDragDrop(DependencyObject obj) => (bool)obj.GetValue(EnableDragDropProperty);
    public static void SetEnableDragDrop(DependencyObject obj, bool value) => obj.SetValue(EnableDragDropProperty, value);

    /// <summary>内部节点拖拽完成时触发。参数：(draggedNode, targetNode, dropPosition)</summary>
    public static event Action<DocumentNode, DocumentNode, DropPosition>? NodeDropped;

    /// <summary>内部节点拖拽到 TreeView 真正空白处时触发（移动到根末尾）</summary>
    public static event Action<DocumentNode>? NodeDroppedToRoot;

    /// <summary>外部文件拖入时触发。参数：(filePaths, targetFolderOrNull)</summary>
    public static event Action<string[], DocumentNode?>? ExternalFilesDropped;

    /// <summary>点击空白区域时触发（用于取消选中）</summary>
    public static event Action? BlankAreaClicked;

    /// <summary>
    /// 拖拽过程中落点变化时触发，参数：(draggedNode, targetNode, position, description)
    /// description 是给用户看的中文描述，MainWindow 可以显示在状态栏。
    /// </summary>
    public static event Action<DocumentNode, DocumentNode?, DropPosition, string>? DropPositionChanged;

    /// <summary>拖拽结束（Drop 或取消）时触发，MainWindow 用于清空状态栏提示</summary>
    public static event Action? DragEnded;

    // 拖拽数据格式标识
    private const string DragDataFormat = "CHMGenerator.DocumentNode";

    // 上下区域判定为 Before/After，中间区域为 Inside（仅文件夹）
    // 0.30 让前后区域各占 30%，中间区域占 40%，更容易触发同级插入
    private const double EdgeRatio = 0.30;

    // 拖拽起点（相对 TreeView 的本地坐标）+ 状态
    private static Point _startPointLocal;
    private static DocumentNode? _draggedNode;
    private static bool _isDragging;

    // 全局开关：是否输出调试日志
    private const bool EnableDebugLog = true;

    private static void Log(string message)
    {
        if (EnableDebugLog)
        {
            Debug.WriteLine($"[DragDrop] {message}");
        }
    }

    private static void OnEnableDragDropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeView treeView)
        {
            if ((bool)e.NewValue)
            {
                treeView.PreviewMouseLeftButtonDown += TreeView_PreviewMouseLeftButtonDown;
                treeView.PreviewMouseMove += TreeView_PreviewMouseMove;
                treeView.DragOver += TreeView_DragOver;
                treeView.DragLeave += TreeView_DragLeave;
                treeView.Drop += TreeView_Drop;
                treeView.AllowDrop = true;
            }
            else
            {
                treeView.PreviewMouseLeftButtonDown -= TreeView_PreviewMouseLeftButtonDown;
                treeView.PreviewMouseMove -= TreeView_PreviewMouseMove;
                treeView.DragOver -= TreeView_DragOver;
                treeView.DragLeave -= TreeView_DragLeave;
                treeView.Drop -= TreeView_Drop;
            }
        }
    }

    private static void TreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeView treeView)
        {
            // 关键修复：用相对 treeView 的本地坐标，不再用 e.GetPosition(null) 的屏幕坐标
            _startPointLocal = e.GetPosition(treeView);

            // 检测是否点击了空白区域
            var hitItem = FindTreeViewItem(treeView, _startPointLocal);
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
        if (sender is not TreeView treeView) return;

        // 用相对 treeView 的本地坐标做差值判断（跟 _startPointLocal 同坐标系）
        var pos = e.GetPosition(treeView);
        var diff = _startPointLocal - pos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            // 关键修复：现在 _startPointLocal 是相对 treeView 的本地坐标，
            // InputHitTest 接受的就是相对坐标，命中会正确
            var hitItem = FindTreeViewItem(treeView, _startPointLocal);
            if (hitItem?.DataContext is DocumentNode node)
            {
                Log($"Start drag: '{node.Title}' at local ({_startPointLocal.X:F0},{_startPointLocal.Y:F0})");
                _draggedNode = node;
                _isDragging = true;

                var data = new DataObject();
                data.SetData(DragDataFormat, node);
                data.SetData(DataFormats.Text, node.Title);

                try
                {
                    DragDrop.DoDragDrop(treeView, data, DragDropEffects.Move);
                }
                finally
                {
                    RemoveDropAdorner();
                    DragEnded?.Invoke();
                    _draggedNode = null;
                    _isDragging = false;
                }
            }
            else
            {
                Log($"Start drag: hitItem={hitItem?.GetType().Name ?? "null"}, no DocumentNode");
            }
        }
    }

    private static void TreeView_DragOver(object sender, DragEventArgs e)
    {
        var draggedNode = e.Data.GetDataPresent(DragDataFormat)
            ? e.Data.GetData(DragDataFormat) as DocumentNode
            : _draggedNode;

        if (draggedNode != null)
        {
            if (sender is TreeView treeView)
            {
                var (targetItem, targetNode, position) = ResolveDropTarget(treeView, e, draggedNode);

                if (targetNode == null)
                {
                    // 命中空白处 → 移动到根
                    e.Effects = DragDropEffects.Move;
                    ShowDropAdorner(treeView, null, DropPosition.Inside);
                    RaiseDropPositionChanged(draggedNode, null, DropPosition.Inside, "移到根目录末尾");
                    Log("DragOver: blank area (move to root)");
                }
                else if (targetNode == draggedNode || IsAncestorOrSelf(draggedNode, targetNode))
                {
                    e.Effects = DragDropEffects.None;
                    RemoveDropAdorner();
                    RaiseDropPositionChanged(draggedNode, targetNode, DropPosition.Inside, "无法拖到自身或子节点");
                    Log($"DragOver: forbidden (self/ancestor of '{targetNode.Title}')");
                }
                else
                {
                    e.Effects = DragDropEffects.Move;
                    ShowDropAdorner(treeView, targetItem, position);
                    RaiseDropPositionChanged(draggedNode, targetNode, position, DescribePosition(targetNode, position));
                    Log($"DragOver: target='{targetNode.Title}' pos={position} mods={Keyboard.Modifiers}");
                }
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

    private static void TreeView_DragLeave(object sender, DragEventArgs e)
    {
        RemoveDropAdorner();
        Log("DragLeave");
    }

    private static void TreeView_Drop(object sender, DragEventArgs e)
    {
        Log("=== Drop ===");
        if (sender is TreeView treeView)
        {
            try
            {
                var draggedNode = e.Data.GetDataPresent(DragDataFormat)
                    ? e.Data.GetData(DragDataFormat) as DocumentNode
                    : _draggedNode;

                if (draggedNode != null)
                {
                    var (targetItem, targetNode, position) = ResolveDropTarget(treeView, e, draggedNode);

                    Log($"Drop: dragged='{draggedNode.Title}', target='{targetNode?.Title ?? "<null>"}', pos={position}");

                    if (targetNode == null)
                    {
                        NodeDroppedToRoot?.Invoke(draggedNode);
                        e.Handled = true;
                    }
                    else if (targetNode != draggedNode && !IsAncestorOrSelf(draggedNode, targetNode))
                    {
                        NodeDropped?.Invoke(draggedNode, targetNode, position);
                        e.Handled = true;
                    }
                    else
                    {
                        Log("Drop rejected (self/ancestor)");
                    }
                    return;
                }

                // 外部文件拖入：draggedNode 为 null，需要单独解析目标
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        var pos2 = e.GetPosition(treeView);
                        var hitItem2 = FindTreeViewItem(treeView, pos2);
                        DocumentNode? hitNode2 = hitItem2?.DataContext as DocumentNode;

                        DocumentNode? targetFolder = null;
                        if (hitNode2 != null)
                        {
                            targetFolder = hitNode2.IsFolder ? hitNode2 : hitNode2.Parent;
                        }

                        ExternalFilesDropped?.Invoke(files, targetFolder);
                        e.Handled = true;
                    }
                }
            }
            finally
            {
                RemoveDropAdorner();
            }
        }
    }

    /// <summary>
    /// 根据鼠标位置 + 键盘修饰键算出 (命中的 TreeViewItem, 对应 DocumentNode, DropPosition)。
    /// 
    /// 默认规则（无修饰键）：
    ///   - 命中节点的上半 EdgeRatio → Before
    ///   - 命中节点的下半 EdgeRatio → After
    ///   - 命中节点的中间区域：
    ///       * 目标是文件夹 → Inside
    ///       * 目标是文件 → After
    /// 
    /// 修饰键覆盖规则：
    ///   - 按住 Shift → 强制同级（Before/After，按上下半区判断）
    ///   - 按住 Ctrl  → 强制 Inside（仅文件夹有效；如果是文件则退化为 After）
    /// </summary>
    private static (TreeViewItem? item, DocumentNode? node, DropPosition pos) ResolveDropTarget(
        TreeView treeView, DragEventArgs e, DocumentNode draggedNode)
    {
        var pos = e.GetPosition(treeView);
        var hitItem = FindTreeViewItem(treeView, pos);
        if (hitItem == null || hitItem.DataContext is not DocumentNode targetNode)
        {
            return (null, null, DropPosition.Inside);
        }

        var itemPos = e.GetPosition(hitItem);
        var itemHeight = hitItem.ActualHeight > 0 ? hitItem.ActualHeight : 20.0;

        double topZone = itemHeight * EdgeRatio;
        double bottomZone = itemHeight * (1 - EdgeRatio);

        bool inTopZone = itemPos.Y < topZone;
        bool inBottomZone = itemPos.Y > bottomZone;

        var mods = Keyboard.Modifiers;
        bool shiftPressed = (mods & ModifierKeys.Shift) != 0;
        bool ctrlPressed = (mods & ModifierKeys.Control) != 0;

        DropPosition position;
        if (shiftPressed)
        {
            // Shift = 强制同级
            position = inBottomZone ? DropPosition.After : DropPosition.Before;
        }
        else if (ctrlPressed)
        {
            // Ctrl = 强制 Inside（仅文件夹有效）
            position = targetNode.IsFolder ? DropPosition.Inside : DropPosition.After;
        }
        else
        {
            // 默认规则
            if (inTopZone)
            {
                position = DropPosition.Before;
            }
            else if (inBottomZone)
            {
                position = DropPosition.After;
            }
            else
            {
                position = targetNode.IsFolder ? DropPosition.Inside : DropPosition.After;
            }
        }

        return (hitItem, targetNode, position);
    }

    private static string DescribePosition(DocumentNode target, DropPosition pos)
    {
        return pos switch
        {
            DropPosition.Before => $"插到「{target.Title}」前面（同级）",
            DropPosition.After => $"插到「{target.Title}」后面（同级）",
            DropPosition.Inside => target.IsFolder
                ? $"作为「{target.Title}」的子节点"
                : $"插到「{target.Title}」后面（同级）",
            _ => ""
        };
    }

    private static void RaiseDropPositionChanged(DocumentNode dragged, DocumentNode? target, DropPosition pos, string desc)
    {
        DropPositionChanged?.Invoke(dragged, target, pos, desc);
    }

    /// <summary>
    /// 在 TreeView 中查找指定位置上的 TreeViewItem。
    /// 用 InputHitTest 替代 VisualTreeHelper.HitTest，对鼠标事件敏感。
    /// 注意：position 必须是相对 treeView 的本地坐标。
    /// </summary>
    private static TreeViewItem? FindTreeViewItem(TreeView treeView, Point position)
    {
        var hit = treeView.InputHitTest(position) as DependencyObject;
        while (hit != null)
        {
            if (hit is TreeViewItem tvi)
            {
                return tvi;
            }
            hit = hit is Visual || hit is Visual3D
                ? VisualTreeHelper.GetParent(hit)
                : LogicalTreeHelper.GetParent(hit);
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

    // ============== 可视化插入指示线（Adorner） ==============

    private static WindowLevelDropAdorner? _currentAdorner;

    private static void ShowDropAdorner(TreeView treeView, TreeViewItem? targetItem, DropPosition position)
    {
        var window = Window.GetWindow(treeView);
        if (window == null) return;

        var adornerLayer = AdornerLayer.GetAdornerLayer(window);
        if (adornerLayer == null) return;

        if (_currentAdorner != null)
        {
            adornerLayer.Remove(_currentAdorner);
            _currentAdorner = null;
        }

        if (targetItem != null)
        {
            _currentAdorner = new WindowLevelDropAdorner(window, targetItem, position);
            adornerLayer.Add(_currentAdorner);
        }

        adornerLayer.Update();
    }

    private static void RemoveDropAdorner()
    {
        if (_currentAdorner == null) return;
        var window = _currentAdorner.AdornedElement as Window;
        var adornerLayer = window != null ? AdornerLayer.GetAdornerLayer(window) : null;
        adornerLayer?.Remove(_currentAdorner);
        _currentAdorner = null;
    }

    /// <summary>
    /// Window 级拖拽指示线 Adorner：
    /// - Before / After → 在目标节点上/下边画一根 2px 蓝色横线 + 三角箭头 + 文字提示
    /// - Inside → 给目标节点套一个 1px 蓝色高亮边框 + 文字提示
    /// </summary>
    private sealed class WindowLevelDropAdorner : Adorner
    {
        public TreeViewItem TargetItem { get; }
        public DropPosition Position { get; }

        private static readonly Brush IndicatorBrush = new SolidColorBrush(Color.FromRgb(0x26, 0xA6, 0xFF));
        private static readonly Brush InsideBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x26, 0xA6, 0xFF));
        private static readonly Brush LabelBgBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x6F, 0xB0));
        private static readonly Brush LabelTextBrush = Brushes.White;

        static WindowLevelDropAdorner()
        {
            IndicatorBrush.Freeze();
            InsideBrush.Freeze();
            LabelBgBrush.Freeze();
            LabelTextBrush.Freeze();
        }

        public WindowLevelDropAdorner(Window window, TreeViewItem targetItem, DropPosition position)
            : base(window)
        {
            TargetItem = targetItem;
            Position = position;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            try
            {
                if (!TargetItem.IsVisible || TargetItem.ActualWidth <= 0 || TargetItem.ActualHeight <= 0)
                    return;

                var transform = TargetItem.TransformToVisual((Visual)AdornedElement);
                var itemBounds = transform.TransformBounds(
                    new Rect(0, 0, TargetItem.ActualWidth, TargetItem.ActualHeight));

                var indicatorLeft = itemBounds.Left + 8;
                var indicatorRight = itemBounds.Right - 4;

                var pen = new Pen(IndicatorBrush, 2.0);
                pen.Freeze();

                string label;
                Point labelPos;

                switch (Position)
                {
                    case DropPosition.Before:
                        drawingContext.DrawLine(pen,
                            new Point(indicatorLeft, itemBounds.Top),
                            new Point(indicatorRight, itemBounds.Top));
                        DrawArrow(drawingContext, IndicatorBrush,
                            new Point(indicatorLeft, itemBounds.Top), pointingDown: false);
                        label = "插入到前面";
                        labelPos = new Point(itemBounds.Right + 8, itemBounds.Top - 8);
                        DrawLabel(drawingContext, label, labelPos);
                        break;
                    case DropPosition.After:
                        drawingContext.DrawLine(pen,
                            new Point(indicatorLeft, itemBounds.Bottom),
                            new Point(indicatorRight, itemBounds.Bottom));
                        DrawArrow(drawingContext, IndicatorBrush,
                            new Point(indicatorLeft, itemBounds.Bottom), pointingDown: true);
                        label = "插入到后面";
                        labelPos = new Point(itemBounds.Right + 8, itemBounds.Bottom - 8);
                        DrawLabel(drawingContext, label, labelPos);
                        break;
                    case DropPosition.Inside:
                        drawingContext.DrawRectangle(InsideBrush, pen, itemBounds);
                        label = "作为子节点";
                        labelPos = new Point(itemBounds.Right + 8, itemBounds.Top + itemBounds.Height / 2 - 8);
                        DrawLabel(drawingContext, label, labelPos);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DragDrop] Adorner render failed: {ex.Message}");
            }
        }

        private static void DrawArrow(DrawingContext dc, Brush brush, Point anchor, bool pointingDown)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(anchor, isFilled: true, isClosed: true);
                if (pointingDown)
                {
                    ctx.LineTo(new Point(anchor.X - 4, anchor.Y - 6), isStroked: true, isSmoothJoin: false);
                    ctx.LineTo(new Point(anchor.X + 4, anchor.Y - 6), isStroked: true, isSmoothJoin: false);
                }
                else
                {
                    ctx.LineTo(new Point(anchor.X - 4, anchor.Y + 6), isStroked: true, isSmoothJoin: false);
                    ctx.LineTo(new Point(anchor.X + 4, anchor.Y + 6), isStroked: true, isSmoothJoin: false);
                }
                // StreamGeometryContext 没有 EndFigure 方法
            }
            geometry.Freeze();
            dc.DrawGeometry(brush, null, geometry);
        }

        /// <summary>
        /// 在指定位置画一个带背景的文字标签
        /// </summary>
        private static void DrawLabel(DrawingContext dc, string text, Point position)
        {
            if (string.IsNullOrEmpty(text)) return;

            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei"),
                12,
                LabelTextBrush,
                1.0);

            var padding = 4;
            var bgRect = new Rect(
                position.X,
                position.Y,
                formattedText.Width + padding * 2,
                formattedText.Height + padding);

            // 画背景
            dc.DrawRoundedRectangle(LabelBgBrush, null, bgRect, 3, 3);

            // 画文字
            dc.DrawText(formattedText, new Point(position.X + padding, position.Y + padding / 2));
        }
    }
}
