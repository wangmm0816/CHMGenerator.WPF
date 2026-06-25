# 配置保存和加载问题修复指南

## 🔍 问题诊断

你遇到的问题：
1. ✅ 勾选了 Python doc2html
2. ❌ 但实际生成时还是用内置方法
3. ❌ 配置文件没有保存
4. ❌ 下次打开配置窗口时勾选消失

## ✅ 已完成的修复

### 1. Python 脚本已集成到项目
```
CHMGenerator.WPF/
└── python_scripts/
    ├── DocToCHM.py              ← 已复制
    └── hyperlink_processor.py   ← 已复制
```

### 2. 配置管理增强
- 添加了详细的调试日志
- 默认路径指向项目中的脚本
- 保存时创建必要的目录
- 加载时显示配置状态

### 3. 生成日志增强
编译日志现在会显示：
```
- 配置检查:
  UsePythonDoc2Html = True/False
  PythonDoc2HtmlScriptPath = 路径
  IsPythonDoc2HtmlAvailable = True/False
- 转换模式: 使用 Python doc2html / 内置转换器
```

## 🧪 测试步骤

### 步骤 1：启动程序并查看默认配置

```bash
dotnet run
```

**预期：**
- 程序启动
- 左下角状态栏显示"就绪"

### 步骤 2：打开设置窗口

1. 点击顶部工具栏的 "⚙ 设置" 按钮
2. 查看默认配置

**预期：**
- Python 路径应该显示：`{项目目录}\python_scripts\DocToCHM.py`
- 复选框未勾选（默认）
- 状态显示："状态：未启用（将使用内置转换器）"

### 步骤 3：启用 Python 并保存

1. 勾选 "使用 Python doc2html"
2. 确认路径正确（应该指向项目中的脚本）
3. 点击"保存"按钮

**预期：**
- 弹出消息："配置已保存！下次生成 CHM 时将使用新配置。"
- 设置窗口关闭

### 步骤 4：验证配置文件

打开命令行，执行：
```bash
cat "$APPDATA/CHMGeneratorWPF/config.json"
```

**预期输出：**
```json
{
  "UsePythonDoc2Html": true,
  "PythonDoc2HtmlScriptPath": "D:\\NetCode\\CHMGenerator.WPF\\bin\\Debug\\net8.0-windows\\python_scripts\\DocToCHM.py"
}
```

### 步骤 5：重新打开设置窗口

1. 点击 "⚙ 设置"
2. 查看配置是否保留

**预期：**
- 复选框应该是勾选状态 ✓
- 路径应该保持不变
- 状态显示："状态：✓ 已启用，脚本有效"（如果脚本存在）

### 步骤 6：生成 CHM 测试

1. 添加一个 Word 文件
2. 点击 "🔨 生成 CHM"
3. 展开底部"编译日志"查看

**预期日志：**
```
=== 开始生成 CHM: 帮助文档 ===
- 配置检查:
  UsePythonDoc2Html = True
  PythonDoc2HtmlScriptPath = D:\NetCode\CHMGenerator.WPF\bin\Debug\net8.0-windows\python_scripts\DocToCHM.py
  IsPythonDoc2HtmlAvailable = True
- 转换模式: 使用 Python doc2html
- 转换 1 个 Word 文件...
  [Python] 开始转换...
  ✓ filename.docx → filename.html
```

## 🐛 如果配置还是不保存

### 检查 1：配置目录权限

```bash
# 创建配置目录
mkdir -p "$APPDATA/CHMGeneratorWPF"

# 测试写入权限
echo "test" > "$APPDATA/CHMGeneratorWPF/test.txt"
cat "$APPDATA/CHMGeneratorWPF/test.txt"
```

### 检查 2：手动创建配置文件

```bash
# 创建配置目录
mkdir -p "$APPDATA/CHMGeneratorWPF"

# 创建配置文件
cat > "$APPDATA/CHMGeneratorWPF/config.json" << 'EOF'
{
  "UsePythonDoc2Html": true,
  "PythonDoc2HtmlScriptPath": "D:\\NetCode\\CHMGenerator.WPF\\bin\\Debug\\net8.0-windows\\python_scripts\\DocToCHM.py"
}
EOF

# 验证
cat "$APPDATA/CHMGeneratorWPF/config.json"
```

### 检查 3：查看操作日志

```bash
# 打开日志目录
cd "D:\NetCode\CHMGenerator.WPF\bin\Debug\net8.0-windows\logs"

# 查看最新的操作日志
ls -lt operation_*.txt | head -1 | xargs cat
```

查找包含 "配置" 的行，应该看到：
```
[时间] 配置已保存: UsePython=True, Path=...
```

## 🔧 强制调试模式

如果还是不行，在设置窗口的 Save_Click 方法中添加断点或日志：

1. 打开 `Views/SettingsWindow.xaml.cs`
2. 在 `Save_Click` 方法的第一行添加：
   ```csharp
   MessageBox.Show($"保存配置:\nUsePython={UsePythonCheckBox.IsChecked}\nPath={PythonPathTextBox.Text}");
   ```
3. 重新编译运行
4. 点击保存时会弹出确认信息

## 📋 检查清单

- [ ] Python 脚本已复制到 `python_scripts` 目录
- [ ] 编译成功，无错误
- [ ] 打开设置窗口，看到默认路径
- [ ] 勾选复选框，点击保存
- [ ] 看到确认消息
- [ ] 配置文件已创建：`%APPDATA%/CHMGeneratorWPF/config.json`
- [ ] 重新打开设置窗口，配置保留
- [ ] 生成 CHM 时日志显示使用 Python
- [ ] 操作日志中有"配置已保存"记录

## 🚀 快速验证脚本

创建并运行此脚本快速验证：

```bash
#!/bin/bash
echo "=== CHM Generator 配置验证 ==="
echo ""

echo "1. 检查 Python 脚本..."
if [ -f "D:/NetCode/CHMGenerator.WPF/python_scripts/DocToCHM.py" ]; then
    echo "   ✓ DocToCHM.py 存在"
else
    echo "   ✗ DocToCHM.py 不存在"
fi

echo ""
echo "2. 检查配置目录..."
if [ -d "$APPDATA/CHMGeneratorWPF" ]; then
    echo "   ✓ 配置目录存在"
else
    echo "   ✗ 配置目录不存在"
    mkdir -p "$APPDATA/CHMGeneratorWPF"
    echo "   → 已创建配置目录"
fi

echo ""
echo "3. 检查配置文件..."
if [ -f "$APPDATA/CHMGeneratorWPF/config.json" ]; then
    echo "   ✓ 配置文件存在"
    echo "   内容："
    cat "$APPDATA/CHMGeneratorWPF/config.json" | sed 's/^/   /'
else
    echo "   ✗ 配置文件不存在"
fi

echo ""
echo "=== 验证完成 ==="
```

保存为 `verify_config.sh` 并运行：
```bash
bash verify_config.sh
```

## 💡 建议

1. **先手动创建配置文件**（上面的检查 2）
2. **重启程序**
3. **打开设置窗口验证配置已加载**
4. **如果加载成功，再测试保存功能**

这样可以分步验证：
- 加载功能是否正常 ✓
- 保存功能是否正常 ✓

## 📞 需要更多帮助

如果按照以上步骤操作后仍有问题，请提供：
1. `%APPDATA%/CHMGeneratorWPF/config.json` 的内容
2. 最新的 `logs/operation_*.txt` 日志
3. 编译日志中的配置检查部分

我会根据这些信息进一步诊断问题。
