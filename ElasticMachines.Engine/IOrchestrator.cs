namespace ElasticMachines.Engine;

public interface IOrchestrator : IAsyncDisposable
{
    Task RunAsync(CancellationToken cancellationToken);
}
