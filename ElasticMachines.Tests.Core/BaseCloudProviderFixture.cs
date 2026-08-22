using ElasticMachines.Engine;
using ElasticMachines.Engine.Models;
using ElasticMachines.Engine.Octokit;
using ElasticMachines.Engine.Octokit.Models;
using ElasticMachines.Utilities;
using Microsoft.Extensions.Configuration;
using Octokit;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ElasticMachines.Tests.Core;

public abstract class BaseCloudProviderFixture : IAsyncLifetime
{
    private IActionsSelfHostedRunnersClient? githubRunnersClient;
    private IActionsRuntimeClient? actionsRuntimeClient;
    private List<Octokit.RunnerApplication>? runnerDownloads;
    private RunnerScaleSet? runnerScaleSet;
    private static readonly SemaphoreSlim semaphore = new(1);

    public string TestSuiteName { get; }
    public Dictionary<string, string> Tags { get; }

    protected IConfigurationSection Settings { get; }
    protected IDeserializer YamlDeserializer;

    protected static IConfiguration AppSettings => appSettingsLazy.Value;
    private static readonly Lazy<IConfiguration> appSettingsLazy =
        new(() =>
            new ConfigurationBuilder()
                .AddJsonFile("appsettings.Test.json", true)
                .AddEnvironmentVariables()
                .Build()
        );

    public BaseCloudProviderFixture(string settingsSectionKey)
    {
        Settings = AppSettings.GetSection(settingsSectionKey);
        YamlDeserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();

        TestSuiteName = AppSettings["TEST_SUITE_NAME"] ?? $"{GenerateUniqueName(Environment.UserName)}";

        Tags = new() { { "test-suite-name", TestSuiteName } };
    }

    public virtual async Task InitializeAsync()
    {
        await semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            var appId = AppSettings["APP_ID"].GetValue();
            var privateKey = AppSettings["PRIVATE_KEY"].GetValue();
            var account = AppSettings["ACCOUNT"].GetValue();
            var repoName = AppSettings["REPOSITORY"].GetValue();

            var appClient = new AppClient(appId, privateKey);
            var installation = (await appClient.GetAllInstallationsForCurrent().ConfigureAwait(false)).First(i => i.Account?.Login == account);

            var installationClient = new InstallationClient(installation.Id, appClient);
            var repo = await installationClient.Repository.Get(account, repoName).ConfigureAwait(false);

            Tags.Add("elastic-machines-repository-id", repo.Id.ToString());
            Tags.Add("elastic-machines-account-id", installation.Account.Id.ToString());
            Tags.Add("elastic-machines-app-id", appId.ToString());

            githubRunnersClient = installationClient.Actions.SelfHostedRunners;
            actionsRuntimeClient = new ActionsRuntimeClient(new Uri(repo.HtmlUrl), installationClient);
            runnerDownloads = (await githubRunnersClient.ListAllRunnerApplicationsForRepository(account, repoName, new ApiOptions() { PageSize = 100 }).ConfigureAwait(false)).ToList();

            var runnerScaleSets = await actionsRuntimeClient.GetRunnerScaleSetsAsync("integration-tests", 1, CancellationToken.None).ConfigureAwait(false);
            runnerScaleSet = runnerScaleSets.FirstOrDefault() ?? await actionsRuntimeClient.CreateRunnerScaleSetAsync("integration-tests", 1, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;

    public TConfig DeserializeAsConfiguration<TConfig>(string configYaml)
    {
        var providerConfig = YamlDeserializer.Deserialize<Configuration>(configYaml) ?? throw new InvalidOperationException("Failed to deserialize configuration");
        return providerConfig.WithAs<TConfig>();
    }

    public Task<(Uri Url, string? Token)> GetRunnerDownloadAsync(ElasticMachines.Engine.Models.OperatingSystem operatingSystem, Architecture architecture, CancellationToken cancellationToken)
    {
        var downloadOs = operatingSystem switch
        {
            ElasticMachines.Engine.Models.OperatingSystem.Linux => "linux",
            ElasticMachines.Engine.Models.OperatingSystem.Windows => "win",
            ElasticMachines.Engine.Models.OperatingSystem.Macos => "osx",
            _ => throw new Exception($"Unknown operating system '{operatingSystem}'")
        };

        var downloadArchitecture = architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => throw new Exception($"Unknown architecture '{architecture}'")
        };

        var download = runnerDownloads.GetValue().Single(d => d.Os == downloadOs && d.Architecture == downloadArchitecture);

        return Task.FromResult<(Uri, string?)>((new Uri(download.DownloadUrl.GetValue()), download.TempDownloadToken));
    }

    public async Task<string> CreateRunnerAsync<T>(CloudProvider<T> provider)
    {
        var jitConfiguration = await actionsRuntimeClient.GetValue().CreateJitConfigurationAsync(runnerScaleSet.GetValue().Id, CancellationToken.None).ConfigureAwait(false);

        var name = await provider.CreateMachineAsync(GenerateUniqueName(), CancellationToken.None).ConfigureAwait(false);
        await provider.ConfigureMachineAsRunnerAsync(name, jitConfiguration.EncodedJitConfig, GetRunnerDownloadAsync, CancellationToken.None).ConfigureAwait(false);

        return name;
    }

    public string GenerateUniqueName(string? baseName = null)
    {
        return $"{baseName ?? TestSuiteName}-{Random.Shared.Next(int.MaxValue):x8}";
    }
}
