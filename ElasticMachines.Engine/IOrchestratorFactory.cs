using ElasticMachines.Engine.Models;
using ElasticMachines.Engine.Octokit;

namespace ElasticMachines.Engine;

public interface IOrchestratorFactory
{
    Task<Orchestrator> CreateOrchestratorAsync(string provider, Configuration configuration, string appId, string privateKey);
    Orchestrator CreateOrchestrator(string provider, Configuration configuration, IActionsRuntimeClient actionsRuntimeClient);
}
