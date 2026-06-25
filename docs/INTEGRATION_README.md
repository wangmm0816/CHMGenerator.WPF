# CHM Generator WPF - 外部工具集成说明

## 概述

本项目已成功集成外部工具支持，可以在生成 CHM 时选择使用：
- **Python doc2html** - 用于 Word 转 HTML
- **CHMGenerator 控制台程序** - 用于生成 CHM

## 功能特性

### 1. 自动检测和切换
- 默认使用内置转换器（OpenXmlPowerTools）
- 如果配置了外部工具，会自动切换到外部工具
- 外部工具失败时自动回退到内置转换器

### 2. 配置界面
点击主界面顶部的"⚙ 设置"按钮可以配置：
- 是否启用 Python doc2html
- Python doc2html 项目路径（默认: D:\python\doc2html）
- 是否启用 CHMGenerator 控制台程序
- CHMGenerator 项目路径（默认: D:\NetCode\CHMGenerator）

配置保存在：`%AppData%\CHMGeneratorWPF\config.json`

## 工作流程

### 当前工作流程（点击"生成 CHM"按钮后）

```
1. 检查配置
   ├─ 如果启用 Python doc2html 且路径有效
   │  └─ 使用 Python 转换 Word → HTML
   └─ 否则
      └─ 使用内置转换器（OpenXmlPowerTools）

2. 生成 CHM 项目文件
   └─ 使用内置生成器（ChmProjectGenerator）
      ├─ 生成 project.hhp
      ├─ 生成 toc.hhc
      └─ 生成 index.hhk

3. 编译 CHM
   └─ 调用 hhc.exe 编译 CHM 文件
```

## 新增文件说明

### 1. Services/ExternalToolsIntegration.cs
外部工具集成服务类，提供：
- `ConvertWordUsingPython()` - 调用 Python doc2html 转换单个 Word 文件
- `GenerateChmUsingConsole()` - 调用 CHMGenerator 控制台程序生成 CHM
- `ConvertWordsUsingPython()` - 批量转换 Word 文件

### 2. Services/ToolConfiguration.cs
工具配置管理类：
- 管理外部工具的启用状态和路径
- 自动保存/加载配置
- 检查外部工具是否可用

### 3. Views/SettingsWindow.xaml + .cs
配置界面窗口：
- 可视化配置外部工具
- 实时检查工具路径有效性
- 显示工具状态（✓ 有效 / ⚠ 警告 / ✗ 无效）

### 4. ViewModels/MainViewModel.cs（已修改）
主视图模型增强：
- `ConvertWordsUsingPython()` - Python 转换逻辑
- `ConvertWordsUsingBuiltIn()` - 内置转换逻辑
- 自动根据配置选择转换方式

## 外部工具要求

### Python doc2html 要求
```
路径结构：
D:\python\doc2html\
├── DocToCHM.py          ← 主转换脚本（必须）
├── DocToHtmlByDir.py
├── hyperlink_processor.py
└── requirements.txt

运行要求：
- Python 3.x
- python-docx 库
- Pillow 库
```

### CHMGenerator 控制台程序要求
```
路径结构：
D:\NetCode\CHMGenerator\
└── CHMGenerator\
    └── bin\
        ├── Release\net8.0\CHMGenerator.exe  ← 或
        └── Debug\net8.0\CHMGenerator.exe    ← 或

运行要求：
- .NET 8.0 Runtime
- 已编译的可执行文件
```

## 使用方法

### 方式一：使用内置转换器（默认）
1. 直接添加文件/文件夹
2. 点击"生成 CHM"
3. 选择输出目录
4. 完成

### 方式二：使用 Python doc2html
1. 点击顶部"⚙ 设置"按钮
2. 勾选"使用 Python doc2html 转换 Word 文件"
3. 设置 Python 项目路径：`D:\python\doc2html`
4. 点击"保存"
5. 返回主界面，点击"生成 CHM"
6. Word 文件会自动使用 Python 转换

### 方式三：完全使用外部工具
1. 点击"⚙ 设置"
2. 勾选两个外部工具选项
3. 设置路径并保存
4. 生成 CHM 时会使用外部工具

## 优势对比

| 功能 | 内置转换器 | Python doc2html |
|------|-----------|----------------|
| 安装部署 | 无需额外配置 | 需要 Python 环境 |
| 转换速度 | 快 | 较慢 |
| 格式支持 | OpenXmlPowerTools | python-docx |
| 图片处理 | Base64 → 独立文件 | 自定义 |
| 表格支持 | 好 | 好 |
| 超链接支持 | 标准 | 增强支持 |

## 故障排查

### Python 转换失败
1. 检查 Python 是否安装：`python --version`
2. 检查依赖是否安装：`pip list | grep docx`
3. 检查 DocToCHM.py 是否存在
4. 查看编译日志中的错误信息

### 外部工具无效
- 打开设置窗口查看状态指示
- 确认路径是否正确
- 如果外部工具失败，程序会自动回退到内置转换器

### 配置丢失
- 配置文件位置：`%AppData%\CHMGeneratorWPF\config.json`
- 可以手动编辑此文件
- 删除此文件会恢复默认配置

## 日志说明

生成 CHM 时，日志会显示：
```
=== 开始生成 CHM: 帮助文档 ===
- 转换模式: 使用 Python doc2html          ← 当前使用的转换器
- 转换 2 个 Word 文件...
  [Python] Processing: example.docx          ← Python 输出
  ✓ example.docx → example.html              ← 转换成功
  ✗ test.docx: Python 转换失败，尝试使用内置转换器  ← 回退
  ✓ (内置) test.docx → 测试文档              ← 回退成功
- 生成 .hhp / .hhc / .hhk...
  ✓ project.hhp
  ✓ toc.hhc
  ✓ index.hhk
=== 编译成功 ===
输出: D:\output\帮助文档.chm
大小: 1234.5 KB
```

## 技术架构

```
MainViewModel (主视图模型)
    ↓
    ├─→ ToolConfiguration (配置管理)
    │       ↓
    │       └─→ config.json (配置文件)
    │
    ├─→ ExternalToolsIntegration (外部工具)
    │       ↓
    │       ├─→ Python doc2html (转换 Word)
    │       └─→ CHMGenerator Console (生成 CHM)
    │
    └─→ 内置服务
            ├─→ WordToHtmlConverter (OpenXmlPowerTools)
            ├─→ ChmProjectGenerator (生成项目文件)
            └─→ ChmCompiler (调用 hhc.exe)
```

## 开发说明

### 添加新的外部工具
1. 在 `ExternalToolsIntegration.cs` 中添加调用方法
2. 在 `ToolConfiguration.cs` 中添加配置项
3. 在 `SettingsWindow.xaml` 中添加 UI 配置
4. 在 `MainViewModel.cs` 中集成调用逻辑

### 测试方法
```csharp
// 测试 Python 转换
var htmlPath = await ExternalToolsIntegration.ConvertWordUsingPython(
    @"D:\python\doc2html",
    @"C:\test.docx",
    @"C:\output",
    progress: new Progress<string>(Console.WriteLine)
);

// 测试配置
var config = ToolConfiguration.Instance;
config.UsePythonDoc2Html = true;
config.Save();
```

## 更新日志

### v2.0 - 外部工具集成
- ✅ 支持调用 Python doc2html 转换 Word
- ✅ 支持配置外部工具路径
- ✅ 自动回退到内置转换器
- ✅ 可视化配置界面
- ✅ 实时状态检查
- ✅ 配置持久化
- ✅ 详细日志输出

## 许可证

MIT License

---

如有问题，请查看编译日志或联系开发者。
