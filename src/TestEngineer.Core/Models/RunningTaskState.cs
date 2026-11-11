namespace TestEngineer.Core.Models;

public enum RunningTaskStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>长耗时任务（传输、monkey、bugreport 等）的进度快照，通过 <see cref="IProgress{T}"/> 上报。</summary>
public sealed record RunningTaskState(
    string Id,
    string Title,
    RunningTaskStatus Status,
    double? Progress = null,
    string? Message = null,
    DateTimeOffset StartedAt = default,
    DateTimeOffset? FinishedAt = null)
{
    public bool IsActive => Status is RunningTaskStatus.Pending or RunningTaskStatus.Running;

    public static RunningTaskState Begin(string title, string? message = null) => new(
        Guid.NewGuid().ToString("N"),
        title,
        RunningTaskStatus.Running,
        null,
        message,
        DateTimeOffset.Now);

    /// <summary>更新进度（0..1，null 表示不确定）。</summary>
    public RunningTaskState Update(double? progress, string? message = null) =>
        this with { Status = RunningTaskStatus.Running, Progress = progress, Message = message ?? Message };

    public RunningTaskState Finish(RunningTaskStatus status, string? message = null) => this with
    {
        Status = status,
        Progress = status == RunningTaskStatus.Succeeded ? 1d : Progress,
        Message = message ?? Message,
        FinishedAt = DateTimeOffset.Now,
    };
}
