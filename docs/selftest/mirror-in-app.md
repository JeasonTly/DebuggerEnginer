# 应用内投屏嵌入验证

时间：2026-11-11 21:36（UTC+8）  
设备：`99990000888800`（H29T）

## 前置修复

- `DeviceContext`：多机在线且无上次选择时，自动选中第一台在线设备（避免「在线 2/2 · 未选择设备」）。

## 步骤

1. 启动 `TestEngineer.exe`，标题栏显示 **H29T (99990000888800)**，状态栏「在线 2/2 · 当前 H29T」。
2. UI Automation 调用按钮 AutomationName=`启动投屏`（`InvokePattern`）。
3. 进程出现 `scrcpy`；右侧常驻区显示设备画面（当时为「所有文件访问权限」设置页），状态为投屏运行中。

## 结果

| 项 | 结果 |
|---|---|
| 自动选中当前设备 | PASS |
| 启动投屏（应用内） | PASS |
| scrcpy 进程 | PASS（pid 存活） |
| 画面嵌入右侧面板 | PASS（可见真机 UI，非占位文案） |
| 触摸 | 依赖 scrcpy 默认转发；未另做坐标点击录证 |
| 按键/截屏/录屏工具栏 | 已接线（此前 HWND/录屏冒烟 PASS） |

## 相关

- 进程级 HWND：`mirror-hwnd-embed.md`
- 双机冒烟：`full-features-dual-device.md`
