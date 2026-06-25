# CHM Generator WPF - 完整使用指南

## 🎯 项目定位

**一个支持混合场景的 CHM 生成工具**

- 支持多个 Word 文档
- 支持 HTML 文件
- 支持文件夹
- 智能转换和整合

---

## 📁 项目结构

```
CHMGenerator.WPF/
├── ExternalTools/              ← 外部工具（Python 脚本）
│   ├── DocToCHM.py            - 单个 Word → HTML
│   ├── DocToHtmlByDir.py      - 批量 Word → HTML
│   ├── hyperlink_processor.py - 超链接处理
│   ├── requirements.txt       - Python 依赖
│   ├── resource/              - 资源文件
│   └── README.md              - 工具说明
├── docs/                       ← 所有文档
│   ├── README.md              - 项目说明
│   ├── QUICK_START.md         - 快速入门
│   ├── COMPILE_FIX_GUIDE.md   - 编译问题修复
│   └── ...
├── Services/                   ← 核心服务
│   ├── ToolConfiguration.cs   - 配置管理
│   ├── ExternalToolsIntegration.cs - 外部工具调用
│   ├── LogManager.cs          - 日志管理
│   ├── ChmProjectGenerator.cs - CHM 项目生成
│   └── WordToHtmlConverter.cs - 内置转换器
├── ViewModels/                 ← 视图模型
│   └── MainViewModel.cs       - 主视图逻辑
├── Views/                      ← 视图
│   └── SettingsWindow.xaml    - 设置窗口
├── logs/                       ← 日志文件
│   ├── operation_*.txt        - 操作日志
│   └── compile_*.txt          - 编译日志
└── fix_hhc_environment.bat    ← hhc.exe 环境修复
```

---

## 🚀 快速开始

### 步骤 1：安装 Python 依赖（如果使用 Python 转换）

```bash
pip install python-docx pillow lxml
```

### 步骤 2：修复 hhc.exe 环境（必须！）

```bash
# 右键以管理员身份运行
fix_hhc_environment.bat
```

### 步骤 3：运行程序

```bash
dotnet run
```

### 步骤 4：配置工具（可选）

1. 点击顶部 "⚙ 设置" 按钮
2. 查看配置：
   - ✓ 默认启用 Python 转换器
   - ✓ 默认路径：`{项目}\ExternalTools`
3. 如果需要修改，调整后点击"保存"

### 步骤 5：生成 CHM

1. 点击 "📂 添加文件" 选择 Word 文档
2. 点击 "🔨 生成 CHM"
3. 选择输出目录
4. 查看编译日志

---

## 🔧 配置说明

### 配置文件位置
```
%APPDATA%\CHMGeneratorWPF\config.json
```

### 默认配置
```json
{
  "UsePythonConverter": true,
  "PythonToolsPath": "{项目目录}\\ExternalTools"
}
```

### 配置选项

#### 1. Python 转换器（推荐）
**优点：**
- ✅ 生成更简洁的 HTML
- ✅ 与 hhc.exe 兼容性更好
- ✅ 适合复杂文档（月报、技术文档）

**缺点：**
- ⚠ 需要 Python 环境
- ⚠ 转换速度稍慢

#### 2. 内置转换器（备选）
**优点：**
- ✅ 无需外部依赖
- ✅ 转换速度快

**缺点：**
- ⚠ 生成的 HTML 包含大量 CSS
- ⚠ hhc.exe 可能无法编译（HHC5003 错误）

---

## 📋 使用场景

### 场景 1：单个 Word 文档

```
1. 添加文件：选择 .docx
2. 生成 CHM
3. 完成 ✓
```

**处理流程：**
```
Word 文档
  ↓ Python/内置转换
HTML 文件
  ↓ 生成 CHM 项目文件
.hhp, .hhc, .hhk
  ↓ hhc.exe 编译
CHM 文件 ✓
```

### 场景 2：多个 Word 文档

```
1. 添加多个 .docx 文件
2. 在树形结构中调整顺序
3. 生成 CHM
```

**处理流程：**
```
多个 Word 文档
  ↓ 批量转换
多个 HTML 文件
  ↓ 整合到一个目录树
单个 CHM 文件 ✓
```

### 场景 3：混合内容（Word + HTML）

```
1. 添加 Word 文档
2. 添加 HTML 文件
3. 添加文件夹（包含 HTML）
4. 调整目录结构
5. 生成 CHM
```

**处理流程：**
```
Word 文档 → 转换为 HTML
HTML 文件 → 直接使用
文件夹   → 扫描 HTML
  ↓ 统一整合
CHM 文件 ✓
```

---

## 🐛 故障排查

### 问题 1：编译失败（HHC5003）

**原因：** hhc.exe 环境问题或 HTML 过于复杂

**解决：**
```bash
# 1. 运行修复脚本
右键 fix_hhc_environment.bat → 以管理员身份运行

# 2. 启用 Python 转换器
打开设置 → 勾选 "使用 Python doc2html" → 保存

# 3. 重新生成
```

### 问题 2：配置未保存

**检查：**
```bash
# 查看配置文件
cat "$APPDATA/CHMGeneratorWPF/config.json"

# 如果不存在，手动创建
mkdir -p "$APPDATA/CHMGeneratorWPF"
cat > "$APPDATA/CHMGeneratorWPF/config.json" << 'EOF'
{
  "UsePythonConverter": true,
  "PythonToolsPath": "D:\\NetCode\\CHMGenerator.WPF\\bin\\Debug\\net8.0-windows\\ExternalTools"
}
EOF
```

### 问题 3：Python 转换失败

**检查：**
```bash
# 1. 检查 Python
python --version

# 2. 检查依赖
pip list | grep docx
pip list | grep Pillow
pip list | grep lxml

# 3. 重新安装
pip install python-docx pillow lxml

# 4. 手动测试
cd ExternalTools
python DocToCHM.py test.docx output
```

### 问题 4：找不到 ExternalTools

**解决：**
```bash
# 检查工具目录
ls -la "D:/NetCode/CHMGenerator.WPF/bin/Debug/net8.0-windows/ExternalTools"

# 如果不存在，需要重新复制
# 或在设置中修改路径指向源代码目录
```

---

## 📊 日志查看

### 操作日志
```
位置：logs/operation_*.txt
内容：所有用户操作、配置变更
```

### 编译日志
```
位置：logs/compile_*.txt
内容：详细的生成过程、转换日志、错误信息
```

### 快速打开
```
点击左下角 "📁 日志" 快速打开日志目录
```

---

## ✨ 核心功能

### 1. 智能转换
- ✅ 自动识别文件类型
- ✅ Word → Python 转换（推荐）
- ✅ Word → 内置转换（备选）
- ✅ HTML → 直接使用

### 2. 树形目录
- ✅ 拖拽排序
- ✅ 文件夹结构
- ✅ 实时预览

### 3. 配置管理
- ✅ 自动保存
- ✅ 自动加载
- ✅ 实时验证

### 4. 日志系统
- ✅ 操作日志
- ✅ 编译日志
- ✅ 异常跟踪

### 5. 错误处理
- ✅ 详细错误信息
- ✅ 自动回退机制
- ✅ 一键修复工具

---

## 🔄 转换流程对比

### Python 转换（推荐）
```
Word 文档
  ↓ python DocToCHM.py
简洁的 HTML（兼容 hhc.exe）
  ↓
✓ 编译成功率高
```

### 内置转换（备选）
```
Word 文档
  ↓ OpenXmlPowerTools
复杂的 HTML（大量 CSS）
  ↓
⚠ 可能编译失败（HHC5003）
```

---

## 📦 部署建议

### 开发环境
```
1. 安装 Python 3.x
2. pip install python-docx pillow lxml
3. 安装 HTML Help Workshop
4. 运行 fix_hhc_environment.bat
5. dotnet run
```

### 生产环境
```
1. 编译发布版本：dotnet publish -c Release
2. 复制 ExternalTools 目录到输出目录
3. 在目标机器安装 Python 和依赖
4. 运行 fix_hhc_environment.bat
5. 启动程序
```

---

## 🎯 最佳实践

### 1. 文档准备
- 使用简洁的 Word 样式
- 避免复杂的表格嵌套
- 图片使用常见格式（PNG、JPG）

### 2. 目录结构
- 合理组织文件夹层级
- 使用清晰的文件名
- 避免特殊字符

### 3. 配置选择
- 简单文档：可用内置转换
- 复杂文档：建议用 Python
- 混合场景：启用 Python

### 4. 错误处理
- 查看编译日志
- 尝试修复环境
- 切换转换方式

---

## 📞 需要帮助？

1. **查看日志**：点击 "📁 日志"
2. **阅读文档**：docs/ 目录
3. **运行修复**：fix_hhc_environment.bat
4. **检查配置**：%APPDATA%\CHMGeneratorWPF\config.json

---

**现在项目结构清晰，配置简化，功能完整！** 🎉

开始使用：`dotnet run`
