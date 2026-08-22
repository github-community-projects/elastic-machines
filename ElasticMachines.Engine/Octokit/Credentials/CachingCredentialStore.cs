using ElasticMachines.Utilities;
using Octokit;
using Octokit.Internal;

namespace ElasticMachines.Engine.Octokit.Credentials;

public class CachingCredentialStore : ICredentialStore
{
    private readonly AsyncExpiringLazy<global::Octokit.Credentials> lazyCredentials;

    public CachingCredentialStore(ICredentialFactory credentialFactory)
    {
        lazyCredentials = new(credentialFactory.CreateCredentials, new Request().Timeout);
    }

    public async Task<global::Octokit.Credentials> GetCredentials()
    {
        return await lazyCredentials.GetValueAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
