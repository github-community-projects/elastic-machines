using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using ElasticMachines.Engine;
using ElasticMachines.Engine.Octokit;
using ElasticMachines.Monolith.Models;
using Octokit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ElasticMachines.Monolith.Services;

public sealed class OrchestratorService : IOrchestratorService, IAsyncDisposable
{
    private readonly AppSettings appSettings;
    private readonly IAppClient appClient;
    private readonly IOrchestratorFactory orchestratorFactory;
    private readonly IActivityService<OrchestratorService> activityService;

    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly ConcurrentDictionary<long, InstallationClient> installationClients = new();
    private readonly ConcurrentDictionary<Uri, IActionsRuntimeClient> actionsRuntimeClients = new();
    private readonly IDeserializer yamlDeserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();

    public ConcurrentDictionary<long, ConcurrentDictionary<long, ConcurrentDictionary<(string FilePath, string FileContents), (CancellationTokenSource CancellationTokenSource, Task Task)>>> Orchestrations { get; } = new();

    public OrchestratorService(AppSettings appSettings, IAppClient appClient, IOrchestratorFactory orchestratorFactory, IActivityService<OrchestratorService> activityService)
    {
        this.appSettings = appSettings;
        this.appClient = appClient;
        this.orchestratorFactory = orchestratorFactory;
        this.activityService = activityService;
    }

    public async Task HandleInstallationAsync(InstallationEvent installationPayload, CancellationToken cancellationToken)
    {
        using var activity = activityService.StartActivity();
        activity?.SetTag("event.action", installationPayload.Action);
        activity?.SetTag("account.name", installationPayload.Installation?.Account?.Login);

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (installationPayload.Installation == null)
                throw new Exception("Invalid event");

            var installationClient = installationClients.GetOrAdd(installationPayload.Installation.Id, id => new InstallationClient(id, appClient));

            switch (installationPayload.Action)
            {
                case InstallationAction.Created:
                case InstallationAction.Unsuspend:
                    var repositories = await installationClient.GetRepositoriesAsync(appSettings.AccountConfigurationPath, appSettings.RepositoryConfigurationPath, cancellationToken).ConfigureAwait(false);
                    await CreateOrchestrationsAsync(installationPayload.Installation, repositories, cancellationToken).ConfigureAwait(false);
                    break;
                case InstallationAction.Deleted:
                case InstallationAction.Suspend:
                    if (Orchestrations.TryRemove(installationPayload.Installation.Id, out var outdated))
                    {
                        await StopOutdatedOrchestrationsAsync(outdated.SelectMany(o => o.Value.Values).ToList()).ConfigureAwait(false);
                    }
                    break;
            }
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task HandleInstallationRepositoriesAsync(InstallationRepositoriesEvent installationRepositoriesPayload, CancellationToken cancellationToken)
    {
        using var activity = activityService.StartActivity();
        activity?.SetTag("account.name", installationRepositoriesPayload.Installation?.Account.Login);

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (installationRepositoriesPayload.Installation == null)
                throw new Exception("Invalid event");

            var installationClient = installationClients.GetOrAdd(installationRepositoriesPayload.Installation.Id, id => new InstallationClient(id, appClient));
            var installationOrchestrations = Orchestrations.GetOrAdd(installationRepositoriesPayload.Installation.Id, _ => new());
            var outdatedOrchestrators = new List<(CancellationTokenSource, Task)>();

            foreach (var repository in installationRepositoriesPayload.RepositoriesRemoved ?? Enumerable.Empty<global::Octokit.Repository>())
            {
                if (installationOrchestrations.TryRemove(repository.Id, out var outdated))
                {
                    outdatedOrchestrators.AddRange(outdated.Values);
                }
            }

            await StopOutdatedOrchestrationsAsync(outdatedOrchestrators).ConfigureAwait(false);

            if (installationRepositoriesPayload.RepositoriesAdded?.Any() ?? false)
            {
                var repositoryNodeIds = installationRepositoriesPayload.RepositoriesAdded.Select(r => r.NodeId).ToArray();
                var repositories = await installationClient.GetRepositoriesAsync(appSettings.AccountConfigurationPath, appSettings.RepositoryConfigurationPath, cancellationToken, repositoryNodeIds).ConfigureAwait(false);
                await CreateOrchestrationsAsync(installationRepositoriesPayload.Installation, repositories, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task HandlePushAsync(PushEvent pushPayload, CancellationToken cancellationToken)
    {
        using var activity = activityService.StartActivity();
        activity?.SetTag("account.name", pushPayload.Repository?.Owner?.Login);
        activity?.SetTag("repository.name", pushPayload.Repository?.Name);
        activity?.SetTag("repository.default_branch", pushPayload.Repository?.DefaultBranch);
        activity?.SetTag("push.commits.count", pushPayload.Commits?.Count);
        activity?.SetTag("push.ref", pushPayload.Ref);

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (pushPayload.Installation == null || pushPayload.Repository == null || pushPayload.Commits == null)
                throw new Exception("Invalid event");

            if (pushPayload.Ref != $"refs/heads/{pushPayload.Repository.DefaultBranch}")
                return;

            var changes = pushPayload.Commits.SelectMany(c => c.Added.Concat(c.Modified).Concat(c.Removed)).ToHashSet();

            if (!changes.Contains(appSettings.RepositoryConfigurationPath) && !changes.Contains(appSettings.AccountConfigurationPath))
                return;

            var installationClient = installationClients.GetOrAdd(pushPayload.Installation.Id, id => new InstallationClient(id, appClient));
            var repositories = await installationClient.GetRepositoriesAsync(appSettings.AccountConfigurationPath, appSettings.RepositoryConfigurationPath, cancellationToken, pushPayload.Repository.NodeId).ConfigureAwait(false);

            if (Orchestrations.TryGetValue(pushPayload.Installation.Id, out var installationOrchestrations) && installationOrchestrations.TryGetValue(pushPayload.Repository.Id, out var repositoryOrchestrations))
            {
                var files = repositories.SelectMany(r => new[]
                {
                    (appSettings.RepositoryConfigurationPath, r.RepositoryConfiguration),
                    (appSettings.AccountConfigurationPath, r.AccountConfiguration),
                });

                var outdated = RemoveOutdated(repositoryOrchestrations, files.OfType<(string, string)>()).ToList();
                await StopOutdatedOrchestrationsAsync(outdated).ConfigureAwait(false);
            }

            // Installation property is incomplete on push events
            var installation = await appClient.GetInstallationForCurrent(pushPayload.Installation.Id).ConfigureAwait(false);
            await CreateOrchestrationsAsync(installation, repositories, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using var activity = activityService.StartActivity();

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await UpdateOrchestrationsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public Task ValidatePermissionsAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopOutdatedOrchestrationsAsync(Orchestrations.Values.SelectMany(o => o.Values.SelectMany(v => v.Values)).ToList()).ConfigureAwait(false);

        // Dispose of clients after all orchestrators have been disposed
        await Task.WhenAll(actionsRuntimeClients.Values.Select(c => c.DisposeAsync().AsTask())).ConfigureAwait(false);
    }

    private async Task UpdateOrchestrationsAsync(CancellationToken cancellationToken)
    {
        using var activity = activityService.StartActivity();

        var installations = (await appClient.GetAllInstallationsForCurrent(new ApiOptions { PageSize = 100 }).ConfigureAwait(false)).Where(i => i.SuspendedAt == null && i.SuspendedBy == null).ToList();
        var installationRepositories = await GetInstallationRepositoriesAsync(installations, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("installations.count", installations.Count);

        var outdated = RemoveOutdatedOrchestrationsAsync(installationRepositories);
        await StopOutdatedOrchestrationsAsync(outdated).ConfigureAwait(false);
        activity?.SetTag("outdated.count", outdated.Count);

        await Task.WhenAll(installations.Select(i => CreateOrchestrationsAsync(i, installationRepositories[i.Id], cancellationToken))).ConfigureAwait(false);
    }

    private async Task<Dictionary<long, List<RepositoryContents>>> GetInstallationRepositoriesAsync(IReadOnlyList<Installation> installations, CancellationToken cancellationToken)
    {
        using var activity = activityService.StartActivity();

        var tasks = installations
            .Select(i => installationClients.GetOrAdd(i.Id, id => new InstallationClient(id, appClient)))
            .Select(c => c.GetRepositoriesAsync(appSettings.AccountConfigurationPath, appSettings.RepositoryConfigurationPath, cancellationToken));

        var repositories = await Task.WhenAll(tasks).ConfigureAwait(false);

        return installations.Zip(repositories).ToDictionary(t => t.First.Id, t => t.Second);
    }

    private List<(CancellationTokenSource, Task)> RemoveOutdatedOrchestrationsAsync(Dictionary<long, List<RepositoryContents>> installationRepositories)
    {
        using var activity = activityService.StartActivity();

        var outdated = RemoveOutdated(Orchestrations, installationRepositories.Keys)
            .SelectMany(o => o.Values)
            .SelectMany(o => o.Values)
            .ToList();

        foreach (var (installationId, installationOrchestrations) in Orchestrations)
        {
            var repositories = installationRepositories[installationId].ToDictionary(r => r.Id);
            outdated.AddRange(RemoveOutdated(installationOrchestrations, repositories.Keys).SelectMany(o => o.Values));

            foreach (var (repositoryId, repositoryOrchestrations) in installationOrchestrations)
            {
                var repository = repositories[repositoryId];

                var files = new[]
                {
                    (appSettings.RepositoryConfigurationPath, repository.RepositoryConfiguration),
                    (appSettings.AccountConfigurationPath, repository.AccountConfiguration),
                };

                outdated.AddRange(RemoveOutdated(repositoryOrchestrations, files.OfType<(string, string)>()));
            }
        }

        return outdated;
    }

    private static IEnumerable<TValue> RemoveOutdated<TKey, TValue>(IDictionary<TKey, TValue> previous, IEnumerable<TKey> current) where TKey : notnull
    {
        current = current.ToHashSet();

        foreach (var key in previous.Keys)
        {
            if (!current.Contains(key) && previous.Remove(key, out var value))
                yield return value;
        }
    }

    private async Task StopOutdatedOrchestrationsAsync(List<(CancellationTokenSource CancellationTokenSource, Task Task)> outdated)
    {
        using var activity = activityService.StartActivity();
        activity?.SetTag("outdated.count", outdated.Count);

        try
        {
            foreach (var (cancellationTokenSource, _) in outdated)
            {
                cancellationTokenSource.Cancel();
            }

            await Task.WhenAll(outdated.Select(o => o.Task)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Cancelling outdated orchestrators will likely cause exceptions, but should not prevent the creation of new orchestrators
            activity?.AddException(exception);
        }
    }

    private async Task CreateOrchestrationsAsync(Installation installation, List<RepositoryContents> repositories, CancellationToken cancellationToken)
    {
        using var activity = activityService.StartActivity();
        activity?.SetTag("account.name", installation.Account.Login);
        activity?.SetTag("repositories.count", repositories.Count);

        var hasCorrectPermissions = installation.Permissions != null
            && installation.Permissions.Actions?.Value >= InstallationReadWritePermissionLevel.Read
            && installation.Permissions.Administration == InstallationReadWritePermissionLevel.Write
            && installation.Permissions.Contents?.Value >= InstallationReadWritePermissionLevel.Read
            // User accounts don't have OrganizationSelfHostedRunners permissions
            && (installation.Account.Type == AccountType.User || installation.Permissions.OrganizationSelfHostedRunners == InstallationReadWritePermissionLevel.Write);

        if (!hasCorrectPermissions)
            throw new Exception("GitHub permissions are insufficient. The following permissions are required: Actions (Read-Only), Administration (Read & Write), Contents (Read-Only), and Organization self-hosted runners (Read & Write)");

        var installationClient = installationClients.GetOrAdd(installation.Id, id => new InstallationClient(id, appClient));
        var installationOrchestrations = Orchestrations.GetOrAdd(installation.Id, _ => new());
        var uniqueConfigurations = new HashSet<Configuration>();

        foreach (var repository in repositories)
        {
            var repositoryOrchestrations = installationOrchestrations.GetOrAdd(repository.Id, _ => new());

            // Create repository level orchestrators
            if (repository.RepositoryConfiguration != null)
            {
                var fileUrl = new Uri($"{repository.HtmlUrl}/blob/{repository.HeadCommit}/{appSettings.RepositoryConfigurationPath}");
                repositoryOrchestrations.GetOrAdd((appSettings.RepositoryConfigurationPath, repository.RepositoryConfiguration), _ => CreateOrchestration(repository.RepositoryConfiguration, fileUrl, uniqueConfigurations, repository, installation));
            }

            // Create organization level orchestrators (without passing the repository so they attach at the organization level)
            if (repository.AccountConfiguration != null && installation.Account.Type == AccountType.Organization && appSettings.AccountConfigurationRepositories.Contains(repository.Name))
            {
                var fileUrl = new Uri($"{repository.HtmlUrl}/blob/{repository.HeadCommit}/{appSettings.AccountConfigurationPath}");
                repositoryOrchestrations.GetOrAdd((appSettings.AccountConfigurationPath, repository.AccountConfiguration), _ => CreateOrchestration(repository.AccountConfiguration, fileUrl, uniqueConfigurations, null, installation));
            }
        }
    }

    private (CancellationTokenSource, Task) CreateOrchestration(string fileContents, Uri fileUrl, HashSet<Configuration> uniqueConfigurations, RepositoryContents? repository, Installation installation)
    {
        using var activity = activityService.StartActivity();
        activity?.SetTag("file.url", fileUrl);
        activity?.SetTag("account.name", installation.Account.Login);
        activity?.SetTag("repository.name", repository?.Name);

        var orchestrators = DeserializeConfigurations(fileContents, fileUrl)
            .Select(c => CreateOrchestrator(c, fileUrl, uniqueConfigurations, repository, installation))
            .OfType<IOrchestrator>()
            .ToList();

        var cancellationTokenSource = new CancellationTokenSource();
        var task = RunOrchestratorsAsync(orchestrators, cancellationTokenSource.Token);

        return (cancellationTokenSource, task);
    }

    public Orchestrator? CreateOrchestrator(Configuration configuration, Uri fileUrl, HashSet<Configuration> uniqueConfigurations, RepositoryContents? repository, Installation installation)
    {
        using var activity = activityService.StartActivity();
        activity?.SetTag("file.url", fileUrl);
        activity?.SetTag("configuration.name", configuration.Name);
        activity?.SetTag("configuration.group", configuration.Group);
        activity?.SetTag("configuration.provider", configuration.Provider);

        try
        {
            // Backfill the URL based on the configuration's source location
            configuration = configuration with { Url = repository?.HtmlUrl ?? new(installation.Account.HtmlUrl) };
            activity?.SetTag("configuration.url", configuration.Url);

            if (!uniqueConfigurations.Add(configuration))
                throw new Exception($"Skipping configuration with duplicate name '{configuration.Name}'");

            var installationClient = installationClients.GetOrAdd(installation.Id, id => new InstallationClient(id, appClient));
            var actionsRuntimeClient = actionsRuntimeClients.GetOrAdd(configuration.Url, installationClient.CreateActionsRuntimeClient);

            return orchestratorFactory.CreateOrchestrator(configuration.Provider, configuration, actionsRuntimeClient);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
        }

        return null;
    }

    private static async Task RunOrchestratorsAsync(List<IOrchestrator> orchestrators, CancellationToken cancellationToken)
    {
        // Disassociate orchestrator spans from the current activity to avoid infinitely large traces
        System.Diagnostics.Activity.Current = null;

        try
        {
            await Task.WhenAll(orchestrators.Select(o => o.RunAsync(cancellationToken))).ConfigureAwait(false);
        }
        finally
        {
            await Task.WhenAll(orchestrators.Select(orchestrator => orchestrator.DisposeAsync().AsTask())).ConfigureAwait(false);
        }
    }

    private List<Configuration> DeserializeConfigurations(string yaml, Uri fileUrl)
    {
        using var activity = activityService.StartActivity();
        activity?.SetTag("file.url", fileUrl);

        try
        {
            if (yamlDeserializer.Deserialize<List<Configuration>>(yaml) is List<Configuration> configurations)
                return configurations;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception, new() { { "file.contents", yaml } });
        }

        return [];
    }
}
