using System.Text;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb;

/// <summary>控制台命令行中的一个词元。</summary>
public sealed record ConsoleToken(string Text, bool WasQuoted);

/// <summary>解析后的控制台命令。</summary>
public sealed record ParsedConsoleCommand(IReadOnlyList<string> AdbArguments, string? Serial, bool NeedsDevice);

/// <summary>控制台命令行解析：词元化（支持单/双引号）、去掉开头的 adb、识别 -s、为 shell 子命令重新引用。</summary>
public static class ConsoleCommandLine
{
    /// <summary>不需要绑定设备的 adb 全局命令。</summary>
    private static readonly HashSet<string> GlobalCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "devices", "version", "help", "start-server", "kill-server", "connect", "disconnect", "pair", "mdns", "track-devices", "keys",
    };

    public static IReadOnlyList<ConsoleToken> Tokenize(string? line)
    {
        var tokens = new List<ConsoleToken>();
        if (string.IsNullOrWhiteSpace(line))
        {
            return tokens;
        }

        var current = new StringBuilder();
        var inToken = false;
        var quoted = false;
        char? quote = null;
        foreach (var c in line)
        {
            if (quote is not null)
            {
                if (c == quote)
                {
                    quote = null;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                quoted = true;
                inToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(new ConsoleToken(current.ToString(), quoted));
                    current.Clear();
                    inToken = false;
                    quoted = false;
                }

                continue;
            }

            current.Append(c);
            inToken = true;
        }

        if (quote is not null)
        {
            throw new FormatException("引号未闭合。");
        }

        if (inToken)
        {
            tokens.Add(new ConsoleToken(current.ToString(), quoted));
        }

        return tokens;
    }

    public static ParsedConsoleCommand Parse(string? line)
    {
        var tokens = Tokenize(line).ToList();
        if (tokens.Count > 0 && (tokens[0].Text.Equals("adb", StringComparison.OrdinalIgnoreCase) || tokens[0].Text.Equals("adb.exe", StringComparison.OrdinalIgnoreCase)))
        {
            tokens.RemoveAt(0);
        }

        string? serial = null;
        if (tokens.Count >= 2 && tokens[0].Text == "-s")
        {
            serial = tokens[1].Text;
            tokens.RemoveRange(0, 2);
        }

        if (tokens.Count == 0)
        {
            throw new FormatException("请输入 adb 命令，例如：shell getprop ro.build.version.release");
        }

        var subcommand = tokens[0].Text;
        if (subcommand == "shell" && tokens.Count == 1)
        {
            throw new FormatException("控制台不支持交互式 shell，请直接给出命令，例如：shell ls /sdcard");
        }

        if (subcommand == "shell" || subcommand == "exec-out")
        {
            // adb 会把 shell 之后的多个参数用空格拼接后交给设备端 shell，因此需要自行保留引用。
            // 只有一个词元时视为用户写好的完整命令（如 shell "ls -la /sdcard"），原样传递。
            var command = tokens.Count == 2 ? tokens[1].Text : string.Join(' ', tokens.Skip(1).Select(t => t.WasQuoted || t.Text.Any(char.IsWhiteSpace) ? ShellQuote.Quote(t.Text) : t.Text));
            return new ParsedConsoleCommand([subcommand, command], serial, NeedsDevice: true);
        }

        var needsDevice = !GlobalCommands.Contains(subcommand);
        return new ParsedConsoleCommand([.. tokens.Select(t => t.Text)], serial, needsDevice);
    }
}

/// <summary>
/// 自由格式 adb 控制台：命令行解析后交给 <see cref="IAdbProcessRunner"/>；
/// 行内没有 <c>-s</c> 时绑定到调用方提供的当前设备。执行前写入历史，执行后触发 <see cref="CommandCompleted"/>。
/// </summary>
public sealed class ConsoleService : IConsoleService
{
    /// <summary>控制台命令默认超时；长时间流式命令（如 logcat）请用取消结束。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly IAdbProcessRunner _runner;
    private readonly IConsoleHistory? _history;

    public ConsoleService(IAdbProcessRunner runner, IConsoleHistory? history = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _history = history;
    }

    public event EventHandler<ConsoleCommandCompletedEventArgs>? CommandCompleted;

    public async Task<AdbCommandResult> ExecuteAsync(
        string commandLine,
        string? boundSerial,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ParsedConsoleCommand parsed;
        try
        {
            parsed = ConsoleCommandLine.Parse(commandLine);
        }
        catch (FormatException ex)
        {
            return AdbCommandResult.Failure(ex.Message, exitCode: 2);
        }

        _history?.Add(commandLine);

        var serial = parsed.Serial ?? (parsed.NeedsDevice ? boundSerial : null);
        if (parsed.NeedsDevice && string.IsNullOrWhiteSpace(serial))
        {
            var failure = AdbCommandResult.Failure("未选择设备：请先选择设备，或在命令中使用 -s <序列号>。", exitCode: 2);
            CommandCompleted?.Invoke(this, new ConsoleCommandCompletedEventArgs(commandLine, null, failure));
            return failure;
        }

        var result = await _runner.RunAsync(parsed.AdbArguments, serial, DefaultTimeout, onOutput, onOutput, cancellationToken).ConfigureAwait(false);
        CommandCompleted?.Invoke(this, new ConsoleCommandCompletedEventArgs(commandLine, serial, result));
        return result;
    }
}
