using System.Net.Http.Json;
using System.Text.Json;
using ElasticMachines.Engine.Octokit.Models;
using ElasticMachines.Utilities;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ElasticMachines.Engine.Octokit;

public sealed class ActionsRuntimeClient : IActionsRuntimeClient
{
    private readonly Uri url;
    private readonly IInstallationClient installationClient;
    private readonly HttpClient httpClient;
    private readonly AsyncExpiringLazy<global::Octokit.AccessToken> lazyAccessToken;
    private readonly AsyncExpiringLazy<RuntimeToken> lazyRuntimeToken;

    public global::Octokit.ProductHeaderValue ProductInformation { get; }

    public ActionsRuntimeClient(Uri url, IInstallationClient installationClient)
    {
        this.url = url;
        this.installationClient = installationClient;

        httpClient = new(new RetryingHandler())
        {
            DefaultRequestHeaders =
            {
                { "User-Agent", installationClient.ProductInformation.ToString() },
                { "Accept", "application/json;api-version=6.0-preview" }
            }
        };

        lazyAccessToken = new(GetAccessTokenAsync, httpClient.Timeout);
        lazyRuntimeToken = new(GetRuntimeTokenAsync, httpClient.Timeout);

        ProductInformation = installationClient.ProductInformation;
    }

    public async Task<List<Runner>> GetRunnersAsync(long runnerScaleSetId, long runnerGroupId, CancellationToken cancellationToken)
    {
        return await SendAsync<Page<Runner>>(HttpMethod.Get, $"_apis/distributedtask/pools/{runnerGroupId}/agents?runnerScaleSetId={runnerScaleSetId}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<long>> AcquireJobsAsync(long runnerScaleSetId, IEnumerable<long> jobIds, CancellationToken cancellationToken)
    {
        return await SendAsync<Page<long>>(HttpMethod.Post, $"_apis/runtime/runnerscalesets/{runnerScaleSetId}/acquirejobs", jobIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<RunnerApplication>> GetRunnerApplicationsAsync(string platform, bool includeToken, int top, CancellationToken cancellationToken)
    {
        return await SendAsync<Page<RunnerApplication>>(HttpMethod.Get, $"_apis/distributedtask/packages/agent?platform={platform}&includeToken={includeToken}&$top={top}", cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteRunnerAsync(long id, long runnerGroupId, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Delete, $"_apis/distributedtask/pools/{runnerGroupId}/agents/{id}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<RunnerGroup>> GetRunnerGroupsAsync(string groupName, CancellationToken cancellationToken)
    {
        return await SendAsync<Page<RunnerGroup>>(HttpMethod.Get, $"_apis/runtime/runnergroups?groupName={groupName}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<RunnerScaleSet> GetRunnerScaleSetAsync(long id, CancellationToken cancellationToken)
    {
        return await SendAsync<RunnerScaleSet>(HttpMethod.Get, $"_apis/runtime/runnerscalesets/{id}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<RunnerScaleSet>> GetRunnerScaleSetsAsync(string name, long runnerGroupId, CancellationToken cancellationToken)
    {
        return await SendAsync<Page<RunnerScaleSet>>(HttpMethod.Get, $"_apis/runtime/runnerscalesets?name={name}&runnerGroupId={runnerGroupId}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<RunnerScaleSet> CreateRunnerScaleSetAsync(string name, long runnerGroupId, CancellationToken cancellationToken)
    {
        var content = new { name, runnerGroupId };

        return await SendAsync<RunnerScaleSet>(HttpMethod.Post, "_apis/runtime/runnerscalesets", content, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<JobMessage>> GetAcquirableJobsAsync(long runnerScaleSetId, int top, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, $"_apis/runtime/runnerscalesets/{runnerScaleSetId}/acquirablejobs?top={top}", cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var page = string.IsNullOrEmpty(responseContent) ? default : JsonSerializer.Deserialize<Page<JobMessage>>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return page?.Value ?? new();
    }

    public async Task<JitConfiguration> CreateJitConfigurationAsync(long runnerScaleSetId, CancellationToken cancellationToken)
    {
        return await SendAsync<JitConfiguration>(HttpMethod.Post, $"_apis/runtime/runnerscalesets/{runnerScaleSetId}/generatejitconfig", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Session> CreateSessionAsync(long runnerScaleSetId, string ownerName, CancellationToken cancellationToken)
    {
        var content = new { ownerName };

        return await SendAsync<Session>(HttpMethod.Post, $"_apis/runtime/runnerscalesets/{runnerScaleSetId}/sessions", content, cancellationToken).ConfigureAwait(false);
    }

    public IActionsSessionClient CreateActionsSessionClient(Session session)
    {
        return new ActionsSessionClient(session, this);
    }

    public async Task DeleteSessionAsync(long runnerScaleSetId, string sessionId, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Delete, $"_apis/runtime/runnerscalesets/{runnerScaleSetId}/sessions/{sessionId}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<SessionRefresh> RefreshSessionAsync(long runnerScaleSetId, string sessionId, CancellationToken cancellationToken)
    {
        return await SendAsync<SessionRefresh>(HttpMethod.Patch, $"_apis/runtime/runnerscalesets/{runnerScaleSetId}/sessions/{sessionId}", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(lazyAccessToken.DisposeAsync().AsTask(), lazyRuntimeToken.DisposeAsync().AsTask()).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativeUri, CancellationToken cancellationToken)
    {
        return await SendAsync<T>(method, relativeUri, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativeUri, object? content, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, relativeUri, content, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken).ConfigureAwait(false)
            ?? throw new Exception("Unable to deserialize response");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUri, CancellationToken cancellationToken)
    {
        return await SendAsync(method, relativeUri, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUri, object? content, CancellationToken cancellationToken)
    {
        var runtimeToken = await lazyRuntimeToken.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var uri = runtimeToken.Url.Combine(relativeUri);

        using var request = new HttpRequestMessage(method, uri)
        {
            Headers = { Authorization = new("Bearer", runtimeToken.Token) },
            Content = content == null ? null : JsonContent.Create(content)
        };

        var response = await httpClient.SendSuccessfullyAsync(request, cancellationToken).ConfigureAwait(false);

        return response;
    }

    private async Task<(global::Octokit.AccessToken, DateTimeOffset)> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var accessToken = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) switch
        {
            [var account] => await installationClient.Actions.SelfHostedRunners.CreateOrganizationRegistrationToken(account, cancellationToken).ConfigureAwait(false),
            [var account, var repository] => await installationClient.Actions.SelfHostedRunners.CreateRepositoryRegistrationToken(account, repository, cancellationToken).ConfigureAwait(false),
            _ => throw new Exception($"Invalid account or repository URL '{url}'")
        };

        return (accessToken, accessToken.ExpiresAt);
    }

    private async Task<(RuntimeToken, DateTimeOffset)> GetRuntimeTokenAsync(CancellationToken cancellationToken)
    {
        var accessToken = await lazyAccessToken.GetValueAsync(cancellationToken).ConfigureAwait(false);
        var uri = installationClient.Connection.BaseAddress.Combine("actions/runner-registration");
        var content = new { url, runner_event = "register" };

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Headers = { Authorization = new("RemoteAuth", accessToken.Token) },
            Content = JsonContent.Create(content)
        };

        using var response = await httpClient.SendSuccessfullyAsync(request, cancellationToken).ConfigureAwait(false);
        var runtimeToken = await response.Content.ReadFromJsonAsync<RuntimeToken>(cancellationToken).ConfigureAwait(false)
            ?? throw new Exception("Unable to deserialize response");

        return (runtimeToken, new JsonWebToken(runtimeToken.Token).ValidTo);
    }
}
