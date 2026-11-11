# 文件拖出

时间: 2026-11-11

## 实现

- `FilesPageViewModel.PrepareDragOutLocalPathsAsync`：勾选/焦点项 `adb pull` 到 `%TEMP%\TestEngineer\drag-out\…`
- `FilesPage`：`PreviewMouseMove` 达到系统拖拽阈值后 `DoDragDrop(FileDrop)`
- 拖入：原有 `AllowDrop` / `OnDrop` → `PushLocalFilesAsync`

## 验证

| 项 | 结果 |
|---|---|
| pull 路径（拖出前置） | PASS（H29T：`/sdcard/te-dragout-test.txt` push→pull） |
| UI DoDragDrop | 已接线；需在应用内按住列表项拖到资源管理器目视确认 |

路径：`src/TestEngineer.Desktop/Views/FilesPage.xaml(.cs)`、`ViewModels/FilesPageViewModel.cs`
