using System.Collections.Concurrent;
using ElasticMachines.Engine.Models;
using ElasticMachines.Engine.Octokit;
using ElasticMachines.Engine.Octokit.Models;
using ElasticMachines.Utilities;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ElasticMachines.Engine;

public sealed class DownloadManager : IDownloadManager, IAsyncDisposable
{
    private readonly IActionsRuntimeClient actionsRuntimeClient;
    private readonly ConcurrentDictionary<(Models.OperatingSystem OperatingSystem, Architecture Architecture), AsyncExpiringLazy<RunnerApplication>> runnerApplications;

    public DownloadManager(IActionsRuntimeClient actionsRuntimeClient)
    {
        this.actionsRuntimeClient = actionsRuntimeClient;
        runnerApplications = new();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(runnerApplications.Values.Select(d => d.DisposeAsync().AsTask())).ConfigureAwait(false);
    }

    public async Task<(Uri Url, string? Token)> GetRunnerDownloadAsync(Models.OperatingSystem operatingSystem, Architecture architecture, CancellationToken cancellationToken)
    {
        var lazy = runnerApplications.GetOrAdd((operatingSystem, architecture), t => new(async c => await GetRunnerApplicationAsync(t.OperatingSystem, t.Architecture, c).ConfigureAwait(false)));
        var runnerApplication = await lazy.GetValueAsync(cancellationToken).ConfigureAwait(false);

        return (runnerApplication.DownloadUrl, runnerApplication.Token);
    }

    private async Task<(RunnerApplication, DateTimeOffset)> GetRunnerApplicationAsync(Models.OperatingSystem operatingSystem, Architecture architecture, CancellationToken cancellationToken)
    {
        var platform = (operatingSystem, architecture) switch
        {
            (Models.OperatingSystem.Linux, Architecture.Arm64) => "linux-arm64",
            (Models.OperatingSystem.Linux, Architecture.X64) => "linux-x64",
            (Models.OperatingSystem.Macos, Architecture.Arm64) => "osx-arm64",
            (Models.OperatingSystem.Macos, Architecture.X64) => "osx-x64",
            (Models.OperatingSystem.Windows, Architecture.Arm64) => "win-arm64",
            (Models.OperatingSystem.Windows, Architecture.X64) => "win-x64",
            _ => throw new Exception($"Unknown operating system '{operatingSystem}' or architecture '{architecture}'")
        };

        var runnerApplications = await actionsRuntimeClient.GetRunnerApplicationsAsync(platform, true, 1, cancellationToken).ConfigureAwait(false);

        if (runnerApplications.SingleOrDefault() is not RunnerApplication runnerApplication)
            throw new Exception($"No runner application found for operating system '{operatingSystem}' and architecture '{architecture}'");

        // If the token has an expiration, refresh after half the period to give start scripts plenty of time to download the application
        var expiration = runnerApplication.Token == null ? TimeSpan.FromHours(1) : (new JsonWebToken(runnerApplication.Token).ValidTo - DateTimeOffset.Now) / 2;

        return (runnerApplication, DateTimeOffset.Now + expiration);
    }
}
