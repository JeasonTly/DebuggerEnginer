using TestEngineer.Core.Files;
using TestEngineer.Core.Models;

namespace TestEngineer.Core.Tests;

public sealed class SelectionBagTests
{
    private static FileEntry File(string path, bool isDirectory = false) =>
        new(RemotePath.GetName(path), path, isDirectory, isDirectory ? "drwxrws---" : "-rw-rw----", isDirectory ? 3452 : 10, null);

    [Fact]
    public void Add_AcrossDirectories_KeepsEverythingKeyedByAbsolutePath()
    {
        var bag = new SelectionBag();

        bag.Add(File("/sdcard/DCIM/a.jpg"));
        bag.Add(File("/sdcard/Download/a.jpg")); // 同名文件、不同目录，必须并存。
        bag.Add(File("/sdcard/相机/照片 1.jpg"));

        Assert.Equal(3, bag.Count);
        Assert.True(bag.Contains("/sdcard/DCIM/a.jpg"));
        Assert.True(bag.Contains("/sdcard/Download/a.jpg"));
        Assert.True(bag.Contains("/sdcard/相机/照片 1.jpg"));
        Assert.Equal(
            ["/sdcard/DCIM/a.jpg", "/sdcard/Download/a.jpg", "/sdcard/相机/照片 1.jpg"],
            bag.Paths);
    }

    [Fact]
    public void Add_SamePathTwice_IsDeduplicatedAndNormalized()
    {
        var bag = new SelectionBag();

        Assert.True(bag.Add(File("/sdcard/a.txt")));
        Assert.False(bag.Add(File("/sdcard//a.txt/"))); // 规范化后同一个路径

        Assert.Equal(1, bag.Count);
        Assert.True(bag.Contains("/sdcard/./a.txt"));
    }

    [Fact]
    public void PathsAreCaseSensitive()
    {
        var bag = new SelectionBag();
        bag.Add(File("/sdcard/A.txt"));
        bag.Add(File("/sdcard/a.txt"));

        Assert.Equal(2, bag.Count);
    }

    [Fact]
    public void Toggle_FlipsSelectionAndReportsNewState()
    {
        var bag = new SelectionBag();
        var entry = File("/sdcard/a.txt");

        Assert.True(bag.Toggle(entry));
        Assert.True(bag.Contains(entry.AbsolutePath));
        Assert.False(bag.Toggle(entry));
        Assert.True(bag.IsEmpty);
    }

    [Fact]
    public void ReplaceDirectorySelection_OnlyTouchesThatDirectorysDirectChildren()
    {
        var bag = new SelectionBag();
        bag.Add(File("/sdcard/DCIM/a.jpg"));
        bag.Add(File("/sdcard/DCIM/b.jpg"));
        bag.Add(File("/sdcard/Download/x.zip"));
        bag.Add(File("/sdcard/DCIM/Camera/deep.jpg")); // 子目录内的选择不属于 /sdcard/DCIM 的直接子项

        // 用户回到 /sdcard/DCIM，列表控件只选了 b.jpg。
        bag.ReplaceDirectorySelection("/sdcard/DCIM", [File("/sdcard/DCIM/b.jpg")]);

        Assert.False(bag.Contains("/sdcard/DCIM/a.jpg"));
        Assert.True(bag.Contains("/sdcard/DCIM/b.jpg"));
        Assert.True(bag.Contains("/sdcard/Download/x.zip"));
        Assert.True(bag.Contains("/sdcard/DCIM/Camera/deep.jpg"));
    }

    [Fact]
    public void ReplaceDirectorySelection_RootDirectory()
    {
        var bag = new SelectionBag();
        bag.Add(File("/etc"));
        bag.Add(File("/sdcard/a.txt"));

        bag.ReplaceDirectorySelection("/", []);

        Assert.False(bag.Contains("/etc"));
        Assert.True(bag.Contains("/sdcard/a.txt"));
    }

    [Fact]
    public void GroupByDirectory_SummarizesSourceDirectories()
    {
        var bag = new SelectionBag();
        bag.AddRange([File("/sdcard/DCIM/a.jpg"), File("/sdcard/DCIM/b.jpg"), File("/sdcard/Download/x.zip")]);

        var groups = bag.GroupByDirectory();

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups["/sdcard/DCIM"].Count);
        Assert.Single(groups["/sdcard/Download"]);
    }

    [Fact]
    public void EffectiveRoots_DropsPathsCoveredBySelectedDirectory()
    {
        var bag = new SelectionBag();
        bag.AddRange(
        [
            File("/sdcard/DCIM", isDirectory: true),
            File("/sdcard/DCIM/a.jpg"),
            File("/sdcard/DCIM/Camera/b.jpg"),
            File("/sdcard/DCIM2/c.jpg"), // 前缀相似但不在 DCIM 之下
            File("/sdcard/Download/x.zip"),
        ]);

        Assert.Equal(
            ["/sdcard/DCIM", "/sdcard/DCIM2/c.jpg", "/sdcard/Download/x.zip"],
            bag.EffectiveRoots());
    }

    [Fact]
    public void Changed_FiresOnMutationsOnly()
    {
        var bag = new SelectionBag();
        var count = 0;
        bag.Changed += (_, _) => count++;

        bag.Add(File("/a"));
        bag.Remove("/a");
        bag.Remove("/not-there"); // 无变化不触发
        bag.Clear();              // 本就为空，不触发

        Assert.Equal(2, count);
    }

    [Fact]
    public void ConcurrentAdds_AreThreadSafe()
    {
        var bag = new SelectionBag();

        Parallel.For(0, 500, i => bag.Add(File($"/sdcard/dir{i % 10}/f{i}.txt")));

        Assert.Equal(500, bag.Count);
        Assert.Equal(10, bag.GroupByDirectory().Count);
    }
}

public sealed class RemotePathTests
{
    [Theory]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("//sdcard//DCIM/", "/sdcard/DCIM")]
    [InlineData("/sdcard/./DCIM/../Download", "/sdcard/Download")]
    [InlineData("/..", "/")]
    [InlineData("sdcard\\DCIM", "/sdcard/DCIM")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, RemotePath.Normalize(input));

    [Fact]
    public void Combine_ParentAndName()
    {
        Assert.Equal("/sdcard/相机", RemotePath.Combine("/sdcard", "相机"));
        Assert.Equal("/sdcard", RemotePath.Combine("/", "sdcard"));
        Assert.Equal("/", RemotePath.GetParent("/sdcard"));
        Assert.Equal("/sdcard", RemotePath.GetParent("/sdcard/DCIM"));
        Assert.Equal("DCIM", RemotePath.GetName("/sdcard/DCIM/"));
    }

    [Fact]
    public void Breadcrumbs_FromRoot()
    {
        var crumbs = RemotePath.Breadcrumbs("/sdcard/相机/2026 11");

        Assert.Equal(["/", "sdcard", "相机", "2026 11"], crumbs.Select(c => c.Name));
        Assert.Equal(["/", "/sdcard", "/sdcard/相机", "/sdcard/相机/2026 11"], crumbs.Select(c => c.Path));
        Assert.Single(RemotePath.Breadcrumbs("/"));
    }

    [Theory]
    [InlineData("/sdcard/DCIM/a.jpg", "/sdcard/DCIM", true)]
    [InlineData("/sdcard/DCIM", "/sdcard/DCIM", false)]
    [InlineData("/sdcard/DCIM2/a.jpg", "/sdcard/DCIM", false)]
    [InlineData("/sdcard", "/", true)]
    public void IsUnder(string path, string directory, bool expected) =>
        Assert.Equal(expected, RemotePath.IsUnder(path, directory));
}
