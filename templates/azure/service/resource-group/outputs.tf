output "details" {
  description = "The details of the Azure Resource Group that was created or queried via this module."
  value = merge(local.resource_group, {
    is_managed = length(azurerm_resource_group.elastic_machines_resource_group) > 0
  })
}
