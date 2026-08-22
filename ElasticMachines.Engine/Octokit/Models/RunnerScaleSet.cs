namespace ElasticMachines.Engine.Octokit.Models;

public record RunnerScaleSet
{
    public required long Id { get; init; }
    public required long RunnerGroupId { get; init; }
}
