using System.IO.Compression;
using System.Text;

namespace TestEngineer.Core.Preset;

public enum PresetBuildFile
{
    Mk,
    Bp,
}

public enum PresetCertificate
{
    Presigned,
    Platform,
    Shared,
    Media,
}

public sealed record PresetPackageOptions(
    string ApkPath,
    string ModuleName,
    string ParentDir,
    PresetBuildFile BuildFile,
    PresetCertificate Certificate,
    bool DexPreopt);

public sealed record PresetPackageResult(
    string OutputDir,
    string Abi,
    int SoCount,
    string BuildFileName);

public sealed class PresetPackageException : Exception
{
    public PresetPackageException(string message) : base(message)
    {
    }
}

/// <summary>把 APK 收成 AOSP 预置模块：复制包体，mk 时抽出一套原生库，并写出 Android.mk 或 Android.bp。</summary>
public static class PresetPackage
{
    private static readonly string[] AbiPriority = ["arm64-v8a", "armeabi-v7a", "armeabi"];
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static bool IsValidModuleName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var ch in name)
        {
            var ok = ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    public static string DefaultModuleName(string apkPath)
    {
        var file = Path.GetFileName(apkPath);
        var dot = file.LastIndexOf('.');
        if (dot > 0)
        {
            file = file[..dot];
        }

        var cleaned = new StringBuilder(file.Length);
        foreach (var ch in file)
        {
            var ok = ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-';
            if (ok)
            {
                cleaned.Append(ch);
            }
            else if (cleaned.Length > 0 && cleaned[^1] != '_')
            {
                cleaned.Append('_');
            }
        }

        while (cleaned.Length > 0 && cleaned[^1] == '_')
        {
            cleaned.Length--;
        }

        return cleaned.Length == 0 ? "PrebuiltApp" : cleaned.ToString();
    }

    public static string CertificateDisplayName(PresetCertificate certificate) => certificate switch
    {
        PresetCertificate.Platform => "platform",
        PresetCertificate.Shared => "shared",
        PresetCertificate.Media => "media",
        _ => "自身签名 (PRESIGNED)",
    };

    public static string CertificateMkValue(PresetCertificate certificate) => certificate switch
    {
        PresetCertificate.Platform => "platform",
        PresetCertificate.Shared => "shared",
        PresetCertificate.Media => "media",
        _ => "PRESIGNED",
    };

    public static string BuildAndroidMk(string moduleName, PresetCertificate certificate, string abi, bool dexPreopt)
    {
        var module = moduleName;
        var cert = CertificateMkValue(certificate);
        var text = new StringBuilder();
        text.Append("LOCAL_PATH := $(call my-dir)\n\n");
        if (!string.IsNullOrEmpty(abi))
        {
            text.Append("define get-all-libraries-module-name-in-subdirs\n");
            text.Append("$(sort $(shell cd $(LOCAL_PATH) ; find -L $(1) -name \"*.so\"))\n");
            text.Append("endef\n\n");
            text.Append("ALL_LIBRARIES_MODULE_NAME := $(call get-all-libraries-module-name-in-subdirs, lib/");
            text.Append(abi);
            text.Append(")\n\n");
            text.Append("$(warning ALL_LIBRARIES_MODULE_NAME:---  $(ALL_LIBRARIES_MODULE_NAME) )\n");
        }

        text.Append("##################################################################\n");
        text.Append("include $(CLEAR_VARS)\n");
        text.Append("LOCAL_MODULE := ").Append(module).Append('\n');
        text.Append("LOCAL_MODULE_CLASS := APPS\n");
        text.Append("LOCAL_MODULE_TAGS := optional\n");
        text.Append("LOCAL_BUILT_MODULE_STEM := package.apk\n");
        text.Append("LOCAL_MODULE_SUFFIX := $(COMMON_ANDROID_PACKAGE_SUFFIX)\n");
        text.Append("LOCAL_CERTIFICATE := ").Append(cert).Append('\n');
        text.Append("LOCAL_SRC_FILES := ").Append(module).Append(".apk\n");
        text.Append("LOCAL_ENFORCE_USES_LIBRARIES := false\n");
        text.Append(dexPreopt ? "LOCAL_DEX_PREOPT := true\n" : "LOCAL_DEX_PREOPT := false\n");
        if (!string.IsNullOrEmpty(abi))
        {
            text.Append("LOCAL_PREBUILT_JNI_LIBS := $(ALL_LIBRARIES_MODULE_NAME)\n");
            if (abi is "armeabi-v7a" or "armeabi")
            {
                text.Append("LOCAL_MULTILIB := 32\n");
            }
        }

        // PRESIGNED 需保留原包；系统签名时去掉，否则不会重签生效。
        if (certificate == PresetCertificate.Presigned)
        {
            text.Append("LOCAL_REPLACE_PREBUILT_APK_INSTALLED := $(LOCAL_PATH)/$(LOCAL_MODULE).apk\n");
        }

        text.Append("include $(BUILD_PREBUILT)\n");
        return text.ToString();
    }

    public static string BuildAndroidBp(string moduleName, PresetCertificate certificate, bool dexPreopt)
    {
        var text = new StringBuilder();
        text.Append("android_app_import {\n");
        text.Append("    name: \"").Append(moduleName).Append("\",\n");
        text.Append("    apk: \"").Append(moduleName).Append(".apk\",\n");
        if (certificate == PresetCertificate.Presigned)
        {
            text.Append("    presigned: true,\n");
        }
        else
        {
            text.Append("    certificate: \"").Append(CertificateMkValue(certificate)).Append("\",\n");
        }

        text.Append("    dex_preopt: {\n");
        text.Append(dexPreopt ? "        enabled: true,\n" : "        enabled: false,\n");
        text.Append("    },\n");
        text.Append("}\n");
        return text.ToString();
    }

    public static PresetPackageResult Generate(PresetPackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApkPath) || !File.Exists(options.ApkPath))
        {
            throw new PresetPackageException("APK 不存在。");
        }

        if (!IsValidModuleName(options.ModuleName))
        {
            throw new PresetPackageException("模块名无效。");
        }

        if (string.IsNullOrWhiteSpace(options.ParentDir))
        {
            throw new PresetPackageException("请选择输出目录。");
        }

        var outputDir = Path.Combine(options.ParentDir, options.ModuleName);
        try
        {
            if (Directory.Exists(outputDir) && !DeleteTree(outputDir))
            {
                throw new PresetPackageException("无法覆盖已有目录 " + outputDir);
            }

            Directory.CreateDirectory(outputDir);
        }
        catch (PresetPackageException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new PresetPackageException("无法创建目录 " + outputDir);
        }

        var destApk = Path.Combine(outputDir, options.ModuleName + ".apk");
        try
        {
            File.Copy(options.ApkPath, destApk, overwrite: true);
        }
        catch (Exception)
        {
            DeleteTree(outputDir);
            throw new PresetPackageException("无法复制 APK 到 " + destApk);
        }

        var abi = "";
        var soCount = 0;
        if (options.BuildFile == PresetBuildFile.Mk)
        {
            try
            {
                (abi, soCount) = ExtractPreferredLibs(destApk, outputDir);
            }
            catch (Exception)
            {
                DeleteTree(outputDir);
                throw new PresetPackageException("无法抽出原生库。");
            }
        }

        var buildName = options.BuildFile == PresetBuildFile.Bp ? "Android.bp" : "Android.mk";
        var buildPath = Path.Combine(outputDir, buildName);
        var content = options.BuildFile == PresetBuildFile.Bp
            ? BuildAndroidBp(options.ModuleName, options.Certificate, options.DexPreopt)
            : BuildAndroidMk(options.ModuleName, options.Certificate, abi, options.DexPreopt);
        try
        {
            File.WriteAllText(buildPath, content, Utf8NoBom);
        }
        catch (Exception)
        {
            DeleteTree(outputDir);
            throw new PresetPackageException("无法写入 " + buildPath);
        }

        return new PresetPackageResult(outputDir, abi, soCount, buildName);
    }

    private static (string Abi, int SoCount) ExtractPreferredLibs(string apkPath, string outputDir)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(apkPath);
        }
        catch (InvalidDataException)
        {
            return ("", 0);
        }

        using (zip)
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                if (TryAbiName(entry.FullName, out var abiName))
                {
                    present.Add(abiName);
                }
            }

            var abi = AbiPriority.FirstOrDefault(present.Contains);
            if (string.IsNullOrEmpty(abi))
            {
                return ("", 0);
            }

            var prefix = "lib/" + abi + "/";
            var destRoot = Path.Combine(outputDir, "lib", abi);
            var soCount = 0;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.EndsWith('/'))
                {
                    continue;
                }

                var relative = name[prefix.Length..];
                if (relative.Length == 0 || relative.Split('/').Contains(".."))
                {
                    continue;
                }

                var dest = Path.Combine(destRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                var parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                entry.ExtractToFile(dest, overwrite: true);
                if (relative.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                {
                    soCount++;
                }
            }

            return (abi, soCount);
        }
    }

    private static bool TryAbiName(string entryName, out string abi)
    {
        abi = "";
        var name = entryName.Replace('\\', '/');
        const string root = "lib/";
        if (!name.StartsWith(root, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = name[root.Length..];
        var slash = rest.IndexOf('/');
        if (slash <= 0)
        {
            return false;
        }

        abi = rest[..slash];
        return true;
    }

    private static bool DeleteTree(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return true;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
