# Elastic Machines Service Deployment

The Terraform templates use a `config.yml` file to control how Elastic Machines is deployed. Many options can be tweaked to make the Elastic Machines deployment compatible with your environment.

This document will help you learn about the basic structure of the `config.yml` file and which options are available.

## Prerequisites

The following items should be done prior to configuring the Elastic Machines service deployment:

1. Learn about the [Elastic Machines deployment templates](README.md) and configure an environment to deploy to the desired cloud provider via Terraform.
2. [Configure a GitHub App](../README.md#github-app) for use with Elastic Machines.
3. Create a [personal access token (PAT)](https://docs.github.com/en/github/authenticating-to-github/keeping-your-account-and-data-secure/creating-a-personal-access-token) with the `read:packages` scope (to access the Elastic Machines container image).

### AWS Prerequisites

Additionally, if hosting the Elastic Machines service on AWS:

* [Register a domain name in AWS Route 53](https://docs.aws.amazon.com/Route53/latest/DeveloperGuide/domain-register.html) that will be used to host the Elastic Machines service. This domain (or a subdomain) will be used in the Elastic Machines service configuration. Using Route 53 to register a domain allows AWS to easily configure DNS and SSL as needed. To use a domain registered outside of AWS, [see the notes](#aws) below.

## Deploying the Service

> [!NOTE]
> Make sure you've completed the [Prerequisites](#prerequisites) detailed above, as the GitHub App ID, App private key (PEM), and GitHub PAT are required configuration settings.

An Elastic Machines deployment is controlled via a Yaml configuration file located at `/templates/<cloud>/config.yml`, e.g. `/templates/azure/config.yml`.

To deploy the Elastic Machines service, follow these steps:

1. Use the Yaml schema below to create a service configuration in the `/templates/<cloud>/config.yml` file.
2. Use `terraform` commands to `init`/`plan`/`apply` the infrastructure deployment, as described in [Using Terraform](README.md#using-terraform).

### Configuration Settings

> [!NOTE]
> Settings with `Default` values are not required.

```yaml
# A map of tags that will be applied across created cloud resources. Any number of tags is supported.
tags: # Default: null
  environment: prod
  # ...

# The name of the Elastic Machines configuration/service being deployed. This will be used as a base name for all Elastic Machines-related cloud resources that are created
name: my-em-service

# The details of the Elastic Machines container image to be used for this service deployment
container:
  # The login and access token that will be used to authenticate to the container image registry. This is used for Docker login to GHCR.io or the registry hosting the container; the login can generally be set to any user name provided the token has valid read access to the container + registry
  login: MyUsername
  token: ghp_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX

  # The image registry that hosts the Elastic Machines container image
  registry: myprivateregistry.io # Default: ghcr.io

  # The owner + name and version/tag of the Elastic Machines container image
  image: owner/elastic-machines # Default: elastic-machines/monolith
  version: 0.2.0 # Default: latest

  # A list of environment variables to be passed to the Elastic Machines container
  # Note: See the `Environment Variables & Templating` note below for information on sourcing these values from an environment variable, file, or secret
  environment:
    # Note: Review the steps for the GitHub app creation to learn how to configure these GitHub app values
    # The ID of the GitHub app that was configured for Elastic Machines
    APP_ID: 12345
    # The contents of a GitHub app private key (PEM) file used to authenticate with GitHub
    PRIVATE_KEY: -----BEGIN RSA PRIVATE KEY-----<|--SNIP--|>-----END RSA PRIVATE KEY-----
    # The webhook secret used for webhook-based Elastic Machines scaling
    WEBHOOK_SECRET: <Webhook secret value> # Default: null
    # (Optional) If using a GitHub Enterprise Server instance rather than GitHub Cloud, the full hostname of the server instance
    GHE_HOST: ghe-lighthouse.westus2.cloudapp.azure.com

    # Other environment variables may be added as needed
    ADDITIONAL_ENV_VAR: value
    # ...

###
# Cloud-specific configuration details as defined below
###
```

#### Azure Configuration

```yaml
###
# Details required to run Elastic Machines in Azure
###

# The ID of the Azure subscription where Elastic Machine service resources will be created and managed
subscription_id: <Subscription GUID>

# The Azure cloud environment to use. Possible values are public, china and usgovernment. See https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs#environment
environment: usgovernment # Default: public

# Details for an existing or new resource group where service resources will be deployed
resource_group:
  # The name of the resource group to deploy resources in
  # Note: Resource group names need to be unique across an Azure subscription, so be sure to use a unique name to avoid collisions.
  name: My-Elastic-Machines-Service # Default: top-level copnfiguration {name}, e.g. "my-em-service" if name is set to "my-em-service" as shown above
  # Azure region resources will be created in
  # If region is specified, a new Resource Group will be created
  region: East US # See also: https://azure.microsoft.com/en-us/global-infrastructure/geographies/#geographies

app_service:
  # The name of the created App Service. This is used as the domain hostname for the service, so a globally unique name must be specified here
  # Note: This name must be supplied if the configuration/service name is not globally unique
  name: <Globally unique name> # Default: Top-level configuration <name> will be used for the app service name

  # The SKU of the App Service Plan that will be created to host the Elastic Machines service
  # Valid values can be found at https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/service_plan#sku_name
  # See also: https://azure.microsoft.com/en-us/pricing/details/app-service/linux/
  plan_sku: P2v2 # Default: S1

  # Details of a user-assigned identity the App Service should use. If this block is omitted, a SystemAssigned identity will be created instead
  identity:
    # The name and resource group of an existing identity to use
    name: <identity_name>
    resource_group: <identity_resource_group>

# Application Insights configuration for telemetry and monitoring of the Elastic Machines service
# Default: When the app_insights node is omitted, or the name/resource_group are omitted, a new App Insights resource will be created
app_insights:
  # A value indicating whether to use App Insights for telemetry/monitoring
  enabled: false # Default: true
  # Details of an Application Insights instance to use for monitoring the App Service
  name: <insights_name>
  resource_group: <insights_resource_group>

# Azure Key Vault configuration for sensitive configuration values
# Default: When the key_vault node is omitted, all configuration settings will be stored in the App Service configuration
key_vault:
  # A value indicating whether to create an Azure Key Vault to store sensitive settings. Set to `false` to use an existing Key Vault
  enabled: false # Default: true
```

#### AWS Configuration

```yaml
###
# Details required to run Elastic Machines in AWS
###

# AWS region resources will be deployed in
# See: https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/using-regions-availability-zones.html
region: us-west-2

# The named profile to authenticate to AWS with (if applicable)
# This value should be a configurated AWS named profile or 'default' to use the default profile created with the AWS CLI. See: https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-files.html
profile: <profile> # Default: 'default'

# The domain details to use for the Elastic Machines service
domain:
  # The full dns name that will be used for the Elastic Machines service
  name: dev.elastic-machines.com
  # True to create a hosted zone in Route53 DNS with the apex domain portion of domain name, e.g. elastic-machines.com
  # This should be set to true for domains registered in external services, and false for domains created in Route53, where a hosted zone will have already been created
  create_hosted_zone: true # Default: false
  # The ARN of an AWS ACM certificate that already exists in the account and region for this domain. If unset, a new certificate will be created
  certificate_arn: arn:aws:acm:<region>:<acct_id>:certificate/<cert_id>

# The network details to be used for the Elastic Machines service. Omit this block to use the default VPC and subnets
network:
  # The ID of an existing VPC to use for the Elastic Machines service
  # Note: The VPC used must have at least 2 subnets in different availability zones
  vpc_id: vpc-89c5a5f0 # Default: default VPC for the account
  # One or more existing subnet(s) to use for the service resources. If not set, default subnets for the specified or default VPC will be used
  # Note: The subnet IDs specified here must belong to the VPC specified above or the default VPC for the account and region. If alb_subnets are not specified separately below, there must be at least 2 subnets in different availability zones set here
  subnets: [existing-subnet-for-vpc, another-subnet] # Default: default subnet(s) for the VPC being used
  # Two or more existing subnet(s) to use for the Application Load Balancer, if separate from the subnets used for the service resources. If not set, the subnets specified above, or the default subnets for the account and region, will be used
  # These should be public subnets that can allow HTTPS traffic
  alb_subnets: [existing-subnet-for-vpc, another-subnet] # Default: the subnets set in network.subnets or the default subnet(s) for the VPC being used
  # One or more existing security group(s) to use for the Application Load Balancer
  # The security group(s) used must allow inbound access for GitHub webhooks and outbound access to make GitHub and cloud API calls
  alb_security_groups: [existing_security_group] # Default: A security group will be created to allow inbound access to the Application Load Balancer over port 443 (HTTPS)

# AWS XRay configuration for telemetry and monitoring of the Elastic Machines service
xray:
  # A value indicating whether to use XRay for telemetry
  traces_enabled: false # Default: true

# AWS CloudWatch configuration for telemetry and monitoring of the Elastic Machines service
cloud_watch:
  # A value indicating whether to use CloudWatch for telemetry/monitoring
  metrics_enabled: false # Default: true
```

#### Service URL

To configure Elastic Machines to use [the webhook-based scaling strategy](../README.md#github-app), the URL created for the Elastic Machine service needs to be configured in the GitHub app.

The service URL will be output after a successful service deployment:

Azure-based service:

```shell
...
host_name = "<app_service.name>.azurewebsites.net"
```

AWS-based service (see also [Domain & DNS notes](#domain-and-dns) for AWS):

```shell
...
host_name = "dev.elastic-machines.com" # Should be the domain name specified in service/aws/domain/name config
```

## Notes

### General

#### Environment Variables & Templating

The `config.yml` file supports [Terraform templating](https://developer.hashicorp.com/terraform/language/expressions/strings#string-templates) via the `${ ... }` syntax. This can be used to reference environment variables and utilize [built-in Terraform functions](https://developer.hashicorp.com/terraform/language/functions) to manipulate values.

> [!NOTE]
> The Terraform templating syntax replaces values before parsing the resulting YAML. Be careful that referenced values do not produce invalid YAML once replaced. Pay particular attention to values that might contain special characters or multiple lines. Consider surrounding templates in quotes or removing new lines to avoid issues.

To reference environment variables:

```yaml
# ...
container:
  token: ${ env.<ENV_VAR> }
```

To load (and manipulate) the contents of a file:

```yaml
# ...
container:
  environment:
    PRIVATE_KEY: ${ replace(file("/path/to/file.pem"), "/[\r\n]/", "") }
```

This example is loading the contents of a PEM file and removing all line feeds and carriage returns from the value to avoid creating invalid YAML.

---

Additionally, any environment variable defined in the `container.environment` block may source its value from a secret or file.

To use a secret:

```yaml
# ...
container:
  environment:
    PRIVATE_KEY:
      # Azure; this value is found in the "Secret Identifier" field in the Azure portal
      secret: https://<vault name>.vault.azure.net/secrets/<secret name> #/<optional version identifier>
      # AWS; this value is found in the "Secret ARN" field in the AWS Secrets Manager console
      secret: arn:aws:secretsmanager:<region>:<account id>:secret:<secret name + 6 chars>:<secret key> #:<optional staging label>:<optional version identifier>
```

> [!NOTE]
> The trailing version/staging values are optional. If omitted, the latest version of the secret will be used.
> AWS secrets will typically store multiple values in JSON format, which require the `<secret key>` portion of the ARN. If the secret is a single value, the `<secret key>` portion may be omitted.
> See the [AWS ECS documentation](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/secrets-envvar-secrets-manager.html#secrets-envvar-secrets-manager-update-container-definition) for more information on the secret ARN format used.

To use a file:

```yaml
# ...
container:
  environment:
    PRIVATE_KEY:
      file: /path/to/file.pem
```

This can be used to load sensitive values from a file _after the configuration has been parsed_. This can help work around issues that may be introduced by inlining files in the configuration, such as invalid YAML or escaping issues.

> [!NOTE]
> When using the `file` node, the file contents will be loaded as is, without the ability to process the contents with Terraform functions. This differs from the templating syntax shown above, which "inlines" file contents prior to parsing the YAML.

Finally, a simple value may also be defined using the `value` node:

```yaml
# ...
container:
  environment:
    APP_ID:
      value: 12345
```

> [!NOTE]
> The nested `value` property is equivalent to assigning a value directly to the environment variable (e.g. `APP_ID: 12345`)

#### Service Identity and Hybrid Cloud Configurations

If the Elastic Machines service needs to manage runners in a cloud other than the one it is deployed to, the service identity will need to be configured to authenticate to those cloud APIs. Each provider documents the environment variables that are needed to configure the service identity for that provider and cloud.

Example: Elastic Machines is deployed on Azure, but needs to manage runners in AWS using the AWS Instance provider. The AWS environment variables should be included in the configuration:

```yaml
container:
  environment:
    # ...
    AWS_ACCESS_KEY_ID: <access key id>
    AWS_SECRET_ACCESS_KEY: ${ env.AWS_SECRET_ACCESS_KEY }
    AWS_REGION: us-west-2
```

> [!NOTE]
> The service identity will need to be configured with the appropriate permissions to create and manage the cloud resources.

The same mechanism can be used to set a preconfigured identity (e.g. Azure service principal) rather than creating one via the deployment. Refer to the provider documentation for details on configuring a service identity and the required environment variables.

### Azure

#### RBAC Permissions

The default system-assigned identity that is created and used by the App Service will be given the following RBAC permissions:

* `Contributor` permissions to the Resource Group used for the Elastic Machines service deployment
* `Reader` permissions to the Subscription used for the Elastic Machines service deployment

These permissions are sufficient for Elastic Machines, provided the scale provider used will scale runner VMs within the same Resource Group and the VM image(s) used exist within the given Subscription. The the identity's permissions will need to be granted manually if either of these conditions are not true, or if the principal deploying Elastic Machines does not have permission to assign roles (see below).

#### Key Vault Considerations

By default, Elastic Machines will store sensitive configuration values in a newly created Azure Key Vault. This is the recommended configuration, and will allow the Terraform templates to manage the secret values directly.

By configuring the [`key_vault` settings](#azure-configuration), this behavior can be disabled, allowing secret values to be sourced from an existing Key Vault or stored directly in the App Service configuration.

To use an _existing_ Azure Key Vault to store sensitive configuration values, the following syntax may be used within the configuration to allow the App Service to access those secrets:

```yaml
# ...
container:
  # ...
  token: "@Microsoft.KeyVault(VaultName=myvault;SecretName=mysecret)"
```

> [!NOTE]
> The enclosing quotes are required for the configuration to be valid yaml that can be correctly parsed.

The identity used by the App Service will need to have the `Key Vault Secrets User` role assignment (if using RBAC access control) or a `Get` policy (if using Key Vault access policies) to read the secrets from the Key Vault. If using a system-assigned App Service identity (the default), this may require restarting the service once the identity has been granted the proper permissions.

#### Azure Environment

The `environment` setting in the Azure configuration can be used to target a specific Azure cloud environment. Possible values are `public`, `china`, and `usgovernment`. See the [Azure Provider documentation](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs#environment) for more details.

If your configuration requires using a different Azure cloud environment, ensure the `environment` setting is configured correctly. Failure to specify the proper `environment` value will result in errors when attempting to deploy the Elastic Machines service similar to the following:

```console
Status=404 Code="SubscriptionNotFound" Message="The subscription '<subscription id>' could not be found."
```

### AWS

#### Service Networking

The Elastic Machines service is run in Elastic Container Service and an Application Load Balancer (ALB) is used to route traffic to the service. By default, all service resources will be placed in the same subnet, with security groups configured to allow HTTPS (port 443) traffic to the ALB, and HTTP (port 80) traffic from the ALB to the service.

For more fine grained control of the network traffic (e.g. to place the service in a private subnet), the `network` configuration node can be used to configure the service networking, including setting the subnets and security group(s) used for the ALB separately from the service resources.

A configuration supporting running the service on a private subnet may look like the following:

```yaml
# ...
network:
  vpc_id: <VPC_ID> # ID of VPC containing the subnets configured below
  subnets: [subnet-private] # private subnet(s) to place the service resources in
  alb_subnets: [subnet-public-1, subnet-public-2] # public subnets to use for the ALB. Note: must have at least 2 subnets in different availability zones for the ALB
  alb_security_groups: [sg-https-custom] # custom security group(s) for the ALB traffic. Note: must allow https traffic to the ALB on port 443 to support GitHub webhook delivery, and outbound HTTPS traffic to the cloud API(s) needed to scale runners.
```

#### Domain and DNS

The easiest way to set up a domain for the Elastic Machines service is to [register a top-level in AWS Route 53](https://docs.aws.amazon.com/Route53/latest/DeveloperGuide/domain-register.html). Once a top-level domain is registered in Route 53, a full subdomain can be used in the `domain.name` config and the proper DNS records will be created in Route 53 to attach the subdomain to the service's application load balancer.

##### How DNS and SSL are configured

The domain specified for the Elastic Machines service is used to create an Alias 'A' record in Route 53, tying that domain to the top level, or Apex, domain. Amazon Certificate Manager is used to provision an SSL certificate for the domain, and it is automatically validated via a DNS record insertion/lookup process.

##### Using a domain registered externally

To use a domain registered in another registrar, update the DNS for that domain to point to AWS Route 53, and set `create_hosted_zone` in the `domain` configuration object to `true`. This will create a hosted zone in Route 53 for the Apex (top level) domain, and allow for proper subdomain and SSL configuration within AWS.
