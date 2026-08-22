using System.Text.Json;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace ElasticMachines.OpenTelemetry.Exporter.CloudWatch;

internal class CloudWatchExporter : BaseExporter<Metric>
{
    private readonly string logGroupName;
    private readonly string logStreamName;
    private readonly AmazonCloudWatchLogsClient client;

    public CloudWatchExporter(string logGroupName, string logStreamName)
    {
        this.logGroupName = logGroupName;
        this.logStreamName = logStreamName;

        client = new AmazonCloudWatchLogsClient();
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        using var scope = SuppressInstrumentationScope.Begin();

        try
        {
            var request = new PutLogEventsRequest
            {
                LogGroupName = logGroupName,
                LogStreamName = logStreamName,
                LogEvents = ConvertMetrics(batch)
            };

            client.PutLogEventsAsync(request).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.WriteLine("Failed to export metrics to CloudWatch\n" + exception);

            return ExportResult.Failure;
        }

        return ExportResult.Success;
    }

    private static List<InputLogEvent> ConvertMetrics(Batch<Metric> batch)
    {
        var logEvents = new List<InputLogEvent>();

        foreach (var metric in batch)
        {
            foreach (ref readonly var metricPoint in metric.GetMetricPoints())
            {
                var emf = new Dictionary<string, object?> { { "OTelLib", metric.MeterName } };

                foreach (var metricPointTag in metricPoint.Tags)
                {
                    emf.Add(metricPointTag.Key, metricPointTag.Value?.ToString());
                }

                // Dimension sets must be calculated before adding the actual metric to avoid including its key
                emf["_aws"] = new
                {
                    Timestamp = metricPoint.EndTime.UtcDateTime,
                    CloudWatchMetrics = new[]
                    {
                        new
                        {
                            Dimensions = new[] { emf.Keys.ToList() },
                            Metrics = new[] { new { metric.Name } },
                        }
                    }
                };

                if (metric.MetricType.IsHistogram())
                {
                    metricPoint.TryGetHistogramMinMaxValues(out var min, out var max);

                    emf[metric.Name] = new
                    {
                        Count = metricPoint.GetHistogramCount(),
                        Sum = metricPoint.GetHistogramSum(),
                        Min = min,
                        Max = max,
                    };
                }
                else if (metric.MetricType.IsDouble())
                {
                    emf[metric.Name] = metric.MetricType.IsSum() ? metricPoint.GetSumDouble() : metricPoint.GetGaugeLastValueDouble();
                }
                else if (metric.MetricType.IsLong())
                {
                    emf[metric.Name] = metric.MetricType.IsSum() ? metricPoint.GetSumLong() : metricPoint.GetGaugeLastValueLong();
                }

                var logEvent = new InputLogEvent()
                {
                    Message = JsonSerializer.Serialize(emf),
                    Timestamp = metricPoint.EndTime.UtcDateTime
                };

                logEvents.Add(logEvent);
            }
        }

        return logEvents;
    }
}
