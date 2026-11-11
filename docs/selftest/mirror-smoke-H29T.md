# 投屏冒烟（scrcpy 进程级）

时间：2026-11-11 21:15（UTC+8）  
设备：`99990000888800`（H29T）

## 步骤

```text
scrcpy.exe -s 99990000888800 --window-title TE-Selftest --max-size 800 --no-audio
```

路径：`tools/third-party/scrcpy-win64-v3.3.3/scrcpy-win64-v3.3.3/scrcpy.exe`

运行约 8 秒后结束进程。

## 结果

| 项 | 结果 |
|---|---|
| 进程启动 | PASS（pid 存活） |
| HWND 嵌入进 WPF | 待应用内「启动投屏」人工/自动化确认 |
| 触摸转发 | 依赖 scrcpy 默认；待嵌入后确认 |
| 录屏 | UI 尚未接 RecordPath 按钮；会话层已支持 `MirrorOptions.RecordPath` |

## 说明

本条证明随包 scrcpy 可对 H29T 拉起；应用内 HwndHost 嵌入需在 UI 点「启动」后另记。
