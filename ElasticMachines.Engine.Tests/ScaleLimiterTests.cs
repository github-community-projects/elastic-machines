using ElasticMachines.Engine.Exceptions;
using Xunit;

namespace ElasticMachines.Engine.Tests;

public class ScaleLimiterTests
{
    private readonly ScaleLimiter scaleLimiter = new();

    public ScaleLimiterTests()
    {
    }

    [Fact]
    public void SuccessStaysWithinLimits()
    {
        scaleLimiter.RecordSuccess(10_001);

        Assert.Equal(ScaleLimiter.MaximumCapacity, scaleLimiter.Capacity);
        Assert.Equal(ScaleLimiter.MaximumConcurrency, scaleLimiter.Concurrency);
    }

    [Fact]
    public void SuccessIncreasesLimits()
    {
        scaleLimiter.RecordFailure(new CapacityException(new()), 1); // Decrease capacity
        scaleLimiter.RecordFailure(new Exception(), 1); // Decrease concurrency

        Assert.Equal(1, scaleLimiter.Capacity);
        Assert.Equal(64, scaleLimiter.Concurrency);

        scaleLimiter.RecordSuccess(1); // Increase

        Assert.Equal(2, scaleLimiter.Capacity);
        Assert.Equal(128, scaleLimiter.Concurrency);
    }

    [Fact]
    public void CapacityExceptionTakesPrecedence()
    {
        scaleLimiter.RecordFailure(new CapacityException(new(), 1), 3);

        Assert.Equal(1, scaleLimiter.Capacity);
    }

    [Fact]
    public void CapacityExceptionDefaultsToCount()
    {
        scaleLimiter.RecordFailure(new CapacityException(new()), 3);

        Assert.Equal(2, scaleLimiter.Capacity);
    }

    [Fact]
    public void CapacityExceptionStaysWithinLimits()
    {
        scaleLimiter.RecordFailure(new CapacityException(new()), 0);

        Assert.Equal(1, scaleLimiter.Capacity);
    }

    [Fact]
    public void ExceptionStaysWithinLimits()
    {
        for (var i = 0; i < 8; i++)
        {
            scaleLimiter.RecordFailure(new Exception(), 0);
        }

        Assert.Equal(1, scaleLimiter.Concurrency);
    }

    [Fact]
    public void ThrottleExceptionSetsRetryAfter()
    {
        for (var i = 0; i < 8; i++)
        {
            scaleLimiter.RecordFailure(new ThrottleException(TimeSpan.FromSeconds(1), new()), 0);
        }

        Assert.True(scaleLimiter.RetryAfter > DateTimeOffset.Now);
    }
}
