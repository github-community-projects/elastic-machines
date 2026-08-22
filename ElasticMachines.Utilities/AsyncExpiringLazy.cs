namespace ElasticMachines.Utilities;

public sealed class AsyncExpiringLazy<T>(Func<CancellationToken, Task<(T Value, DateTimeOffset Expiration)>> factory) : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Func<CancellationToken, Task<(T Value, DateTimeOffset Expiration)>> factory = factory;

    private volatile Task<(T Value, DateTimeOffset Expiration)>? task;

    public AsyncExpiringLazy(Func<CancellationToken, Task<T>> factory)
        : this(async c => (await factory(c).ConfigureAwait(false), DateTimeOffset.MaxValue))
    {
    }

    public AsyncExpiringLazy(Func<CancellationToken, Task<T>> factory, TimeSpan expiration)
        : this(async c => (await factory(c).ConfigureAwait(false), DateTimeOffset.Now + expiration))
    {
    }

    public AsyncExpiringLazy(Func<CancellationToken, Task<(T Value, DateTimeOffset Expiration)>> factory, TimeSpan buffer)
        : this(async c => { var (value, expiration) = await factory(c).ConfigureAwait(false); return (value, expiration - buffer); })
    {
    }

    public bool IsValueCreated => task?.IsCompletedSuccessfully ?? false;

    public async Task<T> GetValueAsync(CancellationToken cancellationToken)
    {
        return await GetValueAsync(false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> GetValueAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var task = this.task;

        if (forceRefresh || task == null || task.IsFaulted || task.IsCanceled || (task.IsCompleted && task.Result.Expiration < DateTimeOffset.Now))
        {
            lock (factory)
            {
                // Replace the task unless another thread has already done so
                if (this.task == null || this.task == task)
                {
                    this.task = factory(cancellationTokenSource.Token);
                }

                task = this.task;
            }
        }

        return (await task.WaitAsync(cancellationToken).ConfigureAwait(false)).Value;
    }

    public async ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();

        if (task == null)
            return;

        var (value, _) = await task.ConfigureAwait(false);

        if (value is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
