# 缺陷证据包 / 系统信息 / 日志导出 — 自测证据

时间：2026-11-11 21:35（UTC+8）  
设备：`99990000888800`（H29T）  
构建：`dotnet build src/TestEngineer.Desktop -c Release` → **0 错误 0 警告**

## UI 接线

| 入口 | 命令 / API | 说明 |
|---|---|---|
| 文件页顶栏 / 侧栏「缺陷证据包」 | `FilesPageViewModel.ExportEvidencePackCommand` | 临时目录组装 → ZIP；默认目录 `Documents/TestEngineer/Exports` |
| 文件页顶栏 / 侧栏「系统信息」 | `FilesPageViewModel.ExportSystemInfoCommand` | `GetSnapshotAsync` → UTF-8 txt；过滤器可选 zip |
| 调试页「导出日志」 | `DebugPageViewModel.ExportLogcatCommand` → `ExportLogcatZipAsync` | 与 bugreport 并列 |

### 缺陷证据包内容

无选中远程文件时仍必含：

1. `screencap.png`（`CaptureScreenshotAsync`）
2. `logcat.txt` + `logcat-meta.txt`（`ExportLogcatZipAsync` 解出）
3. `device-summary.txt`（`DeviceSnapshot` 字段）
4. `manifest.txt`

有勾选/焦点文件时额外写入 `remote-files/`（`PullAsync`）。

## 真机结果（API 同 UI）

产物目录：`docs/selftest/artifacts/`

| 项 | 结果 | 路径 |
|---|---|---|
| 证据包 ZIP | **PASS**（654,973 bytes） | `artifacts/evidence-99990000888800-20261111-213549.zip` |
| logcat ZIP | **PASS** | `artifacts/logcat-99990000888800-20261111-213549.zip` |
| 系统信息 txt/zip | **PASS** | `artifacts/sysinfo-99990000888800-20261111-213549.txt` / `.zip` |
| 短 Monkey | **PASS**（50 事件，`--throttle 500`） | `artifacts/monkey-99990000888800-20261111-213549.txt` |

### 证据包条目

```
device-summary.txt (223)
logcat-meta.txt (123)
logcat.txt (6263707)
manifest.txt (219)
screencap.png (79774)
```

### Monkey 说明

- **UI 路径**：调试页「应用 Monkey」按 **分钟**（`MonkeyMinutes` 最小 1，内部 `DurationSeconds = minutes * 60`），偏长。
- **自测等价**：直接 `adb shell monkey --throttle 500 … 50`（约数十秒），验证设备侧 Monkey 可用；完整分钟级跑法走 UI `RunMonkeyAppCommand`。

## 改动文件

- `src/TestEngineer.Desktop/ViewModels/FilesPageViewModel.cs` — 证据包组装、系统信息导出
- `src/TestEngineer.Desktop/Views/FilesPage.xaml` — 顶栏 + 侧栏按钮
- `src/TestEngineer.Desktop/ViewModels/DebugPageViewModel.cs` — `ExportLogcatCommand`
- `src/TestEngineer.Desktop/Views/DebugPage.xaml` — 「导出日志」按钮
- `docs/selftest/evidence-pack-and-debug.md` — 本文
- `docs/selftest/artifacts/evidence-*.zip` 等 — 真机产物

## 未完成 / 说明

- 人工点 UI 保存对话框未在本机自动化（自测走同一服务 API）。
- Monkey UI 仍为分钟级；未改产品模型为秒/事件数。
- 含选中远程文件的证据包条目未在本次真机包内演示（无选中路径已覆盖必含三项）。
