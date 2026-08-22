namespace ElasticMachines.Monolith;

public record Configuration : Engine.Models.Configuration
{
    public required string Provider { get; init; }

    public virtual bool Equals(Configuration? other)
    {
        return other is not null
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Group, other.Group, StringComparison.OrdinalIgnoreCase)
            && Url == other.Url;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name.ToLowerInvariant(), Group.ToLowerInvariant(), Url);
    }
}
