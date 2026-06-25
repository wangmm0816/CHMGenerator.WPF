# 内置工具修复说明

## 修复的问题

### 1. **RelativePath 计算问题**
**问题：** 当文件节点的 `EffectiveHtmlPath` 为空时，`Path.GetFileName()` 会导致错误。

**修复：**
- 在 `DocumentNode.cs` 的 `RelativePath` 属性中增加了空值检查
- 如果 HTML 路径不存在，使用 `Title + .html` 生成文件名
- 确保文件路径始终有效

**文件：** `Models/DocumentNode.cs` (第 150-180 行)

### 2. **文件复制错误处理**
**问题：** 文件复制失败时没有详细的错误信息，难以排查问题。

**修复：**
- 在 `ChmProjectGenerator.cs` 的 `CopyFilesToSrc()` 方法中添加了详细的调试日志
- 增加了 try-catch 错误处理
- 如果源文件不存在，会打印警告并跳过
- 复制失败会抛出详细的异常信息

**文件：** `Services/ChmProjectGenerator.cs` (第 123-169 行)

### 3. **生成流程日志增强**
**问题：** 生成 CHM 时缺少详细的日志，无法了解哪个步骤失败。

**修复：**
- 在 `MainViewModel.cs` 的 `GenerateChmAsync()` 方法中添加了详细日志：
  - 显示输出目录和临时目录
  - 列出所有文件节点的有效性
  - 显示有效/无效文件统计
  - 在异常时输出完整的堆栈跟踪
- 增加了文件有效性检查，确保所有文件都存在
- 如果没有有效文件，提前返回并提示用户

**文件：** `ViewModels/MainViewModel.cs` (第 610-735 行)

## 现在的工作流程

### 添加 HTML 文件
```
1. 用户添加 HTML 文件
   ↓
2. DocumentNode 创建
   - NodeType = Html
   - SourcePath = HTML 文件路径
   - EffectiveHtmlPath = SourcePath
   ↓
3. 点击"生成 CHM"
   ↓
4. 文件有效性检查
   - 检查 EffectiveHtmlPath 是否存在
   - 显示有效/无效文件统计
   ↓
5. 复制文件到 src/
   - 根据 RelativePath 计算目标路径
   - 复制 HTML 文件
   ↓
6. 生成 hhp/hhc/hhk
   ↓
7. 调用 hhc.exe 编译
   ↓
8. 完成 ✓
```

### 添加 Word 文件
```
1. 用户添加 Word 文件
   ↓
2. DocumentNode 创建
   - NodeType = Word
   - SourcePath = Word 文件路径
   - ConvertedHtmlPath = ""
   - EffectiveHtmlPath = "" (等待转换)
   ↓
3. 点击"生成 CHM"
   ↓
4. Word 转 HTML
   - 使用内置转换器或 Python
   - 设置 ConvertedHtmlPath
   - EffectiveHtmlPath = ConvertedHtmlPath
   ↓
5. 文件有效性检查
   - 检查转换后的 HTML 是否存在
   ↓
6. 复制文件到 src/
   - 复制 HTML 文件
   - 复制关联的图片目录
   ↓
7. 生成 hhp/hhc/hhk
   ↓
8. 调用 hhc.exe 编译
   ↓
9. 完成 ✓
```

## 日志示例

### 成功生成
```
=== 开始生成 CHM: 帮助文档 ===
输出目录: D:\output
临时目录: C:\Users\...\Temp\CHMGen_xxxxx
- 转换模式: 使用内置转换器 (OpenXmlPowerTools)
- 没有 Word 文件需要转换
- 总文件数: 3
  ✓ 首页: D:\docs\index.html
  ✓ 使用手册: D:\docs\manual.html
  ✓ API 文档: D:\docs\api.html
- 有效文件: 3, 无效文件: 0
- 默认首页: 首页.html
- 生成 .hhp / .hhc / .hhk...
  ✓ project.hhp
  ✓ toc.hhc
  ✓ index.hhk
=== 编译成功 ===
输出: D:\output\帮助文档.chm
大小: 1234.5 KB
```

### 有无效文件
```
=== 开始生成 CHM: 帮助文档 ===
输出目录: D:\output
临时目录: C:\Users\...\Temp\CHMGen_xxxxx
- 转换模式: 使用内置转换器 (OpenXmlPowerTools)
- 转换 1 个 Word 文件...
  ✓ test.docx → 测试文档
- 总文件数: 2
  ✓ 测试文档: C:\Users\...\Temp\CHMGen_xxxxx\测试文档.html
  ✗ 缺失文档: HTML 路径无效或文件不存在 (D:\missing.html)
- 有效文件: 1, 无效文件: 1
- 默认首页: 测试文档.html
- 生成 .hhp / .hhc / .hhk...
...
```

## 测试方法

### 测试 HTML 文件
1. 创建一个简单的 HTML 文件：
   ```html
   <!DOCTYPE html>
   <html>
   <head>
       <meta charset="utf-8">
       <title>测试页面</title>
   </head>
   <body>
       <h1>这是测试页面</h1>
       <p>用于测试 CHM 生成器。</p>
   </body>
   </html>
   ```

2. 在程序中添加这个 HTML 文件
3. 点击"生成 CHM"
4. 查看编译日志，应该显示：
   - 总文件数: 1
   - 有效文件: 1, 无效文件: 0
   - 编译成功

### 测试 Word 文件
1. 创建一个简单的 Word 文件（.docx）
2. 在程序中添加这个 Word 文件
3. 点击"生成 CHM"
4. 查看编译日志，应该显示：
   - 转换 1 个 Word 文件
   - ✓ filename.docx → 文档标题
   - 有效文件: 1
   - 编译成功

## 常见问题排查

### 问题 1: "没有有效的 HTML 文件"
**原因：** 所有文件的 `EffectiveHtmlPath` 都无效

**解决：**
1. 查看编译日志中的"总文件数"部分
2. 检查哪些文件标记为 ✗
3. 确认文件路径是否正确
4. 如果是 Word 文件，检查转换是否成功

### 问题 2: "复制文件失败"
**原因：** 源文件不存在或目标路径无法写入

**解决：**
1. 查看编译日志中的详细错误信息
2. 确认源文件路径正确且文件存在
3. 确认输出目录有写入权限
4. 尝试手动复制文件测试

### 问题 3: Word 转换失败
**原因：** Word 文件损坏或格式不支持

**解决：**
1. 查看编译日志中的转换错误信息
2. 尝试用 Word 打开并另存为新文件
3. 确认是 .docx 格式（不是 .doc）
4. 检查文件是否被其他程序占用

## 总结

现在内置工具应该可以正常工作了，主要修复包括：

✅ 修复了 `RelativePath` 计算时的空值问题  
✅ 增强了文件复制的错误处理  
✅ 添加了详细的日志输出  
✅ 增加了文件有效性检查  
✅ 提供了清晰的错误提示  

如果还有问题，请查看编译日志中的详细信息，根据错误提示排查。
