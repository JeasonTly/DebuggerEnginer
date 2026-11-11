# 新包安装/卸载自测

时间: 2026-11-11T20:37:58.2213087+08:00
设备: 99990000888800
APK: C:\Users\Tuly\Desktop\HZTR_ZNXJ.apk

## install
```
Performing Streamed Install
Success
```

## package discovery dump excerpt
```
  Package [com.android.cts.priv.ctsshim] (25ab186):
  Package [com.conquest.core] (6b1046a):
  Package [com.android.uwb.resources] (fd251e0):
  Package [com.android.internal.display.cutout.emulation.corner] (5388b3f):
  Package [com.android.adservices.api] (c03df6a):
  Package [com.android.internal.display.cutout.emulation.double] (494f5d1):
  Package [com.android.providers.telephony] (92c1ed1):
  Package [com.android.dynsystem] (f9e5599):
  Package [com.sprd.engineermode] (cb35d5b):
  Package [com.android.providers.calendar] (5e46da4):
  Package [com.android.providers.media] (9c634e3):
  Package [com.android.networkstack.tethering.overlay] (56df541):
  Package [com.android.internal.systemui.navbar.gestural_wide_back] (e9b4ad4):
  Package [com.android.wallpapercropper] (f47bfc3):
  Package [com.android.wifi.uniresources.marlin3] (7c8f0be):
  Package [com.conquest.sosapp] (e32a9f8):
  Package [com.conquest.talkie] (488090c):
  Package [com.huawei.appmarket] (cfdb0b1):
  Package [com.android.documentsui] (a911104):
  Package [com.hmct.imageedit] (81d3c3f):
  Package [com.android.externalstorage] (e88c4e9):
  Package [com.android.LogSettings] (1d5405e):
  Package [com.android.htmlviewer] (52c0a7a):
  Package [com.hmct.music] (46b2821):
  Package [com.android.companiondevicemanager] (5cc334):
  Package [com.android.mms.service] (fd2fd36):
  Package [com.sprd.camta] (bf5b255):
  Package [com.sprd.omacp] (3c07437):
  Package [com.android.providers.downloads] (813dee0):
  Package [com.google.android.networkstack.tethering.overlay] (10b9c1b):
  Package [it.song.remotestar.hxd] (40c5af6):
  Package [com.hmct.FileManager.Activity] (ab1fcd):
  Package [com.android.soundrecorder] (2752fd0):
  Package [com.hmct.ftmode] (3807d0e):
  Package [com.conquest.caculator] (ad97b85):
  Package [com.android.safetycenter.resources] (96f28e8):
  Package [com.android.providers.downloads.ui] (c82d70d):
  Package [com.android.pacprocessor] (290963d):
  Package [com.android.simappdialog] (15c2d00):
  Package [com.android.networkstack] (b1e1cd3):
```
## pm list packages -f -3 (excerpt)
```
package:/data/app/~~SAx8ps6PUiXTfUh5lFRUFQ==/com.litedo-7SIbPFrP8vDUVhR-EUMp2Q==/base.apk=com.litedo
package:/system/preload/Vision_SoundRecorder/Vision_SoundRecorder.apk=com.android.soundrecorder
package:/system/preload/Vision_DeskClock/Vision_DeskClock.apk=com.android.deskclock
package:/data/app/~~QC35Q7xaU5hg7smW8l1xlw==/com.nawensoft.nsptt-uopr3qljI4sj48Cfcn8LAQ==/base.apk=com.nawensoft.nsptt
package:/data/app/~~ftYgVh-X33aF1nacluSzBQ==/com.example.rfid-4Paqjap0fktT3bg5Ga0Bwg==/base.apk=com.example.rfid
package:/system/preload/UCBrowser/UCBrowser.apk=com.UCMobile
package:/data/app/~~iLiiRijdu_Dmix38TT80jg==/me.zhouzhuo810.aiccheck-tGT2lHqL5f6LLOnPkWLusw==/base.apk=me.zhouzhuo810.aiccheck
package:/system/preload/HxdIR2/HxdIR2.apk=it.song.remotestar.hxd
package:/system/preload/HWAppStore/HWAppStore.apk=com.huawei.appmarket
package:/system/preload/Vision_VideoPlayer/Vision_VideoPlayer.apk=com.android.hplayer
package:/system/preload/Vision_Gallery/Vision_Gallery.apk=com.android.gallery3d
package:/system/preload/Vision_Calendar/Vision_Calendar.apk=com.android.calendar
package:/system/preload/Vision_MusicPlayer/Vision_MusicPlayer.apk=com.hmct.music
```

## aapt badging
```
package: name='me.zhouzhuo810.aiccheck' versionCode='242' versionName='2.4.2' platformBuildVersionName='12' platformBuildVersionCode='32' compileSdkVersion='32' compileSdkVersionCodename='12'
```

## detected package: me.zhouzhuo810.aiccheck
```
package:/data/app/~~iLiiRijdu_Dmix38TT80jg==/me.zhouzhuo810.aiccheck-tGT2lHqL5f6LLOnPkWLusw==/base.apk
```

## uninstall
```
Success
```

## after
```

```

**结果: PASS**
