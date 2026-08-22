using System.Text.Json.Serialization;
using Octokit;

namespace ElasticMachines.Monolith.Models;

public partial class InstallationRepositoriesEvent
{
    [JsonPropertyName("installation")]
    public Installation? Installation { get; set; }

    [JsonPropertyName("repositories_added")]
    public List<Repository>? RepositoriesAdded { get; set; }

    [JsonPropertyName("repositories_removed")]
    public List<Repository>? RepositoriesRemoved { get; set; }
}
