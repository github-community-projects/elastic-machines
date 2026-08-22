using k8s.Models;

namespace ElasticMachines.KubernetesPod.Models;

public record Configuration(V1PodTemplateSpec Template)
{
    public string? Context { get; init; }
}
