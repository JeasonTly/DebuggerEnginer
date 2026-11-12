using System.Text;
using TestEngineer.Core.Apk;

namespace TestEngineer.Core.Tests;

public sealed class ApkManifestTests
{
    private const string Manifest = """
        <manifest package="com.example" android:sharedUserId="android.uid.system" xmlns:android="http://schemas.android.com/apk/res/android">
          <application>
            <activity android:name=".Split">
              <intent-filter>
                <action android:name="android.intent.action.MAIN" />
              </intent-filter>
              <intent-filter>
                <category android:name="android.intent.category.LAUNCHER" />
              </intent-filter>
            </activity>
            <activity android:name=".Main">
              <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
              </intent-filter>
            </activity>
            <activity android:name="Other" />
            <activity-alias android:name=".Alias" android:targetActivity=".Main">
              <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
              </intent-filter>
            </activity-alias>
            <service android:name=".Svc" />
            <provider android:name=".Prov" android:authorities="com.example.p" />
          </application>
          <uses-permission android:name="android.permission.INTERNET" />
          <uses-permission android:name="android.permission.CAMERA" />
          <uses-permission-sdk-23 android:name="android.permission.READ_PHONE_STATE" />
        </manifest>
        """;

    [Fact]
    public void PlainXml_CountsComponentsAndMarksLauncherEntries()
    {
        var report = ApkManifest.ReadXml(Encoding.UTF8.GetBytes(Manifest));

        Assert.Equal("com.example", report.PackageName);
        Assert.True(report.IsSystemSharedUser);
        Assert.Equal(ApkManifestReport.SystemSharedUserId, report.SharedUserId);
        Assert.Equal(4, report.Activities.Count);
        Assert.Equal(["com.example.Main", "com.example.Alias"], report.Activities.Where(item => item.IsMainEntry).Select(item => item.Name));
        Assert.Contains(report.Activities, item => item.Name == "com.example.Other" && !item.IsMainEntry);
        Assert.Contains(report.Activities, item => item.Name == "com.example.Split" && !item.IsMainEntry);
        Assert.True(report.Activities.Single(item => item.Name == "com.example.Alias").IsAlias);
        Assert.Equal(["com.example.Svc"], report.Services);
        Assert.Equal(["com.example.Prov"], report.Providers);
        Assert.Equal(
            ["android.permission.INTERNET", "android.permission.CAMERA", "android.permission.READ_PHONE_STATE"],
            report.Permissions);

        var text = report.ToDisplayText();
        Assert.Contains("界面：4 个", text, StringComparison.Ordinal);
        Assert.Contains("android:sharedUserId=\"android.uid.system\"：是", text, StringComparison.Ordinal);
        Assert.Contains("主入口 com.example.Main", text, StringComparison.Ordinal);
        Assert.Contains("主入口 com.example.Alias（别名）", text, StringComparison.Ordinal);
        Assert.DoesNotContain("主入口 com.example.Split", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainXml_OtherSharedUserIsNotSystem()
    {
        const string xml = """
            <manifest package="com.example" android:sharedUserId="android.uid.shared" xmlns:android="http://schemas.android.com/apk/res/android">
              <application>
                <activity android:name=".Main" />
              </application>
            </manifest>
            """;

        var report = ApkManifest.ReadXml(Encoding.UTF8.GetBytes(xml));

        Assert.False(report.IsSystemSharedUser);
        Assert.Equal("android.uid.shared", report.SharedUserId);
        Assert.Contains("android:sharedUserId=\"android.uid.system\"：否", report.ToDisplayText(), StringComparison.Ordinal);
        Assert.Contains("当前 sharedUserId：android.uid.shared", report.ToDisplayText(), StringComparison.Ordinal);
        Assert.Contains("（没有 ACTION_MAIN + CATEGORY_LAUNCHER）", report.ToDisplayText(), StringComparison.Ordinal);
    }

    [Fact]
    public void BinaryXml_ReadsLauncherServicePermissionAndSystemUser()
    {
        var xml = new BinaryXml();
        var manifest = xml.Add("manifest");
        var packageAttr = xml.Add("package");
        var packageName = xml.Add("com.example");
        var sharedAttr = xml.Add("sharedUserId");
        var sharedValue = xml.Add("android.uid.system");
        var androidNs = xml.Add("http://schemas.android.com/apk/res/android");
        var application = xml.Add("application");
        var activity = xml.Add("activity");
        var nameAttr = xml.Add("name");
        var mainName = xml.Add(".Main");
        var filter = xml.Add("intent-filter");
        var action = xml.Add("action");
        var mainAction = xml.Add("android.intent.action.MAIN");
        var category = xml.Add("category");
        var launcher = xml.Add("android.intent.category.LAUNCHER");
        var service = xml.Add("service");
        var serviceName = xml.Add(".Svc");
        var permission = xml.Add("uses-permission");
        var permissionName = xml.Add(new string('权', 80));

        xml.Start(manifest, (NoNs, packageAttr, packageName), (androidNs, sharedAttr, sharedValue));
        xml.Start(application);
        xml.Start(activity, (androidNs, nameAttr, mainName));
        xml.Start(filter);
        xml.Start(action, (androidNs, nameAttr, mainAction));
        xml.End();
        xml.Start(category, (androidNs, nameAttr, launcher));
        xml.End();
        xml.End();
        xml.End();
        xml.Start(service, (androidNs, nameAttr, serviceName));
        xml.End();
        xml.End();
        xml.Start(permission, (androidNs, nameAttr, permissionName));
        xml.End();
        xml.End();

        var report = ApkManifest.ReadXml(xml.ToArray());

        Assert.Equal("com.example", report.PackageName);
        Assert.True(report.IsSystemSharedUser);
        Assert.Equal(["com.example.Main"], report.Activities.Where(item => item.IsMainEntry).Select(item => item.Name));
        Assert.Equal(["com.example.Svc"], report.Services);
        Assert.Equal([new string('权', 80)], report.Permissions);
        Assert.Empty(report.Providers);
    }

    [Fact]
    public void Read_MissingFile_Throws()
    {
        var error = Assert.Throws<ApkManifestException>(() => ApkManifest.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".apk")));
        Assert.Equal("找不到 APK。", error.Message);
    }

    private const int NoNs = -1;

    private sealed class BinaryXml
    {
        private readonly List<byte[]> _strings = [];
        private readonly MemoryStream _body = new();

        public int Add(string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            using var stream = new MemoryStream();
            WriteLen(stream, value.Length);
            WriteLen(stream, encoded.Length);
            stream.Write(encoded);
            stream.WriteByte(0);
            _strings.Add(stream.ToArray());
            return _strings.Count - 1;
        }

        public void Start(int name, params (int Ns, int Name, int Value)[] attributes)
        {
            var size = 16 + 20 + (attributes.Length * 20);
            var writer = new BinaryWriter(_body, Encoding.UTF8, leaveOpen: true);
            writer.Write((ushort)0x0102);
            writer.Write((ushort)16);
            writer.Write(size);
            writer.Write(1);
            writer.Write(0xFFFFFFFF);
            writer.Write(0xFFFFFFFF);
            writer.Write(name);
            writer.Write((ushort)20);
            writer.Write((ushort)20);
            writer.Write((ushort)attributes.Length);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            foreach (var attribute in attributes)
            {
                writer.Write(attribute.Ns < 0 ? 0xFFFFFFFF : (uint)attribute.Ns);
                writer.Write(attribute.Name);
                writer.Write(attribute.Value);
                writer.Write((ushort)8);
                writer.Write((byte)0);
                writer.Write((byte)0x03);
                writer.Write(attribute.Value);
            }

            writer.Flush();
        }

        public void End()
        {
            var writer = new BinaryWriter(_body, Encoding.UTF8, leaveOpen: true);
            writer.Write((ushort)0x0103);
            writer.Write((ushort)16);
            writer.Write(24);
            writer.Write(1);
            writer.Write(0xFFFFFFFF);
            writer.Write(0xFFFFFFFF);
            writer.Write(0);
            writer.Flush();
        }

        public byte[] ToArray()
        {
            using var pool = new MemoryStream();
            var writer = new BinaryWriter(pool, Encoding.UTF8, leaveOpen: true);
            var stringsStart = 28 + (_strings.Count * 4);
            var cursor = 0;
            var payload = new MemoryStream();
            var offsets = new int[_strings.Count];
            for (var i = 0; i < _strings.Count; i++)
            {
                offsets[i] = cursor;
                payload.Write(_strings[i]);
                cursor += _strings[i].Length;
            }

            while (payload.Length % 4 != 0)
            {
                payload.WriteByte(0);
            }

            var chunkSize = stringsStart + (int)payload.Length;
            writer.Write((ushort)0x0001);
            writer.Write((ushort)28);
            writer.Write(chunkSize);
            writer.Write(_strings.Count);
            writer.Write(0);
            writer.Write(0x100);
            writer.Write(stringsStart);
            writer.Write(0);
            foreach (var offset in offsets)
            {
                writer.Write(offset);
            }

            writer.Write(payload.ToArray());
            writer.Flush();

            var body = _body.ToArray();
            var poolBytes = pool.ToArray();
            using var all = new MemoryStream();
            var outer = new BinaryWriter(all, Encoding.UTF8, leaveOpen: true);
            outer.Write((ushort)0x0003);
            outer.Write((ushort)8);
            outer.Write(8 + poolBytes.Length + body.Length);
            outer.Write(poolBytes);
            outer.Write(body);
            outer.Flush();
            return all.ToArray();
        }

        private static void WriteLen(Stream stream, int length)
        {
            if (length >= 0x80)
            {
                stream.WriteByte((byte)(0x80 | (length >> 8)));
                stream.WriteByte((byte)length);
            }
            else
            {
                stream.WriteByte((byte)length);
            }
        }
    }
}
