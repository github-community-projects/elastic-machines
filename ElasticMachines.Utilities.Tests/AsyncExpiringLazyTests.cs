using Xunit;

namespace ElasticMachines.Utilities.Tests;

public class AsyncExpiringLazyTests
{
    public AsyncExpiringLazyTests()
    {
    }

    [Fact]
    public void NoCreateIfNotNeeded()
    {
        var executions = 0;
        _ = new AsyncExpiringLazy<int>(c => Task.FromResult(executions++));

        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task OnlyCreateOnce()
    {
        var executions = 0;
        var lazy = new AsyncExpiringLazy<int>(c => Task.FromResult(executions++));

        await Parallel.ForEachAsync(Enumerable.Range(0, 100), async (_, c) => await lazy.GetValueAsync(c));

        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task RecreateOnFailure()
    {
        var executions = 0;
        var lazy = new AsyncExpiringLazy<int>(c => Task.FromException<int>(new Exception(executions++.ToString())));

        var exception1 = await Assert.ThrowsAsync<Exception>(async () => await lazy.GetValueAsync(CancellationToken.None));
        var exception2 = await Assert.ThrowsAsync<Exception>(async () => await lazy.GetValueAsync(CancellationToken.None));

        Assert.Equal("0", exception1.Message);
        Assert.Equal("1", exception2.Message);
        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task RecreateOnCancellation()
    {
        var executions = 0;
        var lazy = new AsyncExpiringLazy<int>(c => { executions++; return Task.FromCanceled<int>(new CancellationToken(true)); });

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await lazy.GetValueAsync(CancellationToken.None));
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await lazy.GetValueAsync(CancellationToken.None));

        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task RecreateAfterInitializedExpiration()
    {
        var executions = 0;
        var lazy = new AsyncExpiringLazy<int>(c => Task.FromResult(executions++), TimeSpan.FromMilliseconds(20));

        await Parallel.ForEachAsync(Enumerable.Range(0, 100), async (_, c) => await lazy.GetValueAsync(c));
        await Task.Delay(30);
        await Parallel.ForEachAsync(Enumerable.Range(0, 100), async (_, c) => await lazy.GetValueAsync(c));

        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task RecreateAfterReturnedExpiration()
    {
        var executions = 0;
        var lazy = new AsyncExpiringLazy<int>(c => Task.FromResult((executions++, DateTimeOffset.Now.AddMilliseconds(20))));

        await Parallel.ForEachAsync(Enumerable.Range(0, 100), async (_, c) => await lazy.GetValueAsync(c));
        await Task.Delay(30);
        await Parallel.ForEachAsync(Enumerable.Range(0, 100), async (_, c) => await lazy.GetValueAsync(c));

        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task RecreateAfterBuffer()
    {
        var executions = 0;
        var lazy = new AsyncExpiringLazy<int>(c => Task.FromResult((executions++, DateTimeOffset.Now.AddMilliseconds(40))), TimeSpan.FromMilliseconds(20));

        await lazy.GetValueAsync(CancellationToken.None);
        await Task.Delay(30);
        await lazy.GetValueAsync(CancellationToken.None);

        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task RecreateWhenForced()
    {
        var executions = 0;
        var lazy = new AsyncExpiringLazy<int>(c => Task.FromResult(executions++));

        await lazy.GetValueAsync(true, CancellationToken.None);
        await lazy.GetValueAsync(true, CancellationToken.None);

        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task DisposeCancelsTask()
    {
        var lazy = new AsyncExpiringLazy<int>(async c => { await Task.Delay(TimeSpan.FromSeconds(1), c); return 0; });

        var task = lazy.GetValueAsync(CancellationToken.None);

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await lazy.DisposeAsync());
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await lazy.GetValueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DisposeDisposesValue()
    {
        var lazy = new AsyncExpiringLazy<Disposable>(c => Task.FromResult(new Disposable()));
        var disposable = await lazy.GetValueAsync(CancellationToken.None);

        await lazy.DisposeAsync();

        Assert.True(disposable.IsDisposed);
    }

    private class Disposable : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return default;
        }
    }
}
