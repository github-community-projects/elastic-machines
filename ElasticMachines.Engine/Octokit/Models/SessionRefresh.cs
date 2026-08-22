namespace ElasticMachines.Engine.Octokit.Models;

public record SessionRefresh
{
    public required string SessionId { get; init; }
    public required string MessageQueueAccessToken { get; init; }
    public required Uri MessageQueueUrl { get; init; }
}
