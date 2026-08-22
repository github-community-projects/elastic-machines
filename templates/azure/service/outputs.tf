################################################################
# Azure Service Module Outputs
################################################################

output "deployment" {
  description = "The details of the deployed Elastic Machines configuration."
  value = {
    config_name     = var.config_name
    subscription_id = var.service.subscription_id
    container = {
      registry    = var.container.registry
      image       = var.container.image
      version     = var.container.version
      full_image  = "${var.container.registry}/${var.container.image}:${var.container.version}"
      environment = local.env
    }
    tags = var.tags
  }
  sensitive = true
}

output "resource_group" {
  description = "The details of the resource group where Elastic Machines resources have been deployed."
  value       = module.resource_group.details
}

output "app_service" {
  description = "The details of the deployed App Service."
  value = {
    name      = local.app_service_name
    id        = azurerm_linux_web_app.elastic_machines_app_service.id
    host_name = azurerm_linux_web_app.elastic_machines_app_service.default_hostname
    plan_sku  = azurerm_service_plan.elastic_machines_service_plan.sku_name
    identity  = local.app_service_identity
  }
}

output "app_insights" {
  description = "The details of the Application Insights configuration."
  value = {
    enabled        = var.service.app_insights.enabled
    name           = lookup(local.app_insights, "name", "")
    resource_group = lookup(local.app_insights, "resource_group_name", "")
    workspace_id   = lookup(local.app_insights, "workspace_id", "")
    is_managed     = !local.app_insights_exists && length(azurerm_application_insights.elastic_machines_app_insights) > 0
  }
}
