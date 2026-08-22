namespace ElasticMachines.Engine.Octokit.Credentials;

public interface ICredentialFactory
{
    Task<(global::Octokit.Credentials credentials, DateTimeOffset expiration)> CreateCredentials(CancellationToken cancellationToken);
}
