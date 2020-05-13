FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY ./host/serverless_orleans.csproj ./host/
COPY ./actors/serverless_orleans_actors.csproj ./actors/
COPY ./interfaces/serverless_orleans_interfaces.csproj ./interfaces/
RUN dotnet restore "./host/serverless_orleans.csproj" --disable-parallel
COPY . .
RUN dotnet publish "./host/serverless_orleans.csproj" --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "serverless_orleans.dll"]
