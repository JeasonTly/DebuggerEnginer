# 设备网络 / 运行时间自测

时间: 2026-11-11T21:28:30+08:00

## 结果

| 设备 | 型号 | uptime (`/proc/uptime`) | 非回环 IPv4 | SSID | MAC | 说明 |
|---|---|---|---|---|---|---|
| 99990000888800 | H29T | 181128+ 秒（可读） | 无（仅 `lo`） | 无 | 无 wlan0 | USB 调试在线，当前未连 Wi‑Fi |
| KKSTBE045026 | DSJ-TOPX1A1 | 36037+ 秒（可读） | 无（仅 `lo`） | 无 | 无 wlan0 | 同上 |

## 应用侧

- `DeviceInfoService` 解析 `WifiSsid` / `WifiMac` / `IpAddress` / `NetworkType` / `UptimeSec`
- `DevicePageViewModel` 每秒刷新 `UptimeText`
- UI 绑定见 `DevicePage.xaml`（网络类型 / IP / SSID / MAC / 运行时间）
- 无 Wi‑Fi 时字段显示「—」属预期（不编造）

## 结论

- **运行时间**：PASS（两机可读 + UI  ticker 已实现）
- **网络字段解析路径**：已实现；本环境无活跃非回环接口，真机有 Wi‑Fi 时方可填 SSID/MAC/IP
