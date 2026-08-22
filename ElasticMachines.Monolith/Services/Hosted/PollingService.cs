namespace ElasticMachines.Monolith.Services.Hosted;

public class PollingService : BackgroundService
{
    private readonly AppSettings appSettings;
    private readonly IBackgroundTaskService backgroundTaskService;
    private readonly IOrchestratorService orchestratorService;
    private readonly IActivityService<PollingService> activityService;

    public PollingService(AppSettings appSettings, IBackgroundTaskService backgroundTaskService, IOrchestratorService orchestratorService, IActivityService<PollingService> activityService)
    {
        this.appSettings = appSettings;
        this.backgroundTaskService = backgroundTaskService;
        this.orchestratorService = orchestratorService;
        this.activityService = activityService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await orchestratorService.ValidatePermissionsAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var activity = activityService.StartActivity())
            {
                try
                {
                    backgroundTaskService.RunBackgroundTask(orchestratorService.RefreshAsync);
                }
                catch (Exception e)
                {
                    activity?.AddException(e);
                }
            }

            await Task.Delay(appSettings.PollingInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
