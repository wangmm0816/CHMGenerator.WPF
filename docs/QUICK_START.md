# CHM Generator WPF - 外部工具集成完成

## ✅ 集成完成清单

已成功为你的项目集成了外部工具支持，以下是所有新增和修改的文件：

### 新增文件

1. **Services/ExternalToolsIntegration.cs**
   - 外部工具调用服务
   - 支持调用 Python doc2html 转换 Word
   - 支持调用 CHMGenerator 控制台程序生成 CHM

2. **Services/ToolConfiguration.cs**
   - 配置管理类
   - 自动保存/加载配置到 `%AppData%\CHMGeneratorWPF\config.json`
   - 检查外部工具是否可用

3. **Views/SettingsWindow.xaml + .cs**
   - 可视化配置界面
   - 实时状态检查
   - 路径浏览器

4. **ViewModels/MainViewModelWithExternalTools.cs**
   - 增强版 ViewModel（可选，用于参考）
   - 包含多种转换模式

5. **Tests/ExternalToolsTest.cs**
   - 单元测试脚本
   - 验证外部工具是否正常工作

6. **INTEGRATION_README.md**
   - 完整的集成说明文档

### 修改文件

1. **ViewModels/MainViewModel.cs**
   - 增加了 `ConvertWordsUsingPython()` 方法
   - 增加了 `ConvertWordsUsingBuiltIn()` 方法
   - 修改了 `GenerateChmAsync()` 以支持外部工具

2. **MainWindow.xaml**
   - 添加了"设置"按钮

3. **MainWindow.xaml.cs**
   - 添加了 `Settings_Click()` 方法

## 🚀 使用方法

### 快速开始

1. **编译项目**
   ```bash
   cd d:\NetCode\CHMGenerator.WPF
   dotnet build
   ```

2. **运行程序**
   - 程序启动后，默认使用内置转换器
   - 所有功能正常工作，无需额外配置

3. **配置外部工具（可选）**
   - 点击主界面顶部的 "⚙ 设置" 按钮
   - 勾选 "使用 Python doc2html 转换 Word 文件"
   - 设置路径：`D:\python\doc2html`
   - 点击"保存"

4. **生成 CHM**
   - 添加文件或文件夹
   - 点击 "🔨 生成 CHM"
   - Word 文件会自动使用配置的工具转换

## 📋 工作流程说明

### 当前实现的流程

```
用户点击"生成 CHM"
    ↓
检查配置文件
    ↓
┌─────────────────────────────────┐
│  Word 转 HTML 阶段              │
├─────────────────────────────────┤
│ 如果启用 Python doc2html：      │
│   ├─ 调用 Python 脚本转换       │
│   ├─ 成功 → 使用转换结果       │
│   └─ 失败 → 回退到内置转换器   │
│                                 │
│ 否则：                          │
│   └─ 使用内置转换器             │
│      (OpenXmlPowerTools)        │
└─────────────────────────────────┘
    ↓
┌─────────────────────────────────┐
│  生成 CHM 项目文件              │
├─────────────────────────────────┤
│ 使用内置生成器：                │
│   ├─ 生成 project.hhp           │
│   ├─ 生成 toc.hhc               │
│   └─ 生成 index.hhk             │
└─────────────────────────────────┘
    ↓
┌─────────────────────────────────┐
│  编译 CHM                       │
├─────────────────────────────────┤
│ 调用 hhc.exe 编译               │
│   └─ 生成 .chm 文件             │
└─────────────────────────────────┘
    ↓
完成 ✓
```

## 🔧 配置示例

配置文件位于：`%AppData%\CHMGeneratorWPF\config.json`

```json
{
  "UsePythonDoc2Html": true,
  "PythonDoc2HtmlPath": "D:\\python\\doc2html",
  "UseChmGeneratorConsole": false,
  "ChmGeneratorConsolePath": "D:\\NetCode\\CHMGenerator"
}
```

## 💡 关键特性

### 1. 自动回退机制
如果 Python 转换失败，程序会自动回退到内置转换器，确保始终能生成 CHM。

日志示例：
```
- 转换模式: 使用 Python doc2html
  ✓ document1.docx → document1.html (Python)
  ✗ document2.docx: Python 转换失败，尝试使用内置转换器
  ✓ (内置) document2.docx → 文档2 (OpenXmlPowerTools)
```

### 2. 实时状态检查
设置窗口会实时检查：
- 路径是否存在
- Python 脚本是否存在
- 可执行文件是否已编译

状态显示：
- ✓ 已启用，路径有效（绿色）
- ⚠ 已启用，但有问题（橙色）
- ✗ 已启用，但路径不存在（红色）
- 未启用（灰色）

### 3. 详细日志
编译日志会显示：
- 使用的转换模式
- 每个文件的转换状态
- Python 脚本的输出
- 错误和警告信息

## 🔍 故障排查

### Python doc2html 不工作

**问题：** 勾选后仍使用内置转换器

**检查：**
1. 打开设置窗口，查看状态指示
2. 确认路径是否正确：`D:\python\doc2html`
3. 确认 `DocToCHM.py` 文件存在
4. 测试 Python 环境：
   ```bash
   python --version
   pip list | grep docx
   ```

**解决：**
- 安装 Python 依赖：`pip install python-docx pillow`
- 检查路径是否包含中文或特殊字符
- 查看编译日志中的详细错误信息

### 配置不生效

**问题：** 修改配置后点击"生成 CHM"仍使用旧配置

**解决：**
1. 确认在设置窗口点击了"保存"按钮
2. 检查配置文件：`%AppData%\CHMGeneratorWPF\config.json`
3. 重启程序

### 路径错误

**问题：** 提示路径不存在

**解决：**
- 确保路径使用正斜杠或双反斜杠：
  - ✓ `D:/python/doc2html`
  - ✓ `D:\\python\\doc2html`
  - ✗ `D:\python\doc2html` （单反斜杠在 JSON 中无效）

## 📊 性能对比

| 转换方式 | 速度 | 格式支持 | 部署难度 |
|---------|------|---------|---------|
| 内置转换器 (OpenXmlPowerTools) | ★★★★★ 快 | ★★★★☆ 好 | ★★★★★ 简单 |
| Python doc2html | ★★★☆☆ 较慢 | ★★★★★ 优秀 | ★★☆☆☆ 需要环境 |

**推荐使用场景：**
- **内置转换器**：快速部署、日常使用、简单文档
- **Python doc2html**：复杂格式、特殊需求、需要自定义处理

## 🎯 下一步

### 选项 1：直接使用（推荐）
- 不需要任何配置
- 使用内置转换器
- 所有功能正常工作

### 选项 2：配置 Python doc2html
1. 确保 Python 环境正常
2. 打开设置窗口配置路径
3. 测试转换效果

### 选项 3：完全自定义
- 查看 `ExternalToolsIntegration.cs`
- 修改调用逻辑
- 添加自己的转换工具

## 📝 代码示例

### 手动调用 Python 转换
```csharp
using CHMGenerator.WPF.Services;

// 转换单个文件
var htmlPath = await ExternalToolsIntegration.ConvertWordUsingPython(
    pythonProjectPath: @"D:\python\doc2html",
    docxPath: @"C:\test.docx",
    outputDir: @"C:\output",
    progress: new Progress<string>(Console.WriteLine)
);
```

### 检查配置
```csharp
using CHMGenerator.WPF.Services;

var config = ToolConfiguration.Instance;
if (config.IsPythonDoc2HtmlAvailable())
{
    Console.WriteLine("Python doc2html 已配置且可用");
}
```

### 修改配置
```csharp
var config = ToolConfiguration.Instance;
config.UsePythonDoc2Html = true;
config.PythonDoc2HtmlPath = @"D:\python\doc2html";
config.Save();
```

## 🆘 获取帮助

### 查看日志
主界面底部的"编译日志"面板会显示详细信息：
1. 展开"编译日志"
2. 查找错误或警告信息
3. 根据提示排查问题

### 常见错误信息

**"未找到 Python 脚本: DocToCHM.py"**
- 路径配置错误
- Python 项目不完整

**"Python 转换失败，尝试使用内置转换器"**
- Python 依赖未安装
- Word 文件格式问题
- 查看详细日志了解原因

**"路径不存在"**
- 检查路径拼写
- 确认文件夹存在

## ✨ 总结

你的项目现在支持：

✅ **内置转换器**（默认）
- OpenXmlPowerTools 转换 Word
- 内置生成器生成 CHM
- 无需额外配置

✅ **外部工具支持**（可选）
- Python doc2html 转换 Word
- CHMGenerator 控制台生成 CHM
- 可视化配置界面

✅ **智能回退**
- 外部工具失败自动使用内置工具
- 确保始终能完成任务

✅ **详细日志**
- 显示每一步的执行情况
- 方便问题排查

---

**开始使用：** 直接编译运行，所有功能开箱即用！

**需要帮助：** 查看 `INTEGRATION_README.md` 获取详细文档。
