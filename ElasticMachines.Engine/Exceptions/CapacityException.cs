namespace ElasticMachines.Engine.Exceptions;

public class CapacityException : Exception
{
    public int? Capacity { get; }

    public CapacityException(Exception innerException, int? capacity = null)
        : base(innerException.Message, innerException)
    {
        Capacity = capacity;
    }
}
