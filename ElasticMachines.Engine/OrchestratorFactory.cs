using System.Reflection;
using ElasticMachines.Engine.Models;
using ElasticMachines.Engine.Octokit;
using Microsoft.Extensions.DependencyInjection;

namespace ElasticMachines.Engine;

public class OrchestratorFactory : IOrchestratorFactory
{
    private readonly IServiceProvider serviceProvider;

    private readonly Dictionary<string, (Type provider, Type configuration)> cloudProviderTypes = new();

    public OrchestratorFactory()
        : this(new ServiceCollection().BuildServiceProvider())
    {
    }

    public OrchestratorFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;

        var providerTypes = Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
            .Select(Assembly.LoadFrom)
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ICloudProvider).IsAssignableFrom(t));

        foreach (var type in providerTypes)
        {
            var configType = type?.BaseType?.GetGenericArguments().FirstOrDefault() ?? throw new Exception($"Unable to determine generic argument for '{type}'");
            var attributes = type.GetCustomAttributes(typeof(CloudProviderAttribute), false).Cast<CloudProviderAttribute>();

            foreach (var attribute in attributes)
            {
                cloudProviderTypes.Add(attribute.Name, (type, configType));
            }
        }
    }

    public async Task<Orchestrator> CreateOrchestratorAsync(string provider, Configuration configuration, string appId, string privateKey)
    {
        var appClient = new AppClient(appId, privateKey, configuration.Url.Host);

        var installation = configuration.Url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) switch
        {
            [var accountName] => await appClient.GetOrganizationInstallationForCurrent(accountName).ConfigureAwait(false),
            [var accountName, var repositoryName] => await appClient.GetRepositoryInstallationForCurrent(accountName, repositoryName).ConfigureAwait(false),
            _ => throw new Exception($"Invalid account or repository URL '{configuration.Url}'")
        };

        var installationClient = new InstallationClient(installation.Id, appClient);
        var actionsRuntimeClient = new ActionsRuntimeClient(configuration.Url, installationClient);

        return CreateOrchestrator(provider, configuration, actionsRuntimeClient);
    }

    public Orchestrator CreateOrchestrator(string provider, Configuration configuration, IActionsRuntimeClient actionsRuntimeClient)
    {
        var cloudProvider = CreateCloudProvider(provider, configuration);
        var downloadManager = new DownloadManager(actionsRuntimeClient);

        return ActivatorUtilities.CreateInstance<Orchestrator>(serviceProvider, configuration, cloudProvider, downloadManager, actionsRuntimeClient);
    }

    private ICloudProvider CreateCloudProvider(string provider, Configuration configuration)
    {
        if (!cloudProviderTypes.TryGetValue(provider, out var type))
            throw new Exception($"Provider '{provider}' not found");

        if (configuration.Url == null)
            throw new Exception("Configuration's 'url' property is missing or invalid");

        var tags = new Dictionary<string, string>
        {
            { "elastic-machines-name", configuration.Name.ToLowerInvariant() },
            { "elastic-machines-group", configuration.Group.ToLowerInvariant() },
            { "elastic-machines-url", configuration.Url.AbsoluteUri.ToLowerInvariant() },
        };

        return Activator.CreateInstance(type.provider, configuration.WithAs(type.configuration), tags) as ICloudProvider
            ?? throw new Exception($"Unable to create new instance of provider '{provider}'");
    }
}
