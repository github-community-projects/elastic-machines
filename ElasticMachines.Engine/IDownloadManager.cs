using ElasticMachines.Engine.Models;

namespace ElasticMachines.Engine;

public interface IDownloadManager
{
    Task<(Uri Url, string? Token)> GetRunnerDownloadAsync(Models.OperatingSystem operatingSystem, Architecture architecture, CancellationToken cancellationToken);
}
