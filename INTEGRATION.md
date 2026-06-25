# CHM Generator WPF - 项目集成说明

## 概述

本项目整合了三个独立项目的功能：
1. **CHMGenerator.WPF** (当前项目) - WPF可视化界面
2. **doc2html Python工具** - 强大的Word转HTML转换器
3. **CHMGenerator C#控制台** - CHM项目文件生成器

## 集成架构

```
┌─────────────────────────────────────────────────────────────┐
│                    CHMGenerator.WPF                         │
│                   (主程序 - WPF界面)                         │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌──────────────────┐        ┌──────────────────┐         │
│  │  Word转HTML      │        │  CHM生成         │         │
│  ├──────────────────┤        ├──────────────────┤         │
│  │ • Python doc2html│───────▶│ • 解析txt配置    │         │
│  │   (外部调用)     │        │ • 生成hhp/hhc/hhk│         │
│  │ • OpenXmlPowerTools│      │ • 调用hhc.exe    │         │
│  │   (内置备用)     │        │ • 编译CHM        │         │
│  └──────────────────┘        └──────────────────┘         │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## 主要功能

### 1. Word 转 HTML

**两种转换方式：**

#### 方式一：Python doc2html（推荐）
- **优点**：功能强大，支持复杂格式、表格、脚注、超链接
- **输出结构**：
  ```
  output/
    html/
      {文档名}/          # HTML文件目录
        *.html           # 转换后的HTML文件
        images/          # 图片资源
      {文档名}.txt       # 目录结构配置文件
      css/               # CSS样式（公共）
      scripts/           # JavaScript脚本（公共）
  ```

- **txt配置文件格式**：
  ```
  相对路径\t标题\t父级路径
  chapter_1/chapter_1.html	第一章	
  chapter_1/section_1.html	第一节	chapter_1/chapter_1.html
  ```

#### 方式二：内置转换器（备用）
- **优点**：无需外部依赖，自动回退
- **使用场景**：Python不可用时自动启用

### 2. CHM 项目文件生成

**整合了两个项目的优点：**
- 从WPF界面的文档树生成目录结构
- 解析Python生成的txt配置文件
- 合并两种来源的内容到一个CHM中

**生成的文件：**
- `project.hhp` - CHM项目文件
- `toc.hhc` - 目录结构文件（支持层级关系）
- `index.hhk` - 索引文件（按字母排序）

### 3. 文件夹自动创建

**改进点：** 
- 旧版：选择已存在的输出文件夹
- 新版：选择父目录，自动创建项目文件夹
- 命名规则：基于项目标题，自动处理重名（添加序号）

## 使用流程

### 基本流程

1. **添加文件**
   - 点击"添加文件"按钮，选择Word或HTML文件
   - 支持拖拽文件到文档树
   - 可以创建文件夹组织文档结构

2. **配置项目**
   - 设置CHM标题
   - 调整文档顺序（上移/下移或拖拽）
   - 编辑节点标题

3. **生成CHM**
   - 点击"生成CHM"按钮
   - 选择输出位置（父目录）
   - 自动创建项目文件夹
   - 等待转换和编译完成

### Python doc2html 配置

**首次使用需要配置Python工具路径：**

1. 点击"⚙ 设置"按钮
2. 启用"使用 Python doc2html 转换器"
3. 设置Python工具目录路径（如：`D:\python\doc2html`）
4. 点击"保存配置"

**检查Python是否可用：**
- 程序会自动检测Python工具的可用性
- 需要确保以下文件存在：
  - `DocToCHM.py`
  - `hyperlink_processor.py`

## 文件结构说明

### 项目代码结构

```
CHMGenerator.WPF/
├── Services/
│   ├── ChmProjectGenerator.cs       # CHM项目文件生成器（增强版）
│   ├── ExternalToolsIntegration.cs  # Python工具集成
│   ├── TxtConfigParser.cs           # txt配置文件解析器（新增）
│   ├── ToolConfiguration.cs         # 工具配置管理
│   └── WordToHtmlConverter.cs       # 内置转换器
├── ViewModels/
│   └── MainViewModel.cs             # 主视图模型（增强）
└── Models/
    └── DocumentNode.cs              # 文档节点模型
```

### 输出结构示例

```
帮助文档/                    # 项目文件夹（基于CHM标题）
├── project.hhp             # CHM项目文件
├── toc.hhc                 # 目录文件
├── index.hhk               # 索引文件
├── 帮助文档.chm            # 编译后的CHM文件
└── src/                    # HTML源文件
    ├── chapter_1/          # 来自Python转换
    │   ├── chapter_1.html
    │   └── images/
    ├── chapter_2/
    └── manual.html         # 来自WPF添加的HTML
```

## txt配置文件格式统一

**支持两种格式（自动识别）：**

### 格式一：Python doc2html 输出
```
chapter_1/chapter_1.html	第一章	
chapter_1/section_1.html	第一节	chapter_1/chapter_1.html
```

### 格式二：CHMGenerator 格式
```
src/chapter_1/chapter_1.html	第一章	
src/chapter_1/section_1.html	第一节	src/chapter_1/chapter_1.html
```

**解析器会自动规范化为统一格式（带 src/ 前缀）。**

## 集成的核心代码

### 1. TxtConfigParser.cs
- 统一解析两种txt配置文件格式
- 自动添加路径前缀（src/）
- 支持多文件合并

### 2. ExternalToolsIntegration.cs
- 调用Python doc2html脚本
- 解析Python输出结构
- 返回生成的HTML目录和txt配置文件

### 3. ChmProjectGenerator.cs
- 接收额外的txt配置文件参数
- 合并文档树和txt配置的内容
- 生成统一的HHP/HHC/HHK文件

## 常见问题

### Q: Python转换失败怎么办？
A: 程序会自动回退到内置转换器，无需手动干预。

### Q: 如何混合使用手动添加的HTML和Word转换？
A: 直接在WPF界面添加HTML文件，Word文件会自动转换，最终合并到同一个CHM中。

### Q: txt配置文件的作用是什么？
A: 记录Word转HTML后的目录层级关系，确保CHM的目录结构正确。

### Q: 生成的CHM在哪里？
A: 在你选择的父目录下，自动创建的项目文件夹中。

## 技术细节

### Python调用方式
```csharp
python DocToCHM.py <docx路径> <输出子目录名> -o <输出根目录> -t <标题>
```

### txt配置解析逻辑
1. 读取txt文件每一行
2. 按制表符分割为：路径、标题、父级路径
3. 规范化路径（统一添加 src/ 前缀）
4. 构建父子关系映射
5. 递归生成HHC目录树

### 文件复制策略
- Word转HTML：Python生成在临时目录，最终复制到 src/
- 直接添加的HTML：直接复制到 src/
- 图片资源：自动复制对应的 _images 文件夹

## 未来扩展

可能的改进方向：
1. 支持更多Word转换选项（页眉页脚、批注等）
2. 支持Markdown转CHM
3. 增加CHM预览功能
4. 批量处理多个项目

## 致谢

本项目整合了以下开源技术：
- OpenXmlPowerTools - Word转HTML
- python-docx - Python Word处理
- Microsoft HTML Help Workshop - CHM编译
