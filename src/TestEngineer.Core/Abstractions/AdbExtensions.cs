using TestEngineer.Core.Models;

namespace TestEngineer.Core.Abstractions;

/// <summary>找不到 adb 可执行文件。</summary>
public sealed class AdbNotFoundException(string message) : InvalidOperationException(message);

/// <summary>设备命令执行失败，携带原始结果。</summary>
public sealed class DeviceCommandException(string message, AdbCommandResult? result = null) : InvalidOperationException(message)
{
    public AdbCommandResult? Result { get; } = result;
}

public static class AdbRunnerExtensions
{
    /// <summary>
    /// 在设备上执行 shell 命令。<paramref name="shellCommand"/> 作为单个参数传给 <c>adb shell</c>，
    /// 由设备端 shell 解析；其中的路径等值须先用 <see cref="ShellQuote.Quote"/> 处理。
    /// </summary>
    public static Task<AdbCommandResult> ShellAsync(
        this IAdbProcessRunner runner,
        string serial,
        string shellCommand,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        return runner.RunAsync(["shell", shellCommand], serial, timeout, cancellationToken: cancellationToken);
    }
}

/// <summary>设备端 POSIX shell 的单引号转义。</summary>
public static class ShellQuote
{
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}
