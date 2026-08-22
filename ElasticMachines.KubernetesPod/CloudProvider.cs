using System.Text.Json;
using ElasticMachines.Engine;
using ElasticMachines.KubernetesPod.Clients;
using ElasticMachines.KubernetesPod.Models;
using ElasticMachines.Utilities;
using k8s.Models;

namespace ElasticMachines.KubernetesPod;

[CloudProvider("kubernetes-pod")]
public class CloudProvider : CloudProvider<Configuration>, IAsyncDisposable
{
    private readonly KubernetesClient kubeClient;
    private readonly AsyncExpiringLazy<string> lazyNamespace;

    public CloudProvider(Configuration configuration, Dictionary<string, string> tags) : base(configuration, tags)
    {
        configuration.Template.Spec.RestartPolicy = configuration.Template.Spec.RestartPolicy is null or "Never" ? "Never" : throw new("The pod spec 'restartPolicy' must be set to 'Never' (or left unspecified) to allow Elastic Machines to handle pod orchestration.");

        configuration.Template.Metadata.Annotations ??= new Dictionary<string, string>();
        configuration.Template.Metadata.Annotations.MergeIn(tags.ToDictionary(t => $"github.com/{t.Key}", t => t.Value));

        kubeClient = new(Environment.GetEnvironmentVariable("KUBECONFIG_CONTENTS"), Configuration.Context);
        lazyNamespace = new(GetNamespaceAsync);
    }

    public override async Task<List<string>> GetMachinesAsync(CancellationToken cancellationToken)
    {
        var @namespace = await lazyNamespace.GetValueAsync(cancellationToken).ConfigureAwait(false);
        var pods = await kubeClient.GetPods(@namespace, cancellationToken).ConfigureAwait(false);

        return pods
            .Where(p => Tags.All(t => p.Metadata.Annotations.TryGetValue($"github.com/{t.Key}", out var value) && value == t.Value))
            .Select(p => p.Name())
            .ToList();
    }

    public override Task<string> CreateMachineAsync(string name, CancellationToken cancellationToken)
    {
        return Task.FromResult(name);
    }

    public override async Task ConfigureMachineAsRunnerAsync(string name, string jitConfiguration, GetRunnerDownloadAsync getRunnerDownloadAsync, CancellationToken cancellationToken)
    {
        // Create a copy of the template that we can safely mutate and convert it to a pod
        var pod = JsonSerializer.Deserialize<V1Pod>(JsonSerializer.Serialize(Configuration.Template)) ?? throw new Exception("Unable to deserialize pod metadata");

        // Uppercase letters are not allowed in pod names
        pod.Metadata.Name = name.ToLower();

        foreach (var container in pod.Spec.Containers)
        {
            if (container.Name == "runner")
            {
                container.Env ??= [];
                container.Env.Add(new("ACTIONS_RUNNER_INPUT_JITCONFIG", jitConfiguration));

                break;
            }
        }

        var @namespace = await lazyNamespace.GetValueAsync(cancellationToken).ConfigureAwait(false);
        await kubeClient.CreatePod(@namespace, pod, cancellationToken).ConfigureAwait(false);

        // Ensure that the Pod is Running and the runner container status is Ready
        await kubeClient
            .WaitForContainerToStart(@namespace, pod.Metadata.Name, "runner", cancellationToken)
            .ConfigureAwait(false);

        // Read the log output to detect when the runner is online
        await kubeClient
            .FollowPodLogUntil(@namespace, pod.Metadata.Name, "runner", "Listening for Jobs", cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task DeleteMachineAsync(string name, CancellationToken cancellationToken)
    {
        var @namespace = await lazyNamespace.GetValueAsync(cancellationToken).ConfigureAwait(false);

        await kubeClient
            .DeletePod(@namespace, name.ToLower(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> GetNamespaceAsync(CancellationToken cancellationToken)
    {
        var @namespace = Configuration.Template.Metadata.Namespace() ?? "default";

        await kubeClient.EnsureNamespaceExists(@namespace, cancellationToken).ConfigureAwait(false);

        return @namespace;
    }

    public virtual async ValueTask DisposeAsync()
    {
        await lazyNamespace.DisposeAsync().ConfigureAwait(false);
    }
}
