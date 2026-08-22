using Octokit;

namespace ElasticMachines.Monolith;

public class TranslatingCredentialStore : global::Octokit.GraphQL.ICredentialStore
{
    private readonly ICredentialStore inner;

    public TranslatingCredentialStore(ICredentialStore inner)
    {
        this.inner = inner;
    }

    public async Task<string> GetCredentials(CancellationToken cancellationToken = default)
    {
        var credentials = await inner.GetCredentials().ConfigureAwait(false);

        return credentials.Password;
    }
}
