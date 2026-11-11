namespace TestEngineer.Adb.Tests;

/// <summary>读取 tests/Fixtures 下的样本（真机采集或手工构造，已随测试输出复制）。</summary>
internal static class Fixture
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), System.Text.Encoding.UTF8);
}
