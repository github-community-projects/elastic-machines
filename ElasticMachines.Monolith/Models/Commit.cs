namespace ElasticMachines.Monolith.Models;

public class Commit
{
    public required List<string> Added { get; init; }
    public required List<string> Removed { get; init; }
    public required List<string> Modified { get; init; }
}
