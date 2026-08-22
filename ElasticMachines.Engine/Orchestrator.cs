using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ElasticMachines.Engine.Models;
using ElasticMachines.Engine.Octokit;
using ElasticMachines.Engine.Octokit.Models;
using ElasticMachines.Utilities;

namespace ElasticMachines.Engine;

public sealed class Orchestrator : IOrchestrator, IAsyncDisposable
{
    private readonly Configuration configuration;
    private readonly ICloudProvider cloudProvider;
    private readonly IDownloadManager downloadManager;
    private readonly IActionsRuntimeClient actionsRuntimeClient;

    private readonly TagList tags;
    private readonly TaskMonitor taskMonitor;
    private readonly SemaphoreSlim semaphore;
    private readonly ScaleLimiter scaleLimiter;
    private readonly AsyncExpiringLazy<Session> lazySession;
    private readonly AsyncExpiringLazy<IActionsSessionClient> lazyActionsSessionClient;

    private Statistics? lastStatistics;

    // Not thread safe so only modified within the semaphore
    public UniqueQueue<long> AcquirableJobs { get; } = new();
    public ConcurrentDictionary<string, Runner> CreatingRunners { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, Runner> RegisteredRunners { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Orchestrator(Configuration configuration, ICloudProvider cloudProvider, IDownloadManager downloadManager, IActionsRuntimeClient actionsRuntimeClient)
    {
        this.configuration = configuration;
        this.cloudProvider = cloudProvider;
        this.downloadManager = downloadManager;
        this.actionsRuntimeClient = actionsRuntimeClient;

        tags = new()
        {
            { "configuration.name", configuration.Name },
            { "configuration.group", configuration.Group },
            { "configuration.url", configuration.Url },
            { "configuration.with", configuration.With == null ? null : JsonSerializer.Serialize(configuration.With) },
        };

        taskMonitor = new();
        semaphore = new(1, 1);
        scaleLimiter = new();
        lazySession = new(GetSessionAsync);
        lazyActionsSessionClient = new(GetActionsSessionClientAsync);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (lazySession.IsValueCreated)
            {
                var session = await lazySession.GetValueAsync(CancellationToken.None).ConfigureAwait(false);
                await actionsRuntimeClient.DeleteSessionAsync(session.RunnerScaleSet.Id, session.SessionId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            var disposables = new List<object>
            {
                cloudProvider,
                downloadManager,
                taskMonitor,
                lazySession,
                lazyActionsSessionClient
            };

            await Task.WhenAll(disposables.OfType<IAsyncDisposable>().Select(d => d.DisposeAsync().AsTask())).ConfigureAwait(false);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Telemetry.ConfigurationsCount.Add(1, tags);

        await RetryAsync(RefreshAsync, cancellationToken).ConfigureAwait(false);

        var refreshTask = RepeatAsync(RefreshAsync, TimeSpan.FromHours(1), cancellationToken);
        var pollTask = RepeatAsync(PollAsync, TimeSpan.Zero, cancellationToken);
        await Task.WhenAll(refreshTask, pollTask).ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("orchestrator.refresh", tags);

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var session = await lazySession.GetValueAsync(cancellationToken).ConfigureAwait(false);
            var updatedRunners = await actionsRuntimeClient.GetRunnersAsync(session.RunnerScaleSet.Id, session.RunnerScaleSet.RunnerGroupId, cancellationToken).ConfigureAwait(false);
            var updatedRunnersByName = updatedRunners.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var runner in updatedRunners)
            {
                RegisteredRunners.GetOrAdd(runner.Name, runner);
            }

            var offlineRunners = RegisteredRunners.Values
                .Where(r => !CreatingRunners.ContainsKey(r.Name))
                .Where(r => !updatedRunnersByName.TryGetValue(r.Name, out var updatedRunner) || updatedRunner.Status == RunnerStatus.Offline)
                .ToList();

            activity?.SetTag("runners.count", updatedRunners.Count);
            activity?.SetTag("runners.offline", offlineRunners.Count);

            var updatedMachines = await cloudProvider.GetMachinesAsync(cancellationToken).ConfigureAwait(false);
            var offlineMachines = updatedMachines.Where(m => !RegisteredRunners.ContainsKey(m)).ToList();
            activity?.SetTag("machines.count", updatedMachines.Count);
            activity?.SetTag("machines.offline", offlineMachines.Count);

            var runnerTasks = offlineRunners.Select(r => DeleteRunnerAsync(r.Name, r.Id, cancellationToken));
            var machineTasks = offlineMachines.Select(m => DeleteRunnerAsync(m, null, cancellationToken));

            await Task.WhenAll(runnerTasks.Concat(machineTasks)).ConfigureAwait(false);

            var acquirableJobs = await actionsRuntimeClient.GetAcquirableJobsAsync(session.RunnerScaleSet.Id, 1_000, cancellationToken).ConfigureAwait(false);
            await AcquireJobsAsync(0, acquirableJobs, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);

            throw; // Rethrow to trigger retry
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task PollAsync(CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("orchestrator.poll", tags);

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Make sure statistics are not null (should only happen the first time)
            lastStatistics ??= (await lazySession.GetValueAsync(cancellationToken).ConfigureAwait(false)).Statistics;
            var actionsSessionClient = await lazyActionsSessionClient.GetValueAsync(cancellationToken).ConfigureAwait(false);

            var scale = configuration.GetCurrentScale();
            var maxCapacity = Math.Min(scaleLimiter.Capacity, scale.MaximumMachines ?? ScaleLimiter.MaximumCapacity);
            var message = await actionsSessionClient.GetMessageAsync(maxCapacity, cancellationToken).ConfigureAwait(false);

            if (message != null)
            {
                await actionsSessionClient.DeleteMessageAsync(message.MessageId, cancellationToken).ConfigureAwait(false);
            }

            var jobMessages = message?.Body ?? [];
            lastStatistics = message?.Statistics ?? lastStatistics;
            Telemetry.JobsAssigned.Record(lastStatistics.TotalAssignedJobs, tags);
            Telemetry.JobsRunning.Record(lastStatistics.TotalRunningJobs, tags);
            Telemetry.RunnersBusy.Record(lastStatistics.TotalBusyRunners, tags);
            Telemetry.RunnersIdle.Record(lastStatistics.TotalIdleRunners, tags);
            Telemetry.RunnersRegistered.Record(lastStatistics.TotalRegisteredRunners, tags);

            var (runnersDelta, jobsDelta) = CalculateDelta(scale, lastStatistics);
            await ScaleAsync(runnersDelta, jobMessages, cancellationToken).ConfigureAwait(false);
            await AcquireJobsAsync(jobsDelta, jobMessages, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);

            throw; // Rethrow to trigger retry
        }
        finally
        {
            semaphore.Release();
        }
    }

    public (int Runners, int Jobs) CalculateDelta(Scale scale, Statistics statistics)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("orchestrator.calculate_delta", tags);
        activity?.SetTag("statistics.total_assigned_jobs", statistics.TotalAssignedJobs);
        activity?.SetTag("statistics.total_registered_runners", statistics.TotalRegisteredRunners);

        var min = scale.MinimumMachines ?? 0;
        var max = scale.MaximumMachines ?? ScaleLimiter.MaximumCapacity;
        var minIdle = scale.MinimumIdleMachines ?? 0;
        activity?.SetTag("configuration.scale.minimum_machines", min);
        activity?.SetTag("configuration.scale.maximum_machines", max);
        activity?.SetTag("configuration.scale.minimum_idle_machines", minIdle);

        var capacityLimit = scaleLimiter.Capacity;
        var concurrencyLimit = scaleLimiter.Concurrency;
        activity?.SetTag("limits.capacity", capacityLimit);
        activity?.SetTag("limits.concurrency", concurrencyLimit);

        var creating = CreatingRunners.Count;
        var registered = RegisteredRunners.Count;
        activity?.SetTag("runners.creating", creating);
        activity?.SetTag("runners.registered", registered);

        var capacity = Math.Min(capacityLimit, max);
        var concurrency = concurrencyLimit - creating;
        var wanted = Math.Max(statistics.TotalAssignedJobs + minIdle, min);
        var allowed = Math.Min(capacity, wanted);
        var existing = Math.Max(statistics.TotalRegisteredRunners, registered);
        var target = allowed - existing;
        var runnersDelta = Math.Min(concurrency, target);
        activity?.SetTag("runners.capacity", capacity);
        activity?.SetTag("runners.concurrency", concurrency);
        activity?.SetTag("runners.wanted", wanted);
        activity?.SetTag("runners.allowed", allowed);
        activity?.SetTag("runners.existing", existing);
        activity?.SetTag("runners.target", target);
        activity?.SetTag("runners.delta", runnersDelta);

        var jobsDelta = Math.Min(capacity, concurrency) - statistics.TotalAssignedJobs;
        activity?.SetTag("jobs.delta", jobsDelta);

        return ((int)runnersDelta, (int)jobsDelta);
    }

    public async Task ScaleAsync(int runnersDelta, List<JobMessage> jobMessages, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("orchestrator.scale", tags);
        activity?.SetTag("runners.delta", runnersDelta);
        activity?.SetTag("job_messages.count", jobMessages.Count);

        var retryAfter = scaleLimiter.RetryAfter > DateTimeOffset.Now;
        activity?.SetTag("limits.retry_after", retryAfter);

        if (!retryAfter)
        {
            for (var i = 0; i < runnersDelta; i++)
            {
                // Run serially to reduce spikes in creation
                // Machine creation isn't awaited so this call completes relatively quickly
                await CreateRunnerAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var offlineRunners = jobMessages.Where(j => j.MessageType == JobMessageType.JobCompleted).Select(j => j.RunnerName).OfType<string>().ToList();
        activity?.SetTag("runners.offline", offlineRunners.Count);

        await Task.WhenAll(offlineRunners.Select(n => DeleteRunnerAsync(n, null, cancellationToken))).ConfigureAwait(false);
    }

    public async Task AcquireJobsAsync(int jobsDelta, List<JobMessage> jobMessages, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("orchestrator.acquire_jobs", tags);
        activity?.SetTag("jobs.delta", jobsDelta);
        activity?.SetTag("job_messages.count", jobMessages.Count);

        AcquirableJobs.EnqueueRange(jobMessages.Where(j => j.MessageType == JobMessageType.JobAvailable).Select(j => j.RunnerRequestId));
        activity?.SetTag("job_messages.queued", AcquirableJobs.Count);

        var acquirableJobs = AcquirableJobs.DequeueRange(jobsDelta).ToList();
        activity?.SetTag("job_messages.acquirable", acquirableJobs.Count);

        if (!acquirableJobs.Any())
            return;

        var session = await lazySession.GetValueAsync(cancellationToken).ConfigureAwait(false);
        var acquiredJobs = await actionsRuntimeClient.AcquireJobsAsync(session.RunnerScaleSet.Id, acquirableJobs, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("job_messages.acquired", acquiredJobs.Count);
    }

    public async Task CreateRunnerAsync(CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("orchestrator.create_runner", tags);

        // Create a JIT configuration and update the creation time before saving since the returned time is incorrect
        var session = await lazySession.GetValueAsync(cancellationToken).ConfigureAwait(false);
        var jitConfiguration = await actionsRuntimeClient.CreateJitConfigurationAsync(session.RunnerScaleSet.Id, cancellationToken).ConfigureAwait(false);
        jitConfiguration = jitConfiguration with { Runner = jitConfiguration.Runner with { CreatedOn = DateTimeOffset.UtcNow } };
        activity?.SetTag("runner.name", jitConfiguration.Runner.Name);
        activity?.SetTag("runner.id", jitConfiguration.Runner.Id);

        if (RegisteredRunners.TryAdd(jitConfiguration.Runner.Name, jitConfiguration.Runner) && CreatingRunners.TryAdd(jitConfiguration.Runner.Name, jitConfiguration.Runner))
        {
            taskMonitor.Track(CreateMachineAsync(jitConfiguration, cancellationToken).ContinueWith(t => CreatingRunners.TryRemove(jitConfiguration.Runner.Name, out _)));
        }
    }

    public async Task DeleteRunnerAsync(string name, long? id, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("orchestrator.delete_runner", tags);
        activity?.SetTag("runner.name", name);
        activity?.SetTag("runner.id", id);

        if (id.HasValue)
        {
            var session = await lazySession.GetValueAsync(cancellationToken).ConfigureAwait(false);
            await actionsRuntimeClient.DeleteRunnerAsync(id.Value, session.RunnerScaleSet.RunnerGroupId, cancellationToken).ConfigureAwait(false);
        }

        taskMonitor.Track(DeleteMachineAsync(name, cancellationToken));
    }

    public async Task CreateMachineAsync(JitConfiguration jitConfiguration, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("orchestrator.create_machine", tags);
        activity?.SetTag("runner.name", jitConfiguration.Runner.Name);
        activity?.SetTag("runner.id", jitConfiguration.Runner.Id);

        try
        {
            await cloudProvider.CreateMachineAsync(jitConfiguration.Runner.Name, cancellationToken).ConfigureAwait(false);
            await cloudProvider.ConfigureMachineAsRunnerAsync(jitConfiguration.Runner.Name, jitConfiguration.EncodedJitConfig, downloadManager.GetRunnerDownloadAsync, cancellationToken).ConfigureAwait(false);

            var createTime = DateTimeOffset.UtcNow - jitConfiguration.Runner.CreatedOn;
            Telemetry.RunnersCreateTime.Record(createTime.TotalMilliseconds, tags);

            scaleLimiter.RecordSuccess(RegisteredRunners.Count);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);

            // Record failure before deleting runner to avoid scaling up in the interim
            scaleLimiter.RecordFailure(exception, RegisteredRunners.Count);

            await DeleteRunnerAsync(jitConfiguration.Runner.Name, jitConfiguration.Runner.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteMachineAsync(string name, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("orchestrator.delete_machine", tags);
        activity?.SetTag("runner.name", name);

        try
        {
            await cloudProvider.DeleteMachineAsync(name, cancellationToken).ConfigureAwait(false);

            if (RegisteredRunners.TryRemove(name, out var runner))
            {
                var runTime = DateTimeOffset.UtcNow - runner.CreatedOn;
                Telemetry.RunnersLifeTime.Record(runTime.TotalMilliseconds, tags);
            }
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
        }
    }

    private async Task<Session> GetSessionAsync(CancellationToken cancellationToken)
    {
        var runnerGroups = await actionsRuntimeClient.GetRunnerGroupsAsync(configuration.Group, cancellationToken).ConfigureAwait(false);
        var runnerGroup = runnerGroups.SingleOrDefault() ?? throw new Exception($"Runner group '{configuration.Group}' not found");

        var runnerScaleSets = await actionsRuntimeClient.GetRunnerScaleSetsAsync(configuration.Name, runnerGroup.Id, cancellationToken).ConfigureAwait(false);
        var runnerScaleSet = runnerScaleSets.SingleOrDefault() ?? await actionsRuntimeClient.CreateRunnerScaleSetAsync(configuration.Name, runnerGroup.Id, cancellationToken).ConfigureAwait(false);

        return await actionsRuntimeClient.CreateSessionAsync(runnerScaleSet.Id, Environment.MachineName, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IActionsSessionClient> GetActionsSessionClientAsync(CancellationToken cancellationToken)
    {
        var session = await lazySession.GetValueAsync(cancellationToken).ConfigureAwait(false);

        return actionsRuntimeClient.CreateActionsSessionClient(session);
    }

    public static async Task RepeatAsync(Func<CancellationToken, Task> func, TimeSpan period, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Delay first because refresh is called once before this method
            await Task.Delay(period, cancellationToken).ConfigureAwait(false);

            try
            {
                await RetryAsync(func, cancellationToken).ConfigureAwait(false);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Failures should be recorded by the callee since there is no activity available here
            }
        }
    }

    public static async Task RetryAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken)
    {
        var failures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await func(cancellationToken).ConfigureAwait(false);

                return;
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Failures should be recorded by the callee since there is no activity available here
            }

            await Task.Delay(ExponentialDelay(failures++, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1)), cancellationToken).ConfigureAwait(false);
        }
    }

    public static TimeSpan ExponentialDelay(int failures, TimeSpan minimum, TimeSpan maximum)
    {
        // Multiply by exponential without exceeding TimeSpan's maximum value
        var exponential = minimum * Math.Min(Math.Pow(2, failures), TimeSpan.MaxValue / minimum);

        return TimeSpan.FromTicks(Math.Min(exponential.Ticks, maximum.Ticks));
    }
}
