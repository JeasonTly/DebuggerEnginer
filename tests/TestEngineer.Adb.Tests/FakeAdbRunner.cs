using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb.Tests;

/// <summary>记录调用并按规则应答的 adb 执行器替身。</summary>
internal sealed class FakeAdbRunner : IAdbProcessRunner
{
    private readonly List<(Func<IReadOnlyList<string>, bool> Match, Func<IReadOnlyList<string>, AdbCommandResult> Respond)> _rules = [];

    public List<(IReadOnlyList<string> Args, string? Serial)> Calls { get; } = [];

    public FakeAdbRunner When(Func<IReadOnlyList<string>, bool> match, Func<IReadOnlyList<string>, AdbCommandResult> respond)
    {
        _rules.Add((match, respond));
        return this;
    }

    public FakeAdbRunner WhenShellContains(string fragment, string stdout, int exitCode = 0) =>
        When(
            args => args.Count >= 2 && args[0] == "shell" && args[1].Contains(fragment, StringComparison.Ordinal),
            _ => new AdbCommandResult(exitCode, stdout, string.Empty));

    public Task<AdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? serial = null,
        TimeSpan? timeout = null,
        Action<string>? onStdout = null,
        Action<string>? onStderr = null,
        CancellationToken cancellationToken = default)
    {
        lock (Calls)
        {
            Calls.Add((arguments, serial));
        }

        foreach (var (match, respond) in _rules)
        {
            if (match(arguments))
            {
                return Task.FromResult(respond(arguments));
            }
        }

        return Task.FromResult(new AdbCommandResult(1, string.Empty, "unmatched: " + string.Join(' ', arguments)));
    }
}

internal sealed class FixedToolLocator(string? adb, string? scrcpy = null) : IToolLocator
{
    public string? FindAdb() => adb;

    public string? FindScrcpy() => scrcpy;
}
