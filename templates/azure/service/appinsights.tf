locals {
  app_insights_exists = var.service.app_insights.name != null && var.service.app_insights.resource_group != null
  app_insights = merge(var.service.app_insights.enabled ? (
    local.app_insights_exists ? one(data.azurerm_application_insights.elastic_machines_app_insights) : one(azurerm_application_insights.elastic_machines_app_insights)
  ) : null, {})

  example_query_path    = "${path.module}/example-queries"
  query_parameters_path = "${path.module}/query-parameters"
}

################################################################
# Create or read an Application Insights instance to monitor service
################################################################

resource "azurerm_log_analytics_workspace" "elastic_machines_workspace" {
  count               = var.service.app_insights.enabled && !local.app_insights_exists ? 1 : 0
  name                = "${var.config_name}-workspace"
  location            = module.resource_group.details.location
  resource_group_name = module.resource_group.details.name
  sku                 = "PerGB2018" # Per https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/log_analytics_workspace#sku we are required to use this sku
  retention_in_days   = 90
  tags                = var.tags
}

resource "azurerm_application_insights" "elastic_machines_app_insights" {
  count               = var.service.app_insights.enabled && !local.app_insights_exists ? 1 : 0
  name                = "${var.config_name}-app-insights"
  location            = module.resource_group.details.location
  resource_group_name = module.resource_group.details.name
  workspace_id        = azurerm_log_analytics_workspace.elastic_machines_workspace[0].id
  application_type    = "web"
  tags                = var.tags
}

data "azurerm_application_insights" "elastic_machines_app_insights" {
  count               = var.service.app_insights.enabled && local.app_insights_exists ? 1 : 0
  name                = var.service.app_insights.name
  resource_group_name = var.service.app_insights.resource_group
}
