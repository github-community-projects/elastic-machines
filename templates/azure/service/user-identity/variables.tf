variable "identity" {
  description = "The details for the user-assigned identity the App Service should use."
  type = object({
    # The name and resource group of an existing identity to use
    name           = optional(string)
    resource_group = optional(string)
  })
  default = {}
}
