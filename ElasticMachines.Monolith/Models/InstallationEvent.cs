using System.Text.Json.Serialization;
using Octokit;

namespace ElasticMachines.Monolith.Models;

public partial class InstallationEvent
{
    [JsonPropertyName("action")]
    public InstallationAction? Action { get; set; }

    [JsonPropertyName("installation")]
    public Installation? Installation { get; set; }

    [JsonPropertyName("repositories")]
    public List<Repository>? Repositories { get; set; }
}
