namespace ElasticMachines.Engine.Octokit.Models;

public record Runner
{
    public required DateTimeOffset CreatedOn { get; init; }
    public required long Id { get; init; }
    public required long RunnerGroupId { get; init; }
    public long? RunnerScaleSetId { get; init; }
    public required string Name { get; init; }
    public required bool Ephemeral { get; init; }
    public required bool DisableUpdate { get; init; }
    public required RunnerStatus Status { get; init; }
}
