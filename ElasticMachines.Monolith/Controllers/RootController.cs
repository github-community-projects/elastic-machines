using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElasticMachines.Monolith.Models;
using ElasticMachines.Monolith.Services;
using Microsoft.AspNetCore.Mvc;
using Octokit.Internal;

namespace ElasticMachines.Monolith.Controllers;

[ApiController]
[Route("")]
public class RootController : ControllerBase
{
    private readonly AppSettings appSettings;
    private readonly IBackgroundTaskService backgroundTaskService;
    private readonly IOrchestratorService orchestratorService;
    private readonly SimpleJsonSerializer deserializer = new();

    public RootController(AppSettings appSettings, IBackgroundTaskService backgroundTaskService, IOrchestratorService orchestratorService)
    {
        this.appSettings = appSettings;
        this.backgroundTaskService = backgroundTaskService;
        this.orchestratorService = orchestratorService;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
        var version = typeof(RootController).Assembly.GetName().Version;

        return Ok($"Uptime: {uptime}\nVersion: {version}");
    }

    [HttpPost]
    public IActionResult Post([FromHeader(Name = "X-GitHub-Event")] string eventName, [FromHeader(Name = "X-Hub-Signature-256")] string eventSignature, [FromBody] JsonElement eventPayload)
    {
        Activity.Current?.SetTag("event", eventName);

        if (!IsSignatureValid(eventSignature, eventPayload))
            return Unauthorized();
        switch (eventName)
        {
            case "installation":
                HandleEvent<InstallationEvent>(eventPayload, orchestratorService.HandleInstallationAsync);
                break;
            case "installation_repositories":
                HandleEvent<InstallationRepositoriesEvent>(eventPayload, orchestratorService.HandleInstallationRepositoriesAsync);
                break;
            case "push":
                HandleEvent<PushEvent>(eventPayload, orchestratorService.HandlePushAsync);
                break;
        }

        return Ok();
    }

    private bool IsSignatureValid(string eventSignature, JsonElement eventPayload)
    {
        if (string.IsNullOrEmpty(appSettings.WebhookSecret))
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSettings.WebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(eventPayload.GetRawText()));
        var hashSignature = "sha256=" + Convert.ToHexString(hash);

        return hashSignature.Equals(eventSignature, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleEvent<T>(JsonElement eventPayload, Func<T, CancellationToken, Task> handler)
    {
        var payload = deserializer.Deserialize<T>(eventPayload.GetRawText());
        backgroundTaskService.RunBackgroundTask(c => handler(payload, c));
    }
}
