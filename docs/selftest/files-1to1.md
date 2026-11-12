# 文件管理 1:1 对照（§4）

时间：2026-11-11（UTC+8）  
依据：`docs/ui-1to1-checklist.md` §4、`docs/ui-ux-v1-prototype.html` `#page-files`、`DESIGN.md` 令牌  
构建：`dotnet build src/TestEngineer.Desktop -c Release` → **0 错误 0 警告**

## 改动文件

| 文件 | 说明 |
|---|---|
| `Views/FilesPage.xaml` | 页头/选中条/剪贴板条/入口 150px/列布局/IO 三卡整体按原型重排 |
| `Views/FilesPage.xaml.cs` | 复选框点击、Ctrl+点击多选、右键先入 bag、拖入拖出 |
| `ViewModels/FilesPageViewModel.cs` | 选中/剪贴板 UI 状态、新建目录、可用性、面包屑项；保留选中 bag / 导入导出 / 证据包 / 系统信息命令 |
| `ViewModels/FileRowItem.cs` | 新增：行包装（IsSelected、权限标签、模式/大小/修改展示） |

## §4 对照表

| 对照项 | 落地 | 备注 |
|---|---|---|
| 页头标题「文件管理」22/600 | 是 | `Text.PageTitle` |
| 副标题固定文案 | 是 | 跨目录累积多选 · 勾选/Ctrl+点击 · 右键操作 · 切目录不丢选中 |
| 页头按钮序：粘贴/新建/上传/清空选中/导出选中(Primary) | 是 | 证据包/系统信息不在页头 |
| sel-bar（有选中才显示） | 是 | 已选 N（跨目录）+ 路径摘要 + 查看列表 |
| clip-bar（有剪贴板才显示，虚线边） | 是 | `复制：N 项` / `剪切：N 项` + 清除剪贴板 |
| 主体 150px \| 1fr，间距 10，min-height 420 | 是 | |
| 入口 panel 标题「入口」 | 是 | |
| 内部存储 + `/sdcard`；根目录 + `/system · /system_ext` 两行完整显示 | 是 | 无固定 Height 裁切；当前项 AccentMuted + AccentText |
| 切入口不清空已选 | 是 | |
| 面包屑：非末级 AccentText 可点；末级加粗；根显示「根目录」 | 是 | |
| 列表列 28 \| 1fr \| 72 \| 90 \| 70 \| 100 | 是 | 复选框\|名称\|权限\|模式\|大小\|修改 |
| 表头粘性 | 是 | 表头固定在列表容器顶，内容区滚动 |
| 复选框 14×14 绑定 IsSelected | 是 | `FileRowItem.IsSelected` ↔ bag |
| 名称列 DIR/FILE 标签 + 名称 | 是 | |
| 权限：可读写(绿)/可读(蓝)/不可读(红) 文字+色 | 是 | SuccessBg / AccentMuted / DangerBg |
| 模式等宽 11px | 是 | |
| 不可读行 opacity .65，复选框禁用 | 是 | |
| 跨目录 bag；Ctrl+点击；查看列表/清空 | 是 | |
| 粘贴/新建/上传仅可写目录；导出空袋提示「未选中」 | 是 | |
| IO 三卡等宽：导入 / 缺陷证据包 / 系统信息 | 是 | 无「导出 APK」 |
| 拖入上传、拖出导出 | 是 | 既有逻辑保留 |

## 与原型差异（残留）

| 项 | 状态 |
|---|---|
| 入口当前项边框 Accent 35% mix | 使用实色 `Brush.Accent` 1px（WPF 无 color-mix）；视觉接近 |
| 右键菜单矢量图标 | 仍为文字 `MenuItem`（复制/剪切/导出/删除）；功能与顺序符合 §4.6 |
| 「查看列表」/删除确认 | 使用系统 `MessageBox`，非原型样式模态 |
| 表头 sticky | 容器顶固定表头（非 ScrollViewer 内 CSS sticky），观感一致 |

**布局/文案/列结构/入口裁切/工具栏与 IO 区：按 §4 与原型已对齐；上述残留不影响 1:1 布局验收。**

## 手工抽查建议

1. 入口「根目录」副路径 `/system · /system_ext` 完整两行可见，当前项高亮。  
2. 勾选 A → 进子目录 → 再勾 → 返回/切入口 → 已选不丢；顶部 sel-bar N 为总数。  
3. 只读目录下粘贴/新建/上传禁用且有 tooltip。  
4. 页底三卡可见；页头无证据包/系统信息按钮。
