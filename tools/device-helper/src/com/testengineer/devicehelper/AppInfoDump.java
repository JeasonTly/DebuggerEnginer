package com.testengineer.devicehelper;

import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.drawable.Drawable;
import android.os.Looper;
import android.util.Base64;

import java.io.ByteArrayOutputStream;
import java.lang.reflect.Method;
import java.util.List;

/**
 * 以 app_process + CLASSPATH 方式在设备上运行（无需安装 APK），
 * 通过 PackageManager 输出包名、应用名、版本、系统/启动入口标记与 PNG 图标（Base64）。
 *
 * 行格式（制表符分隔）：
 * TE1 &lt;pkg&gt; &lt;label&gt; &lt;versionName&gt; &lt;versionCode&gt; &lt;isSystem&gt; &lt;hasLauncher&gt; &lt;iconB64|-&gt;
 */
public final class AppInfoDump {
    private static final String HDR = "TE_HELPER_OK";
    private static final String DONE = "TE_HELPER_DONE";
    private static final int ICON_SIZE = 72;

    public static void main(String[] args) {
        try {
            Context ctx = obtainSystemContext();
            PackageManager pm = ctx.getPackageManager();
            boolean withIcons = true;
            String onlyPackage = null;
            for (int i = 0; i < args.length; i++) {
                if ("--no-icons".equals(args[i])) {
                    withIcons = false;
                } else if ("--package".equals(args[i]) && i + 1 < args.length) {
                    onlyPackage = args[++i];
                }
            }

            System.out.println(HDR);
            if (onlyPackage != null) {
                dumpOne(pm, onlyPackage, withIcons);
            } else {
                List<PackageInfo> packages = pm.getInstalledPackages(0);
                for (PackageInfo pi : packages) {
                    dumpPackage(pm, pi, withIcons);
                }
            }
            System.out.println(DONE);
        } catch (Throwable t) {
            System.err.println("TE_HELPER_ERR " + t.getClass().getName() + ": " + t.getMessage());
            t.printStackTrace(System.err);
            System.exit(1);
        }
    }

    private static void dumpOne(PackageManager pm, String packageName, boolean withIcons) throws PackageManager.NameNotFoundException {
        PackageInfo pi = pm.getPackageInfo(packageName, 0);
        dumpPackage(pm, pi, withIcons);
    }

    private static void dumpPackage(PackageManager pm, PackageInfo pi, boolean withIcons) {
        ApplicationInfo ai = pi.applicationInfo;
        if (ai == null) {
            return;
        }

        String label = sanitize(String.valueOf(pm.getApplicationLabel(ai)));
        String versionName = sanitize(pi.versionName == null ? "" : pi.versionName);
        long versionCode = readVersionCode(pi);
        boolean isSystem = (ai.flags & ApplicationInfo.FLAG_SYSTEM) != 0;
        boolean hasLauncher = pm.getLaunchIntentForPackage(pi.packageName) != null
                || hasLauncherActivity(pm, pi.packageName);

        String icon = "-";
        if (withIcons) {
            try {
                icon = encodeIcon(pm.getApplicationIcon(ai));
            } catch (Throwable ignored) {
                icon = "-";
            }
        }

        System.out.println(
                "TE1\t" + pi.packageName + "\t" + label + "\t" + versionName + "\t"
                        + versionCode + "\t" + (isSystem ? "1" : "0") + "\t"
                        + (hasLauncher ? "1" : "0") + "\t" + icon);
    }

    private static boolean hasLauncherActivity(PackageManager pm, String packageName) {
        Intent intent = new Intent(Intent.ACTION_MAIN);
        intent.addCategory(Intent.CATEGORY_LAUNCHER);
        intent.setPackage(packageName);
        return !pm.queryIntentActivities(intent, 0).isEmpty();
    }

    private static String encodeIcon(Drawable drawable) {
        Bitmap bitmap = Bitmap.createBitmap(ICON_SIZE, ICON_SIZE, Bitmap.Config.ARGB_8888);
        Canvas canvas = new Canvas(bitmap);
        drawable.setBounds(0, 0, ICON_SIZE, ICON_SIZE);
        drawable.draw(canvas);
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        bitmap.compress(Bitmap.CompressFormat.PNG, 100, out);
        bitmap.recycle();
        return Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP);
    }

    private static long readVersionCode(PackageInfo pi) {
        try {
            Method m = PackageInfo.class.getMethod("getLongVersionCode");
            Object value = m.invoke(pi);
            if (value instanceof Long) {
                return ((Long) value).longValue();
            }
        } catch (Throwable ignored) {
            // API < 28
        }
        return pi.versionCode;
    }

    private static String sanitize(String value) {
        if (value == null || value.isEmpty()) {
            return "-";
        }
        return value.replace('\t', ' ').replace('\n', ' ').replace('\r', ' ');
    }

    private static Context obtainSystemContext() throws Exception {
        if (Looper.myLooper() == null) {
            Looper.prepareMainLooper();
        }
        Class<?> activityThreadClass = Class.forName("android.app.ActivityThread");
        Method systemMain = activityThreadClass.getDeclaredMethod("systemMain");
        Object activityThread = systemMain.invoke(null);
        Method getSystemContext = activityThreadClass.getDeclaredMethod("getSystemContext");
        Context context = (Context) getSystemContext.invoke(activityThread);
        if (context == null) {
            throw new IllegalStateException("ActivityThread.getSystemContext() returned null");
        }
        return context;
    }

    private AppInfoDump() {
    }
}
