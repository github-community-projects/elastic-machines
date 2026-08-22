terraform {
  required_version = ">= 1.3.0"
}

module "config" {
  source      = "../config"
  config_file = var.config
}

provider "azurerm" {
  subscription_id = module.config.values.service.subscription_id
  environment     = lookup(module.config.values.service, "environment", null)

  features {
    resource_group {
      prevent_deletion_if_contains_resources = false
    }
  }
}

module "service" {
  source      = "./service"
  config_name = module.config.values.name
  tags        = lookup(module.config.values, "tags", {})
  container   = module.config.values.container
  service     = module.config.values.service
}
