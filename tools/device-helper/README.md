# device-helper

设备端辅助 jar（不安装 APK）。PC 通过 `adb push` + `app_process` 拉取应用名与 PNG 图标。

## 构建

```powershell
pwsh -File tools/device-helper/build.ps1
```

需要 JDK（`javac`/`jar`）与 Android SDK（`platforms/*/android.jar`、`build-tools/*/d8`）。

产物：`tools/device-helper/device-helper.jar`。

## 设备端运行

```text
adb push device-helper.jar /data/local/tmp/te-device-helper.jar
adb shell CLASSPATH=/data/local/tmp/te-device-helper.jar app_process /system/bin com.testengineer.devicehelper.AppInfoDump
```

可选参数：`--no-icons`、`--package <pkg>`。

## 输出

- 成功头：`TE_HELPER_OK`
- 行：`TE1\tpkg\tlabel\tversionName\tversionCode\tisSystem\thasLauncher\ticonB64|-`
- 尾：`TE_HELPER_DONE`
