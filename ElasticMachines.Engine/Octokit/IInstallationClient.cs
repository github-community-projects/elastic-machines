using Octokit;

namespace ElasticMachines.Engine.Octokit;

public interface IInstallationClient : IGitHubClient
{
    ProductHeaderValue ProductInformation { get; }
    IActionsRuntimeClient CreateActionsRuntimeClient(Uri url);
}
