provider "azurerm" {
  version = "=2.12.0"
  features {}
}

locals {
  sp_json = jsondecode(file("${path.module}/azureauth.json"))
}

variable "name" {
    type    = string
    default = "serverlessorleans"
}

variable "orleans_container_cpu_cores" {
    type    = string
    default = "1.0"
}

variable "orleans_container_memory_gb" {
    type    = string
    default = "1.5"
}

variable "scaleout_cpu_threshold" {
    type    = number
    default = 80
}

variable "scaleout_memory_threshold" {
    type    = number
    default = 80
}

variable "scalein_cpu_threshold" {
    type    = number
    default = 10
}

variable "scalein_memory_threshold" {
    type    = number
    default = 10
}

data "azurerm_resource_group" "rg" {
  name      = "${var.name}-rg"
}

resource "azurerm_storage_account" "storage" {
  name                     = "${var.name}storage"
  resource_group_name      = data.azurerm_resource_group.rg.name
  location                 = data.azurerm_resource_group.rg.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
}

resource "azurerm_storage_queue" "queue" {
  name                 = "input"
  storage_account_name = azurerm_storage_account.storage.name
}

resource "azurerm_container_registry" "acr" {
  name                     = "${var.name}registry"
  resource_group_name      = data.azurerm_resource_group.rg.name
  location                 = data.azurerm_resource_group.rg.location
  sku                      = "Basic"
  admin_enabled            = true
}

resource "null_resource" "acrimagebuildpush" {

  provisioner "local-exec" {
    command = <<EOT
      docker login ${azurerm_container_registry.acr.login_server} \
                --username ${azurerm_container_registry.acr.admin_username} \
                --password ${azurerm_container_registry.acr.admin_password}

      docker build --build-arg BUILD_ENV=prod -t ${var.name}registry.azurecr.io/frontend:v1 -f frontend.dockerfile .
      docker push ${var.name}registry.azurecr.io/frontend:v1

      docker build -t ${var.name}registry.azurecr.io/backend:v1 -f backend.dockerfile .
      docker push ${var.name}registry.azurecr.io/backend:v1
    EOT
  }

  triggers = {
    acr_id = azurerm_container_registry.acr.id
  }
}

resource "azurerm_log_analytics_workspace" "la" {
  name                = "${var.name}loganalyticsworkspace"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
}

resource "azurerm_virtual_network" "vnet" {
  name                = "${var.name}vnet"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name
  address_space       = ["10.0.0.0/16"]
}

resource "azurerm_subnet" "frontendsubnet" {
  name                 = "${var.name}frontendsubnet"
  resource_group_name  = data.azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = [ "10.0.1.0/24" ]

  delegation {
    name = "delegationconfig"

    service_delegation {
      name    = "Microsoft.Web/serverFarms"
      actions = [
        "Microsoft.Network/virtualNetworks/subnets/action"
      ]
    }
  }
}

resource "azurerm_subnet" "backendsubnet" {
  name                 = "${var.name}backendsubnet"
  resource_group_name  = data.azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = [ "10.0.2.0/24" ]

  delegation {
    name = "delegationconfig"

    service_delegation {
      name    = "Microsoft.ContainerInstance/containerGroups"
      actions = [
        "Microsoft.Network/virtualNetworks/subnets/action"
      ]
    }
  }
}

resource "azurerm_network_profile" "backendnetworkprofile" {
  name                = "backendnetworkprofile"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name

  container_network_interface {
    name = "containernic"

    ip_configuration {
      name      = "ipconfig"
      subnet_id = azurerm_subnet.backendsubnet.id
    }
  }
}

resource "azurerm_container_group" "cg" {
  name                = "${var.name}cg1234"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name
  ip_address_type     = "private"
  network_profile_id  = azurerm_network_profile.backendnetworkprofile.id
  os_type             = "Linux"

  image_registry_credential {
      server    = azurerm_container_registry.acr.login_server
      username  = azurerm_container_registry.acr.admin_username
      password  = azurerm_container_registry.acr.admin_password
  }

  diagnostics {
      log_analytics {
          workspace_id  = azurerm_log_analytics_workspace.la.workspace_id
          workspace_key = azurerm_log_analytics_workspace.la.primary_shared_key
      }
  }

  container {
    name   = "orleanshost"
    image  = "${var.name}registry.azurecr.io/backend:v1"
    cpu    = var.orleans_container_cpu_cores
    memory = var.orleans_container_memory_gb

    environment_variables = {
        "ORLEANS_CONFIG"          = "STORAGE"
        "StorageConnectionString" = azurerm_storage_account.storage.primary_connection_string
    }

    ports {
      port     = 11111
      protocol = "TCP"
    }

    ports {
      port     = 30000
      protocol = "TCP"
    }
  }

  depends_on = [
    null_resource.acrimagebuildpush
  ]
}

resource "null_resource" "metricsoutput" {

  provisioner "local-exec" {
    command = <<EOT
      az monitor diagnostic-settings create \
          --resource ${azurerm_container_group.cg.id} \
          --name ${azurerm_container_group.cg.name}metricsoutput \
          --workspace ${azurerm_log_analytics_workspace.la.id} \
          --metrics '[{"category": "AllMetrics", "enabled": true, "retentionPolicy": {"enabled": true, "days": 7}, "timeGrain": "PT1M"}]'
    EOT
  }

  triggers = {
    acg_id  = azurerm_container_group.cg.id
    la_id   = azurerm_log_analytics_workspace.la.id
  }
}

resource "azurerm_app_service_plan" "appserviceplan" {
  name                = "${var.name}appserviceplan"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name
  kind                = "Linux"
  reserved            = true

  sku {
    tier = "Standard"
    size = "S1"
  }
}

resource "azurerm_app_service" "appservice" {
  name                = "${var.name}appservice"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name
  app_service_plan_id = azurerm_app_service_plan.appserviceplan.id

  site_config {
    app_command_line  = ""
    always_on         = true
    linux_fx_version  = "DOCKER|${var.name}registry.azurecr.io/frontend:v1"
  }

  app_settings = {
    "ORLEANS_CONFIG"                        = "STORAGE"
    "ASPNETCORE_ENVIRONMENT"                = "PRODUCTION"
    "AzureWebJobsStorage"                   = azurerm_storage_account.storage.primary_connection_string
    "ACG_ROOT_NAME"                         = var.name
    "LOG_ANALYTICS_WORKSPACE_ID"            = azurerm_log_analytics_workspace.la.workspace_id
    "LOG_ANALYTICS_WORKSPACE_KEY"           = azurerm_log_analytics_workspace.la.primary_shared_key
    "SERVICE_PRINCIPAL_ID"                  = local.sp_json.clientId
    "SERVICE_PRINCIPAL_SECRET"              = local.sp_json.clientSecret
    "SERVICE_PRINCIPAL_TENANT_ID"           = local.sp_json.tenantId
    "WEBSITES_ENABLE_APP_SERVICE_STORAGE"   = "false"
    "DOCKER_REGISTRY_SERVER_URL"            = azurerm_container_registry.acr.login_server,
    "DOCKER_REGISTRY_SERVER_USERNAME"       = azurerm_container_registry.acr.admin_username,
    "DOCKER_REGISTRY_SERVER_PASSWORD"       = azurerm_container_registry.acr.admin_password
  }

  depends_on = [
    null_resource.acrimagebuildpush
  ]
}

resource "azurerm_app_service_virtual_network_swift_connection" "appservicevnet" {
  app_service_id = azurerm_app_service.appservice.id
  subnet_id      = azurerm_subnet.frontendsubnet.id
}

resource "azurerm_monitor_action_group" "scaleoutaction" {
  name                = "${var.name}scaleoutaction"
  resource_group_name = data.azurerm_resource_group.rg.name
  short_name          = "scaleout"

  webhook_receiver {
    name                    = "webhook"
    service_uri             = "https://${azurerm_app_service.appservice.default_site_hostname}/mgmt/scaleout"
    use_common_alert_schema = true
  }
}

resource "azurerm_monitor_action_group" "scaleinaction" {
  name                = "${var.name}scaleinaction"
  resource_group_name = data.azurerm_resource_group.rg.name
  short_name          = "scalein"

  webhook_receiver {
    name                    = "webhook"
    service_uri             = "https://${azurerm_app_service.appservice.default_site_hostname}/mgmt/scalein"
    use_common_alert_schema = true
  }
}

resource "azurerm_monitor_scheduled_query_rules_alert" "cpuscaleoutalert" {
  name                = "${var.name}cpuscaleoutalert"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name

  action {
    action_group = [
      azurerm_monitor_action_group.scaleoutaction.id
    ]
  }

  data_source_id = azurerm_log_analytics_workspace.la.id
  description    = "scale out when avg aggregate CPU % consumed exceeds X"
  enabled        = true

  query       = format(<<-QUERY
        // 1000 millicores == 1 CPU core
        let configured_millicores = %s * 1000;
        AzureMetrics
        | where ResourceProvider == "MICROSOFT.CONTAINERINSTANCE"
        | where MetricName == "CpuUsage"
        | summarize AggregatedValue = ((sum(Total)/sum(Count))/configured_millicores) * 100 by ResourceProvider, bin(TimeGenerated, 1m)
  QUERY
    , var.orleans_container_cpu_cores)
  
  severity    = 1
  frequency   = 5
  time_window = 10
  trigger {
    operator  = "GreaterThan"
    threshold = var.scaleout_cpu_threshold
    metric_trigger {
      operator            = "GreaterThan"
      threshold           = 0
      metric_trigger_type = "Consecutive"       # alert after two or more consecutive threshold breaches
      metric_column       = "ResourceProvider"
    }
  }
}

resource "azurerm_monitor_scheduled_query_rules_alert" "cpuscaleinalert" {
  name                = "${var.name}cpuscaleinalert"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name

  action {
    action_group = [
      azurerm_monitor_action_group.scaleinaction.id
    ]
  }

  data_source_id = azurerm_log_analytics_workspace.la.id
  description    = "scale in when avg aggregate CPU % consumed goes below X"
  enabled        = true

  query       = format(<<-QUERY
        // 1000 millicores == 1 CPU core
        let configured_millicores = %s * 1000;
        AzureMetrics
        | where ResourceProvider == "MICROSOFT.CONTAINERINSTANCE"
        | where MetricName == "CpuUsage"
        | summarize AggregatedValue = ((sum(Total)/sum(Count))/configured_millicores) * 100 by ResourceProvider, bin(TimeGenerated, 1m)
  QUERY
    , var.orleans_container_cpu_cores)
  
  severity    = 1
  frequency   = 5
  time_window = 10
  trigger {
    operator  = "LessThan"
    threshold = var.scalein_cpu_threshold
    metric_trigger {
      operator            = "GreaterThan"
      threshold           = 0
      metric_trigger_type = "Consecutive"       # alert after two or more consecutive threshold breaches
      metric_column       = "ResourceProvider"
    }
  }
}

resource "azurerm_monitor_scheduled_query_rules_alert" "memscaleoutalert" {
  name                = "${var.name}memscaleoutalert"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name

  action {
    action_group = [
      azurerm_monitor_action_group.scaleoutaction.id
    ]
  }

  data_source_id = azurerm_log_analytics_workspace.la.id
  description    = "scale out when avg aggregate memory % consumed exceeds X"
  enabled        = true

  query       = <<-QUERY
        let configured_memory_in_bytes = ${var.orleans_container_memory_gb} * 1024 * 1024 * 1024;
        AzureMetrics
        | where ResourceProvider == "MICROSOFT.CONTAINERINSTANCE"
        | where MetricName == "MemoryUsage"
        | summarize AggregatedValue = ((sum(Total)/sum(Count))/configured_memory_in_bytes) * 100 by ResourceProvider, bin(TimeGenerated, 1m)
  QUERY
  
  severity    = 1
  frequency   = 5
  time_window = 10
  trigger {
    operator  = "GreaterThan"
    threshold = var.scaleout_memory_threshold
    metric_trigger {
      operator            = "GreaterThan"
      threshold           = 0
      metric_trigger_type = "Consecutive"       # alert after two or more consecutive threshold breaches
      metric_column       = "ResourceProvider"
    }
  }
}

resource "azurerm_monitor_scheduled_query_rules_alert" "memscaleinalert" {
  name                = "${var.name}memscaleinalert"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name

  action {
    action_group = [
      azurerm_monitor_action_group.scaleinaction.id
    ]
  }

  data_source_id = azurerm_log_analytics_workspace.la.id
  description    = "scale in when avg aggregate memory % consumed goes below X"
  enabled        = true

  query       = <<-QUERY
        let configured_memory_in_bytes = ${var.orleans_container_memory_gb} * 1024 * 1024 * 1024;
        AzureMetrics
        | where ResourceProvider == "MICROSOFT.CONTAINERINSTANCE"
        | where MetricName == "MemoryUsage"
        | summarize AggregatedValue = ((sum(Total)/sum(Count))/configured_memory_in_bytes) * 100 by ResourceProvider, bin(TimeGenerated, 1m)
  QUERY
  
  severity    = 1
  frequency   = 5
  time_window = 10
  trigger {
    operator  = "LessThan"
    threshold = var.scalein_memory_threshold
    metric_trigger {
      operator            = "GreaterThan"
      threshold           = 0
      metric_trigger_type = "Consecutive"       # alert after two or more consecutive threshold breaches
      metric_column       = "ResourceProvider"
    }
  }
}
