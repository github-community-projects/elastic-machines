variable "config_name" {
  description = "The name of the Elastic Machines configuration to deploy. This name is used as a prefix for the Azure resources that are created in this module."
  type        = string
  nullable    = false
}

variable "tags" {
  description = "A map of tags that will be applied across created cloud resources."
  type        = map(string)
  default     = null
}

variable "container" {
  description = "The details of the Elastic Machines container image to be used for this deployment."
  type = object({
    # The login and access token that will be used to authenticate to the container image registry
    login = string # This is used for Docker login, and can generally be set to any user name provided the token is valid
    token = string

    # The image registry and organization that hosts the Elastic Machines container image
    registry = optional(string, "ghcr.io") # default: ghcr.io

    # The name and version/tag of the Elastic Machines container image
    image   = optional(string, "elastic-machines/monolith") # default: elastic-machines/monolith
    version = optional(string, "latest")                                      # default: latest

    # A list of environment variables to be passed to the Elastic Machines container
    environment = any
  })
  nullable = false
}

variable "service" {
  description = "The details of the App Service to be deployed that will host Elastic Machines."
  type = object({
    # The subscription ID to use for this deployment
    subscription_id = string
    environment     = optional(string)

    # The Azure resource group resources will be created in
    resource_group = object({
      name   = optional(string)
      region = optional(string)
    })

    app_service = optional(object({
      # App Service creates a host name, so the name specified here must be globally unique. If left unset, the configuration name (general.config_name) will be used and also must be globally unique.
      name = optional(string)
      # The SKU of the App Service Plan that will be created to host the Elastic Machines service.
      plan_sku = optional(string, "S1") # Default: S1

      # Details for the user-assigned identity the App Service should use
      identity = optional(object({
        # The name and resource group of an existing identity to use
        name           = optional(string)
        resource_group = optional(string)
      }))
      }), {
      plan_sku = "S1"
    })

    # Application Insights configuration for telemetry and monitoring of the Elastic Machines service
    # Default: When the app_insights node is omitted, or the name/resource_group are omitted, a new App Insights resource will be created
    app_insights = optional(object({
      # A value indicating whether to use App Insights for telemetry/monitoring
      enabled = optional(bool, true)
      # Details of an Application Insights instance to use for monitoring the App Service
      name           = optional(string)
      resource_group = optional(string)
      }), {
      enabled = true
    })
  })
  nullable = false
}
