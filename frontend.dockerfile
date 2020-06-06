FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
ARG BUILD_ENV
WORKDIR /src
COPY ./azureauth.json .
RUN if [ "$BUILD_ENV" = "dev" ]; then \
        mkdir /publish; \
        cp ./azureauth.json /publish/azureauth.json; \
    fi
COPY ./app/message_actor_interfaces ./message_actor_interfaces
COPY ./app/message_app ./message_app
RUN dotnet publish "./message_app/Message.App.csproj" -c Release -o /publish

FROM serverlessorleans/frontend-base:v1
COPY --from=build /publish ./app
