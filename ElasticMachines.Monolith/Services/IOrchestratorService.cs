
using ElasticMachines.Monolith.Models;

namespace ElasticMachines.Monolith.Services;

public interface IOrchestratorService
{
    Task HandleInstallationAsync(InstallationEvent installationPayload, CancellationToken cancellationToken);
    Task HandleInstallationRepositoriesAsync(InstallationRepositoriesEvent installationRepositoriesPayload, CancellationToken cancellationToken);
    Task HandlePushAsync(PushEvent pushPayload, CancellationToken cancellationToken);
    Task RefreshAsync(CancellationToken cancellationToken);
    Task ValidatePermissionsAsync(CancellationToken cancellationToken);
}
