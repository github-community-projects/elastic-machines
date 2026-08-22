################################################################
# Resource Group Module entry point
################################################################

terraform {
  required_version = ">= 1.3.0"

  required_providers {
    azurerm = {
      source = "hashicorp/azurerm"
    }
  }
}

################################################################
# Conditionally create a new resource group or query an existing one
################################################################

locals {
  resource_group = length(azurerm_resource_group.elastic_machines_resource_group) > 0 ? one(azurerm_resource_group.elastic_machines_resource_group) : one(data.azurerm_resource_group.elastic_machines_resource_group)
}

resource "azurerm_resource_group" "elastic_machines_resource_group" {
  count    = var.location != null ? 1 : 0
  name     = var.name
  location = var.location
  tags     = var.tags
}

data "azurerm_resource_group" "elastic_machines_resource_group" {
  count = var.location != null ? 0 : 1
  name  = var.name
}
