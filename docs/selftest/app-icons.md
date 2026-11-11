# 应用名称与图标（D3）自测说明

日期：2026-11-11  
设备：`99990000888800`（H29T，API 33）

## 做到哪一步

| 项 | 状态 |
| --- | --- |
| `tools/device-helper` Java 源码 + `build.ps1` → `device-helper.jar` | 完成 |
| PC：`DeviceHelperClient` push + `app_process`，解析 `TE1` 行写入 `AppPackage` | 完成 |
| 失败回退：`DisplayName`=包名末段、`Initial` 首字占位、`HasIcon=false` | 完成 |
| AppsPage：36×36 圆角 8、系统/第三方标签、Segment「有图标」 | 完成 |
| 按 `packageName+versionCode` 磁盘图标缓存 | 未做（每次刷新跑 helper） |
| Launcher 置顶排序 | 完成（AppsPage 按 `HasLauncher` 降序） |

## 如何构建 helper

```powershell
pwsh -File tools/device-helper/build.ps1
```

产物：`tools/device-helper/device-helper.jar`（Desktop 工程会 CopyToOutputDirectory）。

## 真机验证（本机已跑）

```powershell
$adb = "tools/third-party/platform-tools/adb.exe"
$jar = "tools/device-helper/device-helper.jar"
& $adb -s 99990000888800 push $jar /data/local/tmp/te-device-helper.jar
& $adb -s 99990000888800 shell "CLASSPATH=/data/local/tmp/te-device-helper.jar app_process /system/bin com.testengineer.devicehelper.AppInfoDump --package com.litedo"
```

结果摘要（`--no-icons` 抽查 label）：

| 包名 | label |
| --- | --- |
| `com.litedo` | LiteDo |
| `com.example.rfid` | rfid_cf_uart1 |
| `com.nawensoft.nsptt` | 南山对讲 |
| `com.UCMobile` | UC浏览器 |
| `com.android.deskclock` | 时钟 |

另：`com.litedo` 带图标运行时 PNG Base64 非空、`hasLauncher=1`。

## UI / 单元验证

```powershell
dotnet test tests/TestEngineer.Adb.Tests -c Release --filter "FullyQualifiedName~DeviceHelper|FullyQualifiedName~AppPackageModel|FullyQualifiedName~AppManagerEnrichment"
dotnet build src/TestEngineer.Desktop -c Release
```

应用管理页：连上 `99990000888800` → 刷新 → 应见应用名（非仅包名）与图标；断 jar / 推送失败时仍显示末段名 + 首字色块，「有图标」段可能为 0。
