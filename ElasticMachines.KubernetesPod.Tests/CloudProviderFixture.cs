using ElasticMachines.KubernetesPod.Clients;
using ElasticMachines.Tests.Core;
using Microsoft.Extensions.Configuration;

namespace ElasticMachines.KubernetesPod.Tests;

public class CloudProviderFixture : BaseCloudProviderFixture
{
    public string? Context { get; }
    public string Namespace { get; }
    public KubernetesClient KubernetesClient { get; }
    public bool TestWindows { get; }

    public CloudProviderFixture() : base("kubernetes-pod")
    {
        Context = Settings["context"];
        Namespace = TestSuiteName;
        KubernetesClient = new KubernetesClient(AppSettings["KUBECONFIG_CONTENTS"], Context);
        TestWindows = Settings.GetValue<bool>("testWindows");
    }

    public override async Task InitializeAsync()
    {
        var baseTask = base.InitializeAsync();

        await KubernetesClient.EnsureNamespaceExists(Namespace, CancellationToken.None);

        await baseTask;
    }

    public override async Task DisposeAsync()
    {
        await Task.WhenAll(base.DisposeAsync(), KubernetesClient.DeleteNamespace(Namespace));
    }
}
