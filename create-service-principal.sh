#!/bin/bash

# assumes you're logged into the Azure CLI and configured to use the desired tenant + subscription
#  run 'az account show' to get current ambient connection details

az ad sp create-for-rbac --sdk-auth > azureauth.json