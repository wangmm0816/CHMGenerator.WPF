# Python 脚本修改需求分析

## 📊 当前逻辑分析

### Python 脚本 (DocToCHM.py) 的当前逻辑

**命令行参数：**
```bash
python DocToCHM.py <input_file> <out_html_dir> -o <output_dir>
```

**示例：**
```bash
python DocToCHM.py "report.docx" "report_html" -o "D:\temp"
```

**输出目录结构：**
```
D:\temp\
└── html\
    └── report_html\
        ├── report.html
        ├── images\
        │   ├── img1.png
        │   └── img2.png
        └── ...
```

**关键代码：**
```python
# 第 2104 行
doc_dir = os.path.join(args.output_dir, "html", out_html_dir)
```

**问题：** 硬编码了 `html` 子目录

---

### WPF 这边的调用逻辑

**C# 调用：**
```csharp
// ExternalToolsIntegration.cs
var outputSubDir = Path.GetFileNameWithoutExtension(docxPath);  // "report"
var psi = new ProcessStartInfo
{
    Arguments = $"\"{docToChmScript}\" \"{docxPath}\" \"{outputSubDir}\" -o \"{outputDir}\""
};
```

**假设：**
- `docxPath` = `"D:\docs\report.docx"`
- `outputDir` = `"C:\Temp\CHMGen_xxx"`
- `outputSubDir` = `"report"`

**实际 Python 执行：**
```bash
python DocToCHM.py "D:\docs\report.docx" "report" -o "C:\Temp\CHMGen_xxx"
```

**实际输出：**
```
C:\Temp\CHMGen_xxx\
└── html\           ← 多了这一层！
    └── report\
        └── report.html
```

**WPF 期望的输出：**
```
C:\Temp\CHMGen_xxx\
└── report\         ← 直接在 outputDir 下
    └── report.html
```

**查找逻辑（第 93-105 行）：**
```csharp
// 查找生成的 HTML 文件
// Python 会在 outputDir/outputSubDir 下生成文件
var targetDir = Path.Combine(outputDir, outputSubDir);  // 错误！实际在 outputDir/html/outputSubDir
if (Directory.Exists(targetDir))
{
    var htmlFiles = Directory.GetFiles(targetDir, "*.html", SearchOption.TopDirectoryOnly);
    // 找不到文件！
}
```

---

## 🎯 需要的修改

### 方案 A：修改 Python 脚本（推荐）

**目标：** 移除硬编码的 `html` 子目录

**修改位置：**
- 文件：`ExternalTools/DocToCHM.py`
- 行号：2104
- 函数：`create_output_doc_dir`

**修改前：**
```python
def create_output_doc_dir(output_dir, out_html_dir, is_only_parse):
    doc_dir = os.path.join(args.output_dir, "html", out_html_dir);  # 硬编码 "html"
    # doc_dir = os.path.join(args.output_dir,"html",f"{uuid.uuid4()}");

    if not is_only_parse and not os.path.exists(doc_dir):
        os.makedirs(doc_dir)
    return doc_dir
```

**修改后：**
```python
def create_output_doc_dir(output_dir, out_html_dir, is_only_parse):
    doc_dir = os.path.join(args.output_dir, out_html_dir);  # 直接使用 out_html_dir
    # doc_dir = os.path.join(args.output_dir, f"{uuid.uuid4()}");

    if not is_only_parse and not os.path.exists(doc_dir):
        os.makedirs(doc_dir)
    return doc_dir
```

**影响：**
- ✅ 输出目录直接在 `output_dir/out_html_dir`
- ✅ 与 WPF 期望一致
- ⚠ 需要检查是否有其他地方依赖 `html` 子目录

---

### 方案 B：修改 WPF 调用（不推荐）

**目标：** 适配 Python 脚本的 `html` 子目录

**修改 C# 查找逻辑：**
```csharp
// 修改前
var targetDir = Path.Combine(outputDir, outputSubDir);

// 修改后
var targetDir = Path.Combine(outputDir, "html", outputSubDir);
```

**缺点：**
- ❌ 多了一层不必要的目录
- ❌ 与 CHM 生成的临时目录结构不一致
- ❌ 不符合 WPF 的设计意图

---

## 📋 推荐方案：修改 Python 脚本

### 修改步骤

#### 1. 备份原始文件
```bash
cp ExternalTools/DocToCHM.py ExternalTools/DocToCHM.py.backup
```

#### 2. 创建修改记录文件
创建 `ExternalTools/MODIFICATIONS.md` 记录所有修改

#### 3. 修改脚本
只修改第 2104 行

#### 4. 测试验证
```bash
python DocToCHM.py test.docx output_test -o temp
# 验证输出目录为: temp/output_test/xxx.html
# 而不是: temp/html/output_test/xxx.html
```

#### 5. 记录修改
在 `MODIFICATIONS.md` 中详细记录

---

## 🔍 需要检查的其他位置

### 可能依赖 `html` 子目录的地方：

1. **resource 文件复制（第 2145 行）**
```python
copy_resource_to_output("resource", args.output_dir)
```
需要检查这个函数是否依赖 `html` 目录

2. **其他脚本**
检查 `DocToHtmlByDir.py` 是否有类似逻辑

---

## 📝 修改记录模板

创建 `ExternalTools/MODIFICATIONS.md`：

```markdown
# DocToCHM.py 修改记录

## 修改历史

### [修改 #1] 2024-06-24 - 移除硬编码的 html 子目录

**原因：**
与 CHM Generator WPF 的预期输出目录结构不一致

**修改位置：**
- 文件：`DocToCHM.py`
- 函数：`create_output_doc_dir`
- 行号：2104

**修改前：**
\`\`\`python
doc_dir = os.path.join(args.output_dir, "html", out_html_dir);
\`\`\`

**修改后：**
\`\`\`python
doc_dir = os.path.join(args.output_dir, out_html_dir);
\`\`\`

**影响：**
- 输出目录从 `{output_dir}/html/{out_html_dir}` 改为 `{output_dir}/{out_html_dir}`
- WPF 调用可以正确找到生成的 HTML 文件

**测试：**
- [x] 单文件转换测试
- [x] 与 WPF 集成测试
- [x] 验证 resource 文件复制正常

**备份：**
原始文件已备份为 `DocToCHM.py.backup`
```

---

## ✅ 下一步行动

1. **你确认方案 A**（修改 Python 脚本）
2. **我执行修改**：
   - 备份原始文件
   - 创建 MODIFICATIONS.md
   - 修改第 2104 行
   - 检查 resource 复制逻辑
   - 更新项目文档
3. **测试验证**：
   - 单独测试 Python 脚本
   - 与 WPF 集成测试

---

**请确认是否继续修改？** 

如果同意，我会：
1. 创建详细的修改记录
2. 备份原始文件
3. 修改代码
4. 更新文档
