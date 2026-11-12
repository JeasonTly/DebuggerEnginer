using System.IO.Compression;
using TestEngineer.Core.Preset;

namespace TestEngineer.Core.Tests;

public sealed class PresetPackageTests
{
    [Theory]
    [InlineData(@"D:\pkg\Hello World (1).apk", "Hello_World_1")]
    [InlineData("a.b.apk", "a.b")]
    [InlineData("中文.apk", "PrebuiltApp")]
    [InlineData("already_ok-1.2.apk", "already_ok-1.2")]
    public void DefaultModuleName_KeepsLettersDigitsAndCollapsesTheRest(string path, string expected)
    {
        Assert.Equal(expected, PresetPackage.DefaultModuleName(path));
    }

    [Theory]
    [InlineData("Demo", true)]
    [InlineData("a.b-c_1", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("中文", false)]
    public void IsValidModuleName_AllowsAsciiModuleTokens(string name, bool expected)
    {
        Assert.Equal(expected, PresetPackage.IsValidModuleName(name));
    }

    [Fact]
    public void AndroidMk_WithArm64AndPresigned_MatchesHostTemplate()
    {
        var text = PresetPackage.BuildAndroidMk("Demo", PresetCertificate.Presigned, "arm64-v8a", dexPreopt: false);

        Assert.Equal(
            """
            LOCAL_PATH := $(call my-dir)

            define get-all-libraries-module-name-in-subdirs
            $(sort $(shell cd $(LOCAL_PATH) ; find -L $(1) -name "*.so"))
            endef

            ALL_LIBRARIES_MODULE_NAME := $(call get-all-libraries-module-name-in-subdirs, lib/arm64-v8a)

            $(warning ALL_LIBRARIES_MODULE_NAME:---  $(ALL_LIBRARIES_MODULE_NAME) )
            ##################################################################
            include $(CLEAR_VARS)
            LOCAL_MODULE := Demo
            LOCAL_MODULE_CLASS := APPS
            LOCAL_MODULE_TAGS := optional
            LOCAL_BUILT_MODULE_STEM := package.apk
            LOCAL_MODULE_SUFFIX := $(COMMON_ANDROID_PACKAGE_SUFFIX)
            LOCAL_CERTIFICATE := PRESIGNED
            LOCAL_SRC_FILES := Demo.apk
            LOCAL_ENFORCE_USES_LIBRARIES := false
            LOCAL_DEX_PREOPT := false
            LOCAL_PREBUILT_JNI_LIBS := $(ALL_LIBRARIES_MODULE_NAME)
            LOCAL_REPLACE_PREBUILT_APK_INSTALLED := $(LOCAL_PATH)/$(LOCAL_MODULE).apk
            include $(BUILD_PREBUILT)

            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            text);
    }

    [Fact]
    public void AndroidMk_32BitPlatformWithoutReplaceLine()
    {
        var text = PresetPackage.BuildAndroidMk("Demo", PresetCertificate.Platform, "armeabi-v7a", dexPreopt: true);

        Assert.Contains("LOCAL_CERTIFICATE := platform\n", text, StringComparison.Ordinal);
        Assert.Contains("LOCAL_DEX_PREOPT := true\n", text, StringComparison.Ordinal);
        Assert.Contains("LOCAL_MULTILIB := 32\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("LOCAL_REPLACE_PREBUILT_APK_INSTALLED", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidMk_WithoutNativeLibs_SkipsJniBlock()
    {
        var text = PresetPackage.BuildAndroidMk("Demo", PresetCertificate.Presigned, "", dexPreopt: false);

        Assert.DoesNotContain("LOCAL_PREBUILT_JNI_LIBS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("get-all-libraries", text, StringComparison.Ordinal);
        Assert.Contains("LOCAL_REPLACE_PREBUILT_APK_INSTALLED", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidBp_PresignedAndPlatform()
    {
        var presigned = PresetPackage.BuildAndroidBp("Demo", PresetCertificate.Presigned, dexPreopt: false);
        var platform = PresetPackage.BuildAndroidBp("Demo", PresetCertificate.Platform, dexPreopt: true);

        Assert.Contains("presigned: true,", presigned, StringComparison.Ordinal);
        Assert.Contains("enabled: false,", presigned, StringComparison.Ordinal);
        Assert.Contains("certificate: \"platform\",", platform, StringComparison.Ordinal);
        Assert.Contains("enabled: true,", platform, StringComparison.Ordinal);
        Assert.DoesNotContain("presigned:", platform, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_Mk_ExtractsPreferredAbiOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "te-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var apk = Path.Combine(root, "Demo App.apk");
            WriteApk(apk);
            var result = PresetPackage.Generate(new PresetPackageOptions(
                apk,
                "Demo_App",
                root,
                PresetBuildFile.Mk,
                PresetCertificate.Presigned,
                DexPreopt: false));

            Assert.Equal("arm64-v8a", result.Abi);
            Assert.Equal(2, result.SoCount);
            Assert.Equal("Android.mk", result.BuildFileName);
            Assert.True(File.Exists(Path.Combine(result.OutputDir, "Demo_App.apk")));
            Assert.True(File.Exists(Path.Combine(result.OutputDir, "lib", "arm64-v8a", "liba.so")));
            Assert.True(File.Exists(Path.Combine(result.OutputDir, "lib", "arm64-v8a", "nested", "libc.so")));
            Assert.False(Directory.Exists(Path.Combine(result.OutputDir, "lib", "armeabi-v7a")));
            var mk = File.ReadAllText(Path.Combine(result.OutputDir, "Android.mk"));
            Assert.Contains("lib/arm64-v8a", mk, StringComparison.Ordinal);
            Assert.DoesNotContain("\uFEFF", mk, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Generate_Bp_LeavesNativeLibsInsideApk()
    {
        var root = Path.Combine(Path.GetTempPath(), "te-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var apk = Path.Combine(root, "Demo.apk");
            WriteApk(apk);
            var result = PresetPackage.Generate(new PresetPackageOptions(
                apk,
                "Demo",
                root,
                PresetBuildFile.Bp,
                PresetCertificate.Shared,
                DexPreopt: false));

            Assert.Equal("", result.Abi);
            Assert.Equal(0, result.SoCount);
            Assert.Equal("Android.bp", result.BuildFileName);
            Assert.False(Directory.Exists(Path.Combine(result.OutputDir, "lib")));
            var bp = File.ReadAllText(Path.Combine(result.OutputDir, "Android.bp"));
            Assert.Contains("certificate: \"shared\",", bp, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Generate_RejectsMissingApkAndBadModuleName()
    {
        var missing = Assert.Throws<PresetPackageException>(() => PresetPackage.Generate(new PresetPackageOptions(
            Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".apk"),
            "Demo",
            Path.GetTempPath(),
            PresetBuildFile.Mk,
            PresetCertificate.Presigned,
            false)));
        Assert.Equal("APK 不存在。", missing.Message);

        var apk = Path.Combine(Path.GetTempPath(), "te-preset-src-" + Guid.NewGuid().ToString("N") + ".apk");
        File.WriteAllBytes(apk, [1, 2, 3]);
        try
        {
            var badName = Assert.Throws<PresetPackageException>(() => PresetPackage.Generate(new PresetPackageOptions(
                apk,
                "bad name",
                Path.GetTempPath(),
                PresetBuildFile.Bp,
                PresetCertificate.Presigned,
                false)));
            Assert.Equal("模块名无效。", badName.Message);
        }
        finally
        {
            File.Delete(apk);
        }
    }

    private static void WriteApk(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "classes.dex", "dex"u8);
        Add(zip, "lib/armeabi-v7a/libb.so", "v7"u8);
        Add(zip, "lib/arm64-v8a/liba.so", "a64"u8);
        Add(zip, "lib/arm64-v8a/nested/libc.so", "nest"u8);
    }

    private static void Add(ZipArchive zip, string name, ReadOnlySpan<byte> bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
}
