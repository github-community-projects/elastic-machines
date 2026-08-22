namespace ElasticMachines.Engine.Exceptions;

public class ThrottleException : Exception
{
    public TimeSpan RetryAfter { get; }

    public ThrottleException(TimeSpan retryAfter, Exception innerException)
        : base(innerException.Message, innerException)
    {
        RetryAfter = retryAfter;
    }
}
