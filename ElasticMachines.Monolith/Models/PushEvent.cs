using System.Text.Json.Serialization;
using Octokit;

namespace ElasticMachines.Monolith.Models;

public class PushEvent
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("before")]
    public string? Before { get; set; }

    [JsonPropertyName("after")]
    public string? After { get; set; }

    [JsonPropertyName("repository")]
    public Repository? Repository { get; set; }

    [JsonPropertyName("pusher")]
    public Author? Pusher { get; set; }

    [JsonPropertyName("installation")]
    public Installation? Installation { get; set; }

    [JsonPropertyName("created")]
    public bool? Created { get; set; }

    [JsonPropertyName("deleted")]
    public bool? Deleted { get; set; }

    [JsonPropertyName("forced")]
    public bool? Forced { get; set; }

    [JsonPropertyName("base_ref")]
    public object? BaseRef { get; set; }

    [JsonPropertyName("compare")]
    public Uri? Compare { get; set; }

    [JsonPropertyName("commits")]
    public List<Commit>? Commits { get; set; }

    [JsonPropertyName("head_commit")]
    public Commit? HeadCommit { get; set; }
}
