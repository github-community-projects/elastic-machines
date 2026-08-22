using ElasticMachines.Engine.Exceptions;

namespace ElasticMachines.Engine;

public class ScaleLimiter
{
    private readonly object mutex = new();

    // Actions runner group limit
    public const int MaximumCapacity = 10_000;
    public const int MaximumConcurrency = 128;

    public int Capacity { get; private set; } = MaximumCapacity;
    public int Concurrency { get; private set; } = MaximumConcurrency;
    public DateTimeOffset RetryAfter { get; private set; } = DateTimeOffset.Now;

    public ScaleLimiter()
    {
    }

    public void RecordSuccess(int runners)
    {
        lock (mutex)
        {
            // Increase capacity if we are at the limit and successfully created a runner
            Capacity = Math.Min(MaximumCapacity, Math.Max(Capacity, runners + 1));
            Concurrency = Math.Min(MaximumConcurrency, Concurrency * 2);
        }
    }

    public void RecordFailure(Exception exception, int runners)
    {
        lock (mutex)
        {
            if (exception is CapacityException capacityException)
            {
                Capacity = Math.Max(1, capacityException.Capacity ?? runners - 1);
            }
            else if (exception is ThrottleException throttleException)
            {
                RetryAfter = DateTimeOffset.Now + throttleException.RetryAfter;
            }
            else
            {
                Concurrency = Math.Max(1, Concurrency / 2);
            }
        }
    }
}
