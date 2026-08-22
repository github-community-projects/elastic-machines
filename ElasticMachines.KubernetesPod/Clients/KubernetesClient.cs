using System.Text;
using System.Text.Json;
using ElasticMachines.Engine.Exceptions;
using ElasticMachines.KubernetesPod.Models;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace ElasticMachines.KubernetesPod.Clients;

public class KubernetesClient
{
    private readonly int apiLimit;
    private readonly k8s.Kubernetes k8sClient;
    private readonly KubernetesClientConfiguration Config;

    public KubernetesClient(string? kubeConfigContents = null, string? context = null, int? apiLimit = null)
    {
        if (kubeConfigContents != null)
        {
            // Load from an env var
            Config = KubernetesClientConfiguration.BuildConfigFromConfigFile(AsStream(kubeConfigContents), context);
        }
        else
        {
            // Locally we'll assume a standard kubeconfig file at ~/.kube/config or equivalent default location
            var defaultKubeConfigPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "config");
            Config = KubernetesClientConfiguration.BuildConfigFromConfigFile(defaultKubeConfigPath, context);
        }

        this.apiLimit = apiLimit ?? 500;

        // Use the kubeconfig to create a client
        k8sClient = new k8s.Kubernetes(Config);
    }

    public KubernetesClient(KubernetesClientConfiguration kubeConfig, int? apiLimit = null)
    {
        Config = kubeConfig;
        this.apiLimit = apiLimit ?? 500;

        // Use the kubeconfig to create a client
        k8sClient = new k8s.Kubernetes(Config);
    }

    public KubernetesClient(KubernetesClient client, int? apiLimit = null) : this(client.Config, apiLimit)
    {
    }

    private static async IAsyncEnumerable<TItem> ListAllAsync<TItem>(Func<string?, Task<IItems<TItem>>> func)
    {
        string? continuation = null;

        do
        {
            var result = await func(continuation).ConfigureAwait(false);
            continuation = (result as IMetadata<V1ListMeta>)?.Continue();

            foreach (var item in result.Items)
                yield return item;
        }
        while (continuation != null);
    }

    public async Task<int> GetPodCapacityForNodes(string labelSelector)
    {
        try
        {
            return await ListAllAsync<V1Node>(async c => await k8sClient.CoreV1.ListNodeAsync(labelSelector: labelSelector, limit: apiLimit, continueParameter: c).ConfigureAwait(false))
                .Select(n => n.Status.Capacity["pods"].ToInt32())
                .SumAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task<List<V1Namespace>> GetAllNamespaces()
    {
        try
        {
            return await ListAllAsync<V1Namespace>(async c => await k8sClient.CoreV1.ListNamespaceAsync(limit: apiLimit, continueParameter: c).ConfigureAwait(false))
                .ToListAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task EnsureNamespaceExists(string @namespace, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await k8sClient.CoreV1.ReadNamespaceAsync(@namespace, false, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpOperationException httpEx && httpEx.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await k8sClient.CoreV1.CreateNamespaceAsync(
                    new V1Namespace(metadata: new V1ObjectMeta(name: @namespace)),
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task DeleteNamespace(string @namespace)
    {
        await k8sClient.CoreV1.DeleteNamespaceAsync(@namespace).ConfigureAwait(false);
    }

    public async Task<V1Pod> GetPod(string @namespace, string name)
    {
        try
        {
            return await k8sClient.CoreV1.ReadNamespacedPodAsync(name, @namespace).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task<List<V1Pod>> GetPods(string @namespace, CancellationToken cancellationToken)
    {
        try
        {
            return await ListAllAsync<V1Pod>(async c => await k8sClient.CoreV1.ListNamespacedPodAsync(@namespace, limit: apiLimit, continueParameter: c, cancellationToken: cancellationToken).ConfigureAwait(false))
                .Where(p => p.Metadata.DeletionTimestamp == null) // filter out Pods that have already been deleted
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task<V1Pod> CreatePod(string @namespace, V1Pod pod, CancellationToken cancellationToken)
    {
        try
        {
            return await k8sClient.CoreV1.CreateNamespacedPodAsync(pod, @namespace, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public IAsyncEnumerable<(WatchEventType, V1Pod)> WatchPodStatus(string @namespace, string name, CancellationToken cancellationToken)
    {
        try
        {
            var podListResponse = k8sClient.CoreV1.ListNamespacedPodWithHttpMessagesAsync(
                    @namespace,
                    fieldSelector: $"metadata.name={name}",
                    watch: true,
                    timeoutSeconds: 60,
                    cancellationToken: cancellationToken);

            return podListResponse.WatchAsync<V1Pod, V1PodList>(onError: ex => throw FormatException(ex), cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task WaitForContainerToStart(string @namespace, string podName, string containerName, CancellationToken cancellationToken)
    {
        try
        {
            // Ensure that the Pod is Running and the container status is Ready
            await foreach (var (type, pod) in WatchPodStatus(@namespace, podName, cancellationToken).ConfigureAwait(false))
            {
                if (pod.Name() == podName)
                {
                    var containerStatus = GetPodContainerStatus(pod, containerName);

                    if (containerStatus != null)
                    {
                        if (pod.Status.Phase == PodPhase.Running && containerStatus.Ready)
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    private static V1ContainerStatus? GetPodContainerStatus(V1Pod pod, string containerName)
    {
        if (pod.Status.Phase == PodPhase.Failed)
            throw new Exception($"Pod '{pod.Name()}' is in Failed phase");

        var containerStatus = pod.Status.ContainerStatuses?.FirstOrDefault(cs => cs.Name == containerName);
        string[] InvalidContainerWaitingReasons = { "InvalidImageName", "ErrImagePull" };

        if (containerStatus?.State.Terminated != null)
            throw new Exception($"Pod '{pod.Name()}' container is in Terminated state: {containerStatus.State.Terminated.Reason}\n{containerStatus.State.Terminated.Message}");

        if (containerStatus?.State.Waiting != null &&
            InvalidContainerWaitingReasons.Contains(containerStatus.State.Waiting.Reason)) // assumption is we'll find other odd container states, but if not this check can be simplified
            throw new Exception($"Pod '{pod.Name()}' failed to start with container state Waiting and Reason: {containerStatus.State.Waiting.Reason}\n{containerStatus.State.Waiting.Message}");

        return containerStatus;
    }

    public async Task FollowPodLogUntil(string @namespace, string podName, string containerName, string output, CancellationToken cancellationToken)
    {
        try
        {
            // Read & follow the container log output to look for <output>
            var response = await k8sClient.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(podName, @namespace, containerName, true, cancellationToken: cancellationToken).ConfigureAwait(false);

            var sb = new StringBuilder();
            using var reader = new StreamReader(response.Body);

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
            {
                if (line.EndsWith(output, StringComparison.Ordinal))
                    return;

                sb.AppendLine(line);
            }

            throw new ScriptException($"Unable to detect desired output: {output}", sb.ToString());
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task DeletePod(string @namespace, string name, CancellationToken cancellationToken)
    {
        try
        {
            // Set up the watcher prior to issuing the delete request so we can properly monitor the Pod's status
            var watcher = WatchPodStatus(@namespace, name, cancellationToken).ConfigureAwait(false);

            await k8sClient.CoreV1.DeleteNamespacedPodAsync(name, @namespace, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Ensure that the Pod is Terminated
            await foreach (var (type, pod) in watcher)
            {
                if (pod.Name() == name && type == WatchEventType.Deleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task<V1Secret> GetSecret(string @namespace, string name, CancellationToken cancellationToken)
    {
        try
        {
            return await k8sClient.CoreV1.ReadNamespacedSecretAsync(name, @namespace, false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task<V1Secret> CreateSecret(string @namespace, string name, IDictionary<string, string> data, string type, CancellationToken cancellationToken)
    {
        try
        {
            return await k8sClient.CoreV1.CreateNamespacedSecretAsync(
                new V1Secret(
                    metadata: new V1ObjectMeta()
                    {
                        Name = name
                    },
                    stringData: data,
                    type: type
                ),
                @namespace,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task<V1Secret> CreateImagePullSecret(string @namespace, string secretName, CancellationToken cancellationToken, params ImagePullSecret[] credentials)
    {
        try
        {
            V1Secret secret;

            try
            {
                secret = await k8sClient.CoreV1.ReadNamespacedSecretAsync(secretName, @namespace, false, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpOperationException httpEx && httpEx.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var auths = credentials.ToDictionary(c => c.Registry, c => new { username = c.Username, password = c.Token });

                var secretData = new Dictionary<string, string>
                {
                    { ".dockerconfigjson", JsonSerializer.Serialize(new { auths }) }
                };

                secret = await CreateSecret(@namespace, secretName, secretData, "kubernetes.io/dockerconfigjson", cancellationToken).ConfigureAwait(false);
            }

            return secret;
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task DeleteSecret(string @namespace, string secretName, CancellationToken cancellationToken)
    {
        try
        {
            await k8sClient.CoreV1.DeleteNamespacedSecretAsync(secretName, @namespace, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw FormatException(ex);
        }
    }

    public async Task<ImagePullSecret> FindImagePullSecret(string @namespace, IEnumerable<string> secretNames, string registry, CancellationToken cancellationToken)
    {
        foreach (var secretName in secretNames)
        {
            var secret = await GetSecret(@namespace, secretName, cancellationToken).ConfigureAwait(false);
            var secretContent = JsonDocument.Parse(Encoding.UTF8.GetString(secret.Data[".dockerconfigjson"]));

            if (secretContent.RootElement.GetProperty("auths").TryGetProperty(registry, out var registryAuthConfig))
            {
                var username = registryAuthConfig.TryGetProperty("username", out var user) ? user.GetString() : null;
                var authToken = registryAuthConfig.TryGetProperty("auth", out var auth) ? auth.GetString() : null;
                var password = registryAuthConfig.TryGetProperty("password", out var pass) ? pass.GetString() : null;

                return new ImagePullSecret(registry, username ?? "elastic-machines",
                    authToken ?? password ?? throw new Exception("No password or token found in image pull secret"));
            }
        }

        throw new Exception($"Unable to find image pull secret for registry: {registry}. Available secrets: {string.Join(", ", secretNames)}");
    }

    private static Exception FormatException(Exception ex)
    {
        if (ex is HttpOperationException httpEx)
        {
            var msg = $"StatusCode: {httpEx.Response.StatusCode}\nReasonPhrase: {httpEx.Response.ReasonPhrase}\nMessage: {httpEx.Message}\nContent: {httpEx.Response.Content}";
            ex = new Exception(msg, httpEx);
        }

        return ex;
    }

    private static MemoryStream AsStream(string value, Encoding? encoding = null)
    {
        var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, encoding: encoding, leaveOpen: true);
        writer.Write(value);
        writer.Flush();
        stream.Position = 0;

        return stream;
    }
}
