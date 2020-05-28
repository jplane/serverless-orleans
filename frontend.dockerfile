FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
ARG BUILD_ENV
WORKDIR /src
COPY ./azureauth.json .
RUN if [ "$BUILD_ENV" = "dev" ]; then \
        mkdir /publish; \
        cp ./azureauth.json /publish/azureauth.json; \
    fi
COPY ./frontend ./frontend
COPY ./interfaces ./interfaces
RUN dotnet publish "./frontend/frontend.csproj" -c Release -o /publish

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY --from=build /publish .
ENTRYPOINT ["dotnet", "frontend.dll"]
