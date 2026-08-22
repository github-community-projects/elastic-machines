namespace ElasticMachines.Engine.Octokit.Models;

public record JitConfiguration
{
    public required Runner Runner { get; init; }
    public required string EncodedJitConfig { get; init; }
}
