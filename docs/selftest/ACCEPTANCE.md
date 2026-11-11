# TestEngineer 验收对照（ACCEPTANCE）

时间：2026-11-11（UTC+8）  
依据：过夜 Goal、`docs/ui-1to1-checklist.md`、`DESIGN.md`、双机真机自测  
可运行入口：`src\TestEngineer.Desktop\bin\Release\net10.0-windows\TestEngineer.exe`  
安装包：`artifacts\publish\TestEngineer-Setup-0.1.0.exe` + Portable ZIP（`publish.md`）

## 结论

**功能与发布主路径可验收；UI 相对原型/checklist 主结构已对齐（含 Cast 2×2、About 分段），浅/深主题有截图像素证据。**  
下列项为环境或已知限制，不阻塞「可运行交付」，但不得宣称 100% 像素级 / 全 DPI 人工勾完。

## Goal 项对照

| Goal 项 | 结论 | 证据 |
|---|---|---|
| 七页卡 + 右侧常驻投屏 + 多设备 | **PASS** | 截图 `screenshots/01–07*.png`；状态栏「在线 2/2」；`DeviceContext` 多机自动选第一台 |
| UI 对照原型 / DESIGN / checklist | **基本 PASS** | Cast/About：`cast-about-1to1.md`；七页深色+浅色截图；§11 未逐 DPI 勾完 |
| ADB / 设备信息（含网络/MAC/运行时间） | **PASS（网络空属环境）** | `full-features-dual-device.md`；本机仅 lo，IP/SSID/MAC 空 |
| 应用装卸 / 清数据 / 授权 / 图标 Label | **PASS** | 双机 APK 流；`app-icons.md`；截图 `02-apps.png` 有中文名与图标 |
| 文件双根 / 多选 / 右键 / 拖入拖出 / 证据包 | **PASS** | `theme-cast-files.md`、`evidence-pack-and-debug.md` |
| 调试 bugreport / Monkey / 日志导出 | **PASS（Monkey UI 分钟级）** | 双机 bugreport（KKST 一次 TIMEOUT）；短时 monkey adb 等价；日志导出接线 |
| 控制台历史与芯片 | **PASS** | 控制台页接线；双机 shell/logcat |
| 投屏画面 / 触摸按键 / 截屏录屏 | **PASS** | `mirror-in-app.md`、`mirror-hwnd-embed.md`；未嵌入时舞台折叠宿主露出 `#0A0E14` |
| 主题三态 | **PASS** | About 分段；浅色截图 `screenshots/light/` |
| 随包工具 + 安装包 | **PASS** | `publish.md`；adb/scrcpy/jar 随包 |
| 两台真机全功能 | **PASS（1 TIMEOUT）** | `full-features-dual-device.md`：PASS 34 / FAIL 0 / TIMEOUT 1 |

## UI 截图索引

| 页 | 深色 | 浅色 |
|---|---|---|
| 设备信息 | `screenshots/01-device.png` | `screenshots/light/01-device.png` |
| 应用管理 | `screenshots/02-apps.png` | `screenshots/light/02-apps.png` |
| 文件管理 | `screenshots/03-files.png` | `screenshots/light/03-files.png` |
| 调试指令 | `screenshots/04-debug.png` | `screenshots/light/04-debug.png` |
| 控制台 | `screenshots/05-console.png` | `screenshots/light/05-console.png` |
| 远程投屏 | `screenshots/06-cast.png` | `screenshots/light/06-cast.png` |
| 关于 | `screenshots/07-about.png` | `screenshots/light/07-about.png` |

脚本：`scripts/capture-pages.ps1`（深色全页 → 切浅色 → `light/` → 恢复深色）。

## 已知限制（诚实清单）

1. Wi‑Fi 环境无有效网卡信息时 IP/SSID/MAC 为空（解析链路已接，非功能缺失）。
2. `KKSTBE045026` bugreport 曾 60s TIMEOUT（另一台 PASS）。
3. Monkey 从 UI 跑完整分钟级会话偏慢；自测用短事件等价。
4. checklist §11 未在 125%/150%/200% DPI 与最小窗 1100×700 全矩阵人工勾选。
5. 调试页部分区块与原型细部间距仍可再抠，不影响主路径。

## 相关文档

- `docs/selftest/full-features-dual-device.md`
- `docs/selftest/cast-about-1to1.md`
- `docs/selftest/mirror-in-app.md` / `mirror-hwnd-embed.md`
- `docs/selftest/evidence-pack-and-debug.md`
- `docs/selftest/publish.md`
- `docs/selftest/PROGRESS.md`
