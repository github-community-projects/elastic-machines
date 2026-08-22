using Octokit;

namespace ElasticMachines.Engine.Octokit.Credentials;

public class InstallationCredentialFactory : ICredentialFactory
{
    private readonly long installationId;
    private readonly IGitHubAppsClient appsClient;

    public InstallationCredentialFactory(long installationId, IGitHubAppsClient appsClient)
    {
        this.installationId = installationId;
        this.appsClient = appsClient;
    }

    public async Task<(global::Octokit.Credentials credentials, DateTimeOffset expiration)> CreateCredentials(CancellationToken cancellationToken)
    {
        var installationToken = await appsClient.CreateInstallationToken(installationId).ConfigureAwait(false);

        return (new global::Octokit.Credentials(installationToken.Token), installationToken.ExpiresAt);
    }
}
