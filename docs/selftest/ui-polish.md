# UI 打磨记录（对照 docs/ui-1to1-checklist.md）

日期：2026-11-11　构建：`dotnet build src/TestEngineer.Desktop -c Release` → 0 错误 0 警告（仅 adb.exe 被占用的复制清理提示）；启动 6 秒无 XAML 加载异常。

## 已完成

| # | 项 | 结果 |
|---|---|---|
| 1 | 页面标题 22 / SemiBold | Device / Apps / Files / Debug / Console / Cast(SimplePage) / About 全部 22（Files/Debug/Console/About 已为 22，补齐 Device/Apps/SimplePage） |
| 2 | 正文 14 / 辅助 12 | MainWindow `FontSize=14`；副标题、表内 12；状态栏 11（checklist §1.6）；Common.xaml 保留 `FontSize.*` 令牌 |
| 3 | 导航 | 顶部单枚折叠键（矢量 `Icon.Nav.Collapse/Expand`，tooltip/AutomationName「折叠导航/展开导航」）；7 个导航项全部矢量图标（无 Emoji）；图标列 20 宽、距左 10，宽 168/56 两态均左对齐；折叠隐藏文案；当前项 AccentMuted 底 + AccentText 字；「关于」停靠底部；焦点环 Accent 2px |
| 4 | Apps | 行尾为 32×32 图标按钮（启动/清数据/授权/卸载，均带 tooltip 与 AutomationName）；固定 140 宽操作区；`IsSystem` DataTrigger → 授权/卸载 `Collapsed`（不渲染），启动/清数据列位固定；行最小高 48、行间 1px 分隔、悬停 AccentMuted；分段控件高亮当前项；系统图标底用 `Brush.SystemIcon` |
| 5 | Device | 指标卡圆角 6、间距 12；电量/存储/内存三卡加 6 高进度条（真实比例，用 `UsedRatio` 转换器）；信息卡值 12 等宽，运行时间 20 |
| 6 | MirrorStage | 外壳与舞台都用 `Brush.MirrorStage`，两个主题均为 `#0A0E14` |
| 7 | 共享按钮样式 | `TestEngineer.UI/Themes/Common.xaml`：`Button.Secondary`（同时作为全局隐式 Button 样式）、`Button.Primary`、`Button.Danger`、`Button.Ghost`、`Button.Icon`、`Button.IconDanger`、`Button.Chip`（28 高）、`Button.Segment`；统一 MinHeight 32、圆角 6、内边距 6×12、禁用 .45、焦点环；另有 `Icon.Glyph`、`ProgressBar.Meter`、`Text.PageTitle` |
| 8 | 兼容性 | 未改任何 ViewModel / DI；仅在 `UiConverters.cs` 新增转换器（`NavLabelVisibilityConverter`、`PageActiveConverter`、`StringEqualsConverter`、`UsedRatioConverter`、`AvailableToUsedRatioConverter`），`NavToggleTextConverter` 文案改为 tooltip 用语 |

## 变更文件

- `src/TestEngineer.UI/Themes/Common.xaml`（样式、图标几何、令牌）
- `src/TestEngineer.Desktop/MainWindow.xaml`
- `src/TestEngineer.Desktop/Converters/UiConverters.cs`
- `src/TestEngineer.Desktop/Views/DevicePage.xaml`
- `src/TestEngineer.Desktop/Views/AppsPage.xaml`
- `src/TestEngineer.Desktop/Views/ConsolePage.xaml`（芯片/运行/清历史按钮样式、终端令牌色）
- `src/TestEngineer.Desktop/Views/DebugPage.xaml`（按钮样式、输出区令牌色）
- `src/TestEngineer.Desktop/Views/AboutPage.xaml`（卡片圆角 6）
- `src/TestEngineer.Desktop/Views/SimplePage.xaml`（标题 22）
- 未改：`FilesPage.xaml`（父任务并行改动中；其局部按钮写法与新全局样式兼容）

## 未覆盖 / 后续

- 设备芯片下拉、投屏工具区仍为文字按钮（checklist §1.2/§1.5 的矢量图标与等宽平分未做）。
- Apps 分段控件缺「有图标」项（需 ViewModel 支持）；应用行无「系统/第三方」标签、无真实图标。
- Device 页未拆为 g3/g2 卡片结构；运行时间进度条/副文、阈值色未做。
- 未做浅色主题的人工目视验证（仅用语义资源，理论上两主题可用）。
- Files 页按钮仍有局部 Background/Foreground 覆盖，待父任务完成后可改用 `Button.Primary/Secondary`。
