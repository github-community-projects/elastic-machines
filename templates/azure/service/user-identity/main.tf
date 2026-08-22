################################################################
# Locals
################################################################

locals {
  user_assigned_identity = var.identity.name != null && var.identity.resource_group != null
  user_identity = local.user_assigned_identity ? merge(
    one(data.azurerm_user_assigned_identity.service_identity),
    {
      type = "UserAssigned"
    }
  ) : null
}

################################################################
# User-managed Identity (if configured)
################################################################

data "azurerm_user_assigned_identity" "service_identity" {
  count               = local.user_assigned_identity && var.identity.name != null ? 1 : 0
  name                = var.identity.name
  resource_group_name = var.identity.resource_group
}
