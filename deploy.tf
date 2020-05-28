provider "azurerm" {
  version = "=2.0.0"
  features {}
}

variable "location" {
  type      = string
  default   = "southcentralus"
}

variable "name" {
    type    = string
    default = "joshorleans"
}

variable "orleans_container_cpu_cores" {
    type    = string
    default = "1.0"
}

variable "orleans_container_memory_gb" {
    type    = string
    default = "1.5"
}

resource "azurerm_resource_group" "rg" {
  name      = "${var.name}-rg"
  location  = var.location
}

resource "azurerm_storage_account" "storage" {
  name                     = "${var.name}storage"
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = azurerm_resource_group.rg.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
}

resource "azurerm_storage_queue" "queue" {
  name                 = "input"
  storage_account_name = azurerm_storage_account.storage.name
}

resource "azurerm_container_registry" "acr" {
  name                     = "${var.name}registry"
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = azurerm_resource_group.rg.location
  sku                      = "Basic"
  admin_enabled            = true
}

resource "null_resource" "acrimagebuildpush" {

  provisioner "local-exec" {
    command = <<EOT
      docker login ${azurerm_container_registry.acr.login_server} \
                --username ${azurerm_container_registry.acr.admin_username} \
                --password ${azurerm_container_registry.acr.admin_password}

      docker build -t ${var.name}registry.azurecr.io/frontend:v1 -f frontend.dockerfile .
      docker push ${var.name}registry.azurecr.io/frontend:v1

      docker build -t ${var.name}registry.azurecr.io/backend:v1 -f backend.dockerfile .
      docker push ${var.name}registry.azurecr.io/backend:v1

      docker build -t ${var.name}registry.azurecr.io/autoscaler:v1 -f autoscaler.dockerfile .
      docker push ${var.name}registry.azurecr.io/autoscaler:v1
    EOT
  }

  triggers = {
    acr_id = azurerm_container_registry.acr.id
  }
}

resource "azurerm_log_analytics_workspace" "la" {
  name                = "${var.name}loganalyticsworkspace"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
}

resource "azurerm_virtual_network" "vnet" {
  name                = "${var.name}vnet"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  address_space       = ["10.0.0.0/16"]
}

resource "azurerm_subnet" "frontendsubnet" {
  name                 = "${var.name}frontendsubnet"
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefix       = "10.0.1.0/24"

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
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefix       = "10.0.2.0/24"

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
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name

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
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
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
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  kind                = "Linux"
  reserved            = true

  sku {
    tier = "Standard"
    size = "S1"
  }
}

resource "azurerm_app_service" "apiservice" {
  name                = "${var.name}apiservice"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
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
  app_service_id = azurerm_app_service.apiservice.id
  subnet_id      = azurerm_subnet.frontendsubnet.id
}

resource "azurerm_app_service" "autoscalerservice" {
  name                = "${var.name}autoscalerservice"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  app_service_plan_id = azurerm_app_service_plan.appserviceplan.id

  identity {
    type  = "SystemAssigned"
  }

  site_config {
    app_command_line  = ""
    always_on         = false
    linux_fx_version  = "DOCKER|${var.name}registry.azurecr.io/autoscaler:v1"
  }

  app_settings = {
    "ASPNETCORE_ENVIRONMENT"                = "PRODUCTION"
    "ACG_ROOT_NAME"                         = "${var.name}"
    "WEBSITES_ENABLE_APP_SERVICE_STORAGE"   = "false"
    "DOCKER_REGISTRY_SERVER_URL"            = azurerm_container_registry.acr.login_server,
    "DOCKER_REGISTRY_SERVER_USERNAME"       = azurerm_container_registry.acr.admin_username,
    "DOCKER_REGISTRY_SERVER_PASSWORD"       = azurerm_container_registry.acr.admin_password
  }

  depends_on = [
    null_resource.acrimagebuildpush
  ]
}

resource "azurerm_role_assignment" "msirole" {
  scope                = azurerm_resource_group.rg.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_app_service.autoscalerservice.identity.0.principal_id
}

# need to add scheduled rule query alerts for:
#  cpu % scale out
#  cpu % scale in
#  memory % scale out
#  memory % scale in
#  https://www.terraform.io/docs/providers/azurerm/r/monitor_scheduled_query_rules_alert.html
