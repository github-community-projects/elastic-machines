using OpenTelemetry.Metrics;

namespace ElasticMachines.OpenTelemetry.Exporter.CloudWatch;

public static class Extensions
{
    public static MeterProviderBuilder AddCloudWatchExporter(this MeterProviderBuilder builder, string logGroupName, string logStreamName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddReader(new PeriodicExportingMetricReader(new CloudWatchExporter(logGroupName, logStreamName)) { TemporalityPreference = MetricReaderTemporalityPreference.Delta });
    }
}
