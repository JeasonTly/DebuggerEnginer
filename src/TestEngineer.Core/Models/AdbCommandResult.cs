namespace TestEngineer.Core.Models;

/// <summary>一次 adb 进程执行的结果。</summary>
public sealed record AdbCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool Cancelled = false,
    TimeSpan Duration = default)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut && !Cancelled;

    /// <summary>stdout 与 stderr 合并后的文本（用于展示）。</summary>
    public string CombinedOutput => string.IsNullOrEmpty(StandardError)
        ? StandardOutput
        : string.IsNullOrEmpty(StandardOutput)
            ? StandardError
            : StandardOutput.TrimEnd() + Environment.NewLine + StandardError;

    /// <summary>失败摘要，成功时为空串。</summary>
    public string FailureMessage
    {
        get
        {
            if (Succeeded)
            {
                return string.Empty;
            }

            if (TimedOut)
            {
                return "命令执行超时。";
            }

            if (Cancelled)
            {
                return "命令已取消。";
            }

            var text = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
            return string.IsNullOrWhiteSpace(text) ? $"命令失败，退出码 {ExitCode}。" : text.Trim();
        }
    }

    public static AdbCommandResult Failure(string message, int exitCode = -1) => new(exitCode, string.Empty, message);
}
