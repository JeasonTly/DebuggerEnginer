# Cast / About 1:1 对照（§7 / §8）

时间：2026-11-11（UTC+8）  
依据：`docs/ui-1to1-checklist.md` §7 / §8、`docs/ui-ux-v1-prototype.html`  
构建：`dotnet build src/TestEngineer.Desktop -c Release` → **0 错误**

## §7 远程投屏（Cast）

| 对照项 | 落地 | 文件 |
|---|---|---|
| 页头标题「远程投屏」22/600 | 是 | `Views/CastPage.xaml` |
| 副标题「右侧常驻画面 · 本页仅参数」 | 是 | 同上 |
| 右侧 `停止` Danger + `启动 / 重连` Primary | 是；`RelativeSource AncestorType=Window` → `MainViewModel.StopMirrorCommand` / `StartMirrorCommand` | 同上 |
| 页体 2×2 四卡：画面 / 输入 / 录制 / 布局 | 是；间距 12、圆角 8 | 同上 |
| 画面：最大边 / 码率 Mbps / 帧率 / 编码「H.264 自动」 | 是；默认最大边 1280；`BitRateMbps` ↔ `BitRate` bps | `CastPageViewModel` |
| 改动旁「重启会话生效」 | 是 | `CastPage.xaml` |
| 输入：触摸/键 开、拖拽 APK 安装、拖拽文件 `/sdcard/Download` | 是（只读 kv） | 同上 |
| 录制：说明 + `目录` + `开始录屏` Primary | 是；目录 → `RecordDirectory`；开始录屏 → `StartMirrorRecordCommand` | xaml + VM + `MainViewModel` |
| 布局：「分隔条改宽；换页不销毁会话。」 | 是 | `CastPage.xaml` |
| `ToOptions` 可用 | 是；`CreateRecordPath(serial)` 写入 `RecordDirectory` | `CastPageViewModel` |

手工抽查：改最大边/码率后点「启动 / 重连」应带新参数；选目录后「开始录屏」文件落在所选目录；切页投屏会话不销毁。

## §8 关于（About）

| 对照项 | 落地 | 文件 |
|---|---|---|
| 页头「关于」+ 副标题「主题与版本」；无右侧按钮 | 是 | `Views/AboutPage.xaml` |
| g2 两卡：外观 / 版本 | 是；间距 12、圆角 8 | 同上 |
| 外观分段：浅色 \| 深色 \| 跟随系统 | 是；`Button.Segment` + `StringEqualsConverter`；点击设 `SelectedTheme` | xaml + `AboutPage.xaml.cs` |
| 主题立即切换并写入设置 | 是 | `AboutPageViewModel.OnSelectedThemeChanged` |
| 版本：应用（`VersionText`）、设计 `DESIGN.md · UI v1.5`、投屏 scrcpy 随包、第三方许可证说明 | 是 | `AboutPage.xaml` |
| 版本号来自程序集 | 是 | `AboutPageViewModel` |
| 离线可用、无空态 | 是（无设备依赖） | — |

手工抽查：三态主题即时生效；浅/深下投屏舞台仍为 `#0A0E14`。
