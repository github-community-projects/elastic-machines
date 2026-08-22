using System.Text.Json;
using ElasticMachines.Engine.Models;
using ElasticMachines.KubernetesPod.Clients;
using ElasticMachines.KubernetesPod.Models;
using ElasticMachines.Tests.Core;
using k8s.Models;
using Xunit;

namespace ElasticMachines.KubernetesPod.Tests;

[Timed]
public class CloudProviderTests : IClassFixture<CloudProviderFixture>
{
    private readonly CloudProviderFixture fixture;
    private readonly Models.Configuration configuration;

    public CloudProviderTests(CloudProviderFixture fixture)
    {
        this.fixture = fixture;

        var template = new V1PodTemplateSpec
        {
            Metadata = new()
            {
                NamespaceProperty = fixture.Namespace
            },
            Spec = new()
            {
                Containers =
                [
                    new("runner")
                    {
                        Image = "ghcr.io/actions/actions-runner:latest",
                        Command = ["sh", "-c", "echo Listening for Jobs && sleep infinity"]
                    }
                ]
            }
        };

        configuration = new Models.Configuration(template)
        {
            Context = fixture.Context
        };
    }

    [Fact]
    public void DeserializeConfiguration()
    {
        var yaml = $@"
        name: kubernetes
        with:
          context: myContext
          template:
            metadata:
              namespace: {fixture.Namespace}
              labels:
                testLabel: testValue
            spec:
              containers:
                - name: runner
                  image: myImage:latest
              imagePullSecrets:
                - name: image-pull-secret
        ";

        var podConfig = fixture.DeserializeAsConfiguration<Models.Configuration>(yaml);

        Assert.Equal("myContext", podConfig.Context);
        Assert.Equal(fixture.Namespace, podConfig.Template.Metadata.Namespace());
        Assert.Equal("testValue", podConfig.Template.Metadata.Labels["testLabel"]);
        Assert.Equal("myImage:latest", podConfig.Template.Spec.Containers[0].Image);
        Assert.Equal("image-pull-secret", podConfig.Template.Spec.ImagePullSecrets[0].Name);
    }

    [Fact]
    public async Task InitializeWithoutNamespace()
    {
        configuration.Template.Metadata.NamespaceProperty = $"{configuration.Template.Metadata.NamespaceProperty}-new";

        try
        {
            var provider = new CloudProvider(configuration, fixture.Tags);
            await provider.GetMachinesAsync(CancellationToken.None);
        }
        finally
        {
            await fixture.KubernetesClient.DeleteNamespace(configuration.Template.Metadata.NamespaceProperty);
        }
    }

    [Fact]
    public void AllowEmptyContainerName()
    {
        configuration.Template.Spec.Containers[0].Name = null;

        _ = new CloudProvider(configuration, fixture.Tags);
    }

    [Fact]
    public async Task FilterByTags()
    {
        var providers = new[]
        {
            // Clone the configuration so both providers can modify it independently
            new CloudProvider(JsonSerializer.Deserialize<Models.Configuration>(JsonSerializer.Serialize(configuration)) ?? throw new(), new(fixture.Tags) { { "provider", "1" } }),
            new CloudProvider(JsonSerializer.Deserialize<Models.Configuration>(JsonSerializer.Serialize(configuration)) ?? throw new(), new(fixture.Tags) { { "provider", "2" } })
        };

        async Task<string> create(CloudProvider provider)
        {
            var name = fixture.GenerateUniqueName();
            await provider.ConfigureMachineAsRunnerAsync(name, "", fixture.GetRunnerDownloadAsync, CancellationToken.None);

            return name;
        }

        var created = await Task.WhenAll(providers.Select(create));
        var fetched = await Task.WhenAll(providers.Select(p => p.GetMachinesAsync(CancellationToken.None)));

        Assert.Equal(created[0], fetched[0].Single());
        Assert.Equal(created[1], fetched[1].Single());
    }

    [Fact]
    public async Task CreateThenDelete()
    {
        var provider = new CloudProvider(configuration, fixture.Tags);

        var name = fixture.GenerateUniqueName();
        await provider.ConfigureMachineAsRunnerAsync(name, "", fixture.GetRunnerDownloadAsync, CancellationToken.None);
        Assert.Contains(name, await provider.GetMachinesAsync(CancellationToken.None));

        await provider.DeleteMachineAsync(name, CancellationToken.None);
        Assert.DoesNotContain(name, await provider.GetMachinesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateThenDeleteAfterPodCompletion()
    {
        // Simulate a pod that is already completed when Delete is called
        configuration.Template.Spec.Containers[0].Command = new[] { "sh", "-c", "sleep 2 && echo Listening for Jobs" };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var name = fixture.GenerateUniqueName();
        await provider.ConfigureMachineAsRunnerAsync(name, "", fixture.GetRunnerDownloadAsync, CancellationToken.None);
        Assert.Contains(name, await provider.GetMachinesAsync(CancellationToken.None));

        await Task.Delay(2000); // Ensure the pod is completed

        await provider.DeleteMachineAsync(name, CancellationToken.None);
        Assert.DoesNotContain(name, await provider.GetMachinesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateRunner()
    {
        configuration.Template.Spec.Containers[0].Command = ["./run.sh"];

        var provider = new CloudProvider(configuration, fixture.Tags);
        var name = await fixture.CreateRunnerAsync(provider);

        Assert.Contains(name, await provider.GetMachinesAsync(CancellationToken.None));
    }

    [SkippableTheory]
    [InlineData("mcr.microsoft.com/windows/servercore:ltsc2022", null, new[] { "powershell", "-command", "Invoke-WebRequest -Uri \"${env:DOWNLOAD_URL}\" -OutFile runner.zip; Expand-Archive runner.zip; & \".\\runner\\run.cmd\"" }, Engine.Models.OperatingSystem.Windows)]
    // DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 temporarily required until runner supports Ubuntu 24.04's ICU package
    [InlineData("ubuntu:latest", "/runner", new[] { "bash", "-c", "apt-get update && apt-get install -y curl && curl -fsSL \"$DOWNLOAD_URL\" | tar xz && ./bin/installdependencies.sh && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./run.sh" }, Engine.Models.OperatingSystem.Linux)]
    public async Task CreateRunnerFromVanillaImage(string image, string? workingDir, string[] command, Engine.Models.OperatingSystem operatingSystem)
    {
        Skip.If(!fixture.TestWindows && operatingSystem == Engine.Models.OperatingSystem.Windows, "Skipping Windows");

        var (url, _) = await fixture.GetRunnerDownloadAsync(operatingSystem, Architecture.X64, CancellationToken.None);

        configuration.Template.Spec.Containers[0] = new("runner")
        {
            Image = image,
            Command = command,
            WorkingDir = workingDir,
            Env =
            [
                new("DOWNLOAD_URL", url.AbsoluteUri),
                new("RUNNER_ALLOW_RUNASROOT", "1")
            ]
        };

        var provider = new CloudProvider(configuration, fixture.Tags);
        var name = await fixture.CreateRunnerAsync(provider);

        Assert.Contains(name, await provider.GetMachinesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PaginateListApis()
    {
        var client = new KubernetesClient(fixture.KubernetesClient, apiLimit: 1);
        var namespaces = await client.GetAllNamespaces();

        Assert.InRange(namespaces.Count, 2, int.MaxValue);
    }

    [Fact]
    public async Task PodWithMultipleContainers()
    {
        configuration.Template.Spec.Containers.Add(new("other") { Image = "ubuntu:latest", Command = ["sleep", "infinity"] });

        var provider = new CloudProvider(configuration, fixture.Tags);

        var name = fixture.GenerateUniqueName();
        await provider.ConfigureMachineAsRunnerAsync(name, "", fixture.GetRunnerDownloadAsync, CancellationToken.None);

        var pod = await fixture.KubernetesClient.GetPod(fixture.Namespace, name);

        Assert.Equal(2, pod.Status.ContainerStatuses.Count);
        Assert.True(pod.Status.ContainerStatuses.All(status => status.Ready));
        Assert.True(pod.Status.ContainerStatuses.All(status => status.State.Running != null));
        Assert.Equal("ghcr.io/actions/actions-runner:latest", pod.Spec.Containers[0].Image);
        Assert.Equal("ubuntu:latest", pod.Spec.Containers[1].Image);
    }

    [Fact]
    public async Task CanLocateImagePullSecrets()
    {
        // Create a secret for a fake registry that requires authentication to test that we can locate the correct secret by registry
        var secretName = "image-pull-secret";
        await fixture.KubernetesClient.CreateImagePullSecret(fixture.Namespace, secretName, CancellationToken.None, new ImagePullSecret("dummy.io", "my-username", "my-password"));

        var secret = await fixture.KubernetesClient.FindImagePullSecret(fixture.Namespace, [secretName], "dummy.io", CancellationToken.None);

        Assert.Equal("dummy.io", secret.Registry);
        Assert.Equal("my-username", secret.Username);
        Assert.Equal("my-password", secret.Token);

        var exception = await Assert.ThrowsAsync<Exception>(async () => await fixture.KubernetesClient.FindImagePullSecret(fixture.Namespace, [secretName], "not_there.io", CancellationToken.None));
        Assert.StartsWith("Unable to find image pull secret for registry: not_there.io", exception.Message);

        await fixture.KubernetesClient.DeleteSecret(fixture.Namespace, secretName, CancellationToken.None);
    }
}
