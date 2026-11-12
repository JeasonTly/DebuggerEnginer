# 文件 / 调试 / 控制台 / 投屏 验收摘记

时间：2026-11-11 23:08（UTC+8）  
截图：`docs/selftest/screenshots/accept/01–07*.png`、`06-cast-running.png`  
脚本：`scripts/accept-ui-pages.ps1`（导航前临时杀 scrcpy，避免 HwndHost 拖垮 UIA）

## 对照结论

| 页 | 相对原型 | 证据 |
|---|---|---|
| 文件管理 | 入口双行文案、表头六列、自定义 14×14 勾选、IO 三卡 | `03-files.png` |
| 调试指令 | 168 分类轨 + 指令行「执行」 | `04-debug.png` |
| 控制台 | 芯片行 + `$` 输入行 + 清屏/清除历史 | `05-console.png` |
| 投屏 | 默认启动；单实例；letterbox；失败自动重连 | 状态 Running；`scrcpy=1` |

## 投屏稳定性改动

- 启动前清理同序列号残留 scrcpy（避免多实例抢设备）
- `--no-audio` / `--video-buffer` / `--mouse=sdk` / 按舞台尺寸 `--window-width/height`
- 按 `wm size` letterbox；失败退避重连（最多 5 次）
- HwndHost 自定义 AutomationPeer，降低 UIA 卡死

## 已知自动化限制

带嵌入 HWND 时 UI Automation `FindFirst` 仍可能超时；验收脚本在遍历前杀 scrcpy，截完再点「启动投屏」。
