terraform {
  required_version = ">= 1.3.0"

  required_providers {
    environment = {
      source = "EppO/environment"
    }
  }
}

data "environment_variables" "all" {}

locals {
  # Read in the main config and apply any TF function processing + env substitutions
  template = templatefile(var.config_file, {
    env = data.environment_variables.all.items
  })
  # Decode the YAML config into a map
  config_values = yamldecode(local.template)

  shared = ["name", "tags", "container"]

  config = merge({
    for k, v in local.config_values : k => v
    if contains(local.shared, k)
    }, {
    service = {
      for k, v in local.config_values : k => v
      if !contains(local.shared, k)
    }
  })
}
