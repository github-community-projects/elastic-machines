using System.Text.Json;
using ElasticMachines.Engine.Octokit.Models;
using ElasticMachines.Utilities;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ElasticMachines.Engine.Octokit;

public sealed class ActionsSessionClient : IActionsSessionClient
{
    private readonly long runnerScaleSetId;
    private readonly string sessionId;
    private readonly IActionsRuntimeClient actionsRuntimeClient;
    private readonly HttpClient httpClient;
    private readonly AsyncExpiringLazy<SessionRefresh> lazySession;

    public ActionsSessionClient(Session session, IActionsRuntimeClient actionsRuntimeClient)
    {
        this.runnerScaleSetId = session.RunnerScaleSet.Id;
        this.sessionId = session.SessionId;
        this.actionsRuntimeClient = actionsRuntimeClient;

        httpClient = new(new RetryingHandler())
        {
            DefaultRequestHeaders =
            {
                { "User-Agent", actionsRuntimeClient.ProductInformation.ToString() },
            }
        };

        lazySession = new(GetSessionTokenAsync, httpClient.Timeout);
    }

    public async Task<Message?> GetMessageAsync(int maxCapacity, CancellationToken cancellationToken)
    {
        var session = await lazySession.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, session.MessageQueueUrl)
        {
            Headers =
            {
                { "Authorization", $"Bearer {session.MessageQueueAccessToken}" },
                { "X-ScaleSetMaxCapacity", maxCapacity.ToString() }
            }
        };

        using var response = await httpClient.SendSuccessfullyAsync(request, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return string.IsNullOrEmpty(responseContent) ? default : JsonSerializer.Deserialize<Message>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task DeleteMessageAsync(long messageId, CancellationToken cancellationToken)
    {
        var session = await lazySession.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var uri = session.MessageQueueUrl.Combine(messageId.ToString());
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri)
        {
            Headers = { Authorization = new("Bearer", session.MessageQueueAccessToken) },
        };

        using var _ = await httpClient.SendSuccessfullyAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await lazySession.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<(SessionRefresh, DateTimeOffset)> GetSessionTokenAsync(CancellationToken cancellationToken)
    {
        var session = await actionsRuntimeClient.RefreshSessionAsync(runnerScaleSetId, sessionId, cancellationToken).ConfigureAwait(false);

        return (session, new JsonWebToken(session.MessageQueueAccessToken).ValidTo);
    }
}
