# 双机全功能自测证据

时间戳: 2026-11-11T21:21:24+08:00
完成时间: 2026-11-11T21:23:59+08:00
adb: E:\CCode\TestEngineer\tools\third-party\platform-tools\adb.exe
scrcpy: E:\CCode\TestEngineer\tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3\scrcpy.exe (exists=True)
APK: C:\Users\Tuly\Desktop\HZTR_ZNXJ.apk -> package me.zhouzhuo810.aiccheck

## 汇总

- **PASS**: 34
- **FAIL**: 0
- **TIMEOUT**: 1（不计入致命）
- **失败项**: 无

## 设备清单

```
List of devices attached
99990000888800         device product:H29T model:H29T device:H29T transport_id:2
KKSTBE045026           device product:HIS60A1 model:DSJ_TOPX1A1 device:HIS60A1 transport_id:1
```

## 结果表（设备 × 功能）

| 时间 | 设备 | 功能 | 结果 | 命令 | 输出摘要 |
|---|---|---|---|---|---|
| 2026-11-11T21:21:24+08:00 | BOTH | devices -l | **PASS** | `adb devices -l` | List of devices attached ; 99990000888800         device product:H29T model:H29T device:H29T transport_id:2 ; KKSTBE045026           device product:HIS60A1 model:DSJ_TOPX1A1 device:HIS60A1 transport_id:1 ;  ; |
| 2026-11-11T21:21:24+08:00 | HOST | scrcpy.exe 存在性 | **PASS** | `Test-Path E:\CCode\TestEngineer\tools\third-party\scrcpy-win64-v3.3.3\scrcpy-win64-v3.3.3\scrcpy.exe` | exists=True |
| 2026-11-11T21:21:24+08:00 | 99990000888800 | getprop 关键字段 | **PASS** | `getprop model/release/type + ip/ssid` | model=H29T android=13 type=user ip= ssid= |
| 2026-11-11T21:21:24+08:00 | 99990000888800 | dumpsys battery | **PASS** | `dumpsys battery` | status: 2; health: 2; level: 93 |
| 2026-11-11T21:21:24+08:00 | 99990000888800 | df | **PASS** | `df -h` | Filesystem        Size Used Avail Use% Mounted on ; /dev/block/dm-6   1.3G 1.3G     0 100% / ; tmpfs             3.6G 1.5M  3.6G   1% /dev ; tmpfs             3.6G    0  3.6G   0% /mnt ; /dev/block/dm-7   310M 310M     0 100% /system_ext ; /dev/block/dm-8   893M 893M     0 100% /vendor ; /dev/block/...[truncated] |
| 2026-11-11T21:21:25+08:00 | 99990000888800 | meminfo | **PASS** | `cat /proc/meminfo` | MemTotal:        8388608 kB; MemFree:         2818340 kB; MemAvailable:    5553084 kB; Buffers:            6048 kB |
| 2026-11-11T21:21:25+08:00 | 99990000888800 | uptime | **PASS** | `cat /proc/uptime` | 180715.41 180620.24 ; |
| 2026-11-11T21:21:25+08:00 | 99990000888800 | wm size | **PASS** | `wm size` | Physical size: 720x1440 ; |
| 2026-11-11T21:21:25+08:00 | 99990000888800 | pm list packages -3 | **PASS** | `pm list packages -3` | count=12; exit=; package:com.android.calendar, package:com.android.hplayer, package:com.hmct.music, package:com.UCMobile, package:com.example.rfid, package:com.nawensoft.nsptt, package:com.huawei.appmarket, package:co...[truncated] |
| 2026-11-11T21:21:29+08:00 | 99990000888800 | APK install→启动→清数据→卸载 | **PASS** | `install/launch/clear/uninstall me.zhouzhuo810.aiccheck` | install=Success Performing Streamed Install ; Success ; / launch=monkey OK / clear=Success ; / uninstall=Success ; |
| 2026-11-11T21:21:30+08:00 | 99990000888800 | 文件 push/ls/pull/rm (/sdcard/) | **PASS** | `push/ls/pull/rm /sdcard/te-selftest-20261111-212124.txt` | push=True ls=True pull=True rm=True |
| 2026-11-11T21:21:34+08:00 | 99990000888800 | 根目录只读探测 ls / | **PASS** | `ls /` | acct ; apex ; bin ; blackbox ; bugreports ; cache ; config ; conquest ; d ; data ; data_mirror ; debug_ramdisk ; dev ; etc ; init ; init.environ.rc ; linkerconfig ; metadata ; mnt ; odm ; odm_dlkm ; o...[truncated] |
| 2026-11-11T21:21:50+08:00 | 99990000888800 | dumpsys window 焦点 | **PASS** | `dumpsys window / grep focus` | mCurrentFocus=Window{d4689f4 u0 com.android.settings/com.android.settings.Settings$ManageExternalStorageActivity}-[Surface(name=*Title#1389)/@0x356e163] / mFocusedApp=ActivityRecord{7a289ba u0 com.android.settings/.Settings$ManageExternalStorageActivity} t48 d0} / mCurrentFocus=Window{d4689f4 u0 com...[truncated] |
| 2026-11-11T21:21:51+08:00 | 99990000888800 | 截屏 screencap→artifacts | **PASS** | `screencap/pull E:\CCode\TestEngineer\docs\selftest\artifacts\screencap-99990000888800-20261111-212124.png` | size=79470 |
| 2026-11-11T21:22:34+08:00 | 99990000888800 | bugreport (60s限时) | **PASS** | `bugreport E:\CCode\TestEngineer\docs\selftest\artifacts\bugreport-99990000888800-20261111-212124.zip` | size=2850293 |
| 2026-11-11T21:22:34+08:00 | 99990000888800 | getenforce | **PASS** | `getenforce` | Enforcing ; |
| 2026-11-11T21:22:34+08:00 | 99990000888800 | logcat -d -t 20 | **PASS** | `logcat -d -t 20` | --------- beginning of system ; 11-11 15:40:42.426  1084  1199 D PowerManagerService: releaseWakeLockInternal: lock=147349319 [*vibrator*], flags=0x0 , total_time=95ms ; --------- beginning of main ; 11-11 15:40:42.505  1084  1183 W SensorService: co...[truncated] |
| 2026-11-11T21:22:34+08:00 | 99990000888800 | adb -s shell echo ok | **PASS** | `echo ok` | ok ; |
| 2026-11-11T21:22:35+08:00 | KKSTBE045026 | getprop 关键字段 | **PASS** | `getprop model/release/type + ip/ssid` | model=DSJ-TOPX1A1 android=13 type=user ip= ssid= |
| 2026-11-11T21:22:35+08:00 | KKSTBE045026 | dumpsys battery | **PASS** | `dumpsys battery` | status: 2; health: 2; level: 77 |
| 2026-11-11T21:22:35+08:00 | KKSTBE045026 | df | **PASS** | `df -h` | Filesystem        Size Used Avail Use% Mounted on ; /dev/block/dm-6   754M 754M     0 100% / ; tmpfs             1.6G 1.4M  1.6G   1% /dev ; tmpfs             1.6G    0  1.6G   0% /mnt ; /dev/block/dm-7   243M 243M     0 100% /system_ext ; /dev/block/dm-8   902M 902M     0 100% /vendor ; /dev/block/...[truncated] |
| 2026-11-11T21:22:35+08:00 | KKSTBE045026 | meminfo | **PASS** | `cat /proc/meminfo` | MemTotal:        4194304 kB; MemFree:           79260 kB; MemAvailable:    1968748 kB; Buffers:           24116 kB |
| 2026-11-11T21:22:35+08:00 | KKSTBE045026 | uptime | **PASS** | `cat /proc/uptime` | 35694.89 47549.61 ; |
| 2026-11-11T21:22:35+08:00 | KKSTBE045026 | wm size | **PASS** | `wm size` | Physical size: 480x640 ; |
| 2026-11-11T21:22:35+08:00 | KKSTBE045026 | pm list packages -3 | **PASS** | `pm list packages -3` | count=1; exit=; package:com.terminalv3.manager |
| 2026-11-11T21:22:38+08:00 | KKSTBE045026 | APK install→启动→清数据→卸载 | **PASS** | `install/launch/clear/uninstall me.zhouzhuo810.aiccheck` | install=Success Performing Streamed Install ; Success ; / launch=monkey OK / clear=Success ; / uninstall=Success ; |
| 2026-11-11T21:22:39+08:00 | KKSTBE045026 | 文件 push/ls/pull/rm (/sdcard/) | **PASS** | `push/ls/pull/rm /sdcard/te-selftest-20261111-212124.txt` | push=True ls=True pull=True rm=True |
| 2026-11-11T21:22:39+08:00 | KKSTBE045026 | 根目录只读探测 ls / | **PASS** | `ls /` | acct ; apex ; bin ; blackbox ; bugreports ; cache ; config ; d ; data ; data_mirror ; debug_ramdisk ; dev ; etc ; init ; init.environ.rc ; kdebuginfo ; linkerconfig ; metadata ; mnt ; odm ; odm_dlkm ;...[truncated] |
| 2026-11-11T21:22:50+08:00 | KKSTBE045026 | dumpsys window 焦点 | **PASS** | `dumpsys window / grep focus` | mCurrentFocus=Window{cb3c574 u0 com.android.settings/com.android.settings.Settings$ManageExternalStorageActivity}-[Surface(name=*Title#243)/@0x601c4e0] / mFocusedApp=ActivityRecord{daee313 u0 com.android.settings/.Settings$ManageExternalStorageActivity} t13 d0} / mCurrentFocus=Window{cb3c574 u0 com....[truncated] |
| 2026-11-11T21:22:50+08:00 | KKSTBE045026 | 截屏 screencap→artifacts | **PASS** | `screencap/pull E:\CCode\TestEngineer\docs\selftest\artifacts\screencap-KKSTBE045026-20261111-212124.png` | size=31219 |
| 2026-11-11T21:23:50+08:00 | KKSTBE045026 | bugreport (60s限时) | **TIMEOUT** | `adb bugreport timeout 60s` | TIMEOUT — 不计入致命失败; partialSize=0 |
| 2026-11-11T21:23:51+08:00 | KKSTBE045026 | getenforce | **PASS** | `getenforce` | Enforcing ; |
| 2026-11-11T21:23:51+08:00 | KKSTBE045026 | logcat -d -t 20 | **PASS** | `logcat -d -t 20` | --------- beginning of main ; 11-11 13:23:43.885 19301 19301 I flags_health_check: ServerConfigurableFlagsReset updatable crashing detected, resetting flags. ; 11-11 13:23:46.642  2946  2946 W minalv3.manager: type=1400 audit(0.0:12482): avc: denied ...[truncated] |
| 2026-11-11T21:23:51+08:00 | KKSTBE045026 | adb -s shell echo ok | **PASS** | `echo ok` | ok ; |
| 2026-11-11T21:23:59+08:00 | 99990000888800 | scrcpy 短时启动(~8s) | **PASS** | `scrcpy --window-title TestEngineerSelftest --max-size 800` | 进程存活>=8s后已杀 |

## 产物

- 报告: `docs/selftest/full-features-dual-device.md`
- 截屏/拉取/bugreport: `docs/selftest/artifacts/`
- screencap-KKSTBE045026-20261111-212124.png (31219 bytes)
- pulled-KKSTBE045026-20261111-212124.txt (45 bytes)
- bugreport-99990000888800-20261111-212124.zip (2850293 bytes)
- screencap-99990000888800-20261111-212124.png (79470 bytes)
- bugreport-retry-H29T.zip (2877979 bytes)
- bugreport-99990000888800-20261111-211938.zip (2877979 bytes)
- pulled-99990000888800-20261111-212124.txt (47 bytes)
- bugreport-KKSTBE045026-20261111-211938.zip (2490863 bytes)
- screencap-KKSTBE045026-20261111-211922.png (32153 bytes)
- pulled-KKSTBE045026-20261111-211922.txt (36 bytes)
- bugreport-99990000888800-20261111-211922.zip (2900798 bytes)
- screencap-KKSTBE045026-20261111-211938.png (32153 bytes)
- pulled-KKSTBE045026-20261111-211938.txt (45 bytes)
- screencap-99990000888800-20261111-211938.png (79701 bytes)
- pulled-99990000888800-20261111-211938.txt (47 bytes)
- bugreport-KKSTBE045026-20261111-211807.zip (2465870 bytes)
- screencap-99990000888800-20261111-211922.png (79701 bytes)
- pulled-99990000888800-20261111-211922.txt (38 bytes)
- screencap-KKSTBE045026-20261111-211807.png (31791 bytes)
- pulled-KKSTBE045026-20261111-211807.txt (63 bytes)
- screencap-99990000888800-20261111-211807.png (79972 bytes)
- pulled-99990000888800-20261111-211816.txt (38 bytes)
- pulled-99990000888800-20261111-211807.txt (65 bytes)
