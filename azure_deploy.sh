
# assumes you have already logged into the Azure CLI
#  https://docs.microsoft.com/en-us/cli/azure/authenticate-azure-cli?view=azure-cli-latest

DB_PWD=$1
IP_ADDR=$2
NAME=${3:-serverlessorleans}
RG=${NAME}-rg

# create resource group
az group create --name $RG --location southcentralus

# create a storage account
az storage account create \
    --name ${NAME}storage \
    --resource-group $RG \
    --sku Standard_LRS

storage_connection_string=$(az storage account show-connection-string --resource-group $RG --name ${NAME}storage --output tsv)

# create storage queue
az storage queue create \
    --name input \
    --connection-string $storage_connection_string

# create container registry
az acr create \
    --name ${NAME}registry \
    --resource-group $RG \
    --sku Basic \
    --admin-enabled true

# get ACR password
acr_pwd=$(az acr credential show --name ${NAME}registry --query passwords[0].value --output tsv)

# build docker image
docker build --tag ${NAME}:latest .

# login to ACR
docker login ${NAME}registry.azurecr.io --username ${NAME}registry --password $acr_pwd

# tag image for ACR
docker tag ${NAME}:latest ${NAME}registry.azurecr.io/app:v1

# push image to ACR
docker push ${NAME}registry.azurecr.io/app:v1

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
database_connection_string=$(az sql db show-connection-string --server ${NAME}sqlserver --name ${NAME}database --client ado.net --output tsv)

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
storage_key=$(az storage account keys list --resource-group $RG --account-name ${NAME}storage --output tsv --query [0].value)

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

# create app service plan
az appservice plan create \
    --name ${NAME}appserviceplan \
    --resource-group $RG \
    --number-of-workers 2 \
    --sku S1 \
    --is-linux

# create web app
az webapp create \
    --resource-group $RG \
    --plan ${NAME}appserviceplan \
    --name ${NAME}appservice \
    --deployment-container-image-name ${NAME}registry.azurecr.io/app:v1 \
    --docker-registry-server-user ${NAME}registry \
    --docker-registry-server-password $acr_pwd

# configure always-on for webapp
az webapp config set \
    --resource-group $RG \
    --name ${NAME}appservice \
    --always-on true

# configure appsettings for webapp
az webapp config appsettings set \
    --resource-group $RG \
    --name ${NAME}appservice \
    --settings ASPNETCORE_ENVIRONMENT=AZURE-STORAGE AzureWebJobsStorage="${storage_connection_string}" SqlConnectionString="${database_connection_string}"
