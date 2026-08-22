namespace ElasticMachines.Utilities;

public sealed class TaskMonitor : IAsyncDisposable
{
    private readonly TaskCompletionSource taskCompletionSource = new();

    // Start at one so count cannot reach zero and trigger completion until disposed
    private int count = 1;

    public TaskMonitor()
    {
    }

    public void Track(Task task)
    {
        if (taskCompletionSource.Task.IsCompleted)
            throw new ObjectDisposedException(nameof(TaskMonitor));

        Interlocked.Increment(ref count);
        task.ContinueWith(t => Decrement());
    }

    public async ValueTask DisposeAsync()
    {
        Decrement();
        await taskCompletionSource.Task.ConfigureAwait(false);
    }

    private void Decrement()
    {
        if (Interlocked.Decrement(ref count) == 0)
        {
            taskCompletionSource.TrySetResult();
        }
    }
}
