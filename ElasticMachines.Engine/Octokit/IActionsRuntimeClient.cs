using ElasticMachines.Engine.Octokit.Models;

namespace ElasticMachines.Engine.Octokit;

public interface IActionsRuntimeClient : IAsyncDisposable
{
    global::Octokit.ProductHeaderValue ProductInformation { get; }

    Task<List<long>> AcquireJobsAsync(long runnerScaleSetId, IEnumerable<long> jobIds, CancellationToken cancellationToken);
    Task<JitConfiguration> CreateJitConfigurationAsync(long runnerScaleSetId, CancellationToken cancellationToken);
    Task<RunnerScaleSet> CreateRunnerScaleSetAsync(string name, long runnerGroupId, CancellationToken cancellationToken);
    Task<Session> CreateSessionAsync(long runnerScaleSetId, string ownerName, CancellationToken cancellationToken);
    IActionsSessionClient CreateActionsSessionClient(Session session);
    Task DeleteRunnerAsync(long id, long runnerGroupId, CancellationToken cancellationToken);
    Task DeleteSessionAsync(long runnerScaleSetId, string sessionId, CancellationToken cancellationToken);
    Task<List<JobMessage>> GetAcquirableJobsAsync(long runnerScaleSetId, int top, CancellationToken cancellationToken);
    Task<List<RunnerApplication>> GetRunnerApplicationsAsync(string platform, bool includeToken, int top, CancellationToken cancellationToken);
    Task<List<RunnerGroup>> GetRunnerGroupsAsync(string groupName, CancellationToken cancellationToken);
    Task<List<Runner>> GetRunnersAsync(long runnerScaleSetId, long runnerGroupId, CancellationToken cancellationToken);
    Task<RunnerScaleSet> GetRunnerScaleSetAsync(long id, CancellationToken cancellationToken);
    Task<List<RunnerScaleSet>> GetRunnerScaleSetsAsync(string name, long runnerGroupId, CancellationToken cancellationToken);
    Task<SessionRefresh> RefreshSessionAsync(long runnerScaleSetId, string sessionId, CancellationToken cancellationToken);
}
