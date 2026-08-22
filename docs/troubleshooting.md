# Troubleshooting

Elastic Machines is designed to be resilient to failures and support varied environments. The following steps can be useful when diagnosing unexpected behavior.

## Logs

Elastic Machines's raw console output is usually the first place to look for issues.

### AWS logs

Console output in AWS is piped to CloudWatch where it can be viewed in real time, queried, or exported. The easiest way to export log data to a file is to use the following CLI command.

```sh
aws logs get-log-events --log-group-name <group> --log-stream-name <stream> --output text > elastic-machines.log
```

### Azure logs

The App Service's **Log stream** is a good way to see a stream of the most recent logs and scan for obvious errors. However, the **Log stream** has limited history and can be hard to search or analyze in depth.

Navigate to the [App Service's log archive](https://docs.microsoft.com/en-us/azure/app-service/troubleshoot-diagnostic-logs#access-log-files) path for a more complete set of logs (e.g. `https://<app-name>.scm.azurewebsites.net/api/logs/docker/zip`). Open this URL in a browser to download a zip file containing the latest logs.

### Analyzing logs

The log is somewhat verbose because it uses the OpenTelemetry log format to output all telemetry spans.

```console
Activity.TraceId:            00000000000000000000000000000000
Activity.SpanId:             0000000000000000
Activity.TraceFlags:         Recorded
Activity.ActivitySourceName: ElasticMachines.Engine
Activity.DisplayName:        orchestrator.poll
Activity.Kind:               Internal
Activity.StartTime:          2022-02-22T22:22:22.2222222Z
Activity.Duration:           00:00:02.2222222
Activity.Tags:
    account.name: <...>
    <...>
StatusCode: Error
Activity.Events:
    exception [02/22/2022 22:22:22 +00:00]
        exception.type: System.Exception
        exception.stacktrace: <...>
        exception.message: <...>
    <...>
Resource associated with Activity:
    service.name: elastic-machines
    <...>
```

Pay special attention to the following useful span properties.

* `Activity.DisplayName`: the name of each span, which indicates what operations Elastic Machines is performing internally
* `Activity.Tags`: information about Elastic Machines's internal state during each span
* `Activity.Events`: any specific actions or exceptions that occurred during a span

## Telemetry

Elastic Machines also exports telemetry data, which can help visualize complex operations and diagnose less obvious issues.

### AWS telemetry

Telemetry data is sent to X-Ray when running in AWS. Refer to the [X-Ray documentation](https://docs.aws.amazon.com/xray/latest/devguide/xray-console.html) for more information on how to analyze trace data.

### Azure telemetry

Telemetry data is sent to Application Insights when running in Azure. Application Insights has several views that can be helpful when analyzing data.

> Most telemetry spans are categorized as "dependencies" in Application Insights. Make sure to select the **Dependencies** tab in the **Failures** and **Performance** views. Query data from the **dependencies** table in the **Logs** view.

#### Transaction search view

This view shows a filterable list of all telemetry spans. This is the best place to make simple queries because clicking on a span from this view shows more details and a complete timeline. Search for text within spans or use explicit "pill" filters. e.g.

* `Dependency Name = CreateRunnerAsync`
* `Exception Message = Object reference not set to an instance of an object.`
* `runner.name = ubuntu-custom-Runner-0123456789`
* `configuration.name = ubuntu-custom`

If the filters in this view are not powerful enough, use the [**Logs** view](#logs-view) for more complex queries.

#### Failures view

Use this view to analyze errors in Elastic Machines. Pay special attention to the **Dependencies** and **Exceptions** tabs, which group failed internal operations and exceptions by frequency. Drilling into a particular operation shows a list of response codes. Drilling into a particular response code will show recent sample failures and drilling in further on a specific span will show the complete timeline.

#### Performance view

Use this view to monitor Elastic Machines's performance. Pay special attention to the **Dependencies** tab. Performance can vary somewhat based on which resources Elastic Machines is managing, but this view is a good place to check for degradation or changes over time.

#### Logs view

Use this view to query the span database. It's often easiest to drill into individual spans using the other views, but this view is very powerful. Use queries to aggregate data or parse it in ways that aren't possible otherwise. e.g.

* Show any spans with an exception message

  ```kusto
  dependencies
  | where isnotnull(customDimensions["exception.message"])
  ```

* Show the count of each exception message

  ```kusto
  dependencies
  | project exception=customDimensions["exception.message"]
  | where isnotnull(exception)
  | summarize count() by tostring(exception)
  ```

* Show the number of runners created every hour

  ```kusto
  dependencies
  | where name=="CreateRunnerAsync"
  | where tobool(success)
  | summarize count() by bin(timestamp, 1h)
  | render timechart
  ```

### Analyzing telemetry

Although telemetry and logs contain largely the same data, telemetry is easier to visualize and query. Errors are surfaced clearly in the UI and the timeline view makes it easier to see relationships between failing operations.

Telemetry data can also be used to measure basic metrics like how long a particular operation typically takes or how many runners are created over time.

## Diagnosing issues

Elastic Machines tries to be flexible and handle highly customized runner configurations. However, this flexibility has limits and it's possible to set up resources that won't work with Elastic Machines. Every environment is slightly different, but the following steps will help narrow down any potential issues.

1. **Verify scaling issues:** It can take several minutes for Elastic Machines to create a new VM and start the runner application, but there is likely an issue if no runners appear within 10 minutes of starting a workflow. Make sure the workflow is targeting the right Elastic Machines configuration with the `runs-on` property and make sure the configuration is valid.

2. **Check logs:** Once the scaling issue has been verified, the next step is to spot check the [logs](#logs) for glaring issues. Glance through the console output to uncover obvious issues like exceptions or failed HTTP requests.

3. **Check telemetry:** If the logs don't reveal the underlying issue, use AWS X-Ray or Azure Application Insights to inspect the [telemetry](#telemetry) data. Check for any exceptions or other failures that might prevent Elastic Machines from creating VMs or point to potential problem areas. In Azure, use the [**Failures** view](#failures-view) to investigate errors and the [**Transaction search** view](#transaction-search-view) to find specific spans.

   If there are no obvious errors, search for spans named `CreateRunnerAsync`. If there are no results, Elastic Machines is not trying to scale up. There may be an issue with the configuration or communication with GitHub.

4. **Create a VM manually:** If the telemetry data doesn't reveal a root cause, it may be worth testing the cloud provider by creating a VM manually. Try to keep the VM's configuration similar to Elastic Machines's configuration. For example, use the same base image and network settings.

   For AWS, use the [`aws ec2 run-instances`](https://docs.aws.amazon.com/cli/latest/reference/ec2/run-instances.html) command to create an instance. For Azure, the [`az vm create`](https://docs.microsoft.com/en-us/cli/azure/vm?view=azure-cli-latest#az-vm-create) command is the simplest way to create a new VM.

   ```sh
   az vm create --resource-group <resource_group> --name <name> --image <image>
   ```

   If creating a single VM fails, the issue likely lies with the environment rather than Elastic Machines. Check for resource quota limits or issues with supporting resources like the base image or network.

5. **Run the start script manually:** It's possible that Elastic Machines can create VMs, but isn't able to run the script that installs and starts the runner application. Try [installing and configuring the runner manually](https://docs.github.com/en/actions/hosting-your-own-runners/adding-self-hosted-runners) on a test VM if there are error messages in the logs indicating this might be the case.

   Also, Double check that the VM's image and configuration are [compatible](https://docs.github.com/en/actions/hosting-your-own-runners/about-self-hosted-runners#supported-architectures-and-operating-systems-for-self-hosted-runners) with the runner application. If manual setup works, there may be an issue with communication between Elastic Machines and the VM.

## Report an issue

There may be a bug in Elastic Machines if the above troubleshooting steps don't reveal the root cause of the problem. Please report an issue and include the following information.

* Recent logs
  * On AWS, [export logs from CloudWatch](#aws-logs) to a log file
  * On Azure, [download a zip archive](#azure-logs) of the logs
* Relevant environment information
  * Configurations from `elastic-machines.yml` files that aren't working
  * Information about supporting resources like custom images, virtual networks, etc.
