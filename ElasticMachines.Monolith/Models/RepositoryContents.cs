namespace ElasticMachines.Monolith.Models;

public record RepositoryContents
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public required Uri HtmlUrl { get; init; }
    public string? HeadCommit { get; init; }
    public string? AccountConfiguration { get; init; }
    public string? RepositoryConfiguration { get; init; }
}
