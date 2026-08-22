using ElasticMachines.Engine.Octokit;
using ElasticMachines.Monolith.Models;
using ElasticMachines.Utilities;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;

namespace ElasticMachines.Monolith;

public class InstallationClient : Engine.Octokit.InstallationClient
{
    private readonly IConnection connection;

    public InstallationClient(long installationId, IAppClient appClient)
        : base(installationId, appClient)
    {
        var graphqlProductInformation = new ProductHeaderValue(ProductInformation.Name, ProductInformation.Version);
        var graphqlBaseAddress = Connection.BaseAddress.Host switch
        {
            "api.github.com" => new Uri(Connection.BaseAddress, "/graphql"),
            _ => new Uri(Connection.BaseAddress, "/api/graphql"),
        };

        connection = new Connection(graphqlProductInformation, graphqlBaseAddress, new TranslatingCredentialStore(Connection.CredentialStore), new HttpClient(new RetryingHandler()));
    }

    public async Task<List<RepositoryContents>> GetRepositoriesAsync(string accountConfigurationPath, string repositoryConfigurationPath, CancellationToken cancellationToken, params string[] nodeIds)
    {
        var query = new Query()
            .Nodes(nodeIds.Select(id => new ID(id)).ToList())
            .OfType<Repository>()
            .Select(r => new RepositoryContents
            {
                Id = r.DatabaseId.GetValue(nameof(r.DatabaseId)),
                Name = r.Name,
                HtmlUrl = new(r.Url),
                HeadCommit = r.DefaultBranchRef.Select(x => x.Target.Oid).SingleOrDefault(),
                AccountConfiguration = r.Object($"HEAD:{accountConfigurationPath}", null).Select(o => o.Cast<Blob>().Text).SingleOrDefault(),
                RepositoryConfiguration = r.Object($"HEAD:{repositoryConfigurationPath}", null).Select(o => o.Cast<Blob>().Text).SingleOrDefault(),
            }).Compile();

        return (List<RepositoryContents>)await connection.Run(query, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<RepositoryContents>> GetRepositoriesAsync(string accountConfigurationPath, string repositoryConfigurationPath, CancellationToken cancellationToken)
    {
        var after = "after";
        var vars = new Dictionary<string, object?> { [after] = null };
        var repositories = new List<RepositoryContents>();

        var query = new Query()
            .Viewer
            .Repositories(first: 25, after: Variable.Var(after), affiliations: new RepositoryAffiliation?[] { RepositoryAffiliation.Owner })
            .Select(connection => new
            {
                connection.PageInfo.EndCursor,
                connection.PageInfo.HasNextPage,
                Items = connection.Nodes.Select(r => new RepositoryContents
                {
                    Id = r.DatabaseId.GetValue(nameof(r.DatabaseId)),
                    Name = r.Name,
                    HtmlUrl = new(r.Url),
                    HeadCommit = r.DefaultBranchRef.Select(x => x.Target.Oid).SingleOrDefault(),
                    AccountConfiguration = r.Object($"HEAD:{accountConfigurationPath}", null).Select(o => o.Cast<Blob>().Text).SingleOrDefault(),
                    RepositoryConfiguration = r.Object($"HEAD:{repositoryConfigurationPath}", null).Select(o => o.Cast<Blob>().Text).SingleOrDefault(),
                }).ToList()
            }).Compile();

        do
        {
            var page = await connection.Run(query, vars, cancellationToken).ConfigureAwait(false);
            vars[after] = page.HasNextPage ? page.EndCursor : null;
            repositories.AddRange(page.Items);
        }
        while (vars[after] != null);

        return repositories;
    }
}
