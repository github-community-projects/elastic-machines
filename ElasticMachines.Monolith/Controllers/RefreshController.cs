using ElasticMachines.Monolith.Services;
using Microsoft.AspNetCore.Mvc;

namespace ElasticMachines.Monolith.Controllers;

[ApiController]
[Route("refresh")]
public class RefreshController : ControllerBase
{
    private readonly IBackgroundTaskService backgroundTaskService;
    private readonly IOrchestratorService orchestratorService;

    public RefreshController(IBackgroundTaskService backgroundTaskService, IOrchestratorService orchestratorService)
    {
        this.backgroundTaskService = backgroundTaskService;
        this.orchestratorService = orchestratorService;
    }

    [HttpPost]
    public IActionResult Post()
    {
        backgroundTaskService.RunBackgroundTask(orchestratorService.RefreshAsync);

        return Ok();
    }
}
