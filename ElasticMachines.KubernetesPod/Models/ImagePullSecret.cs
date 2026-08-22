namespace ElasticMachines.KubernetesPod.Models;

public record ImagePullSecret(string Registry, string Username, string Token);
