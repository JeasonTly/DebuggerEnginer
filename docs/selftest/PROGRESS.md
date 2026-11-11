# TestEngineer 过夜构建进度

更新时间：2026-11-11 21:45（UTC+8）

## 可验收入口

- Release：`src\TestEngineer.Desktop\bin\Release\net10.0-windows\TestEngineer.exe`
- 发布：`artifacts\publish\TestEngineer-Setup-0.1.0.exe` + Portable ZIP（见 `publish.md`）
- **验收结论**：`docs/selftest/ACCEPTANCE.md`
- 证据总目录：`docs/selftest/`

## 目标对照（摘要）

| 要求 | 状态 | 证据 |
|---|---|---|
| 七页卡 + 常驻投屏 + 多设备 | **PASS** | `screenshots/` + `light/`；在线 2/2 |
| ADB 跟踪 / 设备信息 / 运行时间 | **PASS**（无 Wi‑Fi 时 IP/SSID 空） | `full-features-dual-device.md` |
| 应用列表 / 装卸 / 清数据 / 授权 / 图标 | **PASS** | 双机报告、`app-icons.md`、`02-apps.png` |
| 文件双根 / 多选 / 右键 / 拖入拖出 / 证据包 | **PASS** | `theme-cast-files.md`、`evidence-pack-and-debug.md` |
| 调试 Monkey / bugreport / 日志导出 | **PASS**（Monkey UI 分钟级） | `evidence-pack-and-debug.md`、双机报告 |
| 控制台历史 / 芯片 | **PASS** | 控制台页 + shell 证据 |
| 投屏画面 / 按键 / 截屏录屏 | **PASS**；未启动舞台 `#0A0E14` | `mirror-in-app.md`；宿主折叠修复 |
| Cast 2×2 + About 分段 | **PASS** | `cast-about-1to1.md`、`06/07` 截图 |
| 主题三态浅/深截图 | **PASS** | `screenshots/` + `screenshots/light/` |
| 随包 adb/scrcpy/jar + 安装包 | **PASS** | `publish.md` |

## 已知限制

见 `ACCEPTANCE.md`：Wi‑Fi 空字段、KKST bugreport 一次 TIMEOUT、§11 未全 DPI 勾选。
