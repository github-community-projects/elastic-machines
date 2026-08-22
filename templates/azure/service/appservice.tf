################################################################
# Locals
################################################################

locals {
  resource_manager_endpoint = {
    "usgovernment" = "https://management.usgovcloudapi.net"
    "china"        = "https://management.chinacloudapi.cn"
    "public"       = "https://management.azure.com"
  }

  env = {
    # Try to resolve the value as a KeyVault secret, then a file, then a literal value in either form
    for envName, envValue in var.container.environment : envName => try(
      "@Microsoft.KeyVault(SecretUri=${envValue.secret})",
      chomp(file(envValue.file)),
      envValue.value,
      envValue
    )
  }

  app_settings = merge(
    var.service.app_insights.enabled ? {
      "APPLICATIONINSIGHTS_CONNECTION_STRING"      = "${local.app_insights.connection_string}"
      "ApplicationInsightsAgent_EXTENSION_VERSION" = "~2"
      } : {}, var.service.environment != null ? {
      "AZURE_RESOURCE_MANAGER_ENDPOINT" = lookup(local.resource_manager_endpoint, var.service.environment, null)
      } : {}, module.user_identity.is_configured ? {
      "AZURE_CLIENT_ID" = module.user_identity.details.client_id
    } : {},
    local.env
  )

  # Either use the name applied directly to the app_service config, or the service name
  app_service_name = var.service.app_service.name != null ? var.service.app_service.name : var.config_name
}

################################################################
# App Service Plan & App Service configured to run Elastic Machines in a container
################################################################

resource "azurerm_service_plan" "elastic_machines_service_plan" {
  name                = "${var.config_name}-app-svc-plan"
  resource_group_name = module.resource_group.details.name
  location            = module.resource_group.details.location
  os_type             = "Linux"
  sku_name            = var.service.app_service.plan_sku
  tags                = var.tags
}

resource "azurerm_linux_web_app" "elastic_machines_app_service" {
  name                                           = local.app_service_name
  resource_group_name                            = module.resource_group.details.name
  location                                       = module.resource_group.details.location
  service_plan_id                                = azurerm_service_plan.elastic_machines_service_plan.id
  tags                                           = var.tags
  https_only                                     = true
  ftp_publish_basic_authentication_enabled       = false
  webdeploy_publish_basic_authentication_enabled = false

  site_config {
    always_on                         = true
    health_check_path                 = "/"
    health_check_eviction_time_in_min = 10
    http2_enabled                     = true
    ftps_state                        = "Disabled"

    application_stack {
      docker_image_name        = "${var.container.image}:${var.container.version}"
      docker_registry_url      = "https://${var.container.registry}"
      docker_registry_username = var.container.login
      docker_registry_password = var.container.token
    }
  }

  key_vault_reference_identity_id = module.user_identity.is_configured ? module.user_identity.details.id : null

  identity {
    type         = module.user_identity.is_configured ? module.user_identity.details.type : "SystemAssigned"
    identity_ids = module.user_identity.is_configured ? [module.user_identity.details.id] : null
  }

  logs {
    http_logs {
      file_system {
        retention_in_days = 90
        retention_in_mb   = 100
      }
    }

    application_logs {
      file_system_level = "Warning"
    }

    detailed_error_messages = true
  }

  app_settings = local.app_settings

  lifecycle {
    # Ignore noise caused by the retention days being marked dirty incorrectly
    ignore_changes = [
      logs.0.http_logs.0.file_system.0.retention_in_days
    ]
  }
}
