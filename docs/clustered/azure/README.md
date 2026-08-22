# Azure Container Instances

We recommend using Azure Container Instances when running an Elastic Machines cluster in Azure. Use the following example YAML file to deploy a single Elastic Machines configuration or modify it to work in your environment (e.g. multiple configurations, higher resource limits, etc.):

```yaml
apiVersion: 2021-10-01
location: centralus
name: <name>
identity:
  type: SystemAssigned
properties:
  osType: Linux
  imageRegistryCredentials:
    - server: ghcr.io
      username: <username>
      password: ${GHCR_TOKEN}
  containers:
    - name: <name>
      properties:
        image: ghcr.io/elastic-machines/azure-virtual-machine:<version>
        resources:
          requests:
            cpu: 1.0
            memoryInGb: 2.0
        environmentVariables:
          - name: APP_ID
            value: <app-id>
          - name: PRIVATE_KEY
            secureValue: ${PRIVATE_KEY}
          - name: APPLICATIONINSIGHTS_CONNECTION_STRING
            value: <applicationinsights-connection-string>
          - name: CONFIGURATION
            value: |
              name: <name>
              url: <url>
              with:
                image: Canonical:0001-com-ubuntu-server-jammy:22_04-lts-gen2:latest
                location: centralus
                resourceGroup: <resource-group>
                subscription: <subscription>
                vmSku: Standard_DS1_v2
                # ...
```

Copy this template to a new `.yml` file and fill in any placeholders (denoted in `<brackets>`) with proper values for your environment. To deploy multiple Elastic Machines configurations, add more entries under the `containers` property or create multiple files from this template for more isolation. Refer to the [multiple containers](#multiple-containers) section for more information on the different ways to organize containers.

> [!NOTE]
> Local environment variables can be referenced with the `${...}` syntax and will get substituted before deployment.

To deploy the container group, make sure any referenced environment variables are exported and then run the following [command](https://learn.microsoft.com/en-us/cli/azure/container?view=azure-cli-latest#az-container-create):

```shell
az container create --resource-group <resource-group> --file <file>.yml
```

## Post deployment

Once the deployment is complete, give the container group's managed identity access to any resources Elastic Machines needs to manage (e.g. resource groups, custom images, etc.). Giving the identity **Contributor** access at the subscription level is usually the easiest way to ensure Elastic Machines has sufficient privileges and will allow it to create new resource groups automatically when needed.

> [!NOTE]
> There is a bug in the Azure portal where a system assigned managed identity will not show up on the container group page when first created. However, the managed identity does exist and can be given role assignments as usual. The identity will show up on the container group once it's updated with role assignments.

## Application Insights

We highly recommend specifying an Application Insights instance connection string when deploying Elastic Machines so that telemetry data is persisted and easier to monitor and analyze. The same Application Insights instance can be used for as many Elastic Machines configurations as desired. Create a new instance through the portal or with the following [command](https://learn.microsoft.com/en-us/cli/azure/monitor/app-insights/component?view=azure-cli-latest#az-monitor-app-insights-component-create):

```shell
az monitor app-insights component create \
    --app <app> \
    --location centralus \
    --resource-group <resource-group> \
    --workspace <workspace>
```

If your subscription doesn't already have a default Log Analytics Workspace, you will need to create one first with the following [command](https://learn.microsoft.com/en-us/cli/azure/monitor/log-analytics/workspace?view=azure-cli-latest#az-monitor-log-analytics-workspace-create):

```shell
az monitor log-analytics workspace create \
    --name <name> \
    --resource-group <resource-group>
```

## Limits

Azure Container Instances has [several limits](https://learn.microsoft.com/en-us/azure/container-instances/container-instances-resource-and-quota-limits) to be aware of, especially:

* Sixty containers per container group
* Four vCPUs per container group
* Sixteen gigabytes of memory per container group

Depending on the environment, these limits are enough to run quite a few Elastic Machines configurations since each container can be allocated fractional vCPUs and memory. However, you might want to consider splitting configurations into separate container groups if you want redundancy or need to run many high traffic configurations that require more resources.

## Multiple containers

Each container group in Azure can contain multiple containers, but containers can also be split between multiple container groups. Putting multiple containers in a single container group is the easiest way to deploy in most situations, but creating multiple container groups can be useful if you want to allocate more resources to each configuration or reduce the number of containers that need to be restarted when deploying changes. You may even choose to have each configuration in it's own container group so that each configuration can be updated separately. This approach isn't necessary in most situations since Elastic Machines is designed to restart quickly and be easy to roll back, but the option is available.

To split containers into multiple container groups, create multiple files using the template above. Each file will become it's own container group that can be deployed separately and have it's own resource allocations. If all your configurations are in the same folder, you can deploy them all with the following command:

```shell
find . -maxdepth 1 -name "*.yml" -exec az container create --resource-group <resource-group> --file {} \;
```

Multiple container groups can alternatively be deployed with a matrix job in GitHub Actions:

```yaml
name: Deploy

on:
  push:
    branches: ["main"]

jobs:
  deploy:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        file:
          - <file>
          # ...
    env:
      GHCR_TOKEN: ${{ secrets.GHCR_TOKEN }}
      PRIVATE_KEY: ${{ secrets.PRIVATE_KEY }}
      # ...
    steps:
      - name: Checkout
        uses: actions/checkout@v4
      - name: Azure login
        uses: azure/login@v2
        with:
          # ...
      - name: Create container group
        run: az container create --resource-group <resource-group> --file ${{ matrix.file }}
```

## Troubleshooting

Elastic Machines should begin managing runners within moments of starting up. If it hasn't started after several seconds, check the portal to see if the container is stuck in a restart loop. Multiple restarts may indicate a bad configuration that's preventing proper initialization. Run the following command to get the failing container's output:

```shell
az container attach --resource-group <resource-group> --name <name> --container-name <container-name>
```

This command is the best way to get logs from the container if it encounters startup errors, but there are several issues with it to be aware of:

* The command may throw it's own error when the log stream is closed by the container restarting. Run the command again if it exits with a Python error and no useful logs.

* The command may not capture the entire log depending on when it's run in relation to the container's lifecycle. Run the command again if it exits without any useful output. It may need to be run multiple times to capture the exception causing the container to fail.

* The command may hang without producing more output even though the container continues to restart. Try stopping the container group from the **Overview** page in the Azure portal. Wait a few moments and then start the container group again (clicking the **Restart** button alone doesn't seem to have the desired effect). Run the command again to capture the logs from the container's next startup.

The container output should indicate the cause of the error, but here are some common issues to check for if it's not apparent:

* Ensure all configuration (URLs, variables, etc.) is correct
* Ensure any referenced runner groups already exist in GitHub
* Ensure the GitHub App is valid and has been installed in the organization
* Ensure the GitHub App has sufficient permissions to manage runners
* Ensure the `PRIVATE_KEY` environment variable is set correctly on the container. The value should be the contents of a `pem` file generated from the GitHub App (with or without newlines). Refer to the [container configuration](../../../README.md#container-configuration) documentation for more information.

## Troubleshooting after startup

Once the container is fully initialized, Elastic Machines should retry any errors without exiting the container. However, it's still possible for Elastic Machines to encounter issues that prevent it from managing runners. If the container is running, but isn't managing runners as expected, check Application Insights for failures or irregular telemetry data. Refer to the [troubleshooting](../../troubleshooting.md) guide for more information on how to diagnose issues in Application Insights.

Also check for these common issues:

* Ensure each workflow's `runs-on` property matches the `name` property of the desired Elastic Machines configuration
* Ensure the Azure identity has **Contributor** access to the subscription or resource group where runners will be created. The Identity must also have access to any other supporting resources (e.g. custom images).
