namespace ElasticMachines.Engine;

public abstract class CloudProvider<TConfiguration>(TConfiguration configuration, Dictionary<string, string> tags) : ICloudProvider
{
    protected TConfiguration Configuration { get; } = configuration;
    protected Dictionary<string, string> Tags { get; } = tags;

    public abstract Task<List<string>> GetMachinesAsync(CancellationToken cancellationToken);
    public abstract Task<string> CreateMachineAsync(string name, CancellationToken cancellationToken);
    public abstract Task ConfigureMachineAsRunnerAsync(string name, string jitConfiguration, GetRunnerDownloadAsync getRunnerDownloadAsync, CancellationToken cancellationToken);
    public abstract Task DeleteMachineAsync(string name, CancellationToken cancellationToken);
}
