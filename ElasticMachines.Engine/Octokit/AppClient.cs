using ElasticMachines.Engine.Octokit.Credentials;
using ElasticMachines.Utilities;
using Octokit;
using Octokit.Internal;

namespace ElasticMachines.Engine.Octokit;

public class AppClient : GitHubAppsClient, IAppClient
{
    private static readonly ProductHeaderValue productInformation = new(Telemetry.Name, Telemetry.AssemblyVersion);

    public Uri BaseAddress { get; }
    public ProductHeaderValue ProductInformation => productInformation;

    public AppClient(string appId, string privateKey, string host = "github.com")
        : base(CreateApiConnection(appId, privateKey, host))
    {
        BaseAddress = Connection.BaseAddress;
    }

    private static IApiConnection CreateApiConnection(string appId, string privateKey, string host)
    {
        var url = host == "github.com" ? "https://api.github.com/" : $"https://{host}/api/v3/";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new Exception($"Host '{host}' is invalid");

        var credentialFactory = new AppCredentialFactory(appId, privateKey);
        var credentialStore = new CachingCredentialStore(credentialFactory);
        var connection = new Connection(productInformation, uri, credentialStore, new HttpClientAdapter(() => new RetryingHandler()), new SimpleJsonSerializer());

        return new ApiConnection(connection);
    }

    public IInstallationClient CreateInstallationClient(long installationId)
    {
        return new InstallationClient(installationId, this);
    }
}
