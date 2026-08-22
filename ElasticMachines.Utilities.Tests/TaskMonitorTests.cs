using Xunit;

namespace ElasticMachines.Utilities.Tests;

public class TaskMonitorTests
{
    private readonly TaskMonitor taskMonitor = new();

    public TaskMonitorTests()
    {
    }

    [Fact]
    public async Task DisposeBeforeUse()
    {
        await taskMonitor.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => taskMonitor.Track(Task.CompletedTask));
    }

    [Fact]
    public async Task DisposeAfterUse()
    {
        taskMonitor.Track(Task.CompletedTask);

        await taskMonitor.DisposeAsync();
    }

    [Fact]
    public async Task WaitForCompletion()
    {
        var taskCompletionSource = new TaskCompletionSource();
        taskMonitor.Track(taskCompletionSource.Task);

        var task = taskMonitor.DisposeAsync();
        Assert.False(task.IsCompleted);

        taskCompletionSource.SetResult();
        await task;
    }

    [Fact]
    public async Task WaitForMany()
    {
        Parallel.ForEach(Enumerable.Range(0, 100), i => taskMonitor.Track(Task.Delay(i)));

        await taskMonitor.DisposeAsync();
    }
}
