# AskShot UI 美化方案 — Claude Design System 风格

> 将 Claude Design System 的 token 和设计语言翻译为 WPF XAML 资源，
> 统一应用到 AskShot 所有窗口和控件。

---

## 一、设计语言映射

### Claude → WPF 关键对照

| Claude CSS Token | WPF 资源名 | 值 | 用途 |
|---|---|---|---|
| `--brand-500` | `BrandBrush` | `#C96442` | 主按钮、强调色、Tab 活态下划线 |
| `--bg-100` | `BackgroundBrush` | `#FAFAF5` | 窗口主背景 |
| `--bg-200` | `CardBackgroundBrush` | `#F5F4EF` | 卡片/分组背景 |
| `--bg-50` | `PopoverBackgroundBrush` | `#FFFFFF` | 弹窗/输入框背景 |
| `--bg-300` | `MutedBrush` | `#EDE9DE` | 禁用态、分隔区 |
| `--text-800` | `ForegroundBrush` | `#3D3929` | 正文文字 |
| `--text-900` | `HeavyForegroundBrush` | `#28261B` | 标题文字 |
| `--text-500` | `MutedForegroundBrush` | `#6E6D68` | 辅助说明文字 |
| `--border-300` | `BorderBrush` | `#DAD9D4` | 输入框/卡片边框 |
| `--border-500` | `InputBorderBrush` | `#B4B2A7` | 输入框聚焦态 |
| `--success-500` | `SuccessBrush` | `#788C5D` | 成功状态 |
| `--error-500` | `ErrorBrush` | `#D64545` | 错误/危险操作 |
| `--secondary` | `SecondaryBrush` | `#E9E6DC` | 次按钮/Tag 背景 |

### 字体映射

| Claude Token | WPF FontFamily | 用途 |
|---|---|---|
| `--font-sans` (Poppins) | Segoe UI (Windows 原生) | UI 控件（WPF 不支持 Google Fonts，用近似无衬线） |
| `--font-serif` (Lora) | Georgia | 标题、强调文字（Windows 内置） |
| `--font-mono` (Geist Mono) | Consolas | 代码、JSON 展示 |

> 注：Poppins 和 Lora 是 Web 字体，WPF 原生不支持 `@import`。
> 使用 Windows 系统近似字体：Segoe UI ≈ Poppins，Georgia ≈ Lora。
> 如需精确匹配可后续嵌入字体文件。

### 圆角映射

| Claude Token | WPF CornerRadius | 用途 |
|---|---|---|
| `--radius-sm` (8px) | `8` | 小控件（Input、Badge） |
| `--radius` (16px) | `16` | 按钮、Tab、Card |
| `--radius-xl` (20px) | `20` | 大面板、弹窗容器 |

### 阴影映射

WPF 不支持 CSS 级精细阴影分层，用 `DropShadowEffect` 近似：

| 用途 | BlurRadius | Direction | ShadowDepth | Opacity | Color |
|---|---|---|---|---|---|
| `shadow-xs/sm` (卡片默认) | `8` | `270` | `1` | `0.08` | `#000` |
| `shadow-md/lg` (悬浮窗) | `18` | `270` | `4` | `0.16` | `#000` |
| `shadow-xl` (弹窗叠加) | `24` | `270` | `6` | `0.20` | `#000` |

---

## 二、涉及文件清单

| # | 文件 | 改动类型 | 改动说明 |
|---|---|---|---|
| 1 | `src/AskShot.Client/App.xaml` | **重写** | 空的 Resources → 填入全套颜色/字体/圆角/阴影 Brush + 控件 implicit Style |
| 2 | `src/AskShot.Client/MainWindow.xaml` | **重写** | 去掉默认 Window chrome → 自定义标题栏 + 简约 Tab + 卡片分组表单 |
| 3 | `src/AskShot.Client/MainWindow.xaml.cs` | **修改** | 窗口拖拽 + 自定义关闭/最小化 + 删除 MessageBox 替换为行内通知 |
| 4 | `src/AskShot.Client/Views/ResultPopup.xaml` | **修改** | 按钮样式、spinner 颜色、输入框 placeholder、追问栏美化 |
| 5 | `src/AskShot.Client/Views/HistoryWindow.xaml` | **重写** | 搜索栏加图标 + 卡片列表项模板 + 详情区美化 |
| 6 | `src/AskShot.Client/Views/HistoryWindow.xaml.cs` | **修改** | 列表项模板数据绑定 |
| 7 | `src/AskShot.Client/Services/TrayIconService.cs` | **修改** | 图标色 → BrandBrush、菜单 hover 色、分隔线色 |

---

## 三、各窗口详细改动方案

### 3.1 App.xaml — 全局资源字典

**核心思路**：把 Claude Design System 的 token 翻译成 WPF `SolidColorBrush` 和 `Style`，
所有窗口通过 `{DynamicResource}` 引用，一处修改全局生效。

**资源分类**：

```
Application.Resources
├── Color Brushes (16 个)
│   ├── BrandBrush, BrandHoverBrush, BrandLightBrush
│   ├── BackgroundBrush, CardBackgroundBrush, PopoverBackgroundBrush, MutedBrush
│   ├── ForegroundBrush, HeavyForegroundBrush, MutedForegroundBrush
│   ├── BorderBrush, InputBorderBrush
│   ├── SuccessBrush, ErrorBrush, SecondaryBrush
│   └── RingBrush
├── Font Families (3 个)
│   ├── SansFont (Segoe UI)
│   ├── SerifFont (Georgia)
│   └── MonoFont (Consolas)
├── 控件 Implicit Styles (6 个)
│   ├── Button Style (圆角 16px, 44px 高, 主/次/ghost 三态)
│   ├── TextBox Style (圆角 8px, 40px 高, 暖灰边框, 聚焦高亮)
│   ├── CheckBox Style (自定义标记颜色)
│   ├── Slider Style (轨道/滑块使用 Brand 色)
│   ├── TabControl Style (去掉 3D 效果, 简约底线条)
│   └── ListBox Style (去掉默认选择色, 自定义 hover)
└── 共享模板
    └── ToastBar (用于替换 MessageBox 的行内通知)
```

**按钮三级体系**：

| 级别 | Background | Foreground | Border | 用途 |
|---|---|---|---|---|
| Primary | `#C96442` | `#FFFFFF` | 无 | "保存设置"、"测试连接" |
| Secondary | `#E9E6DC` | `#3D3929` | `#DAD9D4` | "刷新状态" |
| Ghost | 透明 | `#3D3929` | 无 | "清空历史"（危险操作加红色 foreground） |

**按钮交互态**：
- Hover → Background 加深 10%（Primary 用 `#B0562F`）
- Pressed → 再加深
- Disabled → `#E9E6DC` bg + `#6E6D68` fg

### 3.2 MainWindow — 控制台

**改动前**：WPF 默认 Window chrome + 3D TabControl + 裸按钮/输入框

**改动后**：

```
┌─────────────────────────────────────────────┐  ← 自定义标题栏 (24px 高, 背景色)
│  AskShot 控制台              _  □  ×        │  ← "AskShot" 用 SerifFont 加粗
├─────────────────────────────────────────────┤
│                                             │
│   ┌─ 通用 ─┬─ LLM 配置 ─┬─ 截图保存 ─┬─ 服务 ─┐  ← 简约 Tab (白卡片底, 
│   ────────────────────────────────────── │    活态文字 Brand 色, 底部 2px 指示线)
│                                             │
│   ┌─────────────────────────────────┐     │
│   │  截图快捷键                     │     │  ← 白色卡片, 圆角 16px, 微阴影
│   │  ┌───────────────────────────┐  │     │
│   │  │  Ctrl+Shift+A            │  │     │  ← Input 样式
│   │  └───────────────────────────┘  │     │
│   │  提示：Ctrl+Shift+A、Alt+…     │     │  ← 辅助文字 MutedForeground
│   │                                 │     │
│   │  [保存设置]                    │     │  ← Primary 按钮
│   └─────────────────────────────────┘     │
│                                             │
└─────────────────────────────────────────────┘
```

**具体改动**：

1. **Window**：`WindowStyle="None"` + `AllowsTransparency="True"` + 自定义标题栏
   - 标题栏：左侧 SerifFont "AskShot"，右侧最小化/关闭按钮（ghost 按钮）
   - 拖拽区域：整个标题栏 `MouseLeftButtonDown → DragMove()`
   - 关闭按钮 hover 时背景变 `#D64545`

2. **TabControl**：自定义 `ControlTemplate`
   - 去掉 WPF 原生 3D 凸起
   - Tab Header 水平排列，`#F5F4EF` 背景
   - 选中 Tab：白色背景 + 底部 2px `#C96442` 指示线
   - 未选中：`#6E6D68` 文字，hover 时 `#3D3929`
   - 圆角：整个 Tab 面板 `16px`

3. **表单区域**：每组设置用白色卡片包裹
   - `Background="#FFFFFF"`, `BorderBrush="#DAD9D4"`, `CornerRadius="16"`
   - `DropShadowEffect` 轻阴影
   - 内部控件按 16px 间距排列

4. **按钮层级**：
   - "保存设置"、"测试连接" → Primary
   - "重启服务"、"刷新状态"、"打开数据目录" → Secondary
   - "清空历史记录" → Ghost + Foreground 改为 `#D64545`

5. **删除 MessageBox**：
   - "设置已保存" → 顶部短暂出现的 Toast 条（2 秒自动消失）
   - "确定要清空？" → 内联确认（按钮文字从"清空历史记录"变为"确认清空？"再变回来）

6. **服务状态 Tab**：
   - 状态文字旁加圆点指示器（`Ellipse 8x8`）
   - 运行中：`#788C5D` 绿色 + "运行中"
   - 未运行：`#D64545` 红色 + "未运行"

### 3.3 ResultPopup — 悬浮结果窗

**改动前**：已有圆角阴影基础，但按钮是默认 WPF 样式，spinner 用 Google 蓝

**改动后**：

```
┌───────────────────────────────────────────┐  ← 背景 #FAFAF5, 圆角 20px
│  解释              [复制] [固定] [关闭]    │  ← 标题 SerifFont, 按钮 ghost 样式
├───────────────────────────────────────────┤
│                                          │
│   LLM 返回的分析结果文本…                │  ← 正文 ForegroundBrush
│                                          │
│                                          │
├───────────────────────────────────────────┤
│  继续追问…                      [Ask]   │  ← 输入框 card 样式, Ask 按钮用 Brand
└───────────────────────────────────────────┘
```

**具体改动**：

1. **容器背景**：`#FAFAF5`（从 `#FAFAFA` 调整为 Claude bg-100）
2. **标题 "解释"**：改为 `#3D3929`（当前 `#222222`），字体 `Georgia` SerifFont
3. **按钮**：全部应用 ghost 按钮 Style
   - `Width` 统一 `44px`（当前 `54px` 偏宽）
   - 圆角 `12px`，hover 时 `#EDE9DE` 背景
4. **Spinner**：颜色从 `#4285F4` 改为 `#C96442`（Brand 色）
5. **追问输入框**：
   - 外层 `Border` 背景从 `#F1F3F4` 改为 `#FFFFFF`（popover）
   - 边框 `#DAD9D4`，聚焦时 `#C96442`
   - 占位文字"继续追问…"（用 `TextBlock` overlay 实现伪 placeholder）
6. **Ask 按钮**：改为 Primary 样式（`#C96442` 背景 + 白色文字）

### 3.4 HistoryWindow — 历史记录

**改动前**：纯原生 ListBox + TextBox + 按钮

**改动后**：

```
┌─────────────────────────────────────────────────────┐  ← 同 MainWindow 自定义 chrome
│  AskShot 历史记录                        _  □  ×   │
├─────────────────────────────────────────────────────┤
│                                                     │
│  ┌────────────────────────────────── [搜索] [最近] [收藏] ┐  ← 搜索栏 + 操作按钮
│  └──────────────────────────────────────────────────────┘
│                                                     │
│  ┌──────────┐  ┌────────────────────────────────┐ │
│  │ ★ id:001 │  │  {                              │ │
│  │ 这是一段… │  │    "id": "001",                │ │
│  │          │  │    "analysis": "…",             │ │
│  │  id:002  │  │    "timestamp": "…"             │ │  ← JSON 详情用 MonoFont
│  │ 另一条记… │  │  }                              │ │
│  │          │  │                                  │ │
│  └──────────┘  └────────────────────────────────┘ │
│                                                     │
│  共 42 条记录                                        │  ← 状态栏 MutedForeground
└─────────────────────────────────────────────────────┘
```

**具体改动**：

1. **Window**：同 MainWindow 自定义 chrome
2. **搜索栏**：输入框改为 Card 样式（白背景 + 暖灰边框 + 圆角 16px）
   - "搜索"、"最近"按钮 → Secondary
   - "收藏/取消"按钮 → Primary
3. **左侧列表**：
   - `ListBox` 自定义 `ItemTemplate`，每项为卡片：
     ```
     ┌──────────────────────┐
     │ ★ 2024-01-15 14:30  │  ← 标题行：收藏标记 + 时间
     │ 这是一条分析的摘要…  │  ← 摘要前两行
     └──────────────────────┘
     ```
   - 选中项边框变为 `#C96442` Brand 色
   - hover 时背景 `#F5F4EF`
4. **右侧详情**：
   - `TextBox` 改为 MonoFont `Consolas`
   - 白色卡片背景 + 暖灰边框 + 圆角 16px
5. **状态栏**：`#6E6D68` MutedForeground

### 3.5 TrayIconService — 系统托盘

**改动点**（C# 代码中硬编码的颜色）：

1. **图标**：从 `DodgerBlue` 改为 `#C96442` Brand 色
   - 矩形背景 `Brushes` 改为 Claude Brand
2. **菜单背景**：从 `White` 改为 `#FAFAF5`
3. **菜单边框**：从 `LightGray` 改为 `#DAD9D4`
4. **分隔线**：从 `LightGray` 改为 `#EDE9DE`
5. **菜单项 hover**：从 `LightBlue` 改为 `#F5F4EF`
6. **字体**：`FontSize` 保持 13，增加 `FontFamily = new FontFamily("Segoe UI")`

### 3.6 RegionSelector — 区域选择器

**基本不改动**。这是全屏覆盖工具，需要高对比度：
- 半透明遮罩 `Opacity="0.5"` ✓ 保持
- 白色选框 `#FFFFFF` ✓ 保持
- 尺寸标签 `#CC000000` ✓ 保持

唯一可选改动：选区内的半透明背景从 `#22FFFFFF` 改为 `#22FAFAF5`（融入暖色），视觉差异极小，可选。

---

## 四、实施顺序

| 步骤 | 内容 | 依赖 |
|---|---|---|
| 1 | **App.xaml** — 建立全局资源字典（颜色/字体/控件样式） | 无 |
| 2 | **MainWindow.xaml + .cs** — 控制台重做 | 步骤 1 |
| 3 | **ResultPopup.xaml** — 悬浮窗精调 | 步骤 1 |
| 4 | **HistoryWindow.xaml + .cs** — 历史窗口改造 | 步骤 1 |
| 5 | **TrayIconService.cs** — 托盘颜色调整 | 步骤 1 |
| 6 | **RegionSelector.xaml** — 可选微调 | 无 |

每个步骤完成后可独立编译验证。

---

## 五、风险和注意事项

1. **字体缺失**：Segoe UI、Georgia、Consolas 都是 Windows 内置字体，无需额外安装
2. **DPI 缩放**：圆角和阴影值已在考虑 DPI，使用 WPF device-independent units
3. **向后兼容**：所有改动仅限 XAML 样式层，不影响业务逻辑和数据模型
4. **性能**：`DropShadowEffect` 在 WPF 中会触发软件渲染，但 AskShot 窗口数量少（最多 3-4 个），性能影响可忽略
5. **暗色模式**：本次只做 Light Mode，Claude 设计系统有 dark token 可后续扩展