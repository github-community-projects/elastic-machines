# Metrics

Elastic Machines uses [OpenTelemetry](https://opentelemetry.io/) to record and periodically report metrics about the state of configurations, runners, jobs, etc. These metrics are automatically sent to either Azure Application Insights or AWS Cloud Watch when deployed through Terraform, but can be disabled in the `config.yml` file if needed. Refer to the [service deployment documentation](../templates/service-deployment.md) for more information.

## UpDownCounters

[UpDownCounters](https://opentelemetry.io/docs/specs/otel/metrics/api/#updowncounter) are recorded every minute and represent a sampled value at that time.

* `configurations.count` - The number of configurations
* `jobs.assigned` - The number of jobs assigned to this configuration
* `jobs.running` - The number of jobs running on a runner from this configuration
* `runners.registered` - The number of runners registered from this configuration
* `runners.busy` - The number of runners running a job from this configuration
* `runners.idle` - The number of runners unused from this configuration

## Histograms

[Histograms](https://opentelemetry.io/docs/specs/otel/metrics/api/#histogram) are recorded dynamically as needed and have count, sum, min, and max values because they may represent an aggregate of multiple metric points recorded during a short period.

* `runners.create_time` - Runner startup time in milliseconds
* `runners.life_time` - Runner existence time in milliseconds

## Additional metrics

Elastic Machines also includes several other built in metrics related to [HTTP requests](https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/metrics/semantic_conventions/http-metrics.md), [the current process](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.Process), and [the .NET runtime](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.Runtime). These metrics may be helpful when troubleshooting performance issues or analyzing the processing and memory requirements for your Elastic Machines deployment.

## Tags

Each metric also contains the following tags, which can be used for grouping or more advanced queries.

* `account.name` - The name of the account that the configuration belongs to
* `repository.name` - The name of the repository that the configuration belongs to
* `runner_group.name` - The name of the runner group that the configuration belongs to
* `configuration.name` - The name of the configuration
* `configuration.provider` - The name of the configuration's provider
* `configuration.with` - The contents of the configuration's `with` property represented as JSON

## Example queries

Elastic Machines includes several example queries to help you start analyzing these metrics. The example queries are automatically deployed to either Azure Application Insights or AWS Cloud Watch when deployed through Terraform.

### Azure Application Insights Instructions

* Open the [Azure Portal](https://portal.azure.com)
* Navigate to the Application Insights instance used for Elastic Machines
* Open the **WorkBooks** blade
* Navigate to the **Elastic Machines** Category
* Select any of the three provided workbook templates to configure and create a new metrics workbook.

### AWS Cloud Watch Logs Insights Instructions

* Open the [AWS CloudWatch Console](https://console.aws.amazon.com/cloudwatch/home)
* Navigate to the **Logs Insights** blade underneath the **Logs** section
* Open the **Queries** tab on the right hand side and select any of the provided queries.
* Modify the query by adjusting the time frame on the top right hand side of the query editor. Adjust the scope of the query by applying additional filters like the example in the commented out section of the query.
  > [!NOTE]
  > Comment out the `| stats ...` statement to view the raw data, expand one of the rows to view the properties that can be used for further filtering.
* Select the appropriate log group for Elastic Machines.
* Click **Run Query** to view the results of the query.
