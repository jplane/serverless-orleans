provider "azurerm" {
  version = "=1.44.0"
}

variable "location" {
  type      = string
  default   = "southcentralus"
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

resource "azurerm_subnet" "gatewaysubnet" {
  name                 = "gateway"
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefix       = "10.0.1.0/24"
}

resource "azurerm_subnet" "apisubnet" {
  name                 = "api"
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

resource "azurerm_subnet" "backendsubnet" {
  name                 = "backend"
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefix       = "10.0.3.0/24"

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

resource "azurerm_public_ip" "publicip" {
  name                = "${var.name}publicip"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  allocation_method   = "Dynamic"
}

locals {
  backend_address_pool_name      = "${azurerm_virtual_network.vnet.name}-beap"
  frontend_port_name             = "${azurerm_virtual_network.vnet.name}-feport"
  frontend_ip_configuration_name = "${azurerm_virtual_network.vnet.name}-feip"
  http_setting_name              = "${azurerm_virtual_network.vnet.name}-be-htst"
  listener_name                  = "${azurerm_virtual_network.vnet.name}-httplstn"
  request_routing_rule_name      = "${azurerm_virtual_network.vnet.name}-rqrt"
  redirect_configuration_name    = "${azurerm_virtual_network.vnet.name}-rdrcfg"
}

resource "azurerm_application_gateway" "gateway" {
  name                = "${var.name}appgateway"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location

  sku {
    name     = "Standard_Small"
    tier     = "Standard"
    capacity = 1
  }

  gateway_ip_configuration {
    name      = "my-gateway-ip-configuration"
    subnet_id = azurerm_subnet.gatewaysubnet.id
  }

  frontend_port {
    name = local.frontend_port_name
    port = 80
  }

  frontend_ip_configuration {
    name                 = local.frontend_ip_configuration_name
    public_ip_address_id = azurerm_public_ip.publicip.id
    subnet_id            = azurerm_subnet.gatewaysubnet.id
  }

  backend_address_pool {
    name = local.backend_address_pool_name
    ip_addresses = [
      azurerm_container_group.apicg.ip_address
    ]
  }

  backend_http_settings {
    name                  = local.http_setting_name
    cookie_based_affinity = "Disabled"
    port                  = 80
    protocol              = "Http"
    request_timeout       = 30
  }

  http_listener {
    name                           = local.listener_name
    frontend_ip_configuration_name = local.frontend_ip_configuration_name
    frontend_port_name             = local.frontend_port_name
    protocol                       = "Http"
  }

  request_routing_rule {
    name                       = local.request_routing_rule_name
    rule_type                  = "Basic"
    http_listener_name         = local.listener_name
    backend_address_pool_name  = local.backend_address_pool_name
    backend_http_settings_name = local.http_setting_name
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

resource "azurerm_container_group" "backendcg" {
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

resource "azurerm_monitor_diagnostic_setting" "metricsoutput" {
  name                          = "${azurerm_container_group.backendcg.name}metricsoutput"
  target_resource_id            = azurerm_container_group.backendcg.id
  log_analytics_workspace_id    = azurerm_log_analytics_workspace.la.id

  metric {
    category = "AllMetrics"
    retention_policy {
      enabled   = true
      days      = 7
    }
  }
}

resource "azurerm_network_profile" "apinetworkprofile" {
  name                = "apinetworkprofile"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name

  container_network_interface {
    name = "containernic"

    ip_configuration {
      name      = "ipconfig"
      subnet_id = azurerm_subnet.apisubnet.id
    }
  }
}

resource "azurerm_container_group" "apicg" {
  name                = "${var.name}cgapi"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  ip_address_type     = "private"
  network_profile_id  = azurerm_network_profile.apinetworkprofile.id
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
    name   = "orleansapi"
    image  = "${var.name}registry.azurecr.io/frontend:v1"
    cpu    = var.orleans_container_cpu_cores
    memory = var.orleans_container_memory_gb

    environment_variables = {
          "ORLEANS_CONFIG"          = "STORAGE"
          "ASPNETCORE_ENVIRONMENT"  = "PRODUCTION"
          "StorageConnectionString" = azurerm_storage_account.storage.primary_connection_string
          "ACG_ROOT_NAME"           = var.name
    }

    ports {
      port     = 80
      protocol = "TCP"
    }
  }

  identity {
    type  = "SystemAssigned"
  }

  depends_on = [
    null_resource.acrimagebuildpush
  ]
}

resource "azurerm_role_assignment" "msirole" {
  scope                = azurerm_resource_group.rg.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_container_group.apicg.identity.0.principal_id
}

# need to add scheduled rule query alerts for:
#  cpu % scale out
#  cpu % scale in
#  memory % scale out
#  memory % scale in
#  https://www.terraform.io/docs/providers/azurerm/r/monitor_scheduled_query_rules_alert.html
