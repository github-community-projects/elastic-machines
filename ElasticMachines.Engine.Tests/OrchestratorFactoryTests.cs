using AutoFixture;
using ElasticMachines.Engine.Models;
using ElasticMachines.Engine.Octokit;
using Moq;
using Xunit;

namespace ElasticMachines.Engine.Tests;

public class OrchestratorFactoryTests
{
    private readonly Fixture fixture = new();
    private readonly Mock<IActionsRuntimeClient> actionsRuntimeClient = new();

    private readonly OrchestratorFactory orchestratorFactory;

    public OrchestratorFactoryTests()
    {
        orchestratorFactory = new();
    }

    [Fact]
    public void CreateTestProvider()
    {
        var configuration = new Configuration
        {
            Name = fixture.Create<string>(),
            Url = fixture.Create<Uri>(),
            With = new TestProviderConfiguration()
        };

        orchestratorFactory.CreateOrchestrator("test-provider", configuration, actionsRuntimeClient.Object);
    }

    [Fact]
    public async Task ThrowWhenProviderTypeDoesNotExist()
    {
        var configuration = new Configuration
        {
            Name = fixture.Create<string>(),
            Url = fixture.Create<Uri>(),
            With = new TestProviderConfiguration()
        };

        Assert.Throws<Exception>(() => orchestratorFactory.CreateOrchestrator("not-a-provider", configuration, actionsRuntimeClient.Object));
    }

    [Fact]
    public void ThrowWhenProviderWithEmpty()
    {
        var configuration = new Configuration
        {
            Name = fixture.Create<string>(),
            Url = fixture.Create<Uri>(),
        };

        Assert.Throws<Exception>(() => orchestratorFactory.CreateOrchestrator("test-provider", configuration, actionsRuntimeClient.Object));
    }

    [CloudProvider("test-provider")]
    public class TestProvider(TestProviderConfiguration configuration, Dictionary<string, string> tags) : CloudProvider<TestProviderConfiguration>(configuration, tags)
    {
        public override Task<List<string>> GetMachinesAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public override Task<string> CreateMachineAsync(string name, CancellationToken cancellationToken) => throw new NotImplementedException();
        public override Task ConfigureMachineAsRunnerAsync(string name, string jitConfiguration, GetRunnerDownloadAsync getRunnerDownloadAsync, CancellationToken cancellationToken) => throw new NotImplementedException();
        public override Task DeleteMachineAsync(string name, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    public class TestProviderConfiguration { }
}
