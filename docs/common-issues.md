# Common issues

Check for some of the following common issues if the general [troubleshooting steps](troubleshooting.md#diagnosing-issues) do not reveal a root cause for Elastic Machines instability.

## Incorrect permissions

Double check that Elastic Machines has access to all required resources and has the correct permission levels. Check GitHub permissions as well as permissions to resources in AWS and Azure. Restart Elastic Machines after changing any permissions to ensure updates take effect immediately.

## Capacity and throttling constraints

Capacity and throttling errors are caused by exceeding rate limits or exhausting the capacity of a particular VM SKU, region, subscription, etc. Elastic Machines automatically reduces the number of requests it makes when it encounters capacity and throttling errors, but will continue trying to satisfy load by scaling up to the configured `maximumMachines`.

If there are fewer online runners than expected, capacity or throttling errors are a likely cause. Refer to the detailed instruction on how to diagnose this type of issue for [AWS](#diagnose-errors-in-aws) or [Azure](#diagnose-errors-in-azure).

> Rate limits and quotas can be impacted by other users of the same subscription. It is recommended that subscriptions are dedicated to Elastic Machines, however when that is not possible, please be mindful of resources consumed by other workloads.

### Calculating capacity requirements

Make sure the total number of vCPUs required by all configurations does not exceed the total vCPU capacity for the subscription(s), region(s), etc. Multiplying each VM's required vCPU count by the desired number of runners to calculate the total number of vCPUs required. e.g.

* VM SKU: Standard_D3_v2
* vCPUs required: 4
* Maximum machines: 50
* vCPUs needed: 200 (i.e. 4 \* 50)

Refer to the [Azure](https://docs.microsoft.com/en-us/azure/virtual-machines/windows/quotas) and [AWS](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/ec2-resource-limits.html) documentation on quotas for more information.

### Capacity constraint workarounds

To solve a capacity constraint it's usually possible to request a quota increase from your cloud provider. If the quota is still too restrictive, consider creating multiple similar configurations with different VM SKUs, regions, or subscriptions. Keep in mind that different VM SKU families have separate quotas, but contribute to a shared regional quota in Azure subscriptions.

To spread load between multiple similar configurations, it may be desirable for them to share the same `name` so workflows can target all of them at once (i.e. `runs-on: <name>`). Elastic Machines requires configuration names to be unique within a runner group, but configurations can share a name if they are assigned to different organization level runner groups.

For example, the following configurations share the same name to distribute jobs across multiple VM SKU families. Jobs with `runs-on: roulette` could be picked up by either configuration. Note the different `group` names that make this possible.

```yaml
- name: roulette
  group: group-1
  provider: azure-virtual-machine@v1
  with:
    vmSku: Standard_D2s_v3
    # ...

- name: roulette
  group: group-2
  provider: azure-virtual-machine@v1
  with:
    vmSku: Standard_B2ms
    # ...
```

In this arrangement, configurations will compete for jobs so this technique can also be helpful for redundancy in addition to spreading load between configurations.

### Diagnose errors in Azure

1. Open the [Azure Portal](https://portal.azure.com)
2. Navigate to the Application Insights instance used for Elastic Machines
3. Open the **Workbooks** blade
4. Navigate to the **Elastic Machines** category
5. Open the **Troubleshooting** workbook. This will show two charts, one for capacity exceptions and one for throttle exceptions.

### Diagnose errors in AWS

1. Open the [AWS Console](https://console.aws.amazon.com)
2. Navigate to the CloudWatch service
3. Open the **X-Ray traces > Traces** blade
4. Extend the **Time range** to cover the time period you are interested in (6 hour max)
5. Paste in the following query in the search bar that appears at the top of the page: `annotation.activity_operation_name = "CreateMachineAsync"`
6. Click the **Run query** button
7. Click into any of the traces that appear in the results table at the bottom of the page
8. Examine the sub-segments of the trace and look for any calls to `CreateMachineAsync` that have a segment status of `Fault (5xx)` and appear in red. This indicates a failure while attempting to create a new EC2 instance.
9. Navigate to the **Exceptions** panel in the Segment Details on the right section of the page. If you see any exceptions that have the type of `ElasticMachines.Providers.CapacityException` or `ElasticMachines.Providers.ThrottleException` then you are experiencing a capacity constraint or API throttling issue.

## Spot evictions

Jobs might fail more often on providers configured to use spot virtual machines. Spot instances are inherently prone to spontaneous eviction, which results in the runner being terminated and the job failing mid-run. In order to verify that jobs failed because of spot instance evictions, inspect the GitHub Actions job logs under the "Setup Job" step and take note of the "Runner name". Using this "Runner name", you can find a corresponding instance in your cloud provider's console and inspect the logs and events on that instance to validate that it was in fact terminated due to a spot instance eviction. To avoid these evictions, consider increasing the maximum price threshold for spot instances.

## Dedicated host capacity

AWS configurations that launch instances on dedicated hosts using the `placement.tenancy: host` option require pre-configured dedicated hosts. There must be sufficient capacity to support the desired number of runners in the configured region, instance type, and availability zone (which must match the desired subnet). Elastic Machines will fail to scale if there are no available and compatible dedicated hosts. Each dedicated host may only have capacity for a single instance in many cases, so you may needed to provision many dedicated hosts of the same instance type to support the desired number of runners.

Dedicated hosts are required to launch macOS based instances with the instance types of `mac1.metal` and `mac2.metal`.
