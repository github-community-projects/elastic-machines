using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ElasticMachines.Engine;

public static class Telemetry
{
    private static readonly AssemblyName assemblyName = typeof(Telemetry).Assembly.GetName();

    internal const string Name = "elastic-machines";
    internal static readonly string AssemblyName = assemblyName.Name ?? throw new Exception("Could not determine assembly name");
    internal static readonly string AssemblyVersion = assemblyName.Version?.ToString() ?? throw new Exception("Could not determine assembly version");

    internal static readonly ActivitySource ActivitySource = new(AssemblyName, AssemblyVersion);
    internal static readonly Meter Meter = new(AssemblyName, AssemblyVersion);

    internal static readonly Counter<int> ConfigurationsCount = Meter.CreateCounter<int>("configurations.count");
    internal static readonly Gauge<long> JobsAssigned = Meter.CreateGauge<long>("jobs.assigned");
    internal static readonly Gauge<long> JobsRunning = Meter.CreateGauge<long>("jobs.running");
    internal static readonly Gauge<long> RunnersBusy = Meter.CreateGauge<long>("runners.busy");
    internal static readonly Gauge<long> RunnersIdle = Meter.CreateGauge<long>("runners.idle");
    internal static readonly Gauge<long> RunnersRegistered = Meter.CreateGauge<long>("runners.registered");
    internal static readonly Histogram<double> RunnersCreateTime = Meter.CreateHistogram<double>("runners.create_time", "ms");
    internal static readonly Histogram<double> RunnersLifeTime = Meter.CreateHistogram<double>("runners.life_time", "ms");

    public static TracerProviderBuilder CreateTracerProviderBuilder(string serviceName)
    {
        return Sdk.CreateTracerProviderBuilder()
            .ConfigureResource(r => r.AddService(serviceName, serviceNamespace: Name, serviceVersion: AssemblyVersion))
            .AddSource(AssemblyName);
    }

    public static MeterProviderBuilder CreateMeterProviderBuilder(string serviceName)
    {
        return Sdk.CreateMeterProviderBuilder()
            .ConfigureResource(r => r.AddService(serviceName, serviceNamespace: Name, serviceVersion: AssemblyVersion))
            .AddMeter(AssemblyName);
    }
}
