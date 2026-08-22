using System.Runtime.InteropServices;
using Azure.Monitor.OpenTelemetry.Exporter;
using ElasticMachines.Engine;
using ElasticMachines.Engine.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var cancellationTokenSource = new CancellationTokenSource();

void Cancel(PosixSignalContext context)
{
    cancellationTokenSource.Cancel();
    context.Cancel = true;
}

using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Cancel);
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Cancel);

var provider = "azure-virtual-machine";
var appId = Environment.GetEnvironmentVariable("APP_ID") ?? throw new Exception("APP_ID environment variable is not set");
var privateKey = Environment.GetEnvironmentVariable("PRIVATE_KEY") ?? throw new Exception("PRIVATE_KEY environment variable is not set");

var configuration = await Configuration.FromEnvironment(cancellationTokenSource.Token).ConfigureAwait(false);
var orchestrator = await new OrchestratorFactory().CreateOrchestratorAsync(provider, configuration, appId, privateKey).ConfigureAwait(false);

var tracerProviderBuilder = Telemetry.CreateTracerProviderBuilder(provider)
    .AddHttpClientInstrumentation()
    .AddConsoleExporter()
    .AddOtlpExporter();

var meterProviderBuilder = Telemetry.CreateMeterProviderBuilder(provider)
    .AddHttpClientInstrumentation()
    .AddProcessInstrumentation()
    .AddRuntimeInstrumentation()
    .AddConsoleExporter()
    .AddOtlpExporter();

if (Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING") is string applicationInsightsConnectionString)
{
    tracerProviderBuilder.AddAzureMonitorTraceExporter(o => o.ConnectionString = applicationInsightsConnectionString);
    meterProviderBuilder.AddAzureMonitorMetricExporter(o => o.ConnectionString = applicationInsightsConnectionString);
}

using var tracerProvider = tracerProviderBuilder.Build();
using var meterProvider = meterProviderBuilder.Build();

await orchestrator.RunAsync(cancellationTokenSource.Token).ConfigureAwait(false);
