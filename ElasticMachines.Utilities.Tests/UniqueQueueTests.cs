using Xunit;

namespace ElasticMachines.Utilities.Tests;

public class UniqueQueueTests
{
    private readonly UniqueQueue<object> uniqueQueue = new();

    public UniqueQueueTests()
    {
    }

    [Fact]
    public void EnqueueUnique()
    {
        uniqueQueue.EnqueueRange(new[] { "foo", "bar" });

        Assert.Equal(2, uniqueQueue.Count);
        Assert.Equal(uniqueQueue.DequeueRange(2), new[] { "foo", "bar" });
    }

    [Fact]
    public void EnqueueDuplicate()
    {
        uniqueQueue.EnqueueRange(new[] { "foo", "foo" });

        Assert.Equal(1, uniqueQueue.Count);
        Assert.Equal(uniqueQueue.DequeueRange(2), new[] { "foo" });
    }

    [Fact]
    public void DequeueEmpty()
    {
        uniqueQueue.Enqueue("foo");

        Assert.Equal(1, uniqueQueue.Count);
        Assert.True(uniqueQueue.TryDequeue(out var item));
        Assert.False(uniqueQueue.TryDequeue(out _));
        Assert.Equal("foo", item);
    }
}
