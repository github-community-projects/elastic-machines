namespace ElasticMachines.Monolith.Services;

public class BackgroundTaskService : IBackgroundTaskService
{
    private readonly IActivityService<BackgroundTaskService> activityService;

    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly TaskCompletionSource taskCompletionSource = new();

    private int taskCount;

    public BackgroundTaskService(IActivityService<BackgroundTaskService> activityService)
    {
        this.activityService = activityService;
    }

    public async void RunBackgroundTask(Func<CancellationToken, Task> task)
    {
        using var activity = activityService.StartActivity();

        Interlocked.Increment(ref taskCount);

        try
        {
            if (cancellationTokenSource.IsCancellationRequested)
                return;

            // Queue to thread pool to ensure any synchronous portion of the task cannot block the original thread
            await Task.Run(() => task(cancellationTokenSource.Token)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            activity?.AddException(e);
        }
        finally
        {
            if (Interlocked.Decrement(ref taskCount) == 0 && cancellationTokenSource.IsCancellationRequested)
            {
                taskCompletionSource.TrySetResult();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();

        if (taskCount > 0)
        {
            await taskCompletionSource.Task.ConfigureAwait(false);
        }
    }
}
