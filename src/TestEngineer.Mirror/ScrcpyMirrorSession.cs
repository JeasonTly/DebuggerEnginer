using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Mirror;

/// <summary>方案 A：拉起 scrcpy.exe，轮询主窗口 HWND 供 WPF HwndHost 嵌入。</summary>
public sealed partial class ScrcpyMirrorSession : IMirrorSession
{
    private readonly IToolLocator _tools;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private Process? _process;
    private CancellationTokenSource? _lifetime;
    private MirrorSessionState _state = MirrorSessionState.Stopped;
    private IntPtr _hwnd = IntPtr.Zero;
    private string? _lastError;
    private string? _serial;
    private MirrorVideoSize? _videoSize;

    public ScrcpyMirrorSession(IToolLocator tools, ILogger<ScrcpyMirrorSession>? logger = null)
    {
        _tools = tools;
        _logger = logger ?? NullLogger<ScrcpyMirrorSession>.Instance;
    }

    public MirrorSessionState State
    {
        get { lock (_gate) return _state; }
    }

    public IntPtr WindowHandle
    {
        get { lock (_gate) return _hwnd; }
    }

    public string? LastError
    {
        get { lock (_gate) return _lastError; }
    }

    public MirrorVideoSize? VideoSize
    {
        get { lock (_gate) return _videoSize; }
    }

    public event EventHandler? StateChanged;

    public event EventHandler<MirrorVideoSize>? VideoSizeChanged;

    public async Task StartAsync(AndroidDevice device, MirrorOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        options ??= new MirrorOptions();

        await StopAsync(cancellationToken).ConfigureAwait(false);
        // 清掉同序列号残留 scrcpy（重连竞态会产生多实例，抢设备导致花屏/不稳）
        KillOrphanScrcpy(device.Serial);

        var scrcpy = _tools.FindScrcpy();
        if (string.IsNullOrWhiteSpace(scrcpy) || !File.Exists(scrcpy))
        {
            SetFailed("未找到 scrcpy.exe。请确认 tools/third-party 已解压或在设置中指定路径。");
            throw new FileNotFoundException(_lastError);
        }

        SetState(MirrorSessionState.Starting);
        _serial = device.Serial;
        lock (_gate) { _videoSize = null; }
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var psi = new ProcessStartInfo
        {
            FileName = scrcpy,
            WorkingDirectory = Path.GetDirectoryName(scrcpy) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.ArgumentList.Add("--serial");
        psi.ArgumentList.Add(device.Serial);
        psi.ArgumentList.Add("--window-title");
        psi.ArgumentList.Add($"TestEngineer-{device.Serial}");
        if (options.MaxSize > 0)
        {
            psi.ArgumentList.Add("--max-size");
            psi.ArgumentList.Add(options.MaxSize.ToString());
        }

        psi.ArgumentList.Add("--video-bit-rate");
        psi.ArgumentList.Add(options.BitRate.ToString());
        psi.ArgumentList.Add("--max-fps");
        psi.ArgumentList.Add(Math.Clamp(options.MaxFps, 1, 120).ToString());
        if (options.StayAwake) psi.ArgumentList.Add("--stay-awake");
        if (options.TurnScreenOff) psi.ArgumentList.Add("--turn-screen-off");
        if (options.Borderless) psi.ArgumentList.Add("--window-borderless");
        if (options.NoAudio) psi.ArgumentList.Add("--no-audio");
        if (options.VideoBufferMs > 0)
        {
            psi.ArgumentList.Add("--video-buffer");
            psi.ArgumentList.Add(options.VideoBufferMs.ToString());
        }

        // 嵌入场景（对齐官方 window 文档）：
        // - 无边框 + 禁用屏保
        // - 不锁 capture-orientation：视频尺寸随设备旋转（会打出 Texture: WxH）
        // - 初始窗口用宿主客户区尺寸；运行期由宿主铺满，letterbox 交给 scrcpy
        psi.ArgumentList.Add("--disable-screensaver");
        psi.ArgumentList.Add("--mouse=sdk");
        psi.ArgumentList.Add("--video-codec=h264");
        if (options.WindowWidth > 0 && options.WindowHeight > 0)
        {
            psi.ArgumentList.Add("--window-width");
            psi.ArgumentList.Add(options.WindowWidth.ToString());
            psi.ArgumentList.Add("--window-height");
            psi.ArgumentList.Add(options.WindowHeight.ToString());
            psi.ArgumentList.Add("--window-x");
            psi.ArgumentList.Add("0");
            psi.ArgumentList.Add("--window-y");
            psi.ArgumentList.Add("0");
        }

        if (!string.IsNullOrWhiteSpace(options.RecordPath))
        {
            psi.ArgumentList.Add("--record");
            psi.ArgumentList.Add(options.RecordPath);
        }

        // Prefer same-directory adb if present; otherwise rely on PATH / SCRCPY_SERVER_PATH defaults.
        var adb = _tools.FindAdb();
        if (!string.IsNullOrWhiteSpace(adb))
        {
            psi.Environment["ADB"] = adb;
        }

        try
        {
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                _logger.LogDebug("scrcpy[{Serial}] {Line}", _serial, e.Data);
                TryHandleTextureLine(e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                _logger.LogInformation("scrcpy[{Serial}] {Line}", _serial, e.Data);
                TryHandleTextureLine(e.Data);
            };
            _process.Exited += (_, _) =>
            {
                lock (_gate)
                {
                    if (_state is MirrorSessionState.Running or MirrorSessionState.Starting)
                    {
                        _state = MirrorSessionState.Failed;
                        _lastError ??= $"scrcpy 已退出（code={SafeExitCode(_process)}）";
                        _hwnd = IntPtr.Zero;
                    }
                }
                StateChanged?.Invoke(this, EventArgs.Empty);
            };

            if (!_process.Start())
            {
                SetFailed("无法启动 scrcpy 进程。");
                throw new InvalidOperationException(_lastError);
            }

            AssignKillOnClose(_process);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            var hwnd = await WaitForWindowAsync($"TestEngineer-{device.Serial}", _lifetime.Token).ConfigureAwait(false);
            lock (_gate)
            {
                _hwnd = hwnd;
                _state = MirrorSessionState.Running;
                _lastError = null;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetFailed(ex.Message);
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? proc;
        IntPtr hwnd;
        lock (_gate)
        {
            proc = _process;
            hwnd = _hwnd;
            _process = null;
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            // 先标成已停止，退出事件才不会当成意外中断去重连。
            _state = MirrorSessionState.Stopped;
        }

        if (proc is null)
        {
            lock (_gate) { _hwnd = IntPtr.Zero; }
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            if (!proc.HasExited)
            {
                // 先关窗口，让 scrcpy 把录屏 mp4 写完。直接 Kill 会留下无法播放的文件。
                if (hwnd != IntPtr.Zero && IsWindow(hwnd))
                {
                    PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                }

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                wait.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    await proc.WaitForExitAsync(wait.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 下面再强制结束
                }

                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    await proc.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "停止 scrcpy 失败");
        }
        finally
        {
            proc.Dispose();
            lock (_gate) { _hwnd = IntPtr.Zero; }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task SendKeyAsync(string keycode, CancellationToken cancellationToken = default)
    {
        // Key injection is handled by scrcpy itself when focused; host can also use adb keyevent via UI layer.
        _logger.LogDebug("SendKeyAsync({Key}) — 由嵌入窗口焦点转发或上层 adb keyevent 处理", keycode);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        KillOrphanScrcpy(null);
        CloseJob();
    }

    /// <summary>不等待窗口自己退出。供进程退出路径调用，避免调度器已经停了还在等待。</summary>
    public void ForceStop()
    {
        Process? proc;
        lock (_gate)
        {
            proc = _process;
            _process = null;
            _lifetime?.Cancel();
            try { _lifetime?.Dispose(); } catch (ObjectDisposedException) { /* 已释放 */ }
            _lifetime = null;
            _state = MirrorSessionState.Stopped;
            _hwnd = IntPtr.Zero;
        }

        try
        {
            if (proc is { HasExited: false })
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(2000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            _logger.LogWarning(ex, "强制结束 scrcpy 失败");
        }
        finally
        {
            try { proc?.Dispose(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* 已退出 */ }
        }

        KillOrphanScrcpy(null);
        CloseJob();
    }

    private void TryHandleTextureLine(string line)
    {
        var m = TextureSizeRegex().Match(line);
        if (!m.Success) return;
        if (!int.TryParse(m.Groups["w"].Value, out var w) || !int.TryParse(m.Groups["h"].Value, out var h))
        {
            return;
        }

        if (w <= 0 || h <= 0) return;

        var size = new MirrorVideoSize(w, h);
        lock (_gate)
        {
            if (_videoSize is { Width: var ow, Height: var oh } && ow == w && oh == h)
            {
                return;
            }

            _videoSize = size;
        }

        _logger.LogInformation("scrcpy[{Serial}] video size {W}x{H}", _serial, w, h);
        VideoSizeChanged?.Invoke(this, size);
    }

    [GeneratedRegex(@"Texture:\s*(?<w>\d+)x(?<h>\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TextureSizeRegex();

    private void SetFailed(string message)
    {
        lock (_gate)
        {
            _state = MirrorSessionState.Failed;
            _lastError = message;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(MirrorSessionState state)
    {
        lock (_gate)
        {
            _state = state;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int SafeExitCode(Process? process)
    {
        try { return process?.ExitCode ?? -1; }
        catch { return -1; }
    }

    private static void KillOrphanScrcpy(string? serial)
    {
        var title = serial is null ? null : $"TestEngineer-{serial}";
        foreach (var proc in Process.GetProcessesByName("scrcpy"))
        {
            try
            {
                var windowTitle = "";
                try { windowTitle = proc.MainWindowTitle ?? ""; } catch (InvalidOperationException) { /* 进程已退出 */ }

                var ours = windowTitle.Contains("TestEngineer-", StringComparison.Ordinal);
                // 启动竞态时窗口标题还没出来。退出时不按空标题杀，避免误关用户自己开的 scrcpy。
                var startupRace = serial is not null && string.IsNullOrEmpty(windowTitle);
                if (ours || startupRace || (title is not null && windowTitle.Contains(title, StringComparison.Ordinal)))
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // 进程已退出或无权限。
            }
            finally
            {
                try { proc.Dispose(); } catch (InvalidOperationException) { /* 已释放 */ }
            }
        }
    }

    private IntPtr _job;

    private void AssignKillOnClose(Process process)
    {
        if (_job == IntPtr.Zero)
        {
            _job = CreateJobObject(IntPtr.Zero, null);
            if (_job == IntPtr.Zero)
            {
                return;
            }

            var info = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose,
                },
            };
            if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformationClass, in info, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                CloseJob();
                return;
            }
        }

        try
        {
            if (!AssignProcessToJobObject(_job, process.Handle))
            {
                _logger.LogWarning("未能把 scrcpy 放进作业对象，退出时可能残留进程");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "未能把 scrcpy 放进作业对象");
        }
    }

    private void CloseJob()
    {
        var job = _job;
        _job = IntPtr.Zero;
        if (job != IntPtr.Zero)
        {
            CloseHandle(job);
        }
    }

    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectIoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public JobObjectIoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInformationClass, in JobObjectExtendedLimitInformation lpJobObjectInformation, uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static async Task<IntPtr> WaitForWindowAsync(string title, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hwnd = FindWindowByTitle(title);
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"等待 scrcpy 窗口超时：{title}");
    }

    private static IntPtr FindWindowByTitle(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var sb = new System.Text.StringBuilder(512);
            _ = GetWindowText(hWnd, sb, sb.Capacity);
            if (string.Equals(sb.ToString(), title, StringComparison.Ordinal))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WmClose = 0x0010;
}
