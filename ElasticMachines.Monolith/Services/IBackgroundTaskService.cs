namespace ElasticMachines.Monolith.Services;

public interface IBackgroundTaskService : IAsyncDisposable
{
    void RunBackgroundTask(Func<CancellationToken, Task> task);
}
