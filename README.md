# Elastic Machines

Elastic Machines dynamically scales self-hosted GitHub Actions runners based on demand.

## Background

Elastic Machines has the following capabilities:

* ⚙️ Use configuration as code to manage runners
* 🛌 Minimize idle runners by creating machines on demand
* 🏃‍♂️ Keep pre-warmed runners available to run workflows immediately
* 💸 Save cost by scheduling periods of reduced capacity
* ✨ Manage machines with specialized configurations
* ☁️ Distribute runners across multiple cloud providers, accounts, and regions
* 🏗️ Manage runners at the organization or repository level

## Requirements

Elastic Machines supports both AWS and Azure. Refer to the [Terraform template documentation](templates/README.md) for more information on how to deploy Elastic Machines and the [provider documentation](docs/providers/README.md) for more information on how to configure machines in each cloud.

## YAML configuration

Elastic Machines is controlled by YAML configuration files checked in to your repositories. This file defines the schedules, capacities, etc. for each configuration.

```yaml
# A case insensitive name used to target this configuration from workflows with the 'runs-on' property (e.g. runs-on: <name>)
# The name must be unique within each runner group and contain only letters, numbers, -, and _
# Set 'maximumMachines' to zero and wait for the pool to drain before modifying this value to avoid orphaning online machines
- name: <name>
  # The runner group to use for organization level runners
  # Omit this property for repository level runners
  group: <group> # Default: Default
  # The cloud provider to host runners in
  provider: <provider>
  # A set of provider specific options
  # Refer to each provider's documentation for more information
  with:
    # ...
  # A list of scale configurations that determine the number of runners to make available at any given time
  scale:
    # A cron expression that limits when to apply the given scale definition
    - cronExpression: "* * * * MON-FRI" # Default: * * * * *
      # The timezone that should be used when evaluating the cron expression
      # Available time zone names: https://en.wikipedia.org/wiki/List_of_tz_database_time_zones
      timezone: America/Los_Angeles # Default: UTC
      # Scale up if there are fewer than this many idle runners available
      minimumIdleMachines: 0 # Default: 0
      # Always keep at least this number of runners online
      minimumMachines: 0 # Default: 0
      # Never create more than this number of runners
      # This property is optional, but should be set to a value that is less than the maximum capacity of this provider
      maximumMachines: 10

# Additional provider blocks as needed
- name: <name>
  provider: <provider>
  # ...
```

## Deployment options

Elastic Machines can be deployed in a monolithic or clustered configuration. Monolithic deployments use a single instance of Elastic Machines to manage all configurations, while clustered deployments use a separate instance of Elastic Machines for each configuration. Monolithic deployments read configuration data dynamically from repositories' main branches, while clustered deployments require configuration data while deploying. Monolithic deployments work well for most situations, but clustered deployments offer more isolation and flexibility for situations where fault tolerance and scalability are particularly important.

### Monolithic configuration

Use the provided [Terraform templates](templates) to deploy a monolithic instance of Elastic Machines. Once deployed, Elastic Machines will automatically start managing runners based on the configuration files in your repositories.

#### Repository level runners

To manage runners for an individual repository, create a configuration file called **elastic-machines.yml** within the **.github** folder of your repository. Any runners created from this configuration will be attached to the containing repository.

> Ensure the GitHub App has access to any repositories with Elastic Machines configurations. Refer to the [GitHub App documentation](#required-access) for more information.

#### Organization level runners

To manage runners for an organization, create a configuration file in any of the following specially named repositories: **.github**, **.github-internal**, or **.github-private**. Elastic Machines will search these repositories in order and use all configuration files it finds. Configuration file(s) should still be called **elastic-machines.yml** but be located in the root of the repository. Any runners created from these configuration(s) will be attached to the containing organization.

> Ensure the GitHub App has access to any of these repositories with organization level Elastic Machines configurations. Refer to the [GitHub App documentation](#required-access) for more information.

### Clustered configuration

Use the deployment instructions for [AWS](docs/clustered/aws) or [Azure](docs/clustered/azure) to deploy a clustered configuration of Elastic Machines. Each Elastic Machines configuration should be set on a different instance of Elastic Machines using the `CONFIGURATION` environment variable. Since each instance only accepts a single configuration, the YAML should only contain a single map, and not a list of maps as shown above. The configuration should also include an additional `url` property to indicate the organization (i.e. `https://github.com/<organization>`) or repository (e.g. `https://github.com/<organization>/<repository>`) the configuration is for.

```yaml
name: <name>
group: <group>
# An organization or repository URL
url: <url>
provider: <provider>
with:
  # ...
```

## GitHub App

Elastic Machines uses a [GitHub App](https://docs.github.com/en/developers/apps/getting-started-with-apps/about-apps) to manage it's connection with GitHub. The GitHub App provides a safer authentication mechanism and more narrowly scoped permissions than a [personal access token (PAT)](https://docs.github.com/en/github/authenticating-to-github/keeping-your-account-and-data-secure/about-authentication-to-github#authenticating-with-the-api).

### Create

Follow the [GitHub App creation guide](https://docs.github.com/en/developers/apps/building-github-apps/creating-a-github-app) to set up a new GitHub App in the account or organization that will be using Elastic Machines. The following settings are required (leave any other settings blank).

* **GitHub App name** (e.g. Elastic Machines): The desired name of your github app (must be unique across GitHub)

* **Homepage URL** (e.g. <https://github.com/elastic-machines>): A URL that will be linked to from this app's "public page." The app does not need to be made public, but this setting is still required and might as well link to documentation.

* **Webhook**

  * **Active**: Recommended for faster configuration updates

  * **Webhook URL**: The base URL for your deployment of Elastic Machines (e.g. <https://your-app-service.azurewebsites.net>)

    > The **Webhook URL** is specific to your deployment of Elastic Machines and may not be known until you have deployed your instance. Please refer to the [Terraform template documentation](./templates/README.md) for deployment instructions.

  * **Webhook secret**: Provide a secret that Elastic Machines will use to verify webhook events from GitHub. Treat this value like a password.

* **Repository permissions**

  * **Actions**: Read-only
  * **Administration**: Read & write
  * **Contents**: Read-only

* **Organization permissions**

  * **Self-hosted runners**: Read & write

* **Subscribe to events**

  * **Push**: Recommended for faster configuration updates

* **Where can this GitHub App be installed?**: Leave **Only on this account** selected unless Elastic Machines will be used with multiple organizations in a GitHub Enterprise Server instance

Once the GitHub App has been created, select **Generate a private key** to download a PEM file containing a key that Elastic Machines can use to authenticate against GitHub. Refer to the [container configuration section](#container-configuration) for more information on how this file is used.

### Install

After creating the GitHub App, install it in the target account(s). Creating a GitHub App does not automatically give it access to the account it was created in. Installing the app and choosing which repositories it should have access to is an extra step.

#### Required access

For repository level runners, ensure Elastic Machines has access to all repositories containing a **.github/elastic-machines.yml** configuration file.

For organization level runners, ensure Elastic Machines has access to any repositories with organization level configurations (i.e **.github**, **.github-internal**, or **.github-private**).

### Container configuration

Configure an Elastic Machines instance by setting environment variables on the container.

* `APP_ID`: The ID of the [GitHub App](#github-app) Elastic Machines should use for authentication
* `PRIVATE_KEY`: The contents of the PEM file downloaded from the [GitHub App](#github-app) (run `cat path/to/file.pem` to get the contents)
* `WEBHOOK_SECRET` (default `null`): The webhook secret entered into the [GitHub App](#github-app)
* `GHE_HOST` (default `null`): The GitHub Enterprise Server's hostname

> [Individual providers](docs/providers/README.md) may require additional environment variables for authentication.

#### Azure

In addition to console output, Elastic Machines can export telemetry data to Azure Application Insights by configuring the following environment variable:

* `APPLICATIONINSIGHTS_CONNECTION_STRING` (default `null`): The connection string used to export metrics to Azure Application Insights.

#### AWS

In addition to console output, Elastic Machines can export telemetry data to AWS X-Ray and CloudWatch by configuring the following environment variables:

* `ENABLE_AWS_XRAY_TELEMETRY` (default `false`): Set to `true` to export traces to AWS X-Ray
* `CLOUDWATCH_METRICS_LOG_GROUP` (default `null`): The name of the AWS CloudWatch log group where metric data will be exported
* `CLOUDWATCH_METRICS_LOG_STREAM` (default `null`): The name of the AWS CloudWatch log stream where metric data will be exported

> Make sure the container has access to credentials with sufficient permissions to upload telemetry.
>
> * The `AWSXrayWriteOnlyAccess` permissions policy is required to upload traces to X-Ray
> * The `logs:PutLogEvents` permission is required to upload metrics to CloudWatch
>
> Terraform configures an ECS task role with the proper permissions when deploying Elastic Machines to AWS. Use environment variables for authentication when manually running the container outside AWS.
>
> * `AWS_ACCESS_KEY_ID`: IAM user's access key ID
> * `AWS_SECRET_ACCESS_KEY`: IAM users's secret access key
> * `AWS_REGION`: The region to upload telemetry to

#### OTLP

If it's not possible to store telemetry data in AWS or Azure, Elastic Machines can also output telemetry and metrics in the OTLP format. Set `OTEL_EXPORTER_OTLP_ENDPOINT` on the container to enable this exporter and use the standard [configuration options](https://opentelemetry.io/docs/languages/sdk-configuration/otlp-exporter/) for authentication, etc. To send metrics and traces to different OTLP endpoints set `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` and `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` separately.

## Further reading

Use the following resources to learn more about Elastic Machines.

* [Terraform templates](templates/README.md): for deployment instructions
* [Knowledge base](docs/knowledge-base.md): for answers to common questions
* [Troubleshooting](docs/troubleshooting.md): for diagnosing and reporting issues

## License

This project is licensed under the terms of the MIT open source license. Please refer to the [LICENSE](./LICENSE) file for the full terms.

## Maintainers

[@dwhathaway](https://github.com/dwhathaway), [@lineville](https://github.com/lineville), [@MatisseHack](https://github.com/MatisseHack), and [@NateRickard](https://github.com/NateRickard)

## Support

None

## Acknowledgement

Thanks to GitHub for incubating this project. See [actions/scaleset](https://github.com/actions/scaleset) and [actions/actions-runner-controller](https://github.com/actions/actions-runner-controller) for two officially supported projects with similar goals.
