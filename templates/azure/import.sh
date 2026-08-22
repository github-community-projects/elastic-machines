#!/usr/bin/env sh

SUBSCRIPTION="<subscription>" # Subscription ID
RESOURCE_GROUP="<resource-group>" # Resource Group name
NAME="<name>" # config.yml's name property

# Import existing resources into state
terraform import 'module.service.module.resource_group.azurerm_resource_group.elastic_machines_resource_group[0]' "/subscriptions/$SUBSCRIPTION/resourceGroups/$RESOURCE_GROUP"
terraform import 'module.service.azurerm_log_analytics_workspace.elastic_machines_workspace[0]' "/subscriptions/$SUBSCRIPTION/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.OperationalInsights/workspaces/$NAME-workspace"
terraform import 'module.service.azurerm_application_insights.elastic_machines_app_insights[0]' "/subscriptions/$SUBSCRIPTION/resourceGroups/$RESOURCE_GROUP/providers/microsoft.insights/components/$NAME-app-insights"
terraform import 'module.service.azurerm_service_plan.elastic_machines_service_plan' "/subscriptions/$SUBSCRIPTION/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/serverfarms/$NAME-app-svc-plan"
terraform import 'module.service.azurerm_linux_web_app.elastic_machines_app_service' "/subscriptions/$SUBSCRIPTION/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$NAME"
