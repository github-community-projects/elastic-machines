namespace ElasticMachines.Engine.Octokit.Models;

public record Statistics
{
    public required long TotalAssignedJobs { get; init; }
    public required long TotalRunningJobs { get; init; }
    public required long TotalRegisteredRunners { get; init; }
    public required long TotalBusyRunners { get; init; }
    public required long TotalIdleRunners { get; init; }
}
