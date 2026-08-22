terraform {
  required_version = ">= 1.3.0"
}

module "config" {
  source      = "../config"
  config_file = var.config
}

provider "aws" {
  region  = module.config.values.service.region
  profile = lookup(module.config.values.service, "profile", null)

  # apply any tags defined to all created resources
  default_tags {
    tags = lookup(module.config.values, "tags", null)
  }
}

module "service" {
  source      = "./service"
  config_name = module.config.values.name
  tags        = lookup(module.config.values, "tags", {})
  container   = module.config.values.container
  service     = module.config.values.service
}
