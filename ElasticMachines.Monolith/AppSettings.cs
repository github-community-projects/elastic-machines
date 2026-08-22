namespace ElasticMachines.Monolith;

public record AppSettings
{
    [ConfigurationKeyName("APP_ID")]
    public string? AppId { get; init; }

    [ConfigurationKeyName("PRIVATE_KEY")]
    public string? PrivateKey { get; init; }

    [ConfigurationKeyName("GHE_HOST")]
    public string GheHost { get; init; } = "github.com";

    [ConfigurationKeyName("WEBHOOK_SECRET")]
    public string? WebhookSecret { get; init; }

    [ConfigurationKeyName("ENABLE_HTTP_LOGGING")]
    public bool EnableHttpLogging { get; init; } = false;

    [ConfigurationKeyName("ENABLE_AWS_XRAY_TELEMETRY")]
    public bool EnableAwsXrayTelemetry { get; init; } = false;

    [ConfigurationKeyName("CLOUDWATCH_METRICS_LOG_GROUP")]
    public string? CloudwatchMetricsLogGroup { get; init; }

    [ConfigurationKeyName("CLOUDWATCH_METRICS_LOG_STREAM")]
    public string? CloudwatchMetricsLogStream { get; init; }

    [ConfigurationKeyName("APPLICATIONINSIGHTS_CONNECTION_STRING")]
    public string? ApplicationInsightsConnectionString { get; init; }

    [ConfigurationKeyName("OTEL_EXPORTER_OTLP_ENDPOINT")]
    public string? OtelExporterOtlpEndpoint { get; init; }

    [ConfigurationKeyName("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")]
    public string? OtelExporterOtlpTracesEndpoint { get; init; }

    [ConfigurationKeyName("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT")]
    public string? OtelExporterOtlpMetricsEndpoint { get; init; }

    [ConfigurationKeyName("ElasticMachines:RepositoryConfigurationPath")]
    public required string RepositoryConfigurationPath { get; init; }

    [ConfigurationKeyName("ElasticMachines:AccountConfigurationPath")]
    public required string AccountConfigurationPath { get; init; }

    [ConfigurationKeyName("ElasticMachines:AccountConfigurationRepositories")]
    public required HashSet<string> AccountConfigurationRepositories { get; init; }

    [ConfigurationKeyName("ElasticMachines:PollingInterval")]
    public required TimeSpan PollingInterval { get; init; }
}
