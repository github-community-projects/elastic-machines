namespace ElasticMachines.Engine.Octokit.Models;

public record RunnerApplication
{
    public required Uri DownloadUrl { get; init; }
    public string? Token { get; init; }
}
