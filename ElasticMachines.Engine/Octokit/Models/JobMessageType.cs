using System.Text.Json.Serialization;

namespace ElasticMachines.Engine.Octokit.Models;

[JsonConverter(typeof(JsonStringEnumMemberConverter))]
public enum JobMessageType
{
    JobAvailable,
    JobAssigned,
    JobStarted,
    JobCompleted
}
