using ElasticMachines.Engine.Octokit.Models;

namespace ElasticMachines.Engine.Octokit;

public interface IActionsSessionClient : IAsyncDisposable
{
    Task<Message?> GetMessageAsync(int maxCapacity, CancellationToken cancellationToken);
    Task DeleteMessageAsync(long messageId, CancellationToken cancellationToken);
}
