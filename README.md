# CHM Generator WPF v2.0

把原始 C# 控制台工具升级成可视化 GUI 桌面程序，支持拖拽排序、目录树编辑、Word 自动转 HTML、本地调用 hhc.exe 编译 CHM。

## 特性

- 📂 **可视化文件管理**：左侧文档树，支持拖拽排序、跨文件夹移动
- 📝 **Word 自动转换**：上传 .docx，自动用 OpenXML SDK 转 HTML，图片自动处理
- 🌐 **HTML 直接支持**：保留原有 HTML 处理逻辑
- 🏷 **标题编辑**：右侧详情面板可编辑每个文件在 CHM 目录中的标题
- 👁 **实时目录预览**：右侧实时显示未来 CHM 的目录结构
- 🎨 **VS Code 深色主题**：长时间使用不刺眼
- 🔨 **本地编译**：自动查找 hhc.exe，找不到时一键手动指定
- 📋 **编译日志**：完整记录 Word 转换和 hhc.exe 编译过程

## 环境要求

- **操作系统**：Windows 10/11（hhc.exe 只能在 Windows 运行）
- **.NET SDK**：.NET 8.0 或更高版本
- **IDE**：Visual Studio 2022 / JetBrains Rider / VS Code + C# Dev Kit
- **HTML Help Workshop**：必须安装（提供 hhc.exe）
  - 下载地址：https://learn.microsoft.com/en-us/previous-versions/windows/desktop/htmlhelp/microsoft-html-help-downloads
  - 安装后程序会自动从默认路径 `C:\Program Files (x86)\HTML Help Workshop\hhc.exe` 查找

## 如何打开和编译

### 方式一：Visual Studio 2022

1. 双击 `CHMGenerator.WPF.csproj` 打开
2. 等待 NuGet 还原（会自动安装 `DocumentFormat.OpenXml`、`HtmlToOpenXml`、`CommunityToolkit.Mvvm`）
3. 按 F5 调试运行，或 Ctrl+F5 直接运行

### 方式二：命令行

```bash
cd CHMGenerator.WPF
dotnet restore
dotnet build -c Release
# 输出在 bin\Release\net8.0-windows\CHMGenerator.exe
```

## 使用流程

### 1. 添加文件

点击顶部工具栏：
- **添加文件**：选择一个或多个 HTML / Word 文件
- **添加文件夹**：批量导入整个文件夹（递归扫描 HTML 和 Word）
- **新建文件夹**：在根创建空文件夹，然后拖文件进去

文件默认按选中顺序排列。

### 2. 组织目录树

- **拖拽移动**：按住鼠标左键拖动节点，拖到文件夹上会进入文件夹，拖到文件上会插到它前面
- **上移/下移**：选中节点后点击工具栏按钮，或右键菜单
- **重命名**：右键 → 重命名，或选中后按 F2
- **新建子文件夹**：选中一个文件夹后，右键 → 新建子文件夹
- **删除**：选中节点后按 Delete 键，或右键 → 删除

### 3. 编辑标题

选中节点后，中间面板会显示详情：
- 修改"标题"字段，会立即影响 CHM 目录中的显示名称
- HTML 文件初始标题从 `<title>` 标签提取
- Word 文件初始标题用文件名

### 4. 设置 CHM 标题

顶部"CHM 标题"输入框设置最终 CHM 文件名和窗口标题。

### 5. 生成 CHM

点击右上角 **🔨 生成 CHM** 按钮：
1. 选择输出目录
2. 程序自动：
   - 把所有 Word 文件转成 HTML
   - 按 RelativePath 拷贝文件到 `输出目录/src/`
   - 生成 `project.hhp` / `toc.hhc` / `index.hhk`
   - 调用 hhc.exe 编译
3. 完成后弹出提示，可选择打开输出文件夹

### 6. 如果没找到 hhc.exe

状态栏会显示"未找到 hhc.exe"：
1. 点击工具栏右侧 ⚙ 按钮
2. 在弹出的文件对话框中选中你电脑上的 hhc.exe
3. 程序会自动复制到自身目录，下次启动直接可用

## 项目结构

```
CHMGenerator.WPF/
├── CHMGenerator.WPF.csproj    # 工程文件
├── app.manifest               # Windows 清单
├── App.xaml / App.xaml.cs     # 应用入口
├── MainWindow.xaml            # 主窗口 UI（VS Code 风格三栏布局）
├── MainWindow.xaml.cs         # 主窗口代码（事件处理、InputDialog）
├── Models/
│   └── DocumentNode.cs        # 数据模型（节点+工程配置）
├── Services/
│   ├── WordToHtmlConverter.cs      # Word→HTML（OpenXML + HtmlToOpenXml）
│   ├── HtmlTitleExtractor.cs       # HTML <title> 提取
│   └── ChmProjectGenerator.cs      # HHP/HHC/HHK 生成 + hhc.exe 调用
├── ViewModels/
│   └── MainViewModel.cs       # 主视图模型（MVVM）
├── Helpers/
│   ├── Converters.cs          # 通用值转换器
│   └── TreeViewDragDropHelper.cs  # TreeView 拖拽辅助
└── Styles/
    ├── DarkTheme.xaml          # VS Code 风格颜色定义
    └── Controls.xaml           # 控件样式（按钮/TreeView/TextBox 等）
```

## 与原 Console 版本对比

| 项目 | 原 Console 版本 | WPF 版本 |
|---|---|---|
| 文件来源 | 只读 src/apihtml/ 下的 HTML | UI 选择任意 HTML/Word，支持文件夹递归 |
| 目录层级 | 靠 `<title>` 跟文件夹同名自动判断 | 拖拽决定 + 标题编辑（混合模式） |
| Word 支持 | 不支持 | 自动转换 |
| 编码处理 | GB2312 写死 | 读取时自动探测，写入时 GB2312 |
| hhc.exe 调用 | Process.Start 简单调用 | 同样调用，但加了超时、取消、详细日志 |
| 错误处理 | 控制台输出 | 弹窗 + 状态栏 + 可折叠日志面板 |
| 操作反馈 | 只有控制台 | 实时目录预览 + 进度条 + 状态栏 |

## 已知限制

1. **Word 公式不支持**：`HtmlToOpenXml` 不转换 OMML 公式，需要的话请先在 Word 里把公式截图
2. **Word 复杂样式可能丢失**：阴影、3D 效果、SmartArt 等无法完美保留
3. **hhc.exe 路径包含中文**：偶尔会出错，建议程序路径不含中文
4. **拖拽到展开的文件夹**：会插入到第一个位置而不是末尾，可以拖完后再上移目标节点
5. **大文件转换慢**：100+ 页的 Word 文件转换可能需要 10 秒以上

## 常见问题

**Q: 编译失败，提示 "未找到 hhc.exe"？**
A: 安装 Microsoft HTML Help Workshop，或者点击 ⚙ 按钮手动指定。

**Q: 编译后 CHM 显示乱码？**
A: 检查 HTML 源文件编码，建议都是 UTF-8 或 GB2312，混合编码容易出问题。

**Q: Word 转换后图片丢失？**
A: 程序会把图片提取到 `_images` 子目录，确保生成后的 HTML 没有被移动到其他位置。

**Q: 拖拽不生效？**
A: 拖拽需要鼠标移动超过系统阈值（通常 4 像素）才会触发，可以稍微拖远一点。

## 后续可以改进的方向

1. 内联重命名（直接在 TreeView 里改文字，而不是弹对话框）
2. 撤销/重做（Ctrl+Z / Ctrl+Y）
3. 工程保存/加载（保存文档树到 JSON，下次打开继续编辑）
4. 批量修改标题（正则替换）
5. HTML 预览（嵌入 WebView2 在中间面板预览选中文件）
6. 自定义 CHM 主题色和字体

---

需要二次开发？关键类入口：
- 看主流程：`MainViewModel.GenerateChmAsync()`
- 改 Word 转换：`WordToHtmlConverter`
- 改工程文件生成：`ChmProjectGenerator.Generate()`
- 改 hhc.exe 调用：`ChmCompiler.Compile()`
