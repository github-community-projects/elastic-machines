using ElasticMachines.Engine.Octokit.Credentials;
using ElasticMachines.Utilities;
using Octokit;
using Octokit.Internal;

namespace ElasticMachines.Engine.Octokit;

public class InstallationClient : GitHubClient, IInstallationClient
{
    public ProductHeaderValue ProductInformation { get; }

    public InstallationClient(string personalAccessToken, ProductHeaderValue productInformation, Uri baseAddress)
        : this(productInformation, CreateConnection(personalAccessToken, productInformation, baseAddress))
    {
    }

    public InstallationClient(long installationId, IAppClient appClient)
        : this(appClient.ProductInformation, CreateConnection(installationId, appClient))
    {
    }

    private InstallationClient(ProductHeaderValue productInformation, IConnection connection)
        : base(connection)
    {
        ProductInformation = productInformation;
    }

    private static IConnection CreateConnection(string personalAccessToken, ProductHeaderValue productInformation, Uri baseAddress)
    {
        var credentials = new global::Octokit.Credentials(personalAccessToken);
        var credentialStore = new InMemoryCredentialStore(credentials);

        return new Connection(productInformation, baseAddress, credentialStore, new HttpClientAdapter(() => new RetryingHandler()), new SimpleJsonSerializer());
    }

    private static IConnection CreateConnection(long installationId, IAppClient appClient)
    {
        var credentialFactory = new InstallationCredentialFactory(installationId, appClient);
        var credentialStore = new CachingCredentialStore(credentialFactory);

        return new Connection(appClient.ProductInformation, appClient.BaseAddress, credentialStore, new HttpClientAdapter(() => new RetryingHandler()), new SimpleJsonSerializer());
    }

    public IActionsRuntimeClient CreateActionsRuntimeClient(Uri url)
    {
        return new ActionsRuntimeClient(url, this);
    }
}
