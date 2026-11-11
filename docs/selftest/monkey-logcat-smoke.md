# Monkey / logcat 短时冒烟

时间：2026-11-11 21:32（UTC+8）  
设备：`99990000888800`

| 项 | 结果 | 摘要 |
|---|---|---|
| Monkey 50 events → `com.android.settings` | PASS | Events injected: 50 |
| logcat 导出 zip | PASS | `docs/selftest/artifacts/logcat-*.zip` |
| screencap | PASS | `docs/selftest/artifacts/acceptance-screencap-99990000888800.png` (~80KB) |

说明：UI 侧 Monkey 以分钟为单位；本条用 adb monkey 50 事件验证同路径能力。完整分钟级可在调试页「应用 Monkey」跑。
