using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CHMGenerator.WPF.Models;
using CHMGenerator.WPF.ViewModels;

namespace CHMGenerator.WPF;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击"根目录"按钮，取消选中回到根目录模式
    /// </summary>
    private void ReturnToRoot_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SelectedNode = null;
        }
    }

    /// <summary>
    /// 双击节点：文件夹展开/折叠，文件预览
    /// </summary>
    private void TreeViewItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem tvi && tvi.DataContext is DocumentNode node)
        {
            if (node.IsFolder)
            {
                node.IsExpanded = !node.IsExpanded;
            }
            else
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.PreviewNodeCommand.Execute(node);
                }
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// 键盘快捷键：F2 重命名，Delete 删除
    /// </summary>
    private void TreeViewItem_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TreeViewItem tvi && tvi.DataContext is DocumentNode node)
        {
            if (e.Key == Key.F2)
            {
                StartInlineRename(tvi, node);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// 右键菜单：重命名
    /// </summary>
    private void MenuItem_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedNode != null)
        {
            // 弹出输入框重命名
            var newName = InputDialog.Show("重命名", "请输入新名称：", vm.SelectedNode.Title);
            if (!string.IsNullOrEmpty(newName) && newName != vm.SelectedNode.Title)
            {
                vm.SelectedNode.Title = newName;
                vm.RefreshPreview();
            }
        }
    }

    /// <summary>
    /// 在 TreeView 中启动内联重命名（用 TextBox 替换 TextBlock）
    /// </summary>
    private void StartInlineRename(TreeViewItem tvi, DocumentNode node)
    {
        // 简化方案：直接弹出对话框
        var newName = InputDialog.Show("重命名", "请输入新名称：", node.Title);
        if (!string.IsNullOrEmpty(newName) && newName != node.Title)
        {
            node.Title = newName;
            if (DataContext is MainViewModel vm) vm.RefreshPreview();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 全局快捷键
        if (e.Key == Key.Delete && DataContext is MainViewModel vm && vm.SelectedNode != null)
        {
            vm.DeleteNodeCommand.Execute(vm.SelectedNode);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}

/// <summary>
/// 简单的输入对话框
/// </summary>
public class InputDialog : Window
{
    public string InputValue { get; private set; } = "";
    private readonly TextBox _textBox;

    public static string Show(string title, string prompt, string defaultValue = "")
    {
        var dlg = new InputDialog(title, prompt, defaultValue);
        if (dlg.ShowDialog() == true)
        {
            return dlg.InputValue;
        }
        return defaultValue;
    }

    private InputDialog(string title, string prompt, string defaultValue)
    {
        Title = title;
        Width = 400;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("PanelBrush");
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = prompt,
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        _textBox = new TextBox
        {
            Text = defaultValue,
            Margin = new Thickness(0, 0, 0, 16),
            Padding = new Thickness(6, 4, 6, 4)
        };
        _textBox.SelectAll();
        _textBox.Focus();
        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { InputValue = _textBox.Text; DialogResult = true; Close(); }
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
        panel.Children.Add(_textBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okBtn = new Button
        {
            Content = "确定 (Enter)",
            Padding = new Thickness(20, 6, 20, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        okBtn.Click += (_, _) => { InputValue = _textBox.Text; DialogResult = true; Close(); };
        btnPanel.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = "取消 (Esc)",
            Padding = new Thickness(20, 6, 20, 6)
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        btnPanel.Children.Add(cancelBtn);

        panel.Children.Add(btnPanel);

        Content = panel;
    }
}
