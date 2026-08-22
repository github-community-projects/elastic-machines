using Octokit;

namespace ElasticMachines.Engine.Octokit;

public interface IAppClient : IGitHubAppsClient
{
    Uri BaseAddress { get; }
    ProductHeaderValue ProductInformation { get; }
    IInstallationClient CreateInstallationClient(long installationId);
}
