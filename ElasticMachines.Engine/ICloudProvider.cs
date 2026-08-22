using ElasticMachines.Engine.Models;

namespace ElasticMachines.Engine;

public delegate Task<(Uri Url, string? Token)> GetRunnerDownloadAsync(Models.OperatingSystem operatingSystem, Architecture architecture, CancellationToken cancellationToken);

public interface ICloudProvider
{
    Task<List<string>> GetMachinesAsync(CancellationToken cancellationToken);
    Task<string> CreateMachineAsync(string name, CancellationToken cancellationToken);
    Task ConfigureMachineAsRunnerAsync(string name, string jitConfiguration, GetRunnerDownloadAsync getRunnerDownloadAsync, CancellationToken cancellationToken);
    Task DeleteMachineAsync(string name, CancellationToken cancellationToken);
}
