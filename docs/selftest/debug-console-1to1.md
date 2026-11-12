# 调试指令 / 控制台 1:1 对照（§5 / §6）

时间：2026-11-11（UTC+8）  
依据：`docs/ui-1to1-checklist.md` §5 / §6、`docs/ui-ux-v1-prototype.html` `#page-debug` / `#page-console`  
构建：`dotnet build src/TestEngineer.Desktop -c Release` → **0 错误 0 警告**

## §5 调试指令

| 对照项 | 落地 | 说明 |
|---|---|---|
| 页头「调试指令」22/600 | 是 | `Views/DebugPage.xaml` |
| 副标题「快捷操作 · 结果弹窗 · 作用于当前设备」 | 是 | 固定文案；无右侧主操作钮 |
| 两列 `168px \| 1fr`，间距 10，最小高 440 | 是 | 分类轨 + 指令 panel |
| 分类顺序：诊断查询 / 日志入口 / Settings / Monkey / am · pm | 是 | 数据键 `am·pm`，展示「am · pm」 |
| 当前类 AccentMuted + AccentText | 是 | `Button.Category` + `Selector.IsSelected` |
| 指令行：标题 + 等宽摘要 + 右侧按钮 | 是 | 摘要来自 Description / Kind / AdbArgs |
| 普通「执行」/ 参数项「配置」 | 是 | Monkey、export-logs、bugreport、包名参数 → 配置 |
| 结果弹窗：等宽输出 + 复制 + 关闭 | 是 | `CommandResultWindow`；首行 `$ adb -s …` |
| OEM 不可用禁用并说明 | 是 | `IsAvailableAsync` → 摘要「OEM=… 未检测到」 |

## §6 控制台

| 对照项 | 落地 | 说明 |
|---|---|---|
| 页头「控制台」+ 副标题 | 是 | |
| 右侧「清屏」Secondary、「清除历史」Danger | 是 | 清除历史二次确认后提示「命令历史已清空」 |
| 快捷芯片行（高 28、间距 6、下间距 10） | 是 | `Button.Chip` |
| 终端区填满剩余高度（不死锁 480） | 是 | Grid `*` 行；C7 |
| 底 `#0D1117`、等宽 12、文字 `#C9D1D9` | 是 | `Brush.Console` / `Brush.ConsoleText` |
| 输入行 `$ \| 输入 \| 历史 \| 运行(Primary)` | 是 | `$` 用 AccentText；运行中文案变「中止」 |
| 占位 `shell getprop ro.build.type` | 是 | 空输入叠字 |
| 历史面板可开合、最大高 120、填入 Ghost | 是 | 空时「暂无历史」 |
| Enter 运行；↑↓ 历史 | 是 | 到底再 ↓ 清空输入 |
| 初始首行 `TestEngineer Console · ready` | 是 | |

## 改动文件

- `src/TestEngineer.Desktop/Views/DebugPage.xaml` / `.xaml.cs`
- `src/TestEngineer.Desktop/ViewModels/DebugPageViewModel.cs`
- `src/TestEngineer.Desktop/Views/ConsolePage.xaml` / `.xaml.cs`
- `src/TestEngineer.Desktop/ViewModels/ConsolePageViewModel.cs`
- `src/TestEngineer.Desktop/Views/CommandResultWindow.xaml` / `.xaml.cs`（新建）
- `src/TestEngineer.UI/Themes/Common.xaml`（`Button.Category`）
- `docs/selftest/debug-console-1to1.md`（本文）

## 仍偏差项

1. **指令清单内容**：以 `Commands/debug-commands.json` 为准，与 checklist §5.3 / 原型不完全同构（缺「开发者选项」、MTK Logger 专用项；`am·pm` 为强制停止/查 APK 路径，非清数据/列第三方包）。
2. **配置模态**：Monkey / 包名用串联简易输入窗，未做成原型式 `88px \| 1fr` 字段网格单页；导出仍走 `SaveFileDialog`。
3. **结果模态抛光**：有复制/关闭与等宽区，但未做遮罩点击关闭、Esc、最大高 70%、长输出虚拟化等完整通则。
4. **芯片文案**：JSON 为「logcat *:E」「adb root/remount」「avbctl…」，非原型「logcat 仅 Error / root·remount 合并 / 禁用验证 (AVB)」。
5. **logcat 关键字**：填入栏无管道（C6）；关键字本身尚未做输出区应用内过滤，仅 Status 提示手动搜索。
6. **离线空态**：禁用输入/执行并 tooltip，非整页「设备已断开」空态构图。
7. **配置项进度条 / 状态栏长任务条**：Monkey、bugreport 仍主要靠 Status 文案，未接独立进度 UI。
