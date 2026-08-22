using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Octokit;

namespace ElasticMachines.Engine.Octokit.Credentials;

public class AppCredentialFactory : ICredentialFactory
{
    private readonly string appId;
    private readonly RSA rsa;

    public AppCredentialFactory(string appId, string privateKey)
    {
        this.appId = appId;
        rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
    }

    public Task<(global::Octokit.Credentials credentials, DateTimeOffset expiration)> CreateCredentials(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var expires = now.AddMinutes(10);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = appId,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
        };

        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);

        return Task.FromResult((new global::Octokit.Credentials(token, AuthenticationType.Bearer), expires));
    }
}
