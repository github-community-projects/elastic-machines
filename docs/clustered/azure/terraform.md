# Terraform

[Terraform](https://www.terraform.io/) is an alternative way to deploy if you don't want to use the [Azure Container Instances YAML format](README.md). Use the following example Terraform file to deploy a single Elastic Machines configuration or modify it to work in your environment (e.g. multiple configurations, modified resource limits, etc.):

```terraform
terraform {
  backend "azurerm" {
    resource_group_name  = "<resource-group-name>"
    storage_account_name = "<storage-account-name>"
    container_name       = "<container-name>"
    key                  = "<key>"
  }
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }
}

provider "azurerm" {
  features {}
}

variable "ghcr_token" {
  type      = string
  sensitive = true
}

variable "private_key_file" {
  type      = string
  sensitive = true
}

variable "configuration_file" {
  type      = string
}

locals {
  configuration = file(var.configuration_file)
  private_key   = file(var.private_key_file)
}

data "azurerm_resource_group" "resource_group" {
  name = "<name>"
}

resource "azurerm_container_group" "container_group" {
  resource_group_name = data.azurerm_resource_group.resource_group.name
  location            = data.azurerm_resource_group.resource_group.location
  name                = "<name>"
  os_type             = "Linux"
  ip_address_type     = "None"
  identity {
    type = "SystemAssigned"
  }
  image_registry_credential {
    server   = "ghcr.io"
    username = "<username>"
    password = var.ghcr_token
  }
  container {
    name     = yamldecode(local.configuration).name
    image    = "ghcr.io/elastic-machines/azure-virtual-machine:<version>"
    cpu      = 1.0
    memory   = 2.0
    environment_variables = {
      APP_ID                                = "<app-id>"
      APPLICATIONINSIGHTS_CONNECTION_STRING = "<applicationinsights-connection-string>"
      CONFIGURATION                         = local.configuration
    }
    secure_environment_variables = {
      PRIVATE_KEY = local.private_key
    }
  }
}
```

Copy this template to a new `.tf` file and fill in any placeholders (denoted in `<brackets>`) with proper values for your environment. To deploy multiple Elastic Machines configurations, add more `container` entries in the `azurerm_container_group` resource or create multiple `azurerm_container_group` resources for more isolation. Refer to the [multiple containers](README.md#multiple-containers) section in the readme for more information on the different ways to organize containers.

Deploy the Terraform template using the [typical planning and applying flow](https://developer.hashicorp.com/terraform/cli/run).

> [!WARNING]
> Consider setting up remote state before running the following commands. It's possible to deploy with local state by removing the `backend` block, but it will be harder to collaborate with others and deploy updates in the future.

1. Initialize the backend and providers:

   ```shell
   terraform init
   ```

2. Plan the deployment and make sure the changes look correct:

   ```shell
   terraform plan \
       --var ghcr_token=<ghcr-token> \
       --var private_key_file=<private-key>.pem \
       --var configuration_file=<configuration>.yml
   ```

3. Apply the changes once you are satisfied that the plan is correct:

   ```shell
   terraform apply \
       --var ghcr_token=<ghcr-token> \
       --var private_key_file=<private-key>.pem \
       --var configuration_file=<configuration>.yml
   ```

This example references an Elastic Machines configuration stored in a separate YAML file, but the configuration could also be passed in as a parameter or defined inline by using a [heredoc string](https://developer.hashicorp.com/terraform/language/expressions/strings#heredoc-strings):

```hcl
CONFIGURATION = <<EOT
name: <name>
url: <url>
with:
  image: Canonical:0001-com-ubuntu-server-jammy:22_04-lts-gen2:latest
  location: centralus
  resourceGroup: <resource-group>
  subscription: <subscription>
  vmSku: Standard_DS1_v2
  # ...
EOT
```

Once deployed, follow the [post deployment steps](README.md#post-deployment) in the readme. Also refer to the readme for general information, such as [resource limits](README.md#limits), [troubleshooting steps](README.md#troubleshooting), etc.
