# 主题 / 投屏参数页 / 文件拖出 — 实现与验证

时间：2026-11-11 21:30（UTC+8）

## 主题切换

| 项 | 状态 |
|---|---|
| About 页三态（System/Light/Dark） | `AboutPageViewModel` → `IThemeService.Apply` |
| 浅/深资源字典 | `TestEngineer.UI/Themes/Light.xaml`、`Dark.xaml` |
| 启动时按设置应用 | `App.xaml.cs` |

验证：打开关于页切换主题，壳与页卡颜色即时变化（语义刷 `Brush.*`）。

## 远程投屏参数页

| 项 | 状态 |
|---|---|
| `CastPage.xaml` | 最大边长 / 码率 / 帧率 / stay-awake / turn-screen-off |
| `CastPageViewModel.ToOptions()` | 生成 `MirrorOptions` |
| `MainViewModel.StartMirrorCoreAsync` | 启动时消费 `_castPage.ToOptions`（含录屏路径） |

验证：改参数后点右侧「启动」应带对应 scrcpy 参数；录屏按钮走 RecordPath。

## 文件拖出

| 项 | 状态 |
|---|---|
| 列表拖出 | `FilesPage` PreviewMouseMove → `PrepareDragOutLocalPathsAsync` → `DoDragDrop(FileDrop)` |
| 底层 pull | 与导出相同 `IFileManagerService.PullAsync` |
| 路径级冒烟 | push/pull `/sdcard/te-dragout-test.txt` **PASS** |

说明：拖出前会先 pull 到 `%TEMP%\TestEngineer\drag-out\...`，再交给资源管理器。
