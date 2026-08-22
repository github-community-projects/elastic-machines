$subscription="<subscription>" # Subscription ID
$resourceGroup="<resource-group>" # Resource Group name
$name="<name>" # config.yml's name property

# Import existing resources into state
terraform import 'module.service.module.resource_group.azurerm_resource_group.elastic_machines_resource_group[0]' "/subscriptions/$subscription/resourceGroups/$resourceGroup"
terraform import 'module.service.azurerm_log_analytics_workspace.elastic_machines_workspace[0]' "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.OperationalInsights/workspaces/$name-workspace"
terraform import 'module.service.azurerm_application_insights.elastic_machines_app_insights[0]' "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/microsoft.insights/components/$name-app-insights"
terraform import 'module.service.azurerm_service_plan.elastic_machines_service_plan' "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Web/serverfarms/$name-app-svc-plan"
terraform import 'module.service.azurerm_linux_web_app.elastic_machines_app_service' "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Web/sites/$name"
