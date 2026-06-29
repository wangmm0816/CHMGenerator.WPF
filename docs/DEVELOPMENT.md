# CHM Generator WPF - 开发文档

## 项目概述

CHM Generator WPF 是一个基于 WPF 和 .NET 8.0 的可视化 CHM 帮助文档生成工具。支持 Word 文档和 HTML 文件的混合管理，通过可视化界面组织文档结构，最终编译生成 CHM 帮助文档。

### 技术栈

- **框架**：.NET 8.0 / WPF
- **架构模式**：MVVM (Model-View-ViewModel)
- **MVVM 工具包**：CommunityToolkit.Mvvm
- **Word 处理**：DocumentFormat.OpenXml, HtmlToOpenXml
- **CHM 编译**：Microsoft HTML Help Workshop (hhc.exe)
- **Python 集成**：可选的 Python doc2html 工具用于高级 Word 转换

## 项目结构

```
CHMGenerator.WPF/
├── Models/                    # 数据模型
│   ├── DocumentNode.cs       # 文档树节点（支持文件夹、HTML、Word）
│   └── NodeType.cs           # 节点类型枚举
├── ViewModels/               # 视图模型（MVVM）
│   ├── MainViewModel.cs      # 主窗口视图模型
│   └── PreviewItem.cs        # 目录预览项
├── Services/                 # 核心服务层
│   ├── ChmProjectGenerator.cs           # CHM 项目生成器（.hhp/.hhc/.hhk）
│   ├── ExternalToolsIntegration.cs      # Python 工具集成
│   ├── LogManager.cs                    # 日志管理服务
│   └── TxtConfigParser.cs               # Python txt 配置解析器
├── Helpers/                  # UI 辅助类
│   ├── TreeViewDragDropHelper.cs        # 拖拽功能实现
│   ├── TreeViewSelectedItemBinder.cs    # TreeView 选中项绑定
│   └── 各种 Converter                   # 数据转换器
├── Views/                    # 视图/窗口
│   ├── MainWindow.xaml       # 主窗口界面
│   └── SettingsDialog.xaml   # 设置对话框
├── Resources/                # 资源文件
│   └── Styles.xaml           # 全局样式（深色主题）
└── ExternalTools/            # 外部 Python 工具（可选）
    ├── DocToCHM.py           # Word 转 HTML 的 Python 脚本
    └── resource/             # Python 工具的共享资源
        ├── css/
        └── scripts/
```

## 核心架构设计

### 1. MVVM 模式

#### Model 层
- **DocumentNode**：文档树的核心数据结构
  - 支持文件夹、HTML 文件、Word 文件三种类型
  - 树形结构（Parent/Children）
  - 属性包括：Title（标题）、SourcePath（源文件路径）、RelativePath（相对路径）、NodeType（节点类型）
  - 实现 `INotifyPropertyChanged` 支持数据绑定

#### ViewModel 层
- **MainViewModel**：主窗口的业务逻辑
  - 管理文档树（RootNodes）
  - 处理用户操作命令（添加文件、删除节点、移动节点等）
  - 实时生成 CHM 目录预览（PreviewItems）
  - 执行 CHM 生成流程
  - 使用 `ObservableCollection` 实现 UI 自动更新

#### View 层
- **MainWindow.xaml**：主界面，采用四列布局
  1. 文档目录树（TreeView + 拖拽）
  2. 节点详情编辑面板
  3. CHM 目录预览
  4. 编译日志（可折叠）

### 2. 文档树管理

#### 节点类型
```csharp
public enum NodeType
{
    Folder,   // 文件夹（仅用于组织结构）
    Html,     // HTML 文件
    Word      // Word 文档（需要转换）
}
```

#### 路径管理
- **SourcePath**：原始文件的绝对路径
- **RelativePath**：在 CHM 中的相对路径（用于生成 .hhp/.hhc 文件）
- **ConvertedHtmlPath**：Word 转换后的 HTML 路径（仅 Word 节点）

#### 树操作
- **拖拽排序**：通过 `TreeViewDragDropHelper` 实现
  - 支持同级插入（前/后）
  - 支持作为子节点插入
  - 视觉反馈（蓝色高亮/横线）
  - Shift/Ctrl 修饰键控制行为
- **节点移动**：`MoveNode` 方法处理跨文件夹移动，自动更新 RelativePath

### 3. Word 文档处理

支持两种转换模式：

#### 模式一：C# OpenXML SDK（内置）
- 使用 `DocumentFormat.OpenXml` 读取 .docx 结构
- 使用 `HtmlToOpenXml` 转换为 HTML
- 优点：无外部依赖，纯 C# 实现
- 缺点：复杂格式支持有限

#### 模式二：Python doc2html（可选）
- 通过 `ExternalToolsIntegration.cs` 调用 Python 脚本
- Python 脚本生成多页 HTML + 目录配置文件（.txt）
- 支持章节层级、图片处理、样式保留
- 配置文件示例：
  ```
  chapter_1/chapter_1.html|产品说明书|0|chapter_1/chapter_1.html
  chapter_2/chapter_2.html|前言（大标题）|0|chapter_2/chapter_2.html
  chapter_4/chapter_4.html|一级标题|0|chapter_4/chapter_4.html
  chapter_4/section_1/section_1.html|二级标题|1|chapter_4/section_1/section_1.html
  ```
  - 格式：`相对路径|标题|层级|链接路径`

### 4. CHM 生成流程

#### 步骤 1：文件准备
```csharp
CopyFilesToSrc(srcDir, rootNodes);
```
- 将所有文件复制到 `outputDir/src/` 目录
- 普通 HTML：直接复制单个文件
- Word 节点：复制整个 Python 生成的目录结构（包括 images/）
- 复制共享资源（css/, scripts/）

#### 步骤 2：生成 CHM 项目文件

##### .hhp 文件（HTML Help Project）
```ini
[OPTIONS]
Compiled file=帮助文档.chm
Contents file=toc.hhc
Index file=index.hhk
Default topic=A/产品说明书/chapter_1/chapter_1.html
Title=帮助文档
Full-text search=Yes

[FILES]
A/产品说明书/chapter_1/chapter_1.html
A/产品说明书/chapter_2/chapter_2.html
...
```

##### .hhc 文件（HTML Help Contents - 目录）
```html
<ul>
  <li><object type="text/sitemap">
    <param name="Name" value="A">
  </object>
  <ul>
    <li><object type="text/sitemap">
      <param name="Name" value="产品说明书">
      <param name="Local" value="A/产品说明书/chapter_1/chapter_1.html">
    </object></li>
  </ul>
  </li>
</ul>
```

##### .hhk 文件（HTML Help Index - 索引）
- 自动从所有 HTML 文件的 `<title>` 提取关键词

#### 步骤 3：调用 hhc.exe 编译
```csharp
hhc.exe "path/to/src/project.hhp"
```
- 工作目录：`src/` 目录
- 输出 CHM 文件到父目录

### 5. 日志系统

#### 三层日志结构
LogManager 提供了完整的日志记录功能，日志文件按会话分类存储在 `logs/` 目录下：

##### 1. Status 日志 (`logs/Status/status_YYYYMMDD_HHmmss.txt`)
- 记录主界面状态栏的内容
- 用户操作记录（添加文件、删除节点等）
- 用于追踪用户行为流程

##### 2. Compile 日志 (`logs/Compile/compile_YYYYMMDD_HHmmss.txt`)
- CHM 生成过程的详细信息
- Word 转换进度
- hhc.exe 编译输出
- 用于诊断编译问题

##### 3. Debug 日志 (`logs/Debug/debug_YYYYMMDD_HHmmss.txt`)
- 捕获所有 `Debug.WriteLine` 输出
- 内部状态跟踪（节点结构、路径解析等）
- 用于开发调试

#### 使用方式
```csharp
LogManager.Instance.WriteStatus("用户点击了添加文件按钮");
LogManager.Instance.WriteCompile("开始转换 Word 文档...");
LogManager.Instance.WriteDebug("节点路径: A/产品说明书");
LogManager.Instance.WriteException(ex);
```

#### 日志管理
- 自动清理 30 天前的旧日志：`CleanOldLogs()`
- 打开日志目录：`OpenLogDirectory()`
- 线程安全：使用 `lock` 保护文件写入

### 6. 配置管理

#### config.json
```json
{
  "UsePythonConverter": true,
  "PythonToolsPath": "D:\\NetCode\\CHMGenerator.WPF\\bin\\Debug\\net8.0-windows\\ExternalTools"
}
```

#### 配置加载
```csharp
var config = AppConfiguration.LoadConfiguration();
bool usePython = config.UsePythonConverter;
string pythonPath = config.PythonToolsPath;
```

## 关键算法与实现

### 1. 拖拽排序算法

#### 落点判定
```csharp
// 将节点高度分为三区域
double topThreshold = actualHeight * 0.3;    // 上方 30%
double bottomThreshold = actualHeight * 0.7;  // 下方 30%

if (offsetY < topThreshold)
    // 插到前面（同级）
else if (offsetY > bottomThreshold)
    // 插到后面（同级）
else
    // 中间 40%：作为子节点（如果是文件夹）
```

#### 修饰键处理
- **Shift**：强制同级插入
- **Ctrl**：强制作为子节点

### 2. RelativePath 自动更新

当节点移动时，递归更新其所有子节点的 RelativePath：

```csharp
private void UpdateRelativePaths(DocumentNode node, string parentPath)
{
    node.RelativePath = string.IsNullOrEmpty(parentPath)
        ? SanitizeFileName(node.Title)
        : $"{parentPath}/{SanitizeFileName(node.Title)}";

    foreach (var child in node.Children)
    {
        UpdateRelativePaths(child, node.RelativePath);
    }
}
```

### 3. Python txt 配置解析

解析 Python 生成的目录配置文件，构建层级结构：

```csharp
public static List<TxtEntry> Parse(string txtFile)
{
    var lines = File.ReadAllLines(txtFile, Encoding.UTF8);
    var entries = new List<TxtEntry>();

    foreach (var line in lines)
    {
        var parts = line.Split('|');
        entries.Add(new TxtEntry
        {
            RelativePath = parts[0],
            Title = parts[1],
            Level = int.Parse(parts[2]),
            LinkPath = parts[3]
        });
    }

    return entries;
}
```

## UI 设计

### 布局结构
- **四列布局**：文档树 | 节点详情 | 目录预览 | 编译日志
- **GridSplitter**：支持拖动调整列宽
- **深色主题**：`Resources/Styles.xaml` 定义了全局样式

### 颜色方案
```xml
<Color x:Key="BackgroundColor">#1E1E1E</Color>       <!-- 背景 -->
<Color x:Key="PanelColor">#252526</Color>            <!-- 面板 -->
<Color x:Key="AccentColor">#2D4A7C</Color>           <!-- 强调色 -->
<Color x:Key="SuccessColor">#4EC9B0</Color>          <!-- 成功/日志 -->
<Color x:Key="ErrorColor">#F48771</Color>            <!-- 错误 -->
```

### 关键控件
- **TreeView**：文档树，绑定 `RootNodes`
- **TextBox**：标题编辑，双向绑定 `SelectedNode.Title`
- **ListBox**：目录预览，绑定 `PreviewItems`
- **Expander**：编译日志，默认折叠

## 常见开发任务

### 添加新的节点类型

1. 在 `NodeType.cs` 添加枚举值
2. 在 `DocumentNode.cs` 更新 `EffectiveHtmlPath` 逻辑
3. 在 `NodeTypeToIconConverter.cs` 添加图标映射
4. 在 `ChmProjectGenerator.cs` 的 `CopyFilesToSrc` 添加处理逻辑

### 修改拖拽行为

编辑 `Helpers/TreeViewDragDropHelper.cs`：
- `GetDropPosition`：修改落点判定逻辑
- `PerformDrop`：修改节点移动逻辑

### 自定义日志输出

在需要记录的地方调用：
```csharp
LogManager.Instance.WriteStatus("自定义状态信息");
LogManager.Instance.WriteCompile("自定义编译信息");
Debug.WriteLine("这会自动记录到 Debug 日志");
```

### 调整 UI 布局

编辑 `MainWindow.xaml`：
- 修改 `Grid.ColumnDefinitions` 调整列宽
- 修改 `RowDefinitions` 调整行高
- 使用 `GridSplitter` 允许用户调整

## 调试技巧

### 1. 查看日志文件
- 运行程序后，日志自动保存到 `bin/Debug/net8.0-windows/logs/`
- 编译失败时优先查看 `Compile` 日志
- 节点路径问题查看 `Debug` 日志

### 2. 断点调试
- **ChmProjectGenerator.Generate**：CHM 生成入口
- **MainViewModel.GenerateChmCommand**：用户点击生成按钮
- **TreeViewDragDropHelper.DragDrop**：拖拽操作

### 3. 输出目录检查
CHM 生成后，检查输出目录结构：
```
output/
├── src/                   # 所有源文件（HTML、图片等）
│   ├── A/
│   │   └── 产品说明书/
│   ├── css/
│   ├── scripts/
│   ├── project.hhp
│   ├── toc.hhc
│   └── index.hhk
├── html/                  # Python 转换的中间目录
│   └── 产品说明书/
└── 帮助文档.chm           # 最终 CHM 文件
```

### 4. hhc.exe 错误排查
- **HHC5003: Compilation failed**：文件路径不正确或文件不存在
  - 检查 .hhp 的 [FILES] 部分
  - 确认工作目录是 `src/`
- **HHC4006: Warning: The file is already listed**：文件重复
  - 检查是否同时从 `allFiles` 和 `wordNodeTxtMap` 添加

## 性能优化

### 1. 大文件处理
- Word 转换使用异步：`await ConvertWordToPythonHtml()`
- 文件复制使用批量操作
- 避免在 UI 线程执行耗时操作

### 2. 内存优化
- TreeView 使用虚拟化：`VirtualizingPanel.IsVirtualizing="True"`
- 大目录树使用延迟加载
- 及时释放 Process 资源

### 3. UI 响应性
- 长操作显示进度条：`IsBusy` + `ProgressBar`
- 使用 `Task.Run` 将 CPU 密集操作移到后台线程
- 日志写入使用异步 I/O

## 发布与部署

### 独立可执行文件
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### 依赖框架
```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

### 发布清单
- CHMGenerator.exe
- ExternalTools/ （如果使用 Python 模式）
- config.json（可选配置文件）

## 已知问题与限制

1. **仅支持 Windows**：hhc.exe 是 Windows 独占工具
2. **中文编码**：CHM 必须使用 GB2312 编码（已自动处理）
3. **路径长度**：Windows 路径限制 260 字符（深层嵌套可能超限）
4. **Python 依赖**：使用 Python 模式需要安装 Python 3.x

## 参考资料

- [Microsoft HTML Help Workshop 文档](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/htmlhelp/)
- [DocumentFormat.OpenXml SDK](https://github.com/OfficeDev/Open-XML-SDK)
- [WPF MVVM 模式](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)

## 版本历史

- **v2.4**：修复拖拽"开盲盒"问题，增加视觉反馈和修饰键支持
- **v2.3**：集成 Python doc2html 工具，支持高级 Word 转换
- **v2.2**：实现完整日志系统（Status/Compile/Debug 三层）
- **v2.1**：优化目录树生成，修复重复节点问题
- **v2.0**：WPF 可视化界面首版

---

**维护者**：CHM Generator 开发团队  
**最后更新**：2026-06-29
