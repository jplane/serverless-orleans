FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY . .
RUN dotnet publish "./Frontend.csproj" -c Release -o /publish

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /base
COPY --from=build /publish .
ENTRYPOINT ["dotnet", "Frontend.dll"]
