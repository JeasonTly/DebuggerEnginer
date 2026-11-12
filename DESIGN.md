---
name: TestEngineer
description: Windows 安卓调试工作台 — 精密、可扫读、操作优先
colors:
  accent: "#2F6FED"
  accent-hover: "#2558C7"
  accent-muted: "#DCE8FF"
  success: "#1F7A4C"
  warning: "#A15C12"
  danger: "#C23B4A"
  app-bg-light: "#EEF1F6"
  surface-light: "#F7F9FC"
  surface-raised-light: "#FFFFFF"
  border-light: "#C9D3E0"
  text-primary-light: "#1A2433"
  text-secondary-light: "#5B6B7F"
  app-bg-dark: "#0F141C"
  surface-dark: "#171E28"
  surface-raised-dark: "#1E2733"
  border-dark: "#2E3A4A"
  text-primary-dark: "#E8EEF6"
  text-secondary-dark: "#9AA8BA"
  mirror-stage: "#0A0E14"
  focus-ring: "#2F6FED"
typography:
  page-title:
    fontFamily: "Segoe UI, Microsoft YaHei UI, sans-serif"
    fontSize: "22px"
    fontWeight: 600
    lineHeight: 1.25
  body:
    fontFamily: "Segoe UI, Microsoft YaHei UI, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.4
  secondary:
    fontFamily: "Segoe UI, Microsoft YaHei UI, sans-serif"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: 1.35
  mono:
    fontFamily: "Cascadia Mono, Consolas, monospace"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: 1.45
  label:
    fontFamily: "Segoe UI, Microsoft YaHei UI, sans-serif"
    fontSize: "12px"
    fontWeight: 600
    lineHeight: 1.3
rounded:
  control: "6px"
  container: "8px"
  chip: "999px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
  xxl: "32px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "#FFFFFF"
    rounded: "{rounded.control}"
    padding: "6px 12px"
    height: "32px"
  button-secondary:
    backgroundColor: "{colors.surface-raised-light}"
    textColor: "{colors.text-primary-light}"
    rounded: "{rounded.control}"
    padding: "6px 12px"
    height: "32px"
  button-danger:
    backgroundColor: "transparent"
    textColor: "{colors.danger}"
    rounded: "{rounded.control}"
    padding: "6px 12px"
    height: "32px"
  nav-item-active:
    backgroundColor: "{colors.accent-muted}"
    textColor: "{colors.accent}"
    rounded: "{rounded.control}"
    padding: "9px 10px"
  chip-device:
    backgroundColor: "{colors.surface-raised-light}"
    textColor: "{colors.text-secondary-light}"
    rounded: "{rounded.chip}"
    padding: "3px 10px"
---

## Overview

**Mode: Operate.** TestEngineer 是长时盯屏的调试工作台，不是营销页。视觉目标是「精密仪器台」：中性石墨面板、单一蓝色强调色、高信息密度但仍可扫读。品牌不靠大标题，靠稳定布局与状态可读性。

North star：**Bench Console** — 左侧工具架、中间作业面、右侧永远在线的设备视窗。

与 `windows-desktop-development-standard.md` 对齐：语义色、浅/深主题、4 DIP 间距网格、键盘焦点可见、不以颜色作为唯一状态通道。

Dial: ENERGY 1 / RHYTHM 1 / MOTION 1

## Colors

| 语义（WPF 资源名建议） | 浅色 | 深色 |
| --- | --- | --- |
| AppBackground | `#EEF1F6` | `#0F141C` |
| SurfaceBackground | `#F7F9FC` | `#171E28` |
| SurfaceRaised | `#FFFFFF` | `#1E2733` |
| TextPrimary | `#1A2433` | `#E8EEF6` |
| TextSecondary | `#5B6B7F` | `#9AA8BA` |
| Border | `#C9D3E0` | `#2E3A4A` |
| Accent | `#2F6FED` | `#2F6FED`（深色字上可用 `#6B9AFF` 作强调文字） |
| Success / Warning / Danger | `#1F7A4C` / `#A15C12` / `#C23B4A` | 同色相提高明度 |

- 业务页禁止散落硬编码色；经语义资源或动态资源访问。
- 投屏舞台背景固定近黑 `#0A0E14`，不随浅色主题变灰白（保证画面对比）。
- 在线状态：点 + 文案「在线/离线」，不只靠绿点。

## Typography

- UI：Segoe UI / Microsoft YaHei UI（WPF 原生栈）。
- 等宽：Cascadia Mono / Consolas（控制台、命令、序列号）。
- 页面标题 22 DIP；正文 14；辅助/表内 12；极次要 11。
- 不以展示衬线或装饰字体做工具界面。

## Layout

```
┌ TitleBar: 品牌 | 多设备切换 | 窗控 ─────────────────────────┐
├ Nav(可折叠) ┬ Workbench(页卡) ┬ Splitter ┬ Mirror(常驻) ───┤
│ 展开 168    │ 标题+主操作+内容 │ 5px      │ 最小 240 / 默 320 │
│ 折叠 56     │                │          │ 最大 520          │
├─────────────┴────────────────┴──────────┴──────────────────┤
│ StatusBar：ADB · 设备数 · 当前 serial · 权限/最近操作        │
└────────────────────────────────────────────────────────────┘
```

- **左侧功能区可折叠/展开**：展开显示图标+文案；折叠仅图标（tooltip 显示名称）；折叠后工作区吃掉宽度；投屏区宽度不变 unless 用户拖分隔条。
- 折叠状态建议持久化（用户设置）。
- 页卡顺序固定：设备信息 → 应用管理 → 文件管理 → 调试指令 → 控制台 → 远程投屏 → 开发者选项 → 定位日志 → 关于。
- 右侧投屏**永不随页卡卸载**；窗口缩放时画面 letterbox 保设备纵横比。
- 默认窗口约 1280×800；最小 1100×700 DIP。
- 间距只用 4 / 8 / 12 / 16 / 24 / 32。

## Elevation & Depth

- 以边框与表面层级分区，少用阴影。
- 允许：窗口壳一层轻阴影；模态一层中阴影。
- 禁止：多层霓虹光晕、大面积装饰渐变卡片墙。

## Shapes

- 控件圆角 6；容器 8；设备芯片全圆角。
- 列表/表格顶边与容器齐平，避免每格独立大卡片化。

## Components

- **Nav**：顶部一枚折叠/展开图标即可（勿另做底部折叠菜单条）；图标**左对齐**（与导航项图标列对齐），禁止水平居中；当前项用 Accent 浅底 + Accent 字；折叠时仅图标，热区高度一致。
- **Device switch**：标题栏下拉；离线项不可选为当前；显示型号、通道、build、serial。
- **Button**：Primary / Secondary / Ghost / Danger；主按钮高 32，重要主操作可达 36。
- **Modal**：调试快捷与授权结果用模态；标题 + 正文/等宽输出 + 固定底栏操作。
- **Console**：等宽输出区 + 单行输入 + 历史；↑↓ 翻历史。
- **File rows**：权限标签（可读写/可读/不可读）文字+色，不只靠色；左侧复选框。
- **跨目录多选**：选中以绝对路径放入 selection bag；进入其他目录、返回上级、切换根入口均**不清空**已选；可在 B 目录继续勾选追加。支持勾选与 Ctrl/Cmd+点击；顶部显示「已选 N（跨目录）」与查看/清空。
- **File context menu（右键）**：复制、剪切、删除、导出；导出须可选本机目标目录；若当前项在 bag 内且 bag>1，批量作用于全部已选。剪切/复制后显示剪贴板状态，粘贴仅在可写目录可用。
- **App list**：图标优先（36×36），主行仅应用名；包名次级等宽小号。行尾用图标按钮（启动/清数据/授权/卸载），避免「纯文字按钮墙」。筛选用分段控件。系统项无授权/卸载。Launcher3 可用 info 标签。
- **Debug shortcuts**：分类轨 + 一行标题/次行命令摘要；执行开模态。含参数的项（Monkey、导出日志、**bugreport**）先出配置再跑。
- **Device info**：须展示分辨率、内核版本、基带版本、安全补丁；**当前网络类型**（Wi‑Fi / 移动数据 / 以太网等）；若为 Wi‑Fi 须显示 **MAC 地址**（建议同时显示 SSID）；**系统运行时间每秒刷新**。
- **Console quick chips**：常用权限/日志快捷（如 setenforce、logcat 关键字过滤），点击填入或直接执行；历史仍保留。
- **File IO 区**：导入 / 证据包 / 系统信息；**不提供「导出 APK」入口**（文件导出走右键/工具栏即可）。
- **Mirror tools**：返回/Home/多任务/电源 + 截屏/录屏/旋转，始终可用。

## Do's and Don'ts

**Do**

- 先选定当前设备，再执行装包、指令、投屏事件。
- 长任务（Monkey、导出日志、录屏）给进度/可取消或明确不可取消。
- 浅色与深色都验收对比度与遗漏样式。
- 导航折叠后仍可用键盘顺序聚焦到各页。

**Don't**

- 不要用 Emoji 作为导航或工具主图标。
- 不要把导入导出拆回独立顶级页（已并入文件管理）。
- 不要给系统应用提供「一键授权」入口。
- 不要在无 DESIGN 令牌的情况下另起紫色/奶油陶土/酸绿默认皮肤。
- 不要让投屏随页卡切换而销毁会话（除非用户点停止）。
