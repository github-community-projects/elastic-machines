namespace ElasticMachines.Engine.Models;

public record Scale
{
    public string? CronExpression { get; init; }

    public string? Timezone { get; init; }

    public int? MinimumIdleMachines { get; init; }

    public int? MaximumIdleMachines { get; init; }

    public int? MinimumMachines { get; init; }

    public int? MaximumMachines { get; init; }
}
