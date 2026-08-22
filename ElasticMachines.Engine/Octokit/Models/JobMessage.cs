namespace ElasticMachines.Engine.Octokit.Models;

public record JobMessage
{
    public required JobMessageType MessageType { get; init; }
    public string? RunnerName { get; init; }
    public required long RunnerRequestId { get; init; }
    public DateTimeOffset? QueueTime { get; init; }
    public DateTimeOffset? RunnerAssignTime { get; init; }
    public DateTimeOffset? FinishTime { get; init; }
}
