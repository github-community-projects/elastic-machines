namespace ElasticMachines.Engine.Octokit.Models;

public record Session : SessionRefresh
{
    public required RunnerScaleSet RunnerScaleSet { get; init; }
    public required Statistics Statistics { get; init; }
}
