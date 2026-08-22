using System.Text.Json.Serialization;

namespace ElasticMachines.Engine.Octokit.Models;

[JsonConverter(typeof(JsonStringEnumMemberConverter))]
public enum RunnerStatus
{
    Offline,
    Online,
    Busy,
}
