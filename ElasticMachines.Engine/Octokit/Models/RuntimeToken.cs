namespace ElasticMachines.Engine.Octokit.Models;

public record RuntimeToken
{
    public required string Token { get; init; }
    public required Uri Url { get; init; }
}
