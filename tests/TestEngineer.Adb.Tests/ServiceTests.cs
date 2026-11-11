using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;
using TestEngineer.Core.Settings;

namespace TestEngineer.Adb.Tests;

public sealed class FileManagerServiceTests
{
    [Fact]
    public async Task ListAsync_ParsesSortsAndProbesAccess()
    {
        var runner = new FakeAdbRunner()
            .WhenShellContains("ls -lA", Fixture.Read("ls-special.txt"))
            .WhenShellContains("for f in", "111 /sdcard/Download/My Documents\n110 /sdcard/Download/sdcard\n");
        var service = new FileManagerService(runner);

        var entries = await service.ListAsync("S1", "/sdcard/Download/", probeAccess: true);

        // 目录在前；探测结果写回 CanRead/CanWrite；符号链接按 test -d 修正为目录。
        Assert.True(entries[0].IsDirectory);
        var docs = Assert.Single(entries, e => e.Name == "My Documents");
        Assert.True(docs.CanRead);
        Assert.True(docs.CanWrite);
        var link = Assert.Single(entries, e => e.Name == "sdcard");
        Assert.False(link.IsDirectory);

        var listCall = runner.Calls.First(c => c.Args[1].StartsWith("ls -lA", StringComparison.Ordinal));
        Assert.Equal("S1", listCall.Serial);
        Assert.Equal("ls -lA '/sdcard/Download/'", listCall.Args[1]);
    }

    [Fact]
    public async Task ListAsync_QuotesPathsWithSpacesAndApostrophes()
    {
        var runner = new FakeAdbRunner().WhenShellContains("ls -lA", string.Empty);
        var service = new FileManagerService(runner);

        await service.ListAsync("S1", "/sdcard/it's mine", probeAccess: false);

        Assert.Equal("ls -lA '/sdcard/it'\\''s mine/'", runner.Calls[0].Args[1]);
    }

    [Fact]
    public async Task ListAsync_NothingParsedAndCommandFailed_Throws()
    {
        var runner = new FakeAdbRunner().When(_ => true, _ => new AdbCommandResult(1, string.Empty, "ls: /data: Permission denied"));
        var service = new FileManagerService(runner);

        var ex = await Assert.ThrowsAsync<DeviceCommandException>(() => service.ListAsync("S1", "/data"));

        Assert.Contains("Permission denied", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/sdcard")]
    [InlineData("/sdcard/")]
    [InlineData("/storage/emulated/0")]
    [InlineData("/data")]
    [InlineData("/system/bin")]
    public async Task DeleteAsync_RefusesProtectedPaths(string path)
    {
        var runner = new FakeAdbRunner().When(_ => true, _ => new AdbCommandResult(0, string.Empty, string.Empty));
        var service = new FileManagerService(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync("S1", path, recursive: true));

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task DeleteAsync_NormalPath_RunsQuotedRm()
    {
        var runner = new FakeAdbRunner().When(_ => true, _ => new AdbCommandResult(0, string.Empty, string.Empty));
        var service = new FileManagerService(runner);

        await service.DeleteAsync("S1", "/sdcard/Download/旧 文件", recursive: true);

        Assert.Equal("rm -rf '/sdcard/Download/旧 文件'", runner.Calls[0].Args[1]);
    }

    [Fact]
    public async Task PullToEvidenceZipAsync_WithoutPaths_Fails()
    {
        var service = new FileManagerService(new FakeAdbRunner());

        var result = await service.PullToEvidenceZipAsync("S1", [], Path.Combine(Path.GetTempPath(), "x.zip"));

        Assert.False(result.Succeeded);
    }
}

public sealed class DebugAndConsoleTests
{
    [Fact]
    public void Expand_SubstitutesValidatedParameters()
    {
        var definition = DebugCommandCatalog.Default.Single(c => c.Id == "dumpsys.meminfo");

        var args = DebugCommandCatalog.Expand(definition, new Dictionary<string, string> { ["package"] = "com.example.app" });

        Assert.Equal(["shell", "dumpsys", "meminfo", "com.example.app"], args);
    }

    [Theory]
    [InlineData("com.example; rm -rf /")]
    [InlineData("a b")]
    [InlineData("$(reboot)")]
    public void Expand_RejectsShellMetacharacters(string value)
    {
        var definition = DebugCommandCatalog.Default.Single(c => c.Id == "dumpsys.meminfo");

        Assert.Throws<ArgumentException>(() => DebugCommandCatalog.Expand(definition, new Dictionary<string, string> { ["package"] = value }));
    }

    [Fact]
    public void Expand_UsesDefaultValueAndRequiresMissingParameters()
    {
        var size = DebugCommandCatalog.Default.Single(c => c.Id == "screen.size.set");
        var pkg = DebugCommandCatalog.Default.Single(c => c.Id == "pm.path");

        Assert.Equal("720x1440", DebugCommandCatalog.Expand(size, null)[^1]);
        Assert.Throws<ArgumentException>(() => DebugCommandCatalog.Expand(pkg, null));
    }

    [Fact]
    public void Catalog_HasUniqueIds_AndMarksRebootDangerous()
    {
        var ids = DebugCommandCatalog.Default.Select(c => c.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.True(DebugCommandCatalog.Default.Single(c => c.Id == "reboot").IsDangerous);
    }

    [Fact]
    public void BuildMonkeyArguments_UsesHugeEventCountAndDurationIsExternal()
    {
        var args = DebugCommandService.BuildMonkeyArguments(new MonkeyOptions("com.example.app", 60, 200, Seed: 7));

        Assert.Equal("shell", args[0]);
        Assert.Contains("-p com.example.app", args[1], StringComparison.Ordinal);
        Assert.Contains("--throttle 200", args[1], StringComparison.Ordinal);
        Assert.Contains("-s 7", args[1], StringComparison.Ordinal);
        Assert.EndsWith(" 100000000", args[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunMonkeyAsync_StopsAfterDurationAndKillsDeviceProcess()
    {
        var killed = false;
        var monkeyStopped = new TaskCompletionSource();
        var runner = new MonkeyFakeRunner(monkeyStopped, () => killed = true);
        var service = new DebugCommandService(runner);

        var result = await service.RunMonkeyAsync("S1", new MonkeyOptions("com.example.app", 1));

        Assert.True(result.StoppedByDuration);
        Assert.True(killed);
        Assert.True(result.Command.Succeeded);
        Assert.Equal(1, result.CrashCount);
    }

    [Theory]
    [InlineData("adb shell getprop ro.build.type", "shell", "getprop ro.build.type")]
    [InlineData("shell \"ls -la /sdcard/My Files\"", "shell", "ls -la /sdcard/My Files")]
    [InlineData("shell ls -la \"/sdcard/My Files\"", "shell", "ls -la '/sdcard/My Files'")]
    public void ConsoleParse_ShellCommandsKeepDeviceSideQuoting(string line, string first, string second)
    {
        var parsed = ConsoleCommandLine.Parse(line);

        Assert.Equal([first, second], parsed.AdbArguments);
        Assert.True(parsed.NeedsDevice);
    }

    [Fact]
    public void ConsoleParse_ExplicitSerialAndGlobalCommands()
    {
        var withSerial = ConsoleCommandLine.Parse("adb -s KKSTBE045026 install -r \"C:\\apks\\my app.apk\"");
        var global = ConsoleCommandLine.Parse("devices -l");

        Assert.Equal("KKSTBE045026", withSerial.Serial);
        Assert.Equal(["install", "-r", "C:\\apks\\my app.apk"], withSerial.AdbArguments);
        Assert.False(global.NeedsDevice);
    }

    [Theory]
    [InlineData("")]
    [InlineData("adb")]
    [InlineData("shell")]
    [InlineData("shell \"unclosed")]
    public void ConsoleParse_InvalidInput_Throws(string line)
    {
        Assert.Throws<FormatException>(() => ConsoleCommandLine.Parse(line));
    }

    [Fact]
    public async Task ConsoleExecute_BindsSerialRecordsHistoryAndRaisesEvent()
    {
        var runner = new FakeAdbRunner().When(_ => true, _ => new AdbCommandResult(0, "13\n", string.Empty));
        var history = new ListHistory();
        var service = new ConsoleService(runner, history);
        string? completedSerial = null;
        service.CommandCompleted += (_, e) => completedSerial = e.Serial;

        var result = await service.ExecuteAsync("shell getprop ro.build.version.release", "99990000888800");

        Assert.True(result.Succeeded);
        Assert.Equal("99990000888800", runner.Calls[0].Serial);
        Assert.Equal(["shell getprop ro.build.version.release"], history.Items);
        Assert.Equal("99990000888800", completedSerial);
    }

    [Fact]
    public async Task ConsoleExecute_NoDeviceBound_FailsWithoutRunning()
    {
        var runner = new FakeAdbRunner();
        var service = new ConsoleService(runner);

        var result = await service.ExecuteAsync("shell ls", null);

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
        Assert.Contains("未选择设备", result.StandardError, StringComparison.Ordinal);
    }

    private sealed class ListHistory : IConsoleHistory
    {
        private readonly List<string> _items = [];

        public IReadOnlyList<string> Items => _items;

        public void Add(string commandLine) => _items.Add(commandLine);

        public void Clear() => _items.Clear();
    }

    /// <summary>模拟 monkey：不会自行结束，直到收到设备端 pkill 才返回被杀的退出码。</summary>
    private sealed class MonkeyFakeRunner(TaskCompletionSource stopped, Action onKill) : IAdbProcessRunner
    {
        public async Task<AdbCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            string? serial = null,
            TimeSpan? timeout = null,
            Action<string>? onStdout = null,
            Action<string>? onStderr = null,
            CancellationToken cancellationToken = default)
        {
            var command = arguments.Count > 1 ? arguments[1] : string.Empty;
            if (command.StartsWith("monkey", StringComparison.Ordinal))
            {
                onStdout?.Invoke("// CRASH: com.example.app (pid 1234)");
                await stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new AdbCommandResult(143, "Events injected: 10\n", string.Empty);
            }

            if (command.StartsWith("pkill", StringComparison.Ordinal))
            {
                onKill();
                stopped.TrySetResult();
            }

            return new AdbCommandResult(0, string.Empty, string.Empty);
        }
    }
}

public sealed class DeviceTrackerAndContextTests
{
    private static string ListOutput(params string[] lines) =>
        "List of devices attached\n" + string.Join('\n', lines) + "\n\n";

    [Fact]
    public async Task Refresh_RaisesChangedOnlyWhenListChanges()
    {
        var output = ListOutput("A\tdevice product:p model:m device:d transport_id:1");
        var runner = new FakeAdbRunner().When(a => a[0] == "devices", _ => new AdbCommandResult(0, output, string.Empty));
        var tracker = new DeviceTracker(runner);
        var changes = 0;
        tracker.DevicesChanged += (_, _) => changes++;

        await tracker.RefreshAsync();
        await tracker.RefreshAsync();
        output = ListOutput(
            "A\tdevice product:p model:m device:d transport_id:1",
            "B\tunauthorized transport_id:2");
        await tracker.RefreshAsync();

        Assert.Equal(2, changes);
        Assert.Equal(2, tracker.Devices.Count);
        Assert.Null(tracker.LastError);
    }

    [Fact]
    public async Task Refresh_AdbFailure_KeepsPreviousDevicesAndRecordsError()
    {
        var fail = false;
        var runner = new FakeAdbRunner().When(
            a => a[0] == "devices",
            _ => fail
                ? new AdbCommandResult(1, string.Empty, "adb server is out of date")
                : new AdbCommandResult(0, ListOutput("A\tdevice transport_id:1"), string.Empty));
        var tracker = new DeviceTracker(runner);
        await tracker.RefreshAsync();

        fail = true;
        await tracker.RefreshAsync();

        Assert.Single(tracker.Devices);
        Assert.Contains("out of date", tracker.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_WithNonStreamingRunner_FallsBackToPollingAndDetectsDevices()
    {
        var output = ListOutput();
        var runner = new FakeAdbRunner()
            .When(a => a[0] == "devices", _ => new AdbCommandResult(0, output, string.Empty))
            .When(a => a[0] == "track-devices", _ => new AdbCommandResult(1, string.Empty, "unsupported"));
        await using var tracker = new DeviceTracker(runner, pollInterval: TimeSpan.FromMilliseconds(30));
        var appeared = new TaskCompletionSource();
        tracker.DevicesChanged += (_, _) => appeared.TrySetResult();

        await tracker.StartAsync();
        output = ListOutput("A\tdevice transport_id:1");

        // track-devices 快速失败两次后降级为轮询，应在数秒内发现新设备。
        await appeared.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Single(tracker.Devices);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (tracker.Mode != "polling" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal("polling", tracker.Mode);
    }

    [Fact]
    public async Task Context_AutoSelectsSingleOnlineDevice_AndFollowsDisconnect()
    {
        var output = ListOutput("A\tdevice transport_id:1");
        var runner = new FakeAdbRunner().When(a => a[0] == "devices", _ => new AdbCommandResult(0, output, string.Empty));
        var tracker = new DeviceTracker(runner);
        using var context = new DeviceContext(tracker);
        var currentChanges = 0;
        context.CurrentChanged += (_, _) => currentChanges++;

        await context.RefreshAsync();
        Assert.Equal("A", context.Current?.Serial);

        output = ListOutput();
        await context.RefreshAsync();

        Assert.Null(context.Current);
        Assert.Equal(2, currentChanges);
    }

    [Fact]
    public async Task Context_RestoresPersistedSerial_AndPersistsExplicitSelection()
    {
        var output = ListOutput(
            "A\tdevice transport_id:1",
            "B\tdevice transport_id:2");
        var runner = new FakeAdbRunner().When(a => a[0] == "devices", _ => new AdbCommandResult(0, output, string.Empty));
        var settings = new MemorySettings { Current = { LastDeviceSerial = "B" } };
        using var context = new DeviceContext(new DeviceTracker(runner), settings);

        await context.RefreshAsync();
        Assert.Equal("B", context.Current?.Serial);

        Assert.True(context.Select("A"));
        Assert.Equal("A", context.Current?.Serial);
        Assert.Equal("A", settings.Current.LastDeviceSerial);
        Assert.Equal(1, settings.SaveCount);
        Assert.False(context.Select("ZZZ"));
    }

    private sealed class MemorySettings : IAppSettings
    {
        public AppSettings Current { get; } = new();

        public int SaveCount { get; private set; }

        public event EventHandler? Changed;

        public void Save()
        {
            SaveCount++;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class DebugCatalogTests
{
    [Fact]
    public void LoadFromJson_MapsFieldsKindsAndChips()
    {
        const string json = """
            {
              "version": 1,
              "commands": [
                { "id": "a", "title": "A", "category": "C", "args": ["shell", "getprop"], "timeoutSeconds": 15, "dangerous": true,
                  "whenPackageExists": "com.vendor.app" },
                { "id": "fs", "title": "Force stop", "kind": "am-force-stop" },
                { "id": "shot", "title": "Shot", "kind": "screencap", "timeoutSeconds": 60 },
                { "id": "", "title": "ignored" }
              ],
              "consoleChips": [
                { "id": "se", "title": "SELinux", "line": "shell getenforce" },
                { "id": "root", "title": "adb root", "line": "root", "requiresUserdebug": true, "dangerous": true }
              ]
            }
            """;

        var catalog = DebugCommandCatalog.LoadFromJson(json);

        Assert.Equal(3, catalog.Commands.Count);
        var a = catalog.Commands[0];
        Assert.True(a.IsDangerous);
        Assert.Equal(TimeSpan.FromSeconds(15), a.Timeout);
        Assert.Equal("com.vendor.app", a.WhenPackageExists);

        var forceStop = catalog.Commands[1];
        Assert.Equal(["shell", "am", "force-stop", "{package}"], forceStop.AdbArgs);
        Assert.Single(forceStop.ParameterList);

        var shot = catalog.Commands[2];
        Assert.Empty(shot.AdbArgs);
        Assert.Equal("screencap", shot.Kind);

        Assert.Equal(2, catalog.ConsoleChips.Count);
        Assert.True(catalog.ConsoleChips[1].RequiresUserdebug);
    }

    [Fact]
    public void LoadDefault_MissingFile_FallsBackToBuiltIn()
    {
        var empty = Path.Combine(Path.GetTempPath(), "TestEngineer.Tests", Guid.NewGuid().ToString("N"));

        var catalog = DebugCommandCatalog.LoadDefault(empty);

        Assert.Same(DebugCommandCatalog.BuiltIn, catalog);
        Assert.NotEmpty(catalog.ConsoleChips);
    }

    [Fact]
    public void LoadDefault_ShippedJsonIsCopiedAndParses()
    {
        var catalog = DebugCommandCatalog.LoadDefault();

        Assert.NotEmpty(catalog.Commands);
        Assert.Equal(catalog.Commands.Count, catalog.Commands.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Service_WithEmptyEnumerablesLikeDependencyInjectionProvides_StillHasCommands()
    {
        var service = new DebugCommandService(new FakeAdbRunner(), commands: [], chips: []);

        Assert.NotEmpty(service.Commands);
        Assert.NotEmpty(service.ConsoleChips);
    }
}