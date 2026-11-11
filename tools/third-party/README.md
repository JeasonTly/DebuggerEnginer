# tools/third-party

随包分发的第三方命令行工具。**压缩包（`*.zip`）已被 `.gitignore` 忽略**；解压后的二进制保留在仓库中，
这样克隆即可运行（过夜演示场景）。若要精简仓库，可删除解压目录并按下表重新下载。

| 目录 | 来源 | 版本 | 用途 |
|---|---|---|---|
| `platform-tools/` | https://developer.android.com/tools/releases/platform-tools | 37.0.0 | `adb.exe`（设备枚举、shell、push/pull、install 等） |
| `scrcpy-win64-v3.3.3/scrcpy-win64-v3.3.3/` | https://github.com/Genymobile/scrcpy/releases/tag/v3.3.3 | 3.3.3 | `scrcpy.exe`（投屏） |

## 手动准备

1. 下载上述 zip，解压到本目录，保持路径：
   - `tools/third-party/platform-tools/adb.exe`
   - `tools/third-party/scrcpy-win64-v3.3.3/scrcpy-win64-v3.3.3/scrcpy.exe`
2. 无需配置环境变量。`ToolLocator` 的查找顺序：
   设置中的路径覆盖 → 本目录（从程序目录向上逐级查找）→ `ANDROID_HOME` / `ANDROID_SDK_ROOT` → `F:\AndroidSdk\platform-tools` → `PATH`。

> scrcpy 压缩包里也带有一个 `adb.exe`，**不会**被用作 adb 来源，避免与 platform-tools 的 adb server 版本冲突而反复重启服务。