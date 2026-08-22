using System.Diagnostics.Metrics;
using AutoFixture;
using ElasticMachines.Engine.Exceptions;
using ElasticMachines.Engine.Models;
using ElasticMachines.Engine.Octokit;
using ElasticMachines.Engine.Octokit.Models;
using Moq;
using Xunit;

namespace ElasticMachines.Engine.Tests;

public class OrchestratorTests
{
    private readonly Fixture fixture = new();
    private readonly Mock<ICloudProvider> cloudProvider = new();
    private readonly Mock<IDownloadManager> downloadManager = new();
    private readonly Mock<IActionsRuntimeClient> actionsRuntimeClient = new();

    private readonly Configuration configuration;
    private readonly Session session;
    private readonly Orchestrator orchestrator;

    public OrchestratorTests()
    {
        configuration = fixture.Create<Configuration>() with { Scale = null };
        session = fixture.Create<Session>();
        orchestrator = new(configuration, cloudProvider.Object, downloadManager.Object, actionsRuntimeClient.Object);

        var meter = new Meter(nameof(OrchestratorTests));
        actionsRuntimeClient.Setup(m => m.GetRunnerGroupsAsync(configuration.Group, It.IsAny<CancellationToken>())).ReturnsAsync(new List<RunnerGroup> { new() { Id = 1 } });
        actionsRuntimeClient.Setup(m => m.GetRunnerScaleSetsAsync(configuration.Name, 1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<RunnerScaleSet> { session.RunnerScaleSet });
        actionsRuntimeClient.Setup(m => m.CreateSessionAsync(session.RunnerScaleSet.Id, Environment.MachineName, It.IsAny<CancellationToken>())).ReturnsAsync(session);
    }

    [Fact]
    public async Task RefreshKeepsOnlineRunners()
    {
        var runner = fixture.Create<Runner>() with { Status = RunnerStatus.Online };
        actionsRuntimeClient.Setup(m => m.GetRunnersAsync(session.RunnerScaleSet.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None)).ReturnsAsync(new List<Runner> { runner });
        actionsRuntimeClient.Setup(m => m.GetAcquirableJobsAsync(session.RunnerScaleSet.Id, It.IsAny<int>(), CancellationToken.None)).ReturnsAsync(new List<JobMessage>());
        cloudProvider.Setup(m => m.GetMachinesAsync(CancellationToken.None)).ReturnsAsync(new List<string>());

        await orchestrator.RefreshAsync(CancellationToken.None);

        actionsRuntimeClient.Verify(m => m.DeleteRunnerAsync(runner.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None), Times.Never);
        cloudProvider.Verify(m => m.DeleteMachineAsync(runner.Name, CancellationToken.None), Times.Never);
        Assert.Single(orchestrator.RegisteredRunners);
    }

    [Fact]
    public async Task RefreshKeepsCreatingRunners()
    {
        var runner = fixture.Create<Runner>() with { Status = RunnerStatus.Offline };
        orchestrator.RegisteredRunners[runner.Name] = runner;
        orchestrator.CreatingRunners[runner.Name] = runner;
        actionsRuntimeClient.Setup(m => m.GetRunnersAsync(session.RunnerScaleSet.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None)).ReturnsAsync(new List<Runner> { runner });
        actionsRuntimeClient.Setup(m => m.GetAcquirableJobsAsync(session.RunnerScaleSet.Id, It.IsAny<int>(), CancellationToken.None)).ReturnsAsync(new List<JobMessage>());
        cloudProvider.Setup(m => m.GetMachinesAsync(CancellationToken.None)).ReturnsAsync(new List<string>());

        await orchestrator.RefreshAsync(CancellationToken.None);

        actionsRuntimeClient.Verify(m => m.DeleteRunnerAsync(runner.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None), Times.Never);
        cloudProvider.Verify(m => m.DeleteMachineAsync(runner.Name, CancellationToken.None), Times.Never);
        Assert.Single(orchestrator.RegisteredRunners);
    }

    [Fact]
    public async Task RefreshDeletesMissingRunners()
    {
        var runner = fixture.Create<Runner>();
        orchestrator.RegisteredRunners[runner.Name] = runner;
        actionsRuntimeClient.Setup(m => m.GetRunnersAsync(session.RunnerScaleSet.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None)).ReturnsAsync(new List<Runner>());
        actionsRuntimeClient.Setup(m => m.GetAcquirableJobsAsync(session.RunnerScaleSet.Id, It.IsAny<int>(), CancellationToken.None)).ReturnsAsync(new List<JobMessage>());
        cloudProvider.Setup(m => m.GetMachinesAsync(CancellationToken.None)).ReturnsAsync(new List<string>());

        await orchestrator.RefreshAsync(CancellationToken.None);

        cloudProvider.Verify(m => m.DeleteMachineAsync(runner.Name, CancellationToken.None), Times.Once);
        Assert.Empty(orchestrator.RegisteredRunners);
    }

    [Fact]
    public async Task RefreshDeletesOfflineRunners()
    {
        var runner = fixture.Create<Runner>() with { Status = RunnerStatus.Offline };
        actionsRuntimeClient.Setup(m => m.GetRunnersAsync(session.RunnerScaleSet.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None)).ReturnsAsync(new List<Runner> { runner });
        actionsRuntimeClient.Setup(m => m.GetAcquirableJobsAsync(session.RunnerScaleSet.Id, It.IsAny<int>(), CancellationToken.None)).ReturnsAsync(new List<JobMessage>());
        cloudProvider.Setup(m => m.GetMachinesAsync(CancellationToken.None)).ReturnsAsync(new List<string>());

        await orchestrator.RefreshAsync(CancellationToken.None);

        actionsRuntimeClient.Verify(m => m.DeleteRunnerAsync(runner.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None), Times.Once);
        cloudProvider.Verify(m => m.DeleteMachineAsync(runner.Name, CancellationToken.None), Times.Once);
        Assert.Empty(orchestrator.RegisteredRunners);
    }

    [Fact]
    public async Task RefreshDeletesOfflineMachines()
    {
        var runnerName = fixture.Create<string>();
        actionsRuntimeClient.Setup(m => m.GetRunnersAsync(session.RunnerScaleSet.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None)).ReturnsAsync(new List<Runner>());
        actionsRuntimeClient.Setup(m => m.GetAcquirableJobsAsync(session.RunnerScaleSet.Id, It.IsAny<int>(), CancellationToken.None)).ReturnsAsync(new List<JobMessage>());
        cloudProvider.Setup(m => m.GetMachinesAsync(CancellationToken.None)).ReturnsAsync(new List<string> { runnerName });

        await orchestrator.RefreshAsync(CancellationToken.None);

        actionsRuntimeClient.Verify(m => m.DeleteRunnerAsync(It.IsAny<long>(), session.RunnerScaleSet.RunnerGroupId, CancellationToken.None), Times.Never);
        cloudProvider.Verify(m => m.DeleteMachineAsync(runnerName, CancellationToken.None), Times.Once);
        Assert.Empty(orchestrator.RegisteredRunners);
    }

    [Fact]
    public async Task RefreshDeletesOnce()
    {
        var runner = fixture.Create<Runner>();
        actionsRuntimeClient.Setup(m => m.GetRunnersAsync(session.RunnerScaleSet.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None)).ReturnsAsync(new List<Runner> { runner });
        actionsRuntimeClient.Setup(m => m.GetAcquirableJobsAsync(session.RunnerScaleSet.Id, It.IsAny<int>(), CancellationToken.None)).ReturnsAsync(new List<JobMessage>());
        cloudProvider.Setup(m => m.GetMachinesAsync(CancellationToken.None)).ReturnsAsync(new List<string> { runner.Name });

        await orchestrator.RefreshAsync(CancellationToken.None);

        actionsRuntimeClient.Verify(m => m.DeleteRunnerAsync(runner.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None), Times.Once);
        cloudProvider.Verify(m => m.DeleteMachineAsync(runner.Name, CancellationToken.None), Times.Once);
        Assert.Empty(orchestrator.RegisteredRunners);
    }

    [Fact]
    public async Task PollDeletesMessage()
    {
        var statistics = fixture.Create<Statistics>() with { TotalAssignedJobs = 0, TotalRegisteredRunners = 0 };
        var message = fixture.Create<Message>() with { Body = [], Statistics = statistics };
        var actionsSessionClient = new Mock<IActionsSessionClient>();
        actionsRuntimeClient.Setup(m => m.CreateActionsSessionClient(session)).Returns(actionsSessionClient.Object);
        actionsSessionClient.Setup(m => m.GetMessageAsync(It.IsAny<int>(), CancellationToken.None)).ReturnsAsync(message);

        await orchestrator.PollAsync(CancellationToken.None);

        actionsSessionClient.Verify(m => m.DeleteMessageAsync(message.MessageId, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ScaleCreatesNeededRunners()
    {
        var runner = fixture.Create<Runner>();
        var runnerApplication = fixture.Create<RunnerApplication>() with { Token = null };
        actionsRuntimeClient.Setup(m => m.GetRunnerApplicationsAsync("linux-arm64", true, 1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<RunnerApplication> { runnerApplication });
        actionsRuntimeClient.Setup(m => m.CreateJitConfigurationAsync(session.RunnerScaleSet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new JitConfiguration { Runner = runner, EncodedJitConfig = "" });

        await orchestrator.ScaleAsync(1, new(), CancellationToken.None);

        cloudProvider.Verify(m => m.CreateMachineAsync(runner.Name, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScaleDeletesOfflineRunners()
    {
        var jobMessage = fixture.Create<JobMessage>() with { MessageType = JobMessageType.JobCompleted, RunnerName = "runner-1" };

        await orchestrator.ScaleAsync(0, new() { jobMessage }, CancellationToken.None);

        cloudProvider.Verify(m => m.DeleteMachineAsync(jobMessage.RunnerName, CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(0, 2, 0, 1, 0, 1, 1)] // Meet demand
    [InlineData(1, 2, 0, 0, 0, 1, 2)] // Meet minimum
    [InlineData(0, 2, 1, 0, 0, 1, 2)] // Meet minimum idle
    [InlineData(2, 1, 2, 2, 1, 0, -1)] // Never exceed maximum
    [InlineData(0, 2, 0, 1, 1, 0, 1)] // Respect existing runners
    [InlineData(0, 2, 0, 0, 1, -1, 2)] // Over provisioned
    [InlineData(0, 129, 0, 129, 0, 128, -1)] // Respect concurrency limit
    public void CalculateDelta(int min, int max, int minIdle, long totalAssignedJobs, long totalRegisteredRunners, int expectedRunners, int expectedJobs)
    {
        var scale = new Scale
        {
            MinimumMachines = min,
            MaximumMachines = max,
            MinimumIdleMachines = minIdle,
        };

        var statistics = fixture.Create<Statistics>() with
        {
            TotalAssignedJobs = totalAssignedJobs,
            TotalRegisteredRunners = totalRegisteredRunners
        };

        var (runnersDelta, jobsDelta) = orchestrator.CalculateDelta(scale, statistics);

        Assert.Equal(expectedRunners, runnersDelta);
        Assert.Equal(expectedJobs, jobsDelta);
    }

    [Fact]
    public async Task AcquireJobsDeduplicates()
    {
        var jobId = fixture.Create<long>();

        await orchestrator.AcquireJobsAsync(0, new() { fixture.Create<JobMessage>() with { RunnerRequestId = jobId } }, CancellationToken.None);
        await orchestrator.AcquireJobsAsync(0, new() { fixture.Create<JobMessage>() with { RunnerRequestId = jobId } }, CancellationToken.None);

        Assert.Equal(1, orchestrator.AcquirableJobs.Count);
        actionsRuntimeClient.Verify(m => m.AcquireJobsAsync(session.RunnerScaleSet.Id, It.IsAny<List<long>>(), CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task AcquireJobsOnlyAvailable()
    {
        var availableJob = fixture.Create<JobMessage>() with { MessageType = JobMessageType.JobAvailable };
        var assignedJob = fixture.Create<JobMessage>() with { MessageType = JobMessageType.JobAssigned };

        await orchestrator.AcquireJobsAsync(2, new() { availableJob, assignedJob }, CancellationToken.None);

        Assert.Equal(0, orchestrator.AcquirableJobs.Count);
        actionsRuntimeClient.Verify(m => m.AcquireJobsAsync(session.RunnerScaleSet.Id, It.Is<List<long>>(l => l.Single() == availableJob.RunnerRequestId), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task AcquireJobsRespectsDelta()
    {
        var jobMessages = new List<JobMessage>
        {
            fixture.Create<JobMessage>() with { MessageType = JobMessageType.JobAvailable },
            fixture.Create<JobMessage>() with { MessageType = JobMessageType.JobAvailable },
        };

        await orchestrator.AcquireJobsAsync(1, jobMessages, CancellationToken.None);

        Assert.Equal(1, orchestrator.AcquirableJobs.Count);
        actionsRuntimeClient.Verify(m => m.AcquireJobsAsync(session.RunnerScaleSet.Id, It.Is<List<long>>(l => l.Single() == jobMessages.First().RunnerRequestId), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task AcquireJobsSkipWhenNoneAvailable()
    {
        var jobMessage = fixture.Create<JobMessage>() with { MessageType = JobMessageType.JobCompleted };

        await orchestrator.AcquireJobsAsync(1, new() { jobMessage }, CancellationToken.None);

        Assert.Equal(0, orchestrator.AcquirableJobs.Count);
        actionsRuntimeClient.Verify(m => m.AcquireJobsAsync(session.RunnerScaleSet.Id, It.IsAny<List<long>>(), CancellationToken.None), Times.Never);
    }

    [Fact]
    public async Task CreateRunnerTracksCreatingAndRegisteredRunners()
    {
        var jitConfiguration = fixture.Create<JitConfiguration>();
        var taskCompletionSource = new TaskCompletionSource<string>();
        actionsRuntimeClient.Setup(m => m.CreateJitConfigurationAsync(session.RunnerScaleSet.Id, CancellationToken.None)).ReturnsAsync(jitConfiguration);
        cloudProvider.Setup(m => m.CreateMachineAsync(jitConfiguration.Runner.Name, CancellationToken.None)).Returns(taskCompletionSource.Task);

        await orchestrator.CreateRunnerAsync(CancellationToken.None);

        Assert.Single(orchestrator.CreatingRunners);
        Assert.Single(orchestrator.RegisteredRunners);

        taskCompletionSource.SetResult(jitConfiguration.Runner.Name);
        await orchestrator.DisposeAsync(); // Wait for the task continuation to complete

        Assert.Empty(orchestrator.CreatingRunners);
        Assert.Single(orchestrator.RegisteredRunners);
    }

    [Fact]
    public async Task DeleteRunnerRemovesFromActionsAndCloudProvider()
    {
        var runner = fixture.Create<Runner>();
        orchestrator.RegisteredRunners[runner.Name] = runner;

        await orchestrator.DeleteRunnerAsync(runner.Name, runner.Id, CancellationToken.None);

        Assert.Empty(orchestrator.RegisteredRunners);
        actionsRuntimeClient.Verify(m => m.DeleteRunnerAsync(runner.Id, session.RunnerScaleSet.RunnerGroupId, CancellationToken.None), Times.Once);
        cloudProvider.Verify(m => m.DeleteMachineAsync(runner.Name, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task CreateMachineSetsCapacity()
    {
        var jitConfiguration = fixture.Create<JitConfiguration>();
        orchestrator.RegisteredRunners[jitConfiguration.Runner.Name + "1"] = jitConfiguration.Runner;
        orchestrator.RegisteredRunners[jitConfiguration.Runner.Name + "2"] = jitConfiguration.Runner;
        cloudProvider.Setup(m => m.CreateMachineAsync(jitConfiguration.Runner.Name, CancellationToken.None)).ThrowsAsync(new CapacityException(new(), 1));

        await orchestrator.CreateMachineAsync(jitConfiguration, CancellationToken.None);

        cloudProvider.Verify(m => m.CreateMachineAsync(jitConfiguration.Runner.Name, CancellationToken.None), Times.Once);

        orchestrator.RegisteredRunners.Clear();
        var (runnersDelta, jobsDelta) = orchestrator.CalculateDelta(new(), fixture.Create<Statistics>() with { TotalAssignedJobs = 2, TotalRegisteredRunners = 0 });
        Assert.Equal(1, runnersDelta);
        Assert.Equal(-1, jobsDelta);
    }

    [Fact]
    public async Task RepeatHandlesErrors()
    {
        var executions = 0;
        using var cancellationTokenSource = new CancellationTokenSource();

        Task func(CancellationToken cancellationToken)
        {
            if (executions++ > 0)
            {
                cancellationTokenSource.Cancel();
            }

            return Task.FromException(new Exception());
        }

        await Assert.ThrowsAsync<Exception>(() => Orchestrator.RepeatAsync(func, TimeSpan.Zero, cancellationTokenSource.Token));
        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task RepeatStopsWhenCancelled()
    {
        var executions = 0;
        using var cancellationTokenSource = new CancellationTokenSource();

        Task func(CancellationToken cancellationToken)
        {
            if (executions++ > 0)
            {
                cancellationTokenSource.Cancel();
            }

            return Task.CompletedTask;
        }

        await Orchestrator.RepeatAsync(func, TimeSpan.Zero, cancellationTokenSource.Token);
        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task RetryHandlesErrors()
    {
        var executions = 0;

        await Orchestrator.RetryAsync(c => executions++ > 0 ? Task.CompletedTask : Task.FromException(new()), CancellationToken.None);

        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task RetryStopsWhenCancelled()
    {
        var executions = 0;
        using var cancellationTokenSource = new CancellationTokenSource();

        Task func(CancellationToken cancellationToken)
        {
            if (executions++ > 0)
            {
                cancellationTokenSource.Cancel();
            }

            return Task.FromException(new Exception());
        }

        await Assert.ThrowsAsync<Exception>(() => Orchestrator.RetryAsync(func, cancellationTokenSource.Token));

        Assert.Equal(2, executions);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 10)]
    public void ExponentialDelay(int failures, int expected)
    {
        var delay = Orchestrator.ExponentialDelay(failures, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));

        Assert.Equal(expected, delay.TotalSeconds);
    }
}
