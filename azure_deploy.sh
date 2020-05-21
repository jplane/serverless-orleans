#!/bin/bash

# assumes you have already logged into the Azure CLI
#  https://docs.microsoft.com/en-us/cli/azure/authenticate-azure-cli?view=azure-cli-latest

create_resource_group () {
    # create resource group
    az group create --name $RG --location southcentralus
}

create_storage () {
    # create a storage account
    az storage account create \
        --name ${NAME}storage \
        --resource-group $RG \
        --sku Standard_LRS

    storage_connection_string=$(az storage account show-connection-string \
                                    --resource-group $RG \
                                    --name ${NAME}storage \
                                    --output tsv)

    # create storage queue
    az storage queue create \
        --name input \
        --connection-string $storage_connection_string
}

create_acr () {
    # create container registry
    az acr create \
        --resource-group $RG \
        --name ${NAME}registry \
        --sku Basic \
        --admin-enabled true

    # get ACR password
    acr_pwd=$(az acr credential show --name ${NAME}registry --query passwords[0].value --output tsv)
}

create_database () {
    # create database server
    az sql server create \
        --resource-group $RG \
        --name ${NAME}sqlserver \
        --admin-user $NAME \
        --admin-password $DB_PWD

    # update database server firewall
    az sql server firewall-rule create \
        --resource-group $RG \
        --server ${NAME}sqlserver \
        --name ${NAME}scriptdeploy \
        --start-ip-address $IP_ADDR \
        --end-ip-address $IP_ADDR

    # create database
    az sql db create \
        --resource-group $RG \
        --server ${NAME}sqlserver \
        --name ${NAME}database \
        --service-objective S0

    # get database connection string
    database_connection_string=$(az sql db show-connection-string \
                                    --server ${NAME}sqlserver \
                                    --name ${NAME}database \
                                    --client ado.net \
                                    --output tsv)

    # create blob container
    az storage container create \
        --name orleans-bacpacs \
        --connection-string $storage_connection_string

    # upload bacpac file to blob container
    az storage blob upload \
        --file ./db/orleans.bacpac \
        --name orleans.bacpac \
        --container-name orleans-bacpacs \
        --connection-string $storage_connection_string

    # get storage key
    storage_key=$(az storage account keys list \
                    --resource-group $RG \
                    --account-name ${NAME}storage \
                    --output tsv \
                    --query [0].value)

    # restore database backup
    az sql db import \
        --resource-group $RG \
        --server ${NAME}sqlserver \
        --name ${NAME}database \
        --storage-key-type StorageAccessKey \
        --storage-key $storage_key \
        --storage-uri https://${NAME}storage.blob.core.windows.net/orleans-bacpacs/orleans.bacpac \
        --admin-user $NAME \
        --admin-password $DB_PWD
}

upload_container_images () {
    # login to ACR
    docker login ${NAME}registry.azurecr.io --username ${NAME}registry --password $acr_pwd

    # build frontend docker image
    docker build -t ${NAME}registry.azurecr.io/frontend:v1 -f frontend.dockerfile .

    # push frontend image to ACR
    docker push ${NAME}registry.azurecr.io/frontend:v1

    # build backend docker image
    docker build -t ${NAME}registry.azurecr.io/backend:v1 -f backend.dockerfile .

    # push backend image to ACR
    docker push ${NAME}registry.azurecr.io/backend:v1
}

create_log_analytics_workspace () {
    # create workspace
    az monitor log-analytics workspace create \
        --resource-group $RG \
        --workspace-name ${NAME}loganalyticsworkspace

    # get workspace key
    la_wksp_key=$(az monitor log-analytics workspace get-shared-keys \
                    --resource-group ${RG} \
                    --workspace-name ${NAME}loganalyticsworkspace \
                    --query primarySharedKey \
                    --output tsv)
}

create_vnet () {
    # create vnet and frontend subnet
    az network vnet create \
        --resource-group ${RG} \
        --name ${NAME}vnet \
        --address-prefix 10.0.0.0/16 \
        --subnet-name ${NAME}frontendsubnet \
        --subnet-prefix 10.0.1.0/24

    # create backend subnet
    az network vnet subnet create \
        --resource-group ${RG} \
        --name ${NAME}backendsubnet \
        --vnet-name ${NAME}vnet \
        --address-prefix 10.0.2.0/24
}

create_container_group () {
    # create container group attached to vnet
    az container create \
        --resource-group $RG \
        --name ${NAME}cg1234 \
        --image ${NAME}registry.azurecr.io/backend:v1 \
        --registry-username ${NAME}registry \
        --registry-password $acr_pwd \
        --environment-variables \
                ORLEANS_CONFIG=STORAGE \
                StorageConnectionString="${storage_connection_string}" \
        --ports 11111 30000 \
        --vnet ${NAME}vnet \
        --subnet ${NAME}backendsubnet \
        --log-analytics-workspace ${NAME}loganalyticsworkspace \
        --log-analytics-workspace-key $la_wksp_key

    # get container group id
    cg_id=$(az container show --resource-group ${RG} --name ${NAME}cg1234 --query id --output tsv)
}

create_app_service () {
    # create app service plan
    az appservice plan create \
        --name ${NAME}appserviceplan \
        --resource-group $RG \
        --number-of-workers 1 \
        --sku S1 \
        --is-linux

    # create web app
    az webapp create \
        --resource-group $RG \
        --plan ${NAME}appserviceplan \
        --name ${NAME}appservice \
        --deployment-container-image-name ${NAME}registry.azurecr.io/frontend:v1 \
        --docker-registry-server-user ${NAME}registry \
        --docker-registry-server-password $acr_pwd

    # configure always-on
    az webapp config set \
        --resource-group $RG \
        --name ${NAME}appservice \
        --always-on true

    # configure logging
    az webapp log config \
        --resource-group $RG \
        --name ${NAME}appservice \
        --docker-container-logging filesystem \
        --application-logging true \
        --level information

    # configure appsettings
    az webapp config appsettings set \
        --resource-group $RG \
        --name ${NAME}appservice \
        --settings \
            ORLEANS_CONFIG=STORAGE \
            ASPNETCORE_ENVIRONMENT=Production \
            AzureWebJobsStorage="${storage_connection_string}"

    # add to vnet
    az webapp vnet-integration add \
        --resource-group $RG \
        --name ${NAME}appservice \
        --vnet ${NAME}vnet \
        --subnet ${NAME}frontendsubnet
}

create_autoscale_alerts () {
    # create scale up alert
    az monitor metrics alert create \
        --resource-group $RG \
        --name ${NAME}scaleupalert \
        --scopes ${cg_id} \
        --condition "avg Percentage CPU > 75" \
        --window-size 5m \
        --evaluation-frequency 2m \
        --action webhook https://www.contoso.com/scaleup \
        --description "high CPU - scale up"

    # create scale down alert
    az monitor metrics alert create \
        --resource-group $RG \
        --name ${NAME}scaledownalert \
        --scopes ${cg_id} \
        --condition "avg Percentage CPU < 25" \
        --window-size 5m \
        --evaluation-frequency 2m \
        --action webhook https://www.contoso.com/scaledown \
        --description "low CPU - scale down"
}

NAME=${1:-serverlessorleans}
RG=${NAME}-rg

create_resource_group
create_storage
create_acr
upload_container_images
create_log_analytics_workspace
create_vnet
create_container_group
create_app_service
create_autoscale_alerts