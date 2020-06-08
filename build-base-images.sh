#!/bin/bash

docker build -t serverlessorleans/backend-base:v1 ./base/backend

docker build -t serverlessorleans/frontend-base:v1 ./base/frontend
