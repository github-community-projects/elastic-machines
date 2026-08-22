namespace ElasticMachines.Engine.Octokit.Models;

public record Page<T>
{
    public required List<T> Value { get; init; }

    public static implicit operator List<T>(Page<T> page)
    {
        return page.Value;
    }
}
