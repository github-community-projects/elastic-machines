################################################################
# User/system-assigned identity management + RBAC
################################################################

locals {
  app_service_identity = {
    name           = module.user_identity.is_configured ? module.user_identity.details.name : local.app_service_name
    resource_group = module.user_identity.is_configured ? module.user_identity.details.resource_group_name : module.resource_group.details.name
    principal_id   = module.user_identity.is_configured ? module.user_identity.details.principal_id : azurerm_linux_web_app.elastic_machines_app_service.identity.0.principal_id
    tenant_id      = module.user_identity.is_configured ? module.user_identity.details.tenant_id : azurerm_linux_web_app.elastic_machines_app_service.identity.0.tenant_id
    type           = module.user_identity.is_configured ? module.user_identity.details.type : azurerm_linux_web_app.elastic_machines_app_service.identity.0.type
    is_managed     = !module.user_identity.is_configured
  }

  # Configure Contributor access to the resource group and Reader access to the subscription
  role_assignments = !module.user_identity.is_configured && lookup(var.container.environment, "AZURE_CLIENT_ID", null) == null ? {
    "Contributor" = module.resource_group.details.id
    "Reader"      = "/subscriptions/${data.azurerm_subscription.current.subscription_id}"
  } : {}
}

data "azurerm_subscription" "current" {}

# Create or read a user-assigned identity for the App Service, if configured
module "user_identity" {
  source   = "./user-identity"
  identity = coalesce(var.service.app_service.identity, {})
}

################################################################
# Role assignment(s) needed for the the Principal to manage VMs, connect to networks, access shared images, etc.
################################################################

# Create a role assignment for each item in the map to make sure the Service Principal can manage resources at the given scope
resource "azurerm_role_assignment" "elastic_machines_role_assignments" {
  for_each             = local.role_assignments
  scope                = each.value
  role_definition_name = each.key
  principal_id         = local.app_service_identity.principal_id
}
