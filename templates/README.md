# Cloud Resource Deployment with Terraform

The recommended way to deploy Elastic Machines is to use the provided [Terraform](https://www.terraform.io/) templates. These templates will deploy the cloud infrastructure needed for a functioning Elastic Machines service, and allow for customization to match many use cases. Terraform is an Infrastructure as Code (IaC) tool, which allows versioning configuration in source control, iterating in a stateful and repeatable manner, and automating the planning and deployment of changes.

This document will help you configure a working Terraform environment and learn the commands needed to deploy and manage Elastic Machines infrastructure via Terraform.

## Prerequisites

The following tools are required to use these templates:

* [Terraform](https://developer.hashicorp.com/terraform/downloads)

### Azure Prerequisites

When using Azure, ensure Terraform is able to authenticate to the cloud:

* [Install](https://learn.hashicorp.com/tutorials/terraform/azure-build#install-the-azure-cli-tool) the Azure CLI (`az`) or ensure the CLI is updated to the latest version.
* [Configure](https://learn.hashicorp.com/tutorials/terraform/azure-build#prerequisites) a working Terraform installation that can properly manage resources in the Azure cloud.

By default, Terraform will use the authenticated Azure CLI user to perform operations in Azure.

Environment variables can also be set to authenticate Terraform to Azure via a Service Principal in automation/CI/CD scenarios:

* `ARM_CLIENT_ID`
* `ARM_CLIENT_SECRET`
* `ARM_TENANT_ID`
* `ARM_SUBSCRIPTION_ID`

More information on other methods of authentication can be [found here](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/guides/service_principal_client_certificate).

#### Required Azure Permissions

The authenticated identity (CLI user or Service Principal) should have the following RBAC roles (or equivalent permissions) for the subscription(s) that will be used for Azure deployments:

* `Contributor` (used to create needed Azure resources)
* `User Access Administrator` (used to assign needed RBAC permissions for the App Service managed identity or Service Principal)
  * [See here](service-deployment.md#service-identity-and-hybrid-cloud-configurations) for more information on service identity permissions.

### AWS Prerequisites

When using AWS, ensure Terraform is able to authenticate to the cloud:

* [Install](https://docs.aws.amazon.com/cli/latest/userguide/install-cliv2.html) the AWS CLI (`aws`) or ensure the CLI is updated to the latest version.
* [Configure](https://learn.hashicorp.com/tutorials/terraform/aws-build#prerequisites) a working Terraform installation that can properly manage resources in the AWS cloud.

By default, Terraform will use the AWS CLI user (typically via a named profile) that is currently authenticated to perform operations in AWS.

Environment variables can also be set to authenticate Terraform to AWS in automation/CI/CD scenarios:

* `AWS_ACCESS_KEY_ID`
* `AWS_SECRET_ACCESS_KEY`

More information on other methods of authentication can be [found here](https://registry.terraform.io/providers/hashicorp/aws/latest/docs#authentication).

#### Required AWS Permissions

To properly apply the Terraform configuration in AWS, the authenticated user should have the following IAM permission policies (or equivalent access):

* `SecretsManagerReadWrite` - used to store and retrieve secret values needed for container configuration
* `CloudWatchLogsFullAccess` - used for logging
* `AmazonS3FullAccess` - only needed if using S3-based remote state

In order to apply the AWS policies, visit the [IAM Policies page](https://console.aws.amazon.com/iamv2/home#/policies) and search for the policies listed. For each one, click 'Policy Usage' and attach the policy to the user or group used for deployment.

Additionally, the following policies have been created for Elastic Machines that provide the minimum permissions needed to manage the resources for Elastic Machines. These have been organized by the AWS service area:

* [`elastic-machines-deployment-IAM`](aws/policies/elastic-machines-deployment-IAM.json) - policies for Identity and Access Management
* [`elastic-machines-deployment-EC2`](aws/policies/elastic-machines-deployment-EC2.json) - policies for Amazon EC2

If hosting the Elastic Machines service on AWS, these policies are also needed:

* [`elastic-machines-deployment-ECS`](aws/policies/elastic-machines-deployment-ECS.json) - policies for Elastic Container Service
* [`elastic-machines-deployment-ELB`](aws/policies/elastic-machines-deployment-ELB.json) - policies for Elastic Load Balancer
* [`elastic-machines-deployment-ACM`](aws/policies/elastic-machines-deployment-ACM.json) - policies for Amazon Certificate Manager
* [`elastic-machines-deployment-Route53`](aws/policies/elastic-machines-deployment-Route53.json) - policies for Route53 DNS

> [!NOTE]
> These policies have been sanitized to remove the account ID from all resource ARNs. It is recommended to replace the wildcard (\*) from the account ID slot of any resource ARN used and replace it with the correct account ID in order to restrict Terraform operations to that account.

These policies can be [created in AWS](https://docs.aws.amazon.com/IAM/latest/UserGuide/access_policies_create.html) via the JSON, and applied to the user used to deploy to AWS.

## Using Terraform

Terraform has commands to `plan` or `apply` the resources that will be deployed to the cloud based on the current configuration. When resources are already deployed to the cloud, Terraform will calculate the necessary resource additions, modifications, or deletions to match the configuration.

The Terraform commands needed to work with these templates are detailed below.

### Init

Before initializing Terraform, choose which type of [state backend](#state) to use and create a `config.yml` file in the folder of the targeted cloud - `templates/azure` or `templates/aws`. Refer to the [service deployment](service-deployment.md#deploying-the-service) document for this file's schema. Once the state configuration and `config.yml` file have been created, navigate to the containing folder and initialize the Terraform configuration:

```shell
terraform init
```

This will initialize the Terraform state (remote cloud-based state or local file-based state, as configured), initialize the modules, and download any needed Terraform providers.

> [!NOTE]
> Depending on the configuration being applied, it may be necessary to re-run the `init` command when changes are made that affect the state configuration - in these cases the CLI output will report an error indicating this is needed.

### Plan

The `plan` command will determine and output a preview of the resources that will be created, modified, or destroyed in order to bring the cloud deployment into sync with the current configuration.

```shell
terraform plan
```

The plan includes details on the resource changes, including granular detail about the resource attributes that are changing.

The plan can be read much like a Git diff, with indicators (`+`, `-`, `~`, etc.) on each resource attribute to indicate changes:

```console
$ terraform plan
Terraform will perform the following actions:

  # azurerm_role_assignment.terraform_key_vault_access[0] will be created
  + resource "azurerm_role_assignment" "terraform_key_vault_access" {
      + id                               = (known after apply)
      + name                             = (known after apply)
      + principal_id                     = "00000000-0000-0000-0000-000000000000"
      + principal_type                   = (known after apply)
      + role_definition_id               = (known after apply)
      + role_definition_name             = "Key Vault Secrets Officer"
      + scope                            = (known after apply)
      + skip_service_principal_aad_check = (known after apply)
    }

Plan: 1 to add, 0 to change, 0 to destroy.
```

### Apply

The `apply` command will execute an implicit `plan`, and also allow you to choose whether to deploy the planned resource changes to the cloud. If you type `yes` at the prompt, the plan will be executed, and Elastic Machines resources will be created, changed, or destroyed in the targeted cloud.

```shell
terraform apply
```

#### Outputs

Once the configuration is applied to the cloud, Terraform will display Outputs of the operation. This includes useful information on the resources deployed, including some useful resource ID(s) (Azure), ARN(s) (AWS), and other information.

#### Updating the Deployment

The `terraform apply` command can be run again with a different or updated configuration, and Terraform will calculate a new plan to remove, create, modify, or recreate any resources needed to match the new configuration. This makes it easy to alter the details of the Elastic Machines deployment and quickly apply those changes to the cloud.

### Destroy

Finally, to tear down the deployed infrastructure, you can simply run the `destroy` command, which will delete all cloud resources that are being managed by your current Terraform deployment.

```shell
terraform destroy
```

## State

By default, Terraform maintains its state locally in a file. If multiple members of a team will be using Terraform to manage Elastic Machines infrastructure, or the configuration is being applied in automation, it is recommended to [configure a shared remote state](https://www.terraform.io/docs/language/state/remote.html). Choose one of the [available remote state backends](https://developer.hashicorp.com/terraform/language/settings/backends/configuration#available-backends) and configure the backend values within the root Terraform module in `/templates/<cloud>`. State can alternatively be stored in a separate Terraform file (e.g. `state.tf`) within the `/templates/<cloud>` folder.

```terraform
terraform {
  backend "azurerm" {
    # ...
  }
}
```

A state backend should be set up before using the `terraform init` command, as the remote state backend is configured during initialization.

### Reconfigure state

If a configuration has already been initialized or deployed and remote state has been used, changing the remote state backend will typically require using the [`-reconfigure`](https://www.terraform.io/cli/commands/init#backend-initialization) flag and a re-initialization, i.e.:

```shell
terraform init -reconfigure
```

This will configure Terraform to use the currently configured remote state provider and start over, while leaving the previous state untouched. For this reason, it is recommended to `destroy` any deployed resources before attempting this. Alternatively, to preserve state and migrate the state to the new remote state provider, the `-migrate-state` flag can be used.

Refer to Terraform's [state reconfiguration](https://www.terraform.io/docs/language/settings/backends/configuration.html#changing-configuration) documentation for more information.

## Deploy with GitHub Actions

The templates work best when configuration is checked in to a repository and changes are deployed with GitHub Actions. To deploy with a workflow, you will need to create a `config.yml` file in your repository that follows the [service configuration](./service-deployment.md#configuration-settings) YAML schema. You will also need a `state.tf` file that defines a remote backend to store Terraform's state in. Refer to the [state](#state) documentation for more information.

## Next Steps

Once the Terraform environment is configured, an [Elastic Machines deployment configuration can be created and applied](service-deployment.md). This configuration will define the details of the Elastic Machines service that will be deployed to the cloud and is specific to the cloud environment being targeted.
