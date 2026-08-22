################################################################
# Azure Service Module entry point
################################################################

terraform {
  required_version = ">= 1.3.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 3.98.0"
    }
  }
}

# Create or read a user-assigned identity for the App Service, if configured
module "resource_group" {
  source   = "./resource-group"
  name     = coalesce(var.service.resource_group.name, var.config_name)
  location = var.service.resource_group.region
  tags     = var.tags
}
