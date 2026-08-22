using Azure.Monitor.OpenTelemetry.Exporter;
using ElasticMachines.Engine;
using ElasticMachines.Engine.Octokit;
using ElasticMachines.Monolith;
using ElasticMachines.Monolith.Services;
using ElasticMachines.Monolith.Services.Hosted;
using ElasticMachines.OpenTelemetry.Exporter.CloudWatch;
using ElasticMachines.Utilities;
using Microsoft.AspNetCore.HttpLogging;
using OpenTelemetry.Exporter.XRay;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var appSettings = builder.Configuration.Get<AppSettings>().GetValue();

builder.Services.AddSingleton(appSettings);
builder.Services.AddSingleton<IAppClient>(new AppClient(appSettings.AppId.GetValue(), appSettings.PrivateKey.GetValue(), appSettings.GheHost));
builder.Services.AddSingleton<IBackgroundTaskService, BackgroundTaskService>();
builder.Services.AddSingleton<IOrchestratorFactory, OrchestratorFactory>();
builder.Services.AddSingleton<IOrchestratorService, OrchestratorService>();
builder.Services.AddSingleton(typeof(IActivityService<>), typeof(ActivityService<>));
builder.Services.AddHostedService<PollingService>();
builder.Services.AddControllers();
builder.Services.AddHttpLogging(o => o.LoggingFields = HttpLoggingFields.All);

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("elastic-machines", serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
    .WithTracing(builder =>
    {
        builder
            .AddSource(nameof(ElasticMachines) + ".*")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation(options => options.EnrichWithHttpResponseMessage = TelemetryEnricher.AttachHeaders)
            .SetErrorStatusOnException()
            .AddConsoleExporter();

        if (appSettings.EnableAwsXrayTelemetry)
        {
            builder.AddXRayExporter();
        }

        if (!string.IsNullOrEmpty(appSettings.ApplicationInsightsConnectionString))
        {
            builder.AddAzureMonitorTraceExporter(o => o.ConnectionString = appSettings.ApplicationInsightsConnectionString);
        }

        if (!string.IsNullOrEmpty(appSettings.OtelExporterOtlpEndpoint) || !string.IsNullOrEmpty(appSettings.OtelExporterOtlpTracesEndpoint))
        {
            builder.AddOtlpExporter();
        }
    })
    .WithMetrics(builder =>
    {
        builder
            .AddMeter(nameof(ElasticMachines) + ".*")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddProcessInstrumentation()
            .AddRuntimeInstrumentation();

        if (!string.IsNullOrEmpty(appSettings.CloudwatchMetricsLogGroup) && !string.IsNullOrEmpty(appSettings.CloudwatchMetricsLogStream))
        {
            builder.AddCloudWatchExporter(appSettings.CloudwatchMetricsLogGroup.GetValue(), appSettings.CloudwatchMetricsLogStream.GetValue());
        }

        if (appSettings.ApplicationInsightsConnectionString != null)
        {
            builder.AddAzureMonitorMetricExporter(o => o.ConnectionString = appSettings.ApplicationInsightsConnectionString);
        }

        if (!string.IsNullOrEmpty(appSettings.OtelExporterOtlpEndpoint) || !string.IsNullOrEmpty(appSettings.OtelExporterOtlpMetricsEndpoint))
        {
            builder.AddOtlpExporter();
        }
    });

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

if (appSettings.EnableHttpLogging)
{
    app.UseHttpLogging();
}

app.Run();
